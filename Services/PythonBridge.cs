using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedVisionAI.Services
{
    public class PythonBridge
    {
        private readonly string _pythonExe;
        private readonly string _bridgeScript;

        public PythonBridge()
        {
            _pythonExe = FindPythonWithTorch();
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _bridgeScript = Path.Combine(exeDir, "bridge.py");

            // Luôn ghi lại bridge.py để đảm bảo dùng phiên bản mới nhất
            WriteBridgeScript(_bridgeScript);
        }

        public List<float[]> RunInference(string modelPath, string imagePath)
        {
            if (string.IsNullOrEmpty(_pythonExe))
                throw new Exception(
                    "Không tìm thấy Python có cài torch/ultralytics trên máy.\n\n" +
                    "Vui lòng chạy lệnh sau trong PowerShell:\n" +
                    "  pip install ultralytics\n\n" +
                    "Hoặc xuất model sang .onnx để không cần Python:\n" +
                    "  from ultralytics import YOLO\n" +
                    "  YOLO('best.pt').export(format='onnx')");

            if (!File.Exists(_bridgeScript))
                throw new Exception($"Không tìm thấy bridge.py tại: {_bridgeScript}");

            var input = JsonSerializer.Serialize(new { model = modelPath, image = imagePath });

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
            proc.WaitForExit(60_000); // tăng timeout lên 60s vì YOLO load model lâu hơn

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                throw new Exception($"Python bridge lỗi (exit {proc.ExitCode}):\n{stderr}");

            var result = JsonSerializer.Deserialize<BridgeResult>(stdout)
                ?? throw new Exception("Python bridge trả về JSON không hợp lệ.");

            if (!string.IsNullOrEmpty(result.Error))
                throw new Exception($"Python bridge báo lỗi:\n{result.Error}");

            return result.Boxes ?? new List<float[]>();
        }

        /// <summary>
        /// Tìm Python executable có cài torch hoặc ultralytics.
        /// Ưu tiên: đường dẫn tuyệt đối Python313 → python → python3 → py
        /// </summary>
        private static string FindPythonWithTorch()
        {
            // Danh sách candidate: đường dẫn tuyệt đối trước, fallback sau
            var candidates = new[]
            {
                // Đường dẫn tuyệt đối Python 3.13 (phù hợp máy hiện tại)
                @"C:\Users\khang\AppData\Local\Programs\Python\Python313\python.exe",
                // Các đường dẫn phổ biến khác
                @"C:\Python313\python.exe",
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                // PATH-based fallback
                "python",
                "python3",
                "py",
            };

            foreach (var candidate in candidates)
            {
                if (!TryRunPython(candidate, "--version", out _))
                    continue;

                // Kiểm tra có ultralytics không (ưu tiên hơn torch thuần)
                if (TryRunPython(candidate, "-c \"import ultralytics\"", out _))
                    return candidate;

                // Fallback: kiểm tra torch
                if (TryRunPython(candidate, "-c \"import torch\"", out _))
                    return candidate;
            }

            return "";
        }

        private static bool TryRunPython(string exe, string args, out string output)
        {
            output = "";
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName               = exe,
                    Arguments              = args,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                });
                if (p == null) return false;
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteBridgeScript(string path)
        {
            File.WriteAllText(path,
@"#!/usr/bin/env python3
# bridge.py — MedVision AI Python Bridge (Ultralytics YOLO edition)
import sys, json, traceback

def main():
    try:
        req = json.loads(sys.stdin.readline())
        model_path = req['model']
        image_path = req['image']
        ext = model_path.lower().rsplit('.', 1)[-1]

        if ext in ('pt', 'pth'):
            boxes = run_yolo(model_path, image_path)
        elif ext == 'h5':
            boxes = run_keras(model_path, image_path)
        else:
            raise ValueError(f'Unsupported format: .{ext}. Please export to .onnx or use .pt YOLO model.')

        print(json.dumps({'boxes': boxes, 'error': None}))

    except Exception as e:
        print(json.dumps({'boxes': [], 'error': str(e) + chr(10) + traceback.format_exc()}))


def run_yolo(model_path, image_path):
    """"""Chạy YOLO model dùng Ultralytics — hỗ trợ YOLOv5/v8/v9/v10/v11""""""
    try:
        from ultralytics import YOLO
        import cv2

        model = YOLO(model_path)
        img = cv2.imread(image_path)
        if img is None:
            raise ValueError(f'Cannot read image: {image_path}')

        h0, w0 = img.shape[:2]

        # Chạy inference
        results = model(image_path, verbose=False, conf=0.25)

        boxes = []
        for r in results:
            if r.boxes is None:
                continue
            for box in r.boxes:
                # box.xyxy: [x1, y1, x2, y2] trong pixel
                x1, y1, x2, y2 = box.xyxy[0].tolist()
                conf = float(box.conf[0])

                # Normalize về 0..1
                boxes.append([
                    x1 / w0,
                    y1 / h0,
                    x2 / w0,
                    y2 / h0,
                    conf
                ])

        return boxes

    except ImportError:
        # Fallback: dùng torch.load thuần nếu không có ultralytics
        return run_pytorch_raw(model_path, image_path)


def run_pytorch_raw(model_path, image_path):
    """"""Fallback: load model PyTorch thuần (không phải YOLO Ultralytics)""""""
    import torch, cv2, numpy as np

    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    raw = torch.load(model_path, map_location=device, weights_only=False)

    if isinstance(raw, dict):
        raise ValueError(
            'File .pth chứa state_dict, không thể load trực tiếp.\n'
            'Hãy xuất sang .onnx:\n'
            '  from ultralytics import YOLO\n'
            '  YOLO(""best.pt"").export(format=""onnx"")'
        )

    model = raw.eval() if isinstance(raw, torch.nn.Module) else raw.eval()
    img = cv2.imread(image_path)
    if img is None:
        raise ValueError(f'Cannot read image: {image_path}')

    h0, w0 = img.shape[:2]
    inp = preprocess(img, 640)

    with torch.no_grad():
        out = model(inp.to(device))

    return parse_output(out, w0, h0)


def run_keras(model_path, image_path):
    import tensorflow as tf, cv2, numpy as np
    model = tf.keras.models.load_model(model_path)
    img = cv2.imread(image_path)
    if img is None:
        raise ValueError(f'Cannot read image: {image_path}')

    h0, w0 = img.shape[:2]
    inp_size = model.input_shape[1:3]
    inp_h = inp_size[0] or 640
    inp_w = inp_size[1] or 640
    resized = cv2.resize(img, (inp_w, inp_h))
    rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype('float32') / 255.0
    tensor = np.expand_dims(rgb, 0)
    out = model.predict(tensor)
    return parse_output_numpy(out, w0, h0)


def preprocess(img_bgr, size=640):
    import cv2, numpy as np, torch
    resized = cv2.resize(img_bgr, (size, size))
    rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype('float32') / 255.0
    # YOLO-style normalize (không dùng ImageNet mean/std)
    return torch.from_numpy(rgb.transpose(2, 0, 1)).float().unsqueeze(0)


def parse_output(out, orig_w, orig_h, conf_thr=0.25):
    import torch, numpy as np
    if isinstance(out, (tuple, list)):
        out = out[0]
    arr = out.cpu().detach().numpy() if isinstance(out, torch.Tensor) else np.array(out)
    return parse_output_numpy(arr, orig_w, orig_h, conf_thr)


def parse_output_numpy(arr, orig_w, orig_h, conf_thr=0.25):
    import numpy as np
    arr = np.squeeze(arr)
    if arr.ndim == 1:
        return []
    if arr.ndim == 2:
        rows, cols = arr.shape
        if cols >= 5:
            return _parse_rows(arr, orig_w, orig_h, conf_thr)
        elif rows >= 5:
            return _parse_rows(arr.T, orig_w, orig_h, conf_thr)
    return []


def _parse_rows(arr, orig_w, orig_h, conf_thr):
    boxes = []
    for row in arr:
        if len(row) < 5:
            continue
        cx, cy, bw, bh = row[0], row[1], row[2], row[3]
        conf = float(row[4])
        if conf < conf_thr:
            continue
        # Nếu tọa độ là pixel (> 1.0) thì normalize
        if cx > 1 or cy > 1:
            cx /= orig_w
            cy /= orig_h
            bw /= orig_w
            bh /= orig_h
        x1 = max(0.0, cx - bw / 2)
        y1 = max(0.0, cy - bh / 2)
        x2 = min(1.0, cx + bw / 2)
        y2 = min(1.0, cy + bh / 2)
        if x2 > x1 and y2 > y1:
            boxes.append([x1, y1, x2, y2, conf])
    return boxes


if __name__ == '__main__':
    main()
");
        }

        private class BridgeResult
        {
            [JsonPropertyName("boxes")]
            public List<float[]>? Boxes { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }
        }
    }
}
