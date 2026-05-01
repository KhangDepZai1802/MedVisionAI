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
            // OpenCvSharp cannot marshal non-ASCII paths → copy to temp ASCII path
            string workPath = EnsureAscii(imagePath, out string? tempImg);

            try
            {
                using var img = Cv2.ImRead(workPath, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                int origW = img.Cols, origH = img.Rows;
                List<DetectionBox> boxes;

                if (string.IsNullOrEmpty(_config.SegmentationModelPath))
                {
                    boxes = GenerateDemoBoxes(origW, origH);
                }
                else
                {
                    // Model path also needs ASCII
                    string modelWork = EnsureAscii(
                        _config.SegmentationModelPath, out string? tempModel);
                    try
                    {
                        var fmt = ModelFormatDetector.Detect(_config.SegmentationModelPath);
                        boxes = fmt switch
                        {
                            ModelFormat.Onnx    => RunOnnx(modelWork, img, origW, origH),
                            ModelFormat.PyTorch => RunPythonBridge(_config.SegmentationModelPath, imagePath, origW, origH),
                            ModelFormat.Keras   => RunPythonBridge(_config.SegmentationModelPath, imagePath, origW, origH),
                            _ => throw new NotSupportedException(
                                $"Unsupported format: {Path.GetExtension(_config.SegmentationModelPath)}\n" +
                                "Please export model to .onnx")
                        };
                    }
                    finally
                    {
                        if (tempModel != null && File.Exists(tempModel))
                            File.Delete(tempModel);
                    }
                }

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

        private static List<DetectionBox> RunOnnx(
            string modelPath, Mat img, int origW, int origH)
        {
            using var engine  = new OnnxInferenceEngine(modelPath);
            using var outputs = engine.Run(img);
            return OutputParser.ParseDetections(outputs, origW, origH, 640, 640);
        }

        private static List<DetectionBox> RunPythonBridge(
            string modelPath, string imagePath, int origW, int origH)
        {
            var bridge   = new PythonBridge();
            var rawBoxes = bridge.RunInference(modelPath, imagePath);
            return rawBoxes.Select(r => new DetectionBox
            {
                X1 = r[0] * origW, Y1 = r[1] * origH,
                X2 = r[2] * origW, Y2 = r[3] * origH,
                Confidence = r[4],
            }).ToList();
        }

        private static List<DetectionBox> GenerateDemoBoxes(int w, int h)
        {
            var rng = new Random(42);
            var boxes = new List<DetectionBox>();
            for (int i = 0; i < 46; i++)
            {
                float bw = rng.NextSingle() * 0.06f + 0.02f;
                float bh = bw * (rng.NextSingle() * 0.5f + 1.5f);
                float cx = rng.NextSingle() * (1 - bw) + bw / 2;
                float cy = rng.NextSingle() * (1 - bh) + bh / 2;
                boxes.Add(new DetectionBox
                {
                    X1 = (cx - bw / 2) * w, Y1 = (cy - bh / 2) * h,
                    X2 = (cx + bw / 2) * w, Y2 = (cy + bh / 2) * h,
                    Confidence = rng.NextSingle() * 0.3f + 0.7f,
                });
            }
            return boxes;
        }

        // Copy to temp ASCII path if original contains non-ASCII chars
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
