using System;
using System.IO;
using System.Linq;
using MedVisionAI.Models;

namespace MedVisionAI.Services
{
    public class MedicalClassifierService
    {
        private readonly string _modelPath;
        private readonly MedicalClassifierKind _kind;

        public MedicalClassifierService(string modelPath, MedicalClassifierKind kind)
        {
            _modelPath = modelPath;
            _kind = kind;
        }

        public MedicalClassifierResult Analyze(string imagePath)
        {
            string modelWork = EnsureAscii(_modelPath, out string? tempModel);
            string imageWork = EnsureAscii(imagePath, out string? tempImage);

            try
            {
                AnomalyPrediction pred;
                if (ModelFormatDetector.Detect(_modelPath) == ModelFormat.Onnx)
                {
                    var onnx = new OnnxClassifierService(
                        modelWork, ClassNamesFor(_kind));
                    pred = onnx.Predict(imageWork);
                }
                else
                {
                    var bridge = new PythonBridge();
                    pred = bridge.RunImageClassifier(
                        modelWork, imageWork, ModelTypeFor(_kind));
                }

                string displayLabel = ToDisplayLabel(_kind, pred.Label);
                bool isNormal = IsNormal(_kind, pred.Label);
                string risk = BuildRisk(_kind, isNormal, pred.Confidence);
                string recommendation = BuildRecommendation(_kind, isNormal);
                string moduleTitle = _kind == MedicalClassifierKind.BloodCancer
                    ? "Phát hiện ung thư tế bào máu"
                    : "Phát hiện ký sinh trùng sốt rét";

                string report = BuildReport(
                    moduleTitle, imagePath, _modelPath, displayLabel,
                    pred.Confidence, risk, recommendation);

                return new MedicalClassifierResult
                {
                    ModuleTitle = moduleTitle,
                    Label = pred.Label,
                    DisplayLabel = displayLabel,
                    Confidence = pred.Confidence,
                    ClassIndex = pred.ClassIndex,
                    IsNormal = isNormal,
                    RiskLevel = risk,
                    Recommendation = recommendation,
                    AnnotatedImageBytes = ImageAnnotator.AnnotateClassifier(
                        imagePath, moduleTitle, displayLabel, pred.Confidence,
                        isNormal, _kind),
                    ReportPlain = report,
                };
            }
            finally
            {
                if (tempModel != null && File.Exists(tempModel))
                    File.Delete(tempModel);
                if (tempImage != null && File.Exists(tempImage))
                    File.Delete(tempImage);
            }
        }

        private static string ModelTypeFor(MedicalClassifierKind kind) => kind switch
        {
            MedicalClassifierKind.BloodCancer => "blood_cancer_classifier",
            MedicalClassifierKind.Malaria => "malaria_classifier",
            _ => throw new NotSupportedException($"Unsupported classifier: {kind}")
        };

        private static IReadOnlyList<string> ClassNamesFor(MedicalClassifierKind kind)
            => kind switch
            {
                MedicalClassifierKind.BloodCancer => OnnxClassifierService.BloodCancerClassNames,
                MedicalClassifierKind.Malaria => OnnxClassifierService.MalariaClassNames,
                _ => throw new NotSupportedException($"Unsupported classifier: {kind}")
            };

        private static bool IsNormal(MedicalClassifierKind kind, string label)
        {
            string normalized = label.Trim().ToLowerInvariant();
            return kind switch
            {
                MedicalClassifierKind.BloodCancer => normalized.Contains("benign"),
                MedicalClassifierKind.Malaria => normalized.Contains("uninfected"),
                _ => false
            };
        }

        private static string ToDisplayLabel(MedicalClassifierKind kind, string label)
        {
            string normalized = label.Trim().ToLowerInvariant();
            if (kind == MedicalClassifierKind.Malaria)
            {
                if (normalized.Contains("parasitized"))
                    return "Nhiễm ký sinh trùng sốt rét";
                if (normalized.Contains("uninfected"))
                    return "Không phát hiện ký sinh trùng";
            }

            if (kind == MedicalClassifierKind.BloodCancer)
            {
                if (normalized.Contains("benign"))
                    return "Benign - lành tính";
                if (normalized.Contains("early"))
                    return "Early Pre-B ALL";
                if (normalized == "pre" || normalized.Contains("pre-b"))
                    return "Pre-B ALL";
                if (normalized.Contains("pro"))
                    return "Pro-B ALL";
            }

            return label;
        }

        private static string BuildRisk(
            MedicalClassifierKind kind, bool isNormal, float confidence)
        {
            if (isNormal)
                return "Bình thường";

            if (kind == MedicalClassifierKind.Malaria)
                return confidence >= 0.75f ? "Dương tính nguy cơ cao" : "Cần theo dõi";

            return confidence >= 0.75f ? "Nghi ngờ ác tính cao" : "Cần theo dõi";
        }

        private static string BuildRecommendation(
            MedicalClassifierKind kind, bool isNormal)
        {
            if (isNormal)
                return "Không thấy dấu hiệu bất thường rõ theo model AI.";

            return kind == MedicalClassifierKind.Malaria
                ? "Khuyến nghị soi lam máu/xét nghiệm xác nhận theo quy trình lâm sàng."
                : "Khuyến nghị bác sĩ huyết học xem lại tiêu bản và làm xét nghiệm xác nhận.";
        }

        private static string BuildReport(
            string moduleTitle,
            string imagePath,
            string modelPath,
            string label,
            float confidence,
            string risk,
            string recommendation)
        {
            return string.Join(Environment.NewLine, new[]
            {
                $"BÁO CÁO {moduleTitle.ToUpperInvariant()}",
                new string('=', 48),
                $"Thời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                $"Ảnh phân tích: {Path.GetFileName(imagePath)}",
                $"Model AI: {Path.GetFileName(modelPath)}",
                "",
                $"Kết quả AI: {label}",
                $"Độ tin cậy: {confidence:P1}",
                $"Mức đánh giá: {risk}",
                "",
                "Vùng AI tập trung: toàn bộ ảnh đầu vào sau chuẩn hóa 224x224.",
                $"Khuyến nghị: {recommendation}",
                "",
                "Lưu ý: Kết quả AI chỉ hỗ trợ sàng lọc, không thay thế chẩn đoán của bác sĩ chuyên khoa."
            });
        }

        private static string EnsureAscii(string path, out string? tempPath)
        {
            tempPath = null;
            if (path.All(c => c <= 127)) return path;
            string ext = Path.GetExtension(path);
            tempPath = Path.Combine(
                Path.GetTempPath(), $"mv_cls_{Guid.NewGuid():N}{ext}");
            File.Copy(path, tempPath, overwrite: true);
            return tempPath;
        }
    }
}
