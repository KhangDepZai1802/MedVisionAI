using OpenCvSharp;
using MedVisionAI.Models;
using System.IO;

namespace MedVisionAI.Services
{
    /// <summary>
    /// Điều phối toàn bộ pipeline phân tích NST:
    ///   1. Phát hiện định dạng model
    ///   2. Chạy inference (ONNX native hoặc Python bridge)
    ///   3. Parse output → danh sách bounding box
    ///   4. Phân nhóm Denver + phát hiện hội chứng
    ///   5. Annotate ảnh kết quả
    ///   6. Build báo cáo cho bác sĩ
    ///
    /// App không giả định model được train như thế nào —
    /// tự thích nghi với output shape.
    /// </summary>
    public class InferenceService
    {
        private readonly ModelConfig _config;

        public InferenceService(ModelConfig config)
        {
            _config = config;
        }

        // ── Main pipeline ─────────────────────────────────────────────────────

        public AnalysisResult Analyze(string imagePath)
        {
            // Bước 1: Đọc ảnh
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty())
                throw new Exception($"Không đọc được ảnh: {imagePath}");

            int origW = img.Cols, origH = img.Rows;

            // Bước 2: Chạy model phân đoạn (bắt buộc)
            List<DetectionBox> boxes;

            if (string.IsNullOrEmpty(_config.SegmentationModelPath))
            {
                // Demo mode — không có model: trả về kết quả giả để test UI
                boxes = GenerateDemoBoxes(origW, origH);
            }
            else
            {
                var format = ModelFormatDetector.Detect(_config.SegmentationModelPath);
                boxes = format switch
                {
                    ModelFormat.Onnx    => RunOnnx(_config.SegmentationModelPath, img, origW, origH),
                    ModelFormat.PyTorch => RunPythonBridge(_config.SegmentationModelPath, imagePath, origW, origH),
                    ModelFormat.Keras   => RunPythonBridge(_config.SegmentationModelPath, imagePath, origW, origH),
                    _ => throw new NotSupportedException(
                        $"Định dạng model chưa được hỗ trợ: {Path.GetExtension(_config.SegmentationModelPath)}\n" +
                        "Vui lòng xuất model sang .onnx để có hiệu suất tốt nhất.")
                };
            }

            // Bước 3: Phân tích lâm sàng
            var analysis = ChromosomeAnalyzer.Analyze(
                boxes,
                _config.NormalChromosomeCount,
                _config.Tolerance);

            // Bước 4: Annotate ảnh
            byte[] annotatedBytes = ImageAnnotator.Annotate(imagePath, boxes, analysis);

            // Bước 5: Build báo cáo
            string reportPlain = ChromosomeAnalyzer.BuildPlainReport(analysis);

            return new AnalysisResult
            {
                ChromosomeCount    = analysis.TotalCount,
                IsNormalCount      = analysis.IsNormalCount,
                RiskLevel          = analysis.RiskLevel,
                DenverGroups       = analysis.DenverGroups,
                SexEstimation      = analysis.SexEstimation,
                SexConfidence      = analysis.SexConfidence,
                SyndromeFlags      = analysis.SyndromeFlags,
                BoundingBoxes      = boxes.Select(b => new[] { b.X1, b.Y1, b.X2, b.Y2, b.Confidence }).ToList(),
                NormalizedAreas    = analysis.NormalizedAreas,
                AnnotatedImageBytes = annotatedBytes,
                ReportPlain        = reportPlain,
                MeanArea           = analysis.MeanArea,
                StdArea            = analysis.StdArea,
                CvPercent          = analysis.CvPercent,
            };
        }

        // ── ONNX inference (native C#) ────────────────────────────────────────

        private static List<DetectionBox> RunOnnx(
            string modelPath, Mat img, int origW, int origH)
        {
            using var engine = new OnnxInferenceEngine(modelPath);
            using var outputs = engine.Run(img);

            // Model W/H từ engine (default 640x640 nếu dynamic)
            int modelW = 640, modelH = 640;

            return OutputParser.ParseDetections(
                outputs, origW, origH, modelW, modelH);
        }

        // ── Python Bridge (cho .pt / .pth / .h5) ─────────────────────────────

        private static List<DetectionBox> RunPythonBridge(
            string modelPath, string imagePath, int origW, int origH)
        {
            var bridge  = new PythonBridge();
            var rawBoxes = bridge.RunInference(modelPath, imagePath);

            // Bridge trả về [[x1,y1,x2,y2,conf], ...]
            return rawBoxes.Select(r => new DetectionBox
            {
                X1         = r[0] * origW,
                Y1         = r[1] * origH,
                X2         = r[2] * origW,
                Y2         = r[3] * origH,
                Confidence = r[4],
            }).ToList();
        }

        // ── Demo boxes (không có model, test UI) ─────────────────────────────

        private static List<DetectionBox> GenerateDemoBoxes(int w, int h)
        {
            var rng   = new Random(42);
            var boxes = new List<DetectionBox>();
            int count = 46; // demo: 46 NST bình thường

            for (int i = 0; i < count; i++)
            {
                float bw = rng.NextSingle() * 0.06f + 0.02f;
                float bh = bw * (rng.NextSingle() * 0.5f + 1.5f);
                float cx = rng.NextSingle() * (1 - bw) + bw / 2;
                float cy = rng.NextSingle() * (1 - bh) + bh / 2;

                boxes.Add(new DetectionBox
                {
                    X1         = (cx - bw / 2) * w,
                    Y1         = (cy - bh / 2) * h,
                    X2         = (cx + bw / 2) * w,
                    Y2         = (cy + bh / 2) * h,
                    Confidence = rng.NextSingle() * 0.3f + 0.7f,
                });
            }
            return boxes;
        }
    }
}
