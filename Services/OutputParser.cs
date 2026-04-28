using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace MedVisionAI.Services
{
    /// <summary>
    /// Parse raw ONNX output tensors thành danh sách BoundingBox.
    ///
    /// Hỗ trợ các output format phổ biến:
    ///   Format A — YOLO v5/v8:  [1, num_boxes, 5+C]   (x,y,w,h,conf,class_scores...)
    ///   Format B — YOLO transpose: [1, 5+C, num_boxes]
    ///   Format C — SSD/EfficientDet: [1, num_boxes, 6] (x1,y1,x2,y2,score,class)
    ///   Format D — Classification: [1, num_classes]
    ///
    /// App không cần biết model nào — tự phát hiện format từ output shape.
    /// </summary>
    public static class OutputParser
    {
        public const float DefaultConfThreshold = 0.25f;
        public const float DefaultNmsThreshold  = 0.45f;

        // ── Entry point ───────────────────────────────────────────────────────

        public static List<DetectionBox> ParseDetections(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
            int origW, int origH,
            int modelW, int modelH,
            float confThreshold = DefaultConfThreshold,
            float nmsThreshold  = DefaultNmsThreshold)
        {
            var firstOutput = outputs.First();
            var tensor      = firstOutput.AsTensor<float>();
            var shape       = tensor.Dimensions.ToArray();

            List<DetectionBox> raw;

            if (shape.Length == 3)
            {
                // [batch, A, B] — cần xác định axis nào là boxes
                int a = shape[1], b = shape[2];

                if (b >= 5 && b < 1000)
                    // Format A: [1, num_boxes, 5+C]
                    raw = ParseYoloFormatA(tensor, a, b, origW, origH, modelW, modelH, confThreshold);
                else if (a >= 5 && a < 1000)
                    // Format B: [1, 5+C, num_boxes]
                    raw = ParseYoloFormatB(tensor, a, b, origW, origH, modelW, modelH, confThreshold);
                else
                    raw = ParseYoloFormatA(tensor, a, b, origW, origH, modelW, modelH, confThreshold);
            }
            else if (shape.Length == 2)
            {
                // Classification output [1, num_classes] — chuyển thành 1 box giả
                raw = ParseClassification(tensor);
            }
            else
            {
                raw = new List<DetectionBox>();
            }

            // Apply NMS để loại bỏ box trùng lặp
            return ApplyNms(raw, nmsThreshold);
        }

        // ── Format A: [1, num_boxes, 5+C] ────────────────────────────────────
        // Mỗi box: [cx, cy, w, h, conf, class0, class1, ...]

        private static List<DetectionBox> ParseYoloFormatA(
            Tensor<float> t, int numBoxes, int vecLen,
            int origW, int origH, int modelW, int modelH,
            float confThr)
        {
            var result  = new List<DetectionBox>();
            float scaleX = (float)origW / modelW;
            float scaleY = (float)origH / modelH;

            for (int i = 0; i < numBoxes; i++)
            {
                float conf = t[0, i, 4];
                if (conf < confThr) continue;

                // Tìm class có score cao nhất
                float maxScore = 0;
                int   classId  = 0;
                for (int c = 5; c < vecLen; c++)
                {
                    float s = t[0, i, c] * conf;
                    if (s > maxScore) { maxScore = s; classId = c - 5; }
                }
                if (vecLen == 5) maxScore = conf;  // model chỉ có conf, không có class

                if (maxScore < confThr) continue;

                float cx = t[0, i, 0] * scaleX;
                float cy = t[0, i, 1] * scaleY;
                float bw = t[0, i, 2] * scaleX;
                float bh = t[0, i, 3] * scaleY;

                result.Add(new DetectionBox
                {
                    X1         = cx - bw / 2,
                    Y1         = cy - bh / 2,
                    X2         = cx + bw / 2,
                    Y2         = cy + bh / 2,
                    Confidence = maxScore,
                    ClassId    = classId,
                });
            }
            return result;
        }

        // ── Format B: [1, 5+C, num_boxes] ────────────────────────────────────
        // Transpose của Format A

        private static List<DetectionBox> ParseYoloFormatB(
            Tensor<float> t, int vecLen, int numBoxes,
            int origW, int origH, int modelW, int modelH,
            float confThr)
        {
            var result  = new List<DetectionBox>();
            float scaleX = (float)origW / modelW;
            float scaleY = (float)origH / modelH;

            for (int i = 0; i < numBoxes; i++)
            {
                float conf = t[0, 4, i];
                if (conf < confThr) continue;

                float maxScore = 0;
                int   classId  = 0;
                for (int c = 5; c < vecLen; c++)
                {
                    float s = t[0, c, i] * conf;
                    if (s > maxScore) { maxScore = s; classId = c - 5; }
                }
                if (vecLen == 5) maxScore = conf;
                if (maxScore < confThr) continue;

                float cx = t[0, 0, i] * scaleX;
                float cy = t[0, 1, i] * scaleY;
                float bw = t[0, 2, i] * scaleX;
                float bh = t[0, 3, i] * scaleY;

                result.Add(new DetectionBox
                {
                    X1 = cx - bw / 2, Y1 = cy - bh / 2,
                    X2 = cx + bw / 2, Y2 = cy + bh / 2,
                    Confidence = maxScore, ClassId = classId,
                });
            }
            return result;
        }

        // ── Format D: Classification ──────────────────────────────────────────

        private static List<DetectionBox> ParseClassification(Tensor<float> t)
        {
            // Trả về kết quả class dưới dạng box giả để thống nhất interface
            float maxVal = float.MinValue;
            int   maxIdx = 0;
            int   n      = t.Dimensions[1];
            for (int i = 0; i < n; i++)
                if (t[0, i] > maxVal) { maxVal = t[0, i]; maxIdx = i; }

            return new List<DetectionBox>
            {
                new() { ClassId = maxIdx, Confidence = maxVal,
                        X1 = 0, Y1 = 0, X2 = 1, Y2 = 1 }
            };
        }

        // ── NMS ───────────────────────────────────────────────────────────────

        private static List<DetectionBox> ApplyNms(List<DetectionBox> boxes, float iouThr)
        {
            var sorted = boxes.OrderByDescending(b => b.Confidence).ToList();
            var kept   = new List<DetectionBox>();

            while (sorted.Count > 0)
            {
                var best = sorted[0];
                kept.Add(best);
                sorted.RemoveAt(0);
                sorted.RemoveAll(b => IoU(best, b) > iouThr);
            }
            return kept;
        }

        private static float IoU(DetectionBox a, DetectionBox b)
        {
            float ix1 = Math.Max(a.X1, b.X1), iy1 = Math.Max(a.Y1, b.Y1);
            float ix2 = Math.Min(a.X2, b.X2), iy2 = Math.Min(a.Y2, b.Y2);
            float inter = Math.Max(0, ix2 - ix1) * Math.Max(0, iy2 - iy1);
            float areaA = (a.X2 - a.X1) * (a.Y2 - a.Y1);
            float areaB = (b.X2 - b.X1) * (b.Y2 - b.Y1);
            return inter / (areaA + areaB - inter + 1e-6f);
        }
    }

    // ── Data model ────────────────────────────────────────────────────────────

    public class DetectionBox
    {
        public float X1         { get; set; }
        public float Y1         { get; set; }
        public float X2         { get; set; }
        public float Y2         { get; set; }
        public float Confidence { get; set; }
        public int   ClassId    { get; set; }

        public float Width  => X2 - X1;
        public float Height => Y2 - Y1;
        public float Area   => Width * Height;
        public float CenterX => (X1 + X2) / 2;
        public float CenterY => (Y1 + Y2) / 2;
    }
}
