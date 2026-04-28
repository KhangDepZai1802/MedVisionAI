using System.Diagnostics;
using System.Text.Json;

namespace MedVisionAI.Services
{
    /// <summary>
    /// Gọi Python subprocess để chạy inference cho model .pt/.pth/.h5.
    /// C# gửi JSON qua stdin, nhận kết quả JSON qua stdout.
    ///
    /// Yêu cầu: python3 + torch (hoặc tensorflow) được cài trên máy.
    /// Script bridge.py được đặt cùng thư mục với .exe.
    ///
    /// Protocol:
    ///   Input (stdin):  {"model": "path/to/model.pt", "image": "path/to/img.png"}
    ///   Output (stdout): {"boxes": [[x1n,y1n,x2n,y2n,conf], ...], "error": null}
    ///   (tọa độ normalized 0-1)
    /// </summary>
    public class PythonBridge
    {
        private readonly string _pythonExe;
        private readonly string _bridgeScript;

        public PythonBridge()
        {
            // Tìm python3 trong PATH
            _pythonExe = FindPython();

            // bridge.py nằm cùng thư mục với exe
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _bridgeScript = Path.Combine(exeDir, "bridge.py");

            if (!File.Exists(_bridgeScript))
                WriteBridgeScript(_bridgeScript);
        }

        // ── Run inference ─────────────────────────────────────────────────────

        public List<float[]> RunInference(string modelPath, string imagePath)
        {
            if (string.IsNullOrEmpty(_pythonExe))
                throw new Exception(
                    "Không tìm thấy Python trên máy.\n\n" +
                    "Để dùng model .pt/.pth/.h5, vui lòng:\n" +
                    "  1. Cài Python 3.9+\n" +
                    "  2. Cài torch: pip install torch\n" +
                    "  3. Hoặc xuất model sang .onnx để dùng trực tiếp.");

            if (!File.Exists(_bridgeScript))
                throw new Exception($"Không tìm thấy bridge.py tại: {_bridgeScript}");

            var input = JsonSerializer.Serialize(new
            {
                model = modelPath,
                image = imagePath,
            });

            var psi = new ProcessStartInfo
            {
                FileName               = _pythonExe,
                Arguments              = $"\"{_bridgeScript}\"",
                UseShellExecute        = false,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new Exception("Không thể khởi động Python subprocess.");

            proc.StandardInput.WriteLine(input);
            proc.StandardInput.Close();

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30_000); // timeout 30s

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                throw new Exception(
                    $"Python bridge lỗi (exit {proc.ExitCode}):\n{stderr}");

            var result = JsonSerializer.Deserialize<BridgeResult>(stdout)
                ?? throw new Exception("Python bridge trả về JSON không hợp lệ.");

            if (!string.IsNullOrEmpty(result.Error))
                throw new Exception($"Python bridge báo lỗi:\n{result.Error}");

            return result.Boxes ?? new List<float[]>();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FindPython()
        {
            foreach (var candidate in new[] { "python3", "python", "py" })
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName               = candidate,
                        Arguments              = "--version",
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true,
                    });
                    p?.WaitForExit(3000);
                    if (p?.ExitCode == 0) return candidate;
                }
                catch { /* not found, try next */ }
            }
            return "";
        }

        // ── Embed bridge.py (tự ghi ra disk khi cần) ─────────────────────────

        private static void WriteBridgeScript(string path)
        {
            File.WriteAllText(path, """
#!/usr/bin/env python3
# Bridge script cho MedVision AI.
# Nhận JSON từ stdin, chạy inference, trả về JSON qua stdout.
#
# Hỗ trợ:
#   - PyTorch (.pt / .pth): dùng torch.load + forward pass
#   - Keras (.h5): dùng tensorflow.keras.models.load_model

import sys, json, traceback

def main():
    try:
        req   = json.loads(sys.stdin.readline())
        model_path = req["model"]
        image_path = req["image"]
        ext        = model_path.lower().rsplit(".", 1)[-1]

        if ext in ("pt", "pth"):
            boxes = run_pytorch(model_path, image_path)
        elif ext == "h5":
            boxes = run_keras(model_path, image_path)
        else:
            raise ValueError(f"Unsupported format: {ext}")

        print(json.dumps({"boxes": boxes, "error": None}))

    except Exception as e:
        print(json.dumps({"boxes": [], "error": str(e) + "\n" + traceback.format_exc()}))

# ── PyTorch ──────────────────────────────────────────────────────────────────

def run_pytorch(model_path, image_path):
    import torch, cv2, numpy as np

    device = "cuda" if torch.cuda.is_available() else "cpu"
    raw    = torch.load(model_path, map_location=device, weights_only=False)

    # Chuẩn hoá model về nn.Module
    if isinstance(raw, torch.nn.Module):
        model = raw.eval()
    elif isinstance(raw, dict):
        # Thử load như state_dict vào YOLO hoặc ResNet
        raise ValueError(
            "File .pth chứa state_dict, không phải model đầy đủ.\n"
            "Vui lòng xuất model sang .onnx:\n"
            "  torch.onnx.export(model, dummy_input, 'model.onnx')"
        )
    else:
        model = raw.eval()

    # Preprocess
    img  = cv2.imread(image_path)
    h0, w0 = img.shape[:2]
    inp  = preprocess(img, 640)     # resize to 640x640

    with torch.no_grad():
        out = model(inp.to(device))

    # Parse output — thử các format phổ biến
    boxes = parse_output(out, w0, h0)
    return boxes

def run_keras(model_path, image_path):
    import tensorflow as tf, cv2, numpy as np

    model = tf.keras.models.load_model(model_path)
    img   = cv2.imread(image_path)
    h0, w0 = img.shape[:2]

    inp_size = model.input_shape[1:3]  # (H, W)
    inp_h, inp_w = (inp_size[0] or 640), (inp_size[1] or 640)

    resized  = cv2.resize(img, (inp_w, inp_h))
    rgb      = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype("float32") / 255.0
    tensor   = np.expand_dims(rgb, 0)   # [1, H, W, 3]

    out = model.predict(tensor)
    boxes = parse_output_numpy(out, w0, h0)
    return boxes

# ── Preprocessing ─────────────────────────────────────────────────────────────

def preprocess(img_bgr, size=640):
    import cv2, numpy as np, torch
    resized = cv2.resize(img_bgr, (size, size))
    rgb     = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype("float32") / 255.0
    mean    = np.array([0.485, 0.456, 0.406])
    std     = np.array([0.229, 0.224, 0.225])
    rgb     = (rgb - mean) / std
    tensor  = torch.from_numpy(rgb.transpose(2, 0, 1)).float().unsqueeze(0)
    return tensor

# ── Output parsing ────────────────────────────────────────────────────────────

def parse_output(out, orig_w, orig_h, conf_thr=0.25):
    # Parse torch output (Tensor hoặc tuple/list) thành boxes chuẩn hoá.
    import torch, numpy as np

    # Nếu là tuple/list → lấy tensor đầu tiên
    if isinstance(out, (tuple, list)):
        out = out[0]

    if isinstance(out, torch.Tensor):
        arr = out.cpu().detach().numpy()
    else:
        arr = np.array(out)

    return parse_output_numpy(arr, orig_w, orig_h, conf_thr)

def parse_output_numpy(arr, orig_w, orig_h, conf_thr=0.25):
    # Tự động thích nghi với shape output:
    #   [1, num_boxes, 5+C]  -> YOLO format A
    #   [1, 5+C, num_boxes]  -> YOLO format B (transposed)
    # Trả về [[x1n, y1n, x2n, y2n, conf], ...] (normalized 0-1)
    import numpy as np

    arr = np.squeeze(arr)           # remove batch dim

    if arr.ndim == 1:
        # Classification → không có boxes
        return []

    if arr.ndim == 2:
        rows, cols = arr.shape
        if cols >= 5:
            # Format A: [num_boxes, 5+C]
            return _parse_rows(arr, orig_w, orig_h, conf_thr)
        elif rows >= 5:
            # Format B: [5+C, num_boxes]
            return _parse_rows(arr.T, orig_w, orig_h, conf_thr)

    return []

def _parse_rows(arr, orig_w, orig_h, conf_thr):
    import numpy as np
    boxes = []
    for row in arr:
        if len(row) < 5:
            continue
        cx, cy, bw, bh = row[0], row[1], row[2], row[3]
        conf = float(row[4])
        if conf < conf_thr:
            continue
        # Normalize về 0-1 (nếu giá trị > 1 thì đang ở pixel space)
        if cx > 1 or cy > 1:
            cx /= orig_w; cy /= orig_h
            bw /= orig_w; bh /= orig_h
        x1 = cx - bw / 2; y1 = cy - bh / 2
        x2 = cx + bw / 2; y2 = cy + bh / 2
        x1, y1, x2, y2 = max(0,x1), max(0,y1), min(1,x2), min(1,y2)
        if x2 > x1 and y2 > y1:
            boxes.append([x1, y1, x2, y2, conf])
    return boxes

if __name__ == "__main__":
    main()
""");
        }

        private class BridgeResult
        {
            public List<float[]>? Boxes { get; set; }
            public string? Error { get; set; }
        }
    }
}
