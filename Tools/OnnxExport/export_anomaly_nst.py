import argparse
import sys
from pathlib import Path

import torch
from torchvision.models import resnet50

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def build_model(num_classes=24):
    model = resnet50(weights=None)
    model.fc = torch.nn.Linear(model.fc.in_features, num_classes)
    return model


def load_state(path):
    state = torch.load(path, map_location="cpu", weights_only=False)
    if isinstance(state, dict):
        for key in ("model", "state_dict", "model_state_dict"):
            if key in state:
                state = state[key]
                break
    if isinstance(state, dict) and any(k.startswith("module.") for k in state):
        state = {k.replace("module.", "", 1): v for k, v in state.items()}
    return state


def main():
    parser = argparse.ArgumentParser(description="Export BatThuongNST ResNet50 to ONNX.")
    parser.add_argument("--model", required=True, help="Path to BatThuongNST.pth")
    parser.add_argument("--output", required=True, help="Output .onnx path")
    parser.add_argument("--opset", type=int, default=12)
    args = parser.parse_args()

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)

    model = build_model()
    model.load_state_dict(load_state(args.model))
    model.eval()

    dummy = torch.randn(1, 3, 224, 224)
    torch.onnx.export(
        model,
        dummy,
        str(output),
        input_names=["input"],
        output_names=["logits"],
        opset_version=args.opset,
        dynamic_axes={"input": {0: "batch"}, "logits": {0: "batch"}},
        dynamo=False,
    )
    print(f"Exported BatThuongNST ONNX: {output}")


if __name__ == "__main__":
    main()
