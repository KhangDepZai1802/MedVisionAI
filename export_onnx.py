"""
Compatibility wrapper for the old MedVisionAI export script.

Use the dedicated scripts in Tools/OnnxExport instead:

  python Tools/OnnxExport/export_yolo_best.py --model best.pt --output best.onnx
  python Tools/OnnxExport/export_blood_cancer.py --model best_BloodCancerNET.pth --output best_BloodCancerNET.onnx
  python Tools/OnnxExport/export_malaria.py --model best_MalariaNET.pth --output best_MalariaNET.onnx
  python Tools/OnnxExport/export_anomaly_nst.py --model BatThuongNST.pth --output BatThuongNST.onnx
  python Tools/OnnxExport/export_overlap_maskrcnn.py --model NSTChonglan.pth --output NSTChonglan.onnx
"""

from pathlib import Path


def main():
    guide = Path("Tools/OnnxExport/README_ONNX.md")
    print("MedVisionAI ONNX export scripts are in Tools/OnnxExport.")
    if guide.exists():
        print(f"Read: {guide}")


if __name__ == "__main__":
    main()
