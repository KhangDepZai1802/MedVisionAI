using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedVisionAI.Models;

namespace MedVisionAI.Services
{
    public class PythonBridge
    {
        private const int BridgeTimeoutMs = 180_000;
        private static readonly Lazy<string> CachedPythonExe = new(FindPythonWithTorch);

        private readonly string _pythonExe;
        private readonly string _bridgeScript;

        public PythonBridge()
        {
            _pythonExe = CachedPythonExe.Value;
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _bridgeScript = Path.Combine(exeDir, "bridge.py");

            // Luôn ghi lại bridge.py để đảm bảo dùng phiên bản mới nhất
            WriteBridgeScript(_bridgeScript);
        }

        /// <summary>
        /// Chạy inference trả về bounding boxes [x1,y1,x2,y2,conf] normalized 0–1.
        /// modelType: "yolo" | "maskrcnn" | "resnet_classifier" | "keras" | ""
        /// Nếu để trống, bridge.py tự detect theo tên file.
        /// </summary>
        public List<float[]> RunInference(string modelPath, string imagePath,
                                          string modelType = "")
        {
            var result = CallBridge(JsonSerializer.Serialize(new
            {
                model      = modelPath,
                image      = imagePath,
                model_type = modelType
            }));
            return result.Boxes ?? new List<float[]>();
        }

        /// <summary>
        /// Chạy ResNet50 classifier (BatThuongNST.pth), trả về tên nhãn dự đoán.
        /// </summary>
        public string RunClassifier(string modelPath, string imagePath)
        {
            var result = CallBridge(JsonSerializer.Serialize(new
            {
                model      = modelPath,
                image      = imagePath,
                model_type = "resnet_classifier"
            }));

            // bridge.py trả về thêm trường "label" cho classifier
            if (!string.IsNullOrEmpty(result.Label))
                return result.Label;

            // Fallback: dùng confidence của box toàn ảnh
            if (result.Boxes?.Count > 0)
            {
                float conf = result.Boxes[0][4];
                return conf >= 0.5f ? $"Bất thường (conf={conf:P0})" : "Bình thường";
            }

            return "";
        }

        public List<AnomalyPrediction> RunClassifierBatch(
            string modelPath, IReadOnlyList<string> imagePaths)
        {
            if (imagePaths.Count == 0)
                return new List<AnomalyPrediction>();

            var result = CallBridge(JsonSerializer.Serialize(new
            {
                model      = modelPath,
                image      = imagePaths[0],
                images     = imagePaths,
                model_type = "resnet_classifier_batch"
            }));

            return result.Predictions ?? new List<AnomalyPrediction>();
        }

        // ── Shared helper: gọi bridge.py và trả về BridgeResult ─────────────────
        private BridgeResult CallBridge(string jsonInput)
        {
            if (string.IsNullOrEmpty(_pythonExe))
                throw new Exception(
                    "Không tìm thấy Python có cài torch/ultralytics trên máy.\n\n" +
                    "Vui lòng chạy lệnh sau trong PowerShell:\n" +
                    "  pip install ultralytics torchvision\n\n" +
                    "Hoặc xuất model sang .onnx để không cần Python:\n" +
                    "  from ultralytics import YOLO\n" +
                    "  YOLO('best.pt').export(format='onnx')");

            if (!File.Exists(_bridgeScript))
                throw new Exception($"Không tìm thấy bridge.py tại: {_bridgeScript}");

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

            proc.StandardInput.WriteLine(jsonInput);
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(BridgeTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException(
                    $"Python bridge quá thời gian {BridgeTimeoutMs / 1000} giây.\n" +
                    "Model .pth Mask R-CNN/ResNet có thể quá nặng khi chạy CPU, " +
                    "hoặc Python đang bị kẹt khi load model.");
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                throw new Exception($"Python bridge lỗi (exit {proc.ExitCode}):\n{stderr}");

            // Tìm dòng JSON hợp lệ cuối cùng trong stdout
            // (các dòng khác là warning/print từ torch/torchvision)
            string? jsonLine = null;
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                    jsonLine = trimmed;
            }
            if (jsonLine == null)
                throw new Exception(
                    $"Python bridge không trả về JSON hợp lệ.\n" +
                    $"stdout: {stdout}\nstderr: {stderr}");

            var result = JsonSerializer.Deserialize<BridgeResult>(jsonLine)
                ?? throw new Exception("Python bridge trả về JSON không hợp lệ.");

            if (!string.IsNullOrEmpty(result.Error))
                throw new Exception($"Python bridge báo lỗi:\n{result.Error}");

            return result;
        }

        /// <summary>
        /// Tìm Python executable có cài torch hoặc ultralytics.
        /// </summary>
        private static string FindPythonWithTorch()
        {
            var candidates = new[]
            {
                @"C:\Users\khang\AppData\Local\Programs\Python\Python313\python.exe",
                @"C:\Python313\python.exe",
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                "python",
                "python3",
                "py",
            };

            foreach (var candidate in candidates)
            {
                if (!TryRunPython(candidate, "--version", out _))
                    continue;

                if (TryRunPython(candidate, "-c \"import ultralytics\"", out _))
                    return candidate;

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
                var outputTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(5000))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    return false;
                }
                output = outputTask.GetAwaiter().GetResult();
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
# bridge.py — MedVision AI Python Bridge
# Supports:
#   best.pt         -> YOLO (Ultralytics full model)
#   NSTChonglan.pth -> Mask R-CNN state_dict (torchvision)
#   BatThuongNST*   -> ResNet50 classifier state_dict (24 classes)
import sys, json, traceback, os, warnings
# Redirect ALL warnings to stderr so stdout stays clean JSON
warnings.filterwarnings('ignore')
os.environ['TF_CPP_MIN_LOG_LEVEL'] = '3'

# Suppress torchvision/torch print-to-stdout warnings
import logging
logging.disable(logging.WARNING)

def main():
    # Redirect stdout so libraries cannot pollute our JSON output
    import io
    real_stdout = sys.stdout
    sys.stdout = io.StringIO()   # absorb any stray prints from libs

    try:
        req = json.loads(sys.stdin.readline())
        model_path = req['model']
        image_path = req.get('image', '')
        image_paths = req.get('images', [])
        forced_type = req.get('model_type', '').strip()

        model_type = forced_type if forced_type else detect_model_type(model_path)
        if model_type == 'yolo' and not model_path.lower().endswith('.pt'):
            detected_type = detect_model_type(model_path)
            if detected_type != 'unknown':
                model_type = detected_type

        label = None
        predictions = None

        if model_type == 'yolo':
            boxes = run_yolo(model_path, image_path)
        elif model_type == 'maskrcnn':
            boxes = run_maskrcnn(model_path, image_path)
        elif model_type == 'resnet_classifier':
            boxes, label = run_resnet50_classifier(model_path, image_path)
        elif model_type == 'resnet_classifier_batch':
            boxes = []
            predictions = run_resnet50_classifier_batch(model_path, image_paths)
        elif model_type == 'keras':
            boxes = run_keras(model_path, image_path)
        else:
            raise ValueError(
                f'Cannot determine model type for: {model_path}\n'
                'Supported:\n'
                '  best.pt              -> YOLO (Ultralytics)\n'
                '  NSTChonglan.pth      -> Mask R-CNN state_dict\n'
                '  BatThuongNST*.pth    -> ResNet50 classifier (24 classes)\n'
                '  *.h5                 -> Keras/TensorFlow'
            )

        out = {'boxes': boxes, 'error': None}
        if label is not None:
            out['label'] = label
        if predictions is not None:
            out['predictions'] = predictions

        sys.stdout = real_stdout   # restore before printing result
        print(json.dumps(out))

    except Exception as e:
        sys.stdout = real_stdout
        print(json.dumps({'boxes': [], 'error': str(e) + chr(10) + traceback.format_exc()}))


def detect_model_type(model_path):
    """"""Nhan dien loai model dua tren ten file va extension""""""
    name = os.path.basename(model_path).lower()
    ext  = name.rsplit('.', 1)[-1] if '.' in name else ''

    # Uu tien nhan dien theo ten file dac biet
    if 'nstchonglan' in name or 'nst_chonglan' in name or 'chonglan' in name:
        return 'maskrcnn'

    if 'batthuong' in name or 'bat_thuong' in name or 'anomaly' in name:
        return 'resnet_classifier'

    # Fallback theo extension
    if ext == 'pt':
        return 'yolo'
    if ext == 'pth':
        return 'maskrcnn'  # pth mac dinh la Mask R-CNN neu khong khop ten
    if ext == 'h5':
        return 'keras'

    return 'unknown'


# --------------------------------------------------
# 1. YOLO (best.pt) -- Ultralytics full model
# --------------------------------------------------
def run_yolo(model_path, image_path):
    from ultralytics import YOLO
    import cv2

    if not model_path.lower().endswith('.pt'):
        raise ValueError(
            'YOLO/Ultralytics requires a .pt model. '
            f'Received {model_path}; use model_type maskrcnn for NSTChonglan.pth '
            'or resnet_classifier for BatThuongNST.pth.'
        )

    model = YOLO(model_path)
    img = cv2.imread(image_path)
    if img is None:
        raise ValueError(f'Cannot read image: {image_path}')
    h0, w0 = img.shape[:2]

    results = model(image_path, verbose=False, conf=0.25)
    boxes = []
    for r in results:
        if r.boxes is None:
            continue
        for box in r.boxes:
            x1, y1, x2, y2 = box.xyxy[0].tolist()
            conf = float(box.conf[0])
            boxes.append([x1/w0, y1/h0, x2/w0, y2/h0, conf])
    return boxes


# --------------------------------------------------
# 2. Mask R-CNN (NSTChonglan.pth) -- state_dict
# --------------------------------------------------
def run_maskrcnn(model_path, image_path):
    import torch
    import cv2
    import numpy as np
    from torchvision.models.detection import maskrcnn_resnet50_fpn
    from torchvision.models.detection.faster_rcnn import FastRCNNPredictor
    from torchvision.models.detection.mask_rcnn import MaskRCNNPredictor

    device = 'cuda' if torch.cuda.is_available() else 'cpu'

    # Xay dung skeleton Mask R-CNN voi 2 classes (background + chromosome)
    NUM_CLASSES = 2
    model = maskrcnn_resnet50_fpn(weights=None, weights_backbone=None)

    # Thay head phan loai
    in_features = model.roi_heads.box_predictor.cls_score.in_features
    model.roi_heads.box_predictor = FastRCNNPredictor(in_features, NUM_CLASSES)

    # Thay head mask
    in_features_mask = model.roi_heads.mask_predictor.conv5_mask.in_channels
    hidden_layer     = 256
    model.roi_heads.mask_predictor = MaskRCNNPredictor(
        in_features_mask, hidden_layer, NUM_CLASSES)

    # Load state_dict -- ho tro nhieu dinh dang luu
    state = torch.load(model_path, map_location=device, weights_only=False)
    if isinstance(state, dict):
        for key in ('model', 'state_dict', 'model_state_dict'):
            if key in state:
                state = state[key]
                break
    model.load_state_dict(state)
    model.to(device).eval()

    img_bgr = cv2.imread(image_path)
    if img_bgr is None:
        raise ValueError(f'Cannot read image: {image_path}')
    h0, w0 = img_bgr.shape[:2]

    # BGR -> RGB, normalize [0,1], them batch dim
    img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
    tensor  = torch.from_numpy(img_rgb.transpose(2, 0, 1)).unsqueeze(0).to(device)

    with torch.no_grad():
        outputs = model(tensor)

    boxes  = []
    out    = outputs[0]
    scores = out['scores'].cpu().numpy()
    bboxes = out['boxes'].cpu().numpy()  # [x1, y1, x2, y2] pixel coords

    CONF_THR = 0.25
    for i, score in enumerate(scores):
        if score < CONF_THR:
            continue
        x1, y1, x2, y2 = bboxes[i]
        boxes.append([
            float(x1) / w0,
            float(y1) / h0,
            float(x2) / w0,
            float(y2) / h0,
            float(score)
        ])

    return boxes


# --------------------------------------------------
# 3. ResNet50 Classifier (BatThuongNST*.pth)
#    state_dict, 24 classes
#    Classifier khong co toa do -- tra ve 1 box toan anh
# --------------------------------------------------
def run_resnet50_classifier(model_path, image_path):
    predictions = run_resnet50_classifier_batch(model_path, [image_path])
    if not predictions:
        raise ValueError(f'Cannot classify image: {image_path}')
    pred = predictions[0]
    return [[0.0, 0.0, 1.0, 1.0, pred['confidence']]], pred['label']


def run_resnet50_classifier_batch(model_path, image_paths):
    import torch
    import cv2
    import numpy as np
    from torchvision import transforms
    from torchvision.models import resnet50

    if not image_paths:
        return []

    device    = 'cuda' if torch.cuda.is_available() else 'cpu'
    NUM_CLASS = 24

    model = resnet50(weights=None)
    model.fc = torch.nn.Linear(model.fc.in_features, NUM_CLASS)

    state = torch.load(model_path, map_location=device, weights_only=False)
    if isinstance(state, dict):
        for key in ('model', 'state_dict', 'model_state_dict'):
            if key in state:
                state = state[key]
                break
    if isinstance(state, dict) and any(k.startswith('module.') for k in state.keys()):
        state = {k.replace('module.', '', 1): v for k, v in state.items()}
    model.load_state_dict(state)
    model.to(device).eval()

    tfm = transforms.Compose([
        transforms.ToPILImage(),
        transforms.Resize((224, 224)),
        transforms.ToTensor(),
        transforms.Normalize([0.485, 0.456, 0.406],
                             [0.229, 0.224, 0.225]),
    ])

    tensors = []
    valid_indices = []
    for idx, path in enumerate(image_paths):
        img_bgr = cv2.imread(path)
        if img_bgr is None:
            raise ValueError(f'Cannot read cropped image: {path}')
        img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)
        tensors.append(tfm(img_rgb))
        valid_indices.append(idx)

    if not tensors:
        return []

    batch = torch.stack(tensors).to(device)

    with torch.no_grad():
        logits = model(batch)
        probs = torch.softmax(logits, dim=1).cpu().numpy()

    # Mapping class index -> ten hoi chung (24 classes)
    CLASS_NAMES = [
        'Binh thuong (46,XX)', 'Binh thuong (46,XY)',
        'Trisomy 21 - Down', 'Trisomy 13 - Patau', 'Trisomy 18 - Edwards',
        'Monosomy X - Turner', '47,XXX', '47,XXY - Klinefelter', '47,XYY',
        'Del 5p - Cri du chat', 'Del 22q11 - DiGeorge', 'Del 15q - Prader-Willi/Angelman',
        'Triploidy', 'Tetraploidy', 'Mosaic Down', 'Mosaic Turner',
        'Inv dup 15', 'Ring chromosome', 'Marker chromosome',
        'Del 1p36', 'Dup 22q11', 'Lech boi khac',
        'Chuyen doan can bang', 'Chuyen doan khong can bang'
    ]

    predictions = []
    for row_idx, row in enumerate(probs):
        top_class = int(np.argmax(row))
        top_conf = float(row[top_class])
        label = CLASS_NAMES[top_class] if top_class < len(CLASS_NAMES) else f'Class {top_class}'
        predictions.append({
            'index': int(valid_indices[row_idx]),
            'label': label,
            'confidence': top_conf,
            'class_index': top_class
        })

    return predictions


# --------------------------------------------------
# 4. Keras / TensorFlow (.h5)
# --------------------------------------------------
def run_keras(model_path, image_path):
    import tensorflow as tf
    import cv2
    import numpy as np

    model   = tf.keras.models.load_model(model_path)
    img     = cv2.imread(image_path)
    if img is None:
        raise ValueError(f'Cannot read image: {image_path}')
    h0, w0  = img.shape[:2]
    inp_h   = model.input_shape[1] or 640
    inp_w   = model.input_shape[2] or 640
    resized = cv2.resize(img, (inp_w, inp_h))
    rgb     = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype('float32') / 255.0
    out     = model.predict(np.expand_dims(rgb, 0))
    return parse_output_numpy(np.squeeze(out), w0, h0)


def parse_output_numpy(arr, orig_w, orig_h, conf_thr=0.25):
    import numpy as np
    if arr.ndim == 1:
        return []
    if arr.ndim == 2:
        rows, cols = arr.shape
        arr = arr if cols >= 5 else arr.T
        boxes = []
        for row in arr:
            if len(row) < 5:
                continue
            cx, cy, bw, bh, conf = row[0], row[1], row[2], row[3], float(row[4])
            if conf < conf_thr:
                continue
            if cx > 1 or cy > 1:
                cx /= orig_w; cy /= orig_h; bw /= orig_w; bh /= orig_h
            x1 = max(0.0, cx - bw/2); y1 = max(0.0, cy - bh/2)
            x2 = min(1.0, cx + bw/2); y2 = min(1.0, cy + bh/2)
            if x2 > x1 and y2 > y1:
                boxes.append([x1, y1, x2, y2, conf])
        return boxes
    return []


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

            /// <summary>Tên nhãn class — chỉ có với resnet_classifier.</summary>
            [JsonPropertyName("label")]
            public string? Label { get; set; }

            [JsonPropertyName("predictions")]
            public List<AnomalyPrediction>? Predictions { get; set; }
        }
    }
}
