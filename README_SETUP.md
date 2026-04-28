# MedVision AI — Hướng dẫn Setup

## Yêu cầu hệ thống
- **Windows 10/11** (64-bit)
- **Visual Studio 2022** (Community free) với workload **.NET Desktop Development**
- **.NET 8 SDK** (tự động cài cùng VS 2022)
- **Python 3.9+** (chỉ cần nếu dùng model `.pt`/`.pth`/`.h5`)

---

## Bước 1 — Tải Visual Studio 2022

1. Vào https://visualstudio.microsoft.com/vs/community/
2. Cài với workload: **".NET desktop development"**
3. Đảm bảo tick: **.NET 8.0 Runtime**

---

## Bước 2 — Mở project

```
File → Open → Project/Solution → chọn MedVisionAI.csproj
```

Visual Studio sẽ tự động restore NuGet packages lần đầu (~2-5 phút):
- `Microsoft.ML.OnnxRuntime` — chạy model .onnx
- `OpenCvSharp4.Windows`     — xử lý ảnh
- `Microsoft.Web.WebView2`   — hiển thị báo cáo HTML (sau)

---

## Bước 3 — Chạy app

Nhấn **F5** hoặc nút ▶ **Run** (chọn `MedVisionAI` profile).

---

## Bước 4 — Test với model demo

Khi mở module NST, nhấn **"Bỏ qua"** để vào giao diện không cần model.
App sẽ tạo 46 NST giả để test UI đầy đủ.

---

## Bước 5 — Test với model ONNX thật

### Tự xuất model ONNX từ YOLO (bạn đóng vai "bên thứ 3"):

```python
# Nếu có model YOLO của bạn (best.pt từ project Python cũ):
from ultralytics import YOLO
model = YOLO("models/best.pt")
model.export(format="onnx", imgsz=640, opset=11)
# → tạo ra best.onnx → dùng file này trong app C#
```

### Hoặc dùng model ONNX public để test:
- YOLOv8n: https://github.com/ultralytics/assets/releases/download/v8.2.0/yolov8n.onnx

---

## Cấu trúc project

```
MedVisionAI/
├── App.xaml / App.xaml.cs          — Entry point
├── UI/
│   ├── Controls/
│   │   ├── KrakenTheme.xaml        — Toàn bộ design system
│   │   └── ModuleCard.xaml/.cs     — Module card có animation
│   └── Windows/
│       ├── HomeWindow.xaml/.cs     — Trang chủ
│       ├── ModelSelectDialog.xaml/.cs  — Popup chọn model (Yêu cầu [4])
│       ├── NSTWindow.xaml/.cs      — Màn hình phân tích NST
│       ├── SettingsDialog.xaml/.cs — Cài đặt Browse (Yêu cầu [3])
├── Models/
│   ├── ModelConfig.cs              — Cấu hình model AI
│   └── AnalysisResult.cs           — Kết quả phân tích
└── Services/
    ├── ModelFormat.cs              — Phát hiện định dạng model
    ├── OnnxInferenceEngine.cs      — Chạy ONNX native trong C#
    ├── OutputParser.cs             — Parse output tensor (tự thích nghi)
    ├── ChromosomeAnalyzer.cs       — Denver grouping + syndrome detection
    ├── ImageAnnotator.cs           — Vẽ kết quả lên ảnh
    ├── InferenceService.cs         — Điều phối toàn bộ pipeline
    └── PythonBridge.cs             — Bridge cho .pt/.pth/.h5
```

---

## Publish thành .exe

```
Build → Publish → Folder
Profile: FolderProfile
Target Runtime: win-x64
Deployment mode: Self-contained
```

Kết quả: một file `MedVisionAI.exe` duy nhất, chạy được trên mọi máy Windows.

---

## Lưu ý về model

| Định dạng | Runtime | Cần Python? |
|-----------|---------|-------------|
| `.onnx`   | ONNX Runtime (C# native) | ❌ Không |
| `.pt/.pth` | Python Bridge | ✅ Cần Python + torch |
| `.h5`     | Python Bridge | ✅ Cần Python + tensorflow |

**Khuyến nghị:** Yêu cầu "bên thứ 3" xuất model sang `.onnx` — mọi framework đều hỗ trợ.
