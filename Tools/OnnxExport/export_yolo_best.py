import argparse
from pathlib import Path

from ultralytics import YOLO


def main():
    parser = argparse.ArgumentParser(description="Export YOLO best.pt to ONNX.")
    parser.add_argument("--model", required=True, help="Path to best.pt")
    parser.add_argument("--output", required=True, help="Output .onnx path")
    parser.add_argument("--imgsz", type=int, default=640)
    parser.add_argument("--opset", type=int, default=12)
    args = parser.parse_args()

    model_path = Path(args.model)
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    model = YOLO(str(model_path))
    exported = model.export(
        format="onnx",
        imgsz=args.imgsz,
        opset=args.opset,
        simplify=True,
        dynamic=False,
    )

    exported_path = Path(exported)
    if exported_path.resolve() != output_path.resolve():
        output_path.write_bytes(exported_path.read_bytes())

    print(f"Exported YOLO ONNX: {output_path}")


if __name__ == "__main__":
    main()
