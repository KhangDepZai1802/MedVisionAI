using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using OpenCvSharp;
using MedVisionAI.Models;

namespace MedVisionAI.Services
{
    public class InferenceService
    {
        private readonly ModelConfig _config;

        public InferenceService(ModelConfig config)
        {
            _config = config;
        }

        public AnalysisResult Analyze(string imagePath)
        {
            string workPath = EnsureAscii(imagePath, out string? tempImg);

            try
            {
                using var img = Cv2.ImRead(workPath, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                int origW = img.Cols, origH = img.Rows;
                List<DetectionBox> boxes;

                bool hasSegmentationModel =
                    !string.IsNullOrWhiteSpace(_config.SegmentationModelPath);
                bool hasOverlapModel =
                    !string.IsNullOrWhiteSpace(_config.OverlapModelPath);

                // ── BƯỚC 1: Phân đoạn NST (best.pt / YOLO hoặc ONNX) ──────────
                if (!hasSegmentationModel)
                {
                    // Nếu không có model phân đoạn nhưng có model chồng lấn,
                    // tiếp tục với danh sách rỗng rồi chạy bước chồng lấn.
                    if (hasOverlapModel)
                    {
                        boxes = new List<DetectionBox>();
                    }
                    else
                    {
                        throw new Exception(
                            "Mô hình phân đoạn NST chưa được chọn. Vui lòng chọn file model trước khi phân tích.");
                    }
                }
                else
                {
                    string segmentationModelPath = _config.SegmentationModelPath!;
                    string modelWork = EnsureAscii(
                        segmentationModelPath, out string? tempModel);
                    try
                    {
                        var fmt = ModelFormatDetector.Detect(segmentationModelPath);
                        boxes = fmt switch
                        {
                            ModelFormat.Onnx => RunOnnx(modelWork, img, origW, origH),
                            // PyTorch models are not all YOLO: best.pt is YOLO,
                            // NSTChonglan.pth is Mask R-CNN, BatThuongNST.pth is a classifier.
                            ModelFormat.PyTorch => RunPythonBridge(
                                segmentationModelPath, workPath,
                                origW, origH,
                                DetectPythonModelType(segmentationModelPath)),
                            ModelFormat.Keras => RunPythonBridge(
                                segmentationModelPath, workPath,
                                origW, origH, "keras"),
                            _ => throw new NotSupportedException(
                                $"Unsupported format: " +
                                $"{Path.GetExtension(segmentationModelPath)}")
                        };
                    }
                    finally
                    {
                        if (tempModel != null && File.Exists(tempModel))
                            File.Delete(tempModel);
                    }
                }

                // ── BƯỚC 2: NST chồng lấn (NSTChonglan.pth / Mask R-CNN) ───────
                // Nếu có model chồng lấn → chạy thêm để bổ sung / lọc lại boxes
                if (hasOverlapModel)
                {
                    try
                    {
                        var overlapBoxes =
                            ModelFormatDetector.Detect(_config.OverlapModelPath) == ModelFormat.Onnx
                                ? RunOnnxMaskRcnn(_config.OverlapModelPath!, workPath, origW, origH)
                                : RunPythonBridge(
                                    _config.OverlapModelPath!, workPath,
                                    origW, origH, "maskrcnn");

                        // Nếu Mask R-CNN detect được nhiều box hơn → dùng kết quả đó
                        if (!hasSegmentationModel || overlapBoxes.Count > boxes.Count)
                            boxes = overlapBoxes;
                    }
                    catch (Exception ex)
                    {
                        if (!hasSegmentationModel)
                            throw;

                        // Không crash toàn bộ pipeline nếu model chồng lấn lỗi
                        System.Diagnostics.Debug.WriteLine(
                            $"[OverlapModel] Bỏ qua lỗi: {ex.Message}");
                    }
                }

                // ── BƯỚC 3: Phân tích lâm sàng ──────────────────────────────────
                var analysis = ChromosomeAnalyzer.Analyze(
                    boxes, _config.NormalChromosomeCount, _config.Tolerance);

                byte[] annotated = ImageAnnotator.Annotate(imagePath, boxes, analysis);
                string report    = ChromosomeAnalyzer.BuildPlainReport(analysis);

                return new AnalysisResult
                {
                    ChromosomeCount     = analysis.TotalCount,
                    IsNormalCount       = analysis.IsNormalCount,
                    RiskLevel           = analysis.RiskLevel,
                    DenverGroups        = analysis.DenverGroups,
                    SexEstimation       = analysis.SexEstimation,
                    SexConfidence       = analysis.SexConfidence,
                    SyndromeFlags       = analysis.SyndromeFlags,
                    BoundingBoxes       = boxes.Select(b =>
                        new[] { b.X1, b.Y1, b.X2, b.Y2, b.Confidence }).ToList(),
                    NormalizedAreas     = analysis.NormalizedAreas,
                    AnnotatedImageBytes = annotated,
                    ReportPlain         = report,
                    MeanArea            = analysis.MeanArea,
                    StdArea             = analysis.StdArea,
                    CvPercent           = analysis.CvPercent,
                };
            }
            finally
            {
                if (tempImg != null && File.Exists(tempImg))
                    File.Delete(tempImg);
            }
        }

        public AnalysisResult AnalyzeAnomaliesFromSegmentation(
            string imagePath, AnalysisResult segmentationResult, string anomalyModelPath)
        {
            if (segmentationResult.BoundingBoxes.Count == 0)
                throw new InvalidOperationException(
                    "Chưa có bounding box từ bước phân đoạn NST để crop ảnh.");

            string workPath = EnsureAscii(imagePath, out string? tempImg);
            string modelWork = EnsureAscii(anomalyModelPath, out string? tempModel);
            string cropDir = Path.Combine(Path.GetTempPath(), $"mv_crops_{Guid.NewGuid():N}");
            Directory.CreateDirectory(cropDir);

            try
            {
                var crops = CropChromosomes(workPath, segmentationResult.BoundingBoxes, cropDir);
                if (crops.Count == 0)
                    throw new InvalidOperationException("Không crop được NST hợp lệ từ kết quả phân đoạn.");

                List<AnomalyPrediction> rawPredictions;
                var cropPaths = crops.Select(c => c.Path).ToList();
                if (ModelFormatDetector.Detect(anomalyModelPath) == ModelFormat.Onnx)
                {
                    var onnx = new OnnxClassifierService(
                        modelWork, OnnxClassifierService.AnomalyClassNames);
                    rawPredictions = onnx.PredictBatch(cropPaths);
                }
                else
                {
                    var bridge = new PythonBridge();
                    rawPredictions = bridge.RunClassifierBatch(modelWork, cropPaths);
                }

                var mappedPredictions = new List<AnomalyPrediction>();
                foreach (var pred in rawPredictions.OrderBy(p => p.Index))
                {
                    if (pred.Index < 0 || pred.Index >= crops.Count)
                        continue;

                    int boxIndex = crops[pred.Index].BoxIndex;
                    mappedPredictions.Add(new AnomalyPrediction
                    {
                        Index = boxIndex + 1,
                        Label = pred.Label,
                        Confidence = pred.Confidence,
                        ClassIndex = pred.ClassIndex,
                        BoundingBox = segmentationResult.BoundingBoxes[boxIndex].ToArray(),
                    });
                }

                var result = CloneResult(segmentationResult);
                result.AnomalyPredictions = mappedPredictions;
                ApplyAnomalySummary(result);
                result.ReportPlain = BuildAnomalyReport(segmentationResult.ReportPlain, result);
                result.AnnotatedImageBytes = ImageAnnotator.AnnotateAnomalies(imagePath, result);
                return result;
            }
            finally
            {
                if (tempImg != null && File.Exists(tempImg))
                    File.Delete(tempImg);
                if (tempModel != null && File.Exists(tempModel))
                    File.Delete(tempModel);
                if (Directory.Exists(cropDir))
                    Directory.Delete(cropDir, recursive: true);
            }
        }

        // ── ONNX pipeline (không thay đổi) ──────────────────────────────────────
        private static List<DetectionBox> RunOnnx(
            string modelPath, Mat img, int origW, int origH)
        {
            using var engine  = new OnnxInferenceEngine(
                modelPath, OnnxPreprocessMode.UnitScale);
            using var outputs = engine.Run(img);
            return OutputParser.ParseDetections(
                outputs, origW, origH, engine.InputWidth, engine.InputHeight);
        }

        private static List<DetectionBox> RunOnnxMaskRcnn(
            string modelPath, string imagePath, int origW, int origH)
        {
            string modelWork = EnsureAscii(modelPath, out string? tempModel);
            string imageWork = EnsureAscii(imagePath, out string? tempImage);

            try
            {
                using var img = Cv2.ImRead(imageWork, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                using var engine = new OnnxInferenceEngine(
                    modelWork, OnnxPreprocessMode.UnitScale);
                using var outputs = engine.Run(img);
                return OutputParser.ParseMaskRcnn(
                    outputs, origW, origH, engine.InputWidth, engine.InputHeight);
            }
            finally
            {
                if (tempModel != null && File.Exists(tempModel))
                    File.Delete(tempModel);
                if (tempImage != null && File.Exists(tempImage))
                    File.Delete(tempImage);
            }
        }

        // ── Python Bridge — có thêm modelType ────────────────────────────────────
        // modelType: "yolo" | "maskrcnn" | "keras"
        private static List<DetectionBox> RunPythonBridge(
            string modelPath, string imagePath,
            int origW, int origH,
            string modelType = "")
        {
            string modelWork = EnsureAscii(modelPath, out string? tempModel);
            string imageWork = EnsureAscii(imagePath, out string? tempImage);

            try
            {
                var bridge   = new PythonBridge();
                var rawBoxes = bridge.RunInference(modelWork, imageWork, modelType);
                return rawBoxes.Select(r => new DetectionBox
                {
                    X1         = r[0] * origW,
                    Y1         = r[1] * origH,
                    X2         = r[2] * origW,
                    Y2         = r[3] * origH,
                    Confidence = r[4],
                }).ToList();
            }
            finally
            {
                if (tempModel != null && File.Exists(tempModel))
                    File.Delete(tempModel);
                if (tempImage != null && File.Exists(tempImage))
                    File.Delete(tempImage);
            }
        }

        private static string DetectPythonModelType(string modelPath)
        {
            var name = Path.GetFileName(modelPath).ToLowerInvariant();
            var ext  = Path.GetExtension(modelPath).ToLowerInvariant();

            if (name.Contains("nstchonglan") ||
                name.Contains("nst_chonglan") ||
                name.Contains("chonglan"))
                return "maskrcnn";

            if (name.Contains("batthuong") ||
                name.Contains("bat_thuong") ||
                name.Contains("anomaly"))
                return "resnet_classifier";

            return ext switch
            {
                ".pt"  => "yolo",
                ".pth" => "maskrcnn",
                ".h5"  => "keras",
                _      => "",
            };
        }

        private static List<CropInfo> CropChromosomes(
            string imagePath, List<float[]> boxes, string outputDir)
        {
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty())
                throw new Exception($"Cannot read image: {imagePath}");

            var crops = new List<CropInfo>();
            int w = img.Cols, h = img.Rows;

            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b.Length < 4)
                    continue;

                int x1 = Math.Clamp((int)Math.Floor(Math.Min(b[0], b[2])), 0, w - 1);
                int y1 = Math.Clamp((int)Math.Floor(Math.Min(b[1], b[3])), 0, h - 1);
                int x2 = Math.Clamp((int)Math.Ceiling(Math.Max(b[0], b[2])), 0, w - 1);
                int y2 = Math.Clamp((int)Math.Ceiling(Math.Max(b[1], b[3])), 0, h - 1);

                int bw = x2 - x1;
                int bh = y2 - y1;
                if (bw < 2 || bh < 2)
                    continue;

                int pad = Math.Max(6, (int)(Math.Max(bw, bh) * 0.08f));
                x1 = Math.Clamp(x1 - pad, 0, w - 1);
                y1 = Math.Clamp(y1 - pad, 0, h - 1);
                x2 = Math.Clamp(x2 + pad, 0, w - 1);
                y2 = Math.Clamp(y2 + pad, 0, h - 1);

                int cropW = x2 - x1;
                int cropH = y2 - y1;
                if (cropW < 2 || cropH < 2)
                    continue;

                using var crop = new Mat(img, new Rect(x1, y1, cropW, cropH)).Clone();
                string cropPath = Path.Combine(outputDir, $"crop_{i + 1:D3}.png");
                Cv2.ImWrite(cropPath, crop);
                crops.Add(new CropInfo(cropPath, i));
            }

            return crops;
        }

        private static AnalysisResult CloneResult(AnalysisResult source)
            => new()
            {
                ChromosomeCount = source.ChromosomeCount,
                IsNormalCount = source.IsNormalCount,
                RiskLevel = source.RiskLevel,
                DenverGroups = new Dictionary<string, int>(source.DenverGroups),
                SexEstimation = source.SexEstimation,
                SexConfidence = source.SexConfidence,
                SyndromeFlags = new List<string>(source.SyndromeFlags),
                BoundingBoxes = source.BoundingBoxes.Select(b => b.ToArray()).ToList(),
                NormalizedAreas = new List<float>(source.NormalizedAreas),
                AnnotatedImageBytes = source.AnnotatedImageBytes,
                ReportPlain = source.ReportPlain,
                ReportHtml = source.ReportHtml,
                MeanArea = source.MeanArea,
                StdArea = source.StdArea,
                CvPercent = source.CvPercent,
            };

        private static void ApplyAnomalySummary(AnalysisResult result)
        {
            var abnormal = result.AnomalyPredictions
                .Where(p => !p.IsNormal)
                .ToList();

            if (abnormal.Count == 0)
                return;

            float maxConf = abnormal.Max(p => p.Confidence);
            result.RiskLevel = maxConf >= 0.75f ? "Nguy cơ cao" : "Cần theo dõi";

            foreach (var group in abnormal.GroupBy(p => p.Label).OrderByDescending(g => g.Count()))
            {
                string flag = $"[Crop classifier] {group.Key}: {group.Count()} NST";
                if (!result.SyndromeFlags.Contains(flag))
                    result.SyndromeFlags.Add(flag);
            }
        }

        private static string BuildAnomalyReport(string baseReport, AnalysisResult result)
        {
            var predictions = result.AnomalyPredictions
                .OrderBy(p => p.Index)
                .ToList();
            var abnormal = predictions.Where(p => !p.IsNormal).ToList();

            var lines = new List<string>
            {
                baseReport,
                "",
                "KẾT QUẢ XÁC ĐỊNH BẤT THƯỜNG TỪ ẢNH NST ĐÃ CROP",
                new string('=', 52),
                $"Tổng crop đã phân tích: {predictions.Count}",
                $"NST nghi bất thường: {abnormal.Count}",
                $"Mức rủi ro sau classifier: {result.RiskLevel}",
                "",
            };

            if (predictions.Count == 0)
            {
                lines.Add("Không có kết quả classifier.");
                return string.Join(Environment.NewLine, lines);
            }

            lines.Add("Chi tiết từng NST:");
            foreach (var pred in predictions)
            {
                lines.Add(
                    $"  NST #{pred.Index:D2}: {pred.Label} " +
                    $"(conf={pred.Confidence:P1})");
            }

            if (abnormal.Count > 0)
            {
                lines.Add("");
                lines.Add("Tổng hợp bất thường:");
                foreach (var group in abnormal.GroupBy(p => p.Label).OrderByDescending(g => g.Count()))
                {
                    var ids = string.Join(", ", group.Select(p => $"#{p.Index:D2}"));
                    lines.Add($"  - {group.Key}: {group.Count()} NST ({ids})");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private readonly record struct CropInfo(string Path, int BoxIndex);

        // ── Ensure ASCII path cho OpenCvSharp ───────────────────────────────────
        private static string EnsureAscii(string path, out string? tempPath)
        {
            tempPath = null;
            if (path.All(c => c <= 127)) return path;
            string ext = Path.GetExtension(path);
            tempPath = Path.Combine(
                Path.GetTempPath(), $"mv_{Guid.NewGuid():N}{ext}");
            File.Copy(path, tempPath, overwrite: true);
            return tempPath;
        }
    }
}
