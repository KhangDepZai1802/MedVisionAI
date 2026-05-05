import argparse
import sys
from pathlib import Path

import torch
from torchvision.models.detection import maskrcnn_resnet50_fpn
from torchvision.models.detection.faster_rcnn import FastRCNNPredictor
from torchvision.models.detection.mask_rcnn import MaskRCNNPredictor

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


class MaskRcnnWrapper(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, images):
        out = self.model([images[0]])[0]
        return out["boxes"], out["labels"], out["scores"]


def build_model(num_classes=2):
    model = maskrcnn_resnet50_fpn(weights=None, weights_backbone=None)
    in_features = model.roi_heads.box_predictor.cls_score.in_features
    model.roi_heads.box_predictor = FastRCNNPredictor(in_features, num_classes)
    in_features_mask = model.roi_heads.mask_predictor.conv5_mask.in_channels
    model.roi_heads.mask_predictor = MaskRCNNPredictor(
        in_features_mask, 256, num_classes)
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
    parser = argparse.ArgumentParser(description="Export NSTChonglan Mask R-CNN to ONNX.")
    parser.add_argument("--model", required=True, help="Path to NSTChonglan.pth")
    parser.add_argument("--output", required=True, help="Output .onnx path")
    parser.add_argument("--imgsz", type=int, default=640)
    parser.add_argument("--opset", type=int, default=12)
    args = parser.parse_args()

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)

    model = build_model()
    model.load_state_dict(load_state(args.model))
    model.eval()
    wrapped = MaskRcnnWrapper(model).eval()

    dummy = torch.rand(1, 3, args.imgsz, args.imgsz)
    torch.onnx.export(
        wrapped,
        dummy,
        str(output),
        input_names=["input"],
        output_names=["boxes", "labels", "scores"],
        opset_version=args.opset,
        dynamic_axes={
            "boxes": {0: "num_detections"},
            "labels": {0: "num_detections"},
            "scores": {0: "num_detections"},
        },
        dynamo=False,
    )
    print(f"Exported NSTChonglan Mask R-CNN ONNX: {output}")


if __name__ == "__main__":
    main()
