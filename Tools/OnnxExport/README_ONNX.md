# MedVisionAI ONNX Export

These scripts convert the current PyTorch/Ultralytics checkpoints to ONNX so
the deployed app can run with ONNX Runtime without requiring Python on the
customer machine.

Run these commands on the development machine that has Python, PyTorch,
torchvision, ultralytics, and onnx installed.

```powershell
pip install torch torchvision ultralytics onnx onnxsim
```

## Export commands

```powershell
python Tools\OnnxExport\export_yolo_best.py --model "path\to\best.pt" --output "ModelsRuntime\best.onnx"
python Tools\OnnxExport\export_blood_cancer.py --model "path\to\best_BloodCancerNET.pth" --output "ModelsRuntime\best_BloodCancerNET.onnx"
python Tools\OnnxExport\export_malaria.py --model "path\to\best_MalariaNET.pth" --output "ModelsRuntime\best_MalariaNET.onnx"
python Tools\OnnxExport\export_anomaly_nst.py --model "path\to\BatThuongNST.pth" --output "ModelsRuntime\BatThuongNST.onnx"
python Tools\OnnxExport\export_overlap_maskrcnn.py --model "path\to\NSTChonglan.pth" --output "ModelsRuntime\NSTChonglan.onnx"
```

## Class order used by the app

- Malaria: `Parasitized`, `Uninfected`
- Blood cancer: `Benign`, `Early`, `Pre`, `Pro`
- Anomaly NST: the 24-class order currently used by `PythonBridge`

Keep the same class order when retraining/exporting, otherwise labels in the
app will be wrong even if the ONNX model runs correctly.
