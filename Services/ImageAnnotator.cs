using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace MedVisionAI.Services
{
    /// <summary>
    /// Vẽ kết quả phân tích lên ảnh gốc để bác sĩ xem.
    /// Dùng OpenCvSharp — hoàn toàn trong C#, không cần Python.
    /// </summary>
    public static class ImageAnnotator
    {
        // Màu sắc cho từng nhóm Denver (BGR)
        private static readonly Dictionary<string, Scalar> GroupColors = new()
        {
            {"A", new Scalar(245,  50, 113)},  // Tím đậm
            {"B", new Scalar(245, 100,  50)},  // Cam
            {"C", new Scalar( 50, 200, 245)},  // Cyan
            {"D", new Scalar( 50, 245, 150)},  // Xanh lá
            {"E", new Scalar(200, 245,  50)},  // Vàng
            {"F", new Scalar(245, 200,  50)},  // Vàng cam
            {"G", new Scalar(150,  50, 245)},  // Tím nhạt
        };

        private static readonly Scalar DefaultColor = new(113, 50, 245); // Kraken purple

        /// <summary>
        /// Vẽ bounding box cho từng NST, tô màu theo nhóm Denver,
        /// đánh số thứ tự, và thêm overlay thông tin tổng quan.
        /// </summary>
        public static byte[] Annotate(
            string imagePath,
            List<DetectionBox> boxes,
            ChromosomeAnalysisInfo analysis)
        {
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty())
                throw new Exception($"Không đọc được ảnh: {imagePath}");

            int h = img.Rows, w = img.Cols;

            // Sort boxes by area descending for Denver assignment
            var sortedBoxes = boxes
                .Select((b, i) => (Box: b, Index: i,
                    AreaNorm: h * w > 0 ? b.Area / (boxes.Max(x => x.Area) + 1e-6f) : 0f))
                .OrderByDescending(x => x.AreaNorm)
                .ToList();

            // Draw each bounding box
            int denverIdx = 0;
            foreach (var (box, origIdx, areaNorm) in sortedBoxes)
            {
                var group = AssignGroup(areaNorm);
                var color = GroupColors.GetValueOrDefault(group, DefaultColor);

                int x1 = (int)box.X1, y1 = (int)box.Y1;
                int x2 = (int)box.X2, y2 = (int)box.Y2;

                // Clamp to image bounds
                x1 = Math.Clamp(x1, 0, w - 1);
                y1 = Math.Clamp(y1, 0, h - 1);
                x2 = Math.Clamp(x2, 0, w - 1);
                y2 = Math.Clamp(y2, 0, h - 1);

                // Draw rounded-looking box with thick border
                Cv2.Rectangle(img, new Point(x1, y1), new Point(x2, y2), color, 2);

                // Label background
                string label = $"{denverIdx + 1} ({group})";
                int baseLine  = 0;
                var textSize  = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.4, 1, out baseLine);
                var labelBg   = new Rect(x1, y1 - textSize.Height - 4, textSize.Width + 6, textSize.Height + 4);

                // Clamp label bg
                if (labelBg.Y < 0) labelBg = new Rect(x1, y2, labelBg.Width, labelBg.Height);

                Cv2.Rectangle(img, labelBg, color, -1);
                Cv2.PutText(img, label,
                    new Point(x1 + 3, labelBg.Y + textSize.Height),
                    HersheyFonts.HersheySimplex, 0.4,
                    new Scalar(255, 255, 255), 1);

                denverIdx++;
            }

            // ── Info overlay (top-left semi-transparent panel) ────────────────
            DrawInfoOverlay(img, analysis);

            // Encode to PNG bytes
            Cv2.ImEncode(".png", img, out var bytes);
            return bytes;
        }

        private static void DrawInfoOverlay(Mat img, ChromosomeAnalysisInfo info)
        {
            // Semi-transparent dark panel
            int panelW = 260, panelH = 120;
            using var overlay = img.Clone();
            Cv2.Rectangle(overlay, new Rect(10, 10, panelW, panelH),
                new Scalar(20, 20, 20), -1);
            Cv2.AddWeighted(overlay, 0.55, img, 0.45, 0, img);

            var white  = new Scalar(255, 255, 255);
            var green  = new Scalar(86, 205, 100);
            var red    = new Scalar(80, 80, 230);
            var yellow = new Scalar(80, 200, 230);

            int x = 18, y = 30;
            int lineH = 20;

            // Count
            var countColor = info.IsNormalCount ? green : red;
            Cv2.PutText(img, $"NST: {info.TotalCount}  [{(info.IsNormalCount ? "Binh thuong" : "Bat thuong")}]",
                new Point(x, y), HersheyFonts.HersheySimplex, 0.52, countColor, 1);
            y += lineH;

            // Risk
            var riskColor = info.RiskLevel == "Binh thuong" ? green
                : info.RiskLevel.Contains("cao") ? red : yellow;
            Cv2.PutText(img, $"Rui ro: {info.RiskLevel}",
                new Point(x, y), HersheyFonts.HersheySimplex, 0.45, riskColor, 1);
            y += lineH;

            // Sex
            Cv2.PutText(img, $"GT: {info.SexEstimation.Split('(')[0].Trim()}",
                new Point(x, y), HersheyFonts.HersheySimplex, 0.45, white, 1);
            y += lineH;

            // Denver groups mini
            var groupStr = string.Join(" ", "ABCDEFG".Select(
                g => $"{g}:{info.DenverGroups.GetValueOrDefault(g.ToString())}"));
            Cv2.PutText(img, groupStr,
                new Point(x, y), HersheyFonts.HersheySimplex, 0.38, white, 1);
            y += lineH;

            // Syndrome count
            if (info.SyndromeFlags.Count > 0)
            {
                Cv2.PutText(img, $"! {info.SyndromeFlags.Count} hoi chung nghi ngo",
                    new Point(x, y), HersheyFonts.HersheySimplex, 0.42, red, 1);
            }
        }

        private static string AssignGroup(float areaNorm)
        {
            if (areaNorm >= 0.75f) return "A";
            if (areaNorm >= 0.60f) return "B";
            if (areaNorm >= 0.40f) return "C";
            if (areaNorm >= 0.28f) return "D";
            if (areaNorm >= 0.20f) return "E";
            if (areaNorm >= 0.13f) return "F";
            return "G";
        }
    }
}
