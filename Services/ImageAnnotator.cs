using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using MedVisionAI.Models;

using CvPoint = OpenCvSharp.Point;
using CvRect  = OpenCvSharp.Rect;

namespace MedVisionAI.Services
{
    public static class ImageAnnotator
    {
        private static readonly Dictionary<string, Scalar> GroupColors = new()
        {
            {"A", new Scalar(245,  50, 113)},
            {"B", new Scalar(245, 100,  50)},
            {"C", new Scalar( 50, 200, 245)},
            {"D", new Scalar( 50, 245, 150)},
            {"E", new Scalar(200, 245,  50)},
            {"F", new Scalar(245, 200,  50)},
            {"G", new Scalar(150,  50, 245)},
        };
        private static readonly Scalar Purple = new(113, 50, 245);

        public static byte[] Annotate(
            string imagePath,
            List<DetectionBox> boxes,
            ChromosomeAnalysisInfo analysis)
        {
            // OpenCvSharp marshal layer cannot handle non-ASCII paths.
            // Copy to a temp ASCII path when needed.
            string workPath = imagePath;
            string? tempPath = null;

            if (imagePath.Any(c => c > 127))
            {
                tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"mv_{Guid.NewGuid():N}.png");
                File.Copy(imagePath, tempPath, overwrite: true);
                workPath = tempPath;
            }

            try
            {
                using var img = Cv2.ImRead(workPath, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                int h = img.Rows, w = img.Cols;
                float maxArea = boxes.Count > 0
                    ? boxes.Max(b => b.Area) + 1e-6f : 1f;

                var sorted = boxes
                    .Select((b, i) => (Box: b, Norm: b.Area / maxArea))
                    .OrderByDescending(x => x.Norm)
                    .ToList();

                int num = 0;
                foreach (var (box, norm) in sorted)
                {
                    string group = AssignGroup(norm);
                    var    color = GroupColors.GetValueOrDefault(group, Purple);

                    int x1 = Math.Clamp((int)box.X1, 0, w - 1);
                    int y1 = Math.Clamp((int)box.Y1, 0, h - 1);
                    int x2 = Math.Clamp((int)box.X2, 0, w - 1);
                    int y2 = Math.Clamp((int)box.Y2, 0, h - 1);

                    Cv2.Rectangle(img,
                        new CvPoint(x1, y1), new CvPoint(x2, y2),
                        color, 2);

                    // *** ASCII only — no Vietnamese, no emoji ***
                    string lbl  = $"{num + 1}({group})";
                    var    tsz  = Cv2.GetTextSize(
                        lbl, HersheyFonts.HersheySimplex, 0.40, 1, out _);
                    int    lblY = y1 - tsz.Height - 4;
                    if (lblY < 0) lblY = y2;

                    Cv2.Rectangle(img,
                        new CvRect(x1, lblY, tsz.Width + 6, tsz.Height + 4),
                        color, -1);
                    Cv2.PutText(img, lbl,
                        new CvPoint(x1 + 3, lblY + tsz.Height),
                        HersheyFonts.HersheySimplex, 0.40,
                        new Scalar(255, 255, 255), 1);
                    num++;
                }

                DrawOverlay(img, analysis);

                Cv2.ImEncode(".png", img, out var bytes);
                return bytes;
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public static byte[] AnnotateAnomalies(string imagePath, AnalysisResult result)
        {
            string workPath = imagePath;
            string? tempPath = null;

            if (imagePath.Any(c => c > 127))
            {
                tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"mv_{Guid.NewGuid():N}.png");
                File.Copy(imagePath, tempPath, overwrite: true);
                workPath = tempPath;
            }

            try
            {
                using var img = Cv2.ImRead(workPath, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                int h = img.Rows, w = img.Cols;
                var predictions = result.AnomalyPredictions
                    .Where(p => p.BoundingBox is { Length: >= 4 })
                    .OrderBy(p => p.Index)
                    .ToList();

                foreach (var pred in predictions)
                {
                    var b = pred.BoundingBox!;
                    int x1 = Math.Clamp((int)b[0], 0, w - 1);
                    int y1 = Math.Clamp((int)b[1], 0, h - 1);
                    int x2 = Math.Clamp((int)b[2], 0, w - 1);
                    int y2 = Math.Clamp((int)b[3], 0, h - 1);

                    var color = pred.IsNormal
                        ? new Scalar(60, 210, 80)
                        : pred.Confidence >= 0.75f
                            ? new Scalar(60, 60, 230)
                            : new Scalar(50, 200, 230);

                    Cv2.Rectangle(img,
                        new CvPoint(x1, y1), new CvPoint(x2, y2),
                        color, pred.IsNormal ? 2 : 3);

                    string shortLabel = pred.IsNormal ? "OK" : ShortLabel(pred.Label);
                    string lbl = $"{pred.Index}:{shortLabel} {pred.Confidence:P0}";
                    var tsz = Cv2.GetTextSize(
                        lbl, HersheyFonts.HersheySimplex, 0.38, 1, out _);
                    int lblY = y1 - tsz.Height - 4;
                    if (lblY < 0) lblY = y2;

                    Cv2.Rectangle(img,
                        new CvRect(x1, lblY, tsz.Width + 6, tsz.Height + 4),
                        color, -1);
                    Cv2.PutText(img, lbl,
                        new CvPoint(x1 + 3, lblY + tsz.Height),
                        HersheyFonts.HersheySimplex, 0.38,
                        new Scalar(255, 255, 255), 1);
                }

                DrawAnomalyOverlay(img, result);

                Cv2.ImEncode(".png", img, out var bytes);
                return bytes;
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public static byte[] AnnotateClassifier(
            string imagePath,
            string moduleTitle,
            string label,
            float confidence,
            bool isNormal,
            MedicalClassifierKind kind)
        {
            string workPath = imagePath;
            string? tempPath = null;

            if (imagePath.Any(c => c > 127))
            {
                tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"mv_{Guid.NewGuid():N}.png");
                File.Copy(imagePath, tempPath, overwrite: true);
                workPath = tempPath;
            }

            try
            {
                using var img = Cv2.ImRead(workPath, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                var accent = kind == MedicalClassifierKind.BloodCancer
                    ? new Scalar(50, 50, 220)
                    : new Scalar(75, 170, 35);
                var okColor = new Scalar(60, 210, 80);
                var resultColor = isNormal ? okColor : accent;

                DrawFocusFrame(img, resultColor);
                DrawClassifierOverlay(img, moduleTitle, label, confidence, isNormal, resultColor);

                Cv2.ImEncode(".png", img, out var bytes);
                return bytes;
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        // ── Overlay panel — ALL text MUST be pure ASCII ───────────────────────
        private static void DrawOverlay(Mat img, ChromosomeAnalysisInfo info)
        {
            using var ov = img.Clone();
            Cv2.Rectangle(ov, new CvRect(8, 8, 290, 130),
                new Scalar(15, 15, 15), -1);
            Cv2.AddWeighted(ov, 0.55, img, 0.45, 0, img);

            var white  = new Scalar(255, 255, 255);
            var green  = new Scalar(60,  210, 80);
            var red    = new Scalar(60,  60,  230);
            var yellow = new Scalar(50,  200, 230);

            int x = 14, y = 28, lh = 22;

            // Line 1: count
            bool ok   = info.IsNormalCount;
            string st = ok ? "Normal" : "Abnormal";
            Cv2.PutText(img, $"Chromosomes: {info.TotalCount} [{st}]",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.48,
                ok ? green : red, 1);
            y += lh;

            // Line 2: risk (ASCII)
            string risk = info.RiskLevel switch
            {
                var s when s.Contains("cao")    => "High Risk",
                var s when s.Contains("doi")    => "Monitor",
                var s when s.Contains("Monitor")=> "Monitor",
                _                               => "Normal"
            };
            var rc = risk == "High Risk" ? red
                   : risk == "Monitor"   ? yellow : green;
            Cv2.PutText(img, $"Risk: {risk}",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.43, rc, 1);
            y += lh;

            // Line 3: sex (ASCII)
            string sex = info.SexEstimation.Contains("XX") ? "Female (XX) est."
                       : info.SexEstimation.Contains("XY") ? "Male (XY) est."
                       : "Sex: undetermined";
            Cv2.PutText(img, sex,
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.41, white, 1);
            y += lh;

            // Line 4: Denver groups
            string grp = string.Join(" ", "ABCDEFG".Select(
                g => $"{g}:{info.DenverGroups.GetValueOrDefault(g.ToString())}"));
            Cv2.PutText(img, $"Denver: {grp}",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.37, white, 1);
            y += lh;

            // Line 5: syndromes
            if (info.SyndromeFlags.Count > 0)
                Cv2.PutText(img, $"! {info.SyndromeFlags.Count} syndrome(s) detected",
                    new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.39, red, 1);
        }

        private static string AssignGroup(float a)
        {
            if (a >= 0.75f) return "A";
            if (a >= 0.60f) return "B";
            if (a >= 0.40f) return "C";
            if (a >= 0.28f) return "D";
            if (a >= 0.20f) return "E";
            if (a >= 0.13f) return "F";
            return "G";
        }

        private static void DrawAnomalyOverlay(Mat img, AnalysisResult result)
        {
            using var ov = img.Clone();
            Cv2.Rectangle(ov, new CvRect(8, 8, 330, 108),
                new Scalar(15, 15, 15), -1);
            Cv2.AddWeighted(ov, 0.55, img, 0.45, 0, img);

            var white  = new Scalar(255, 255, 255);
            var green  = new Scalar(60,  210, 80);
            var red    = new Scalar(60,  60,  230);
            var yellow = new Scalar(50,  200, 230);

            int total = result.AnomalyPredictions.Count;
            int abnormal = result.AnomalyPredictions.Count(p => !p.IsNormal);
            float maxConf = result.AnomalyPredictions.Count > 0
                ? result.AnomalyPredictions.Max(p => p.Confidence)
                : 0;

            int x = 14, y = 30, lh = 23;
            var riskColor = abnormal == 0 ? green : maxConf >= 0.75f ? red : yellow;

            Cv2.PutText(img, $"Crop classifier: {total} NST",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.48,
                white, 1);
            y += lh;
            Cv2.PutText(img, $"Suspected abnormal: {abnormal}",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.48,
                riskColor, 1);
            y += lh;
            Cv2.PutText(img, $"Risk: {ToAsciiRisk(result.RiskLevel)}",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.43,
                riskColor, 1);
        }

        private static void DrawFocusFrame(Mat img, Scalar color)
        {
            int w = img.Cols, h = img.Rows;
            int fw = Math.Max(80, (int)(w * 0.62));
            int fh = Math.Max(80, (int)(h * 0.62));
            int x1 = (w - fw) / 2;
            int y1 = (h - fh) / 2;
            int x2 = x1 + fw;
            int y2 = y1 + fh;
            int len = Math.Max(18, Math.Min(w, h) / 11);

            using var ov = img.Clone();
            Cv2.Rectangle(ov, new CvRect(0, 0, w, h), new Scalar(15, 15, 15), -1);
            Cv2.Rectangle(ov, new CvRect(x1, y1, fw, fh), new Scalar(0, 0, 0), -1);
            Cv2.AddWeighted(ov, 0.20, img, 0.80, 0, img);

            int thickness = Math.Max(2, Math.Min(w, h) / 180);
            Cv2.Line(img, new CvPoint(x1, y1), new CvPoint(x1 + len, y1), color, thickness);
            Cv2.Line(img, new CvPoint(x1, y1), new CvPoint(x1, y1 + len), color, thickness);
            Cv2.Line(img, new CvPoint(x2, y1), new CvPoint(x2 - len, y1), color, thickness);
            Cv2.Line(img, new CvPoint(x2, y1), new CvPoint(x2, y1 + len), color, thickness);
            Cv2.Line(img, new CvPoint(x1, y2), new CvPoint(x1 + len, y2), color, thickness);
            Cv2.Line(img, new CvPoint(x1, y2), new CvPoint(x1, y2 - len), color, thickness);
            Cv2.Line(img, new CvPoint(x2, y2), new CvPoint(x2 - len, y2), color, thickness);
            Cv2.Line(img, new CvPoint(x2, y2), new CvPoint(x2, y2 - len), color, thickness);

            int cx = w / 2, cy = h / 2;
            Cv2.Line(img, new CvPoint(cx - len / 2, cy), new CvPoint(cx + len / 2, cy), color, 1);
            Cv2.Line(img, new CvPoint(cx, cy - len / 2), new CvPoint(cx, cy + len / 2), color, 1);
            Cv2.Circle(img, new CvPoint(cx, cy), Math.Max(8, len / 4), color, 1);
        }

        private static void DrawClassifierOverlay(
            Mat img,
            string moduleTitle,
            string label,
            float confidence,
            bool isNormal,
            Scalar color)
        {
            using var ov = img.Clone();
            Cv2.Rectangle(ov, new CvRect(8, 8, 360, 118),
                new Scalar(15, 15, 15), -1);
            Cv2.AddWeighted(ov, 0.58, img, 0.42, 0, img);

            var white = new Scalar(255, 255, 255);
            var green = new Scalar(60, 210, 80);
            var resultColor = isNormal ? green : color;

            int x = 16, y = 30, lh = 24;
            Cv2.PutText(img, "AI focus classification",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.50,
                white, 1);
            y += lh;

            string shortModule = moduleTitle.Contains("máu", StringComparison.OrdinalIgnoreCase)
                ? "Blood cell cancer"
                : "Malaria parasite";
            Cv2.PutText(img, shortModule,
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.43,
                white, 1);
            y += lh;

            Cv2.PutText(img, $"Result: {ToAsciiLabel(label)}",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.43,
                resultColor, 1);
            y += lh;

            Cv2.PutText(img, $"Confidence: {confidence:P0}",
                new CvPoint(x, y), HersheyFonts.HersheySimplex, 0.43,
                resultColor, 1);
        }

        private static string ShortLabel(string label)
        {
            if (label.Contains("Trisomy 21")) return "Down";
            if (label.Contains("Trisomy 18")) return "Edwards";
            if (label.Contains("Trisomy 13")) return "Patau";
            if (label.Contains("Turner")) return "Turner";
            if (label.Contains("Klinefelter")) return "XXY";
            if (label.Contains("Binh thuong") || label.Contains("Bình thường")) return "OK";
            return label.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Abn";
        }

        private static string ToAsciiRisk(string risk)
        {
            if (risk.Contains("cao", StringComparison.OrdinalIgnoreCase))
                return "High";
            if (risk.Contains("dõi", StringComparison.OrdinalIgnoreCase) ||
                risk.Contains("doi", StringComparison.OrdinalIgnoreCase))
                return "Monitor";
            return "Normal";
        }

        private static string ToAsciiLabel(string label)
        {
            if (label.Contains("ký sinh", StringComparison.OrdinalIgnoreCase))
                return "Parasitized";
            if (label.Contains("Không", StringComparison.OrdinalIgnoreCase))
                return "Uninfected";
            if (label.Contains("lành", StringComparison.OrdinalIgnoreCase))
                return "Benign";
            return label
                .Replace("á", "a")
                .Replace("Á", "A")
                .Replace("ế", "e")
                .Replace("ề", "e")
                .Replace("ễ", "e")
                .Replace("ệ", "e");
        }
    }
}
