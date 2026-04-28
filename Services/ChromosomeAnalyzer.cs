namespace MedVisionAI.Services
{
    /// <summary>
    /// Phân tích kết quả detection để đưa ra thông tin lâm sàng cho bác sĩ:
    ///   - Phân nhóm Denver A-G theo kích thước tương đối
    ///   - Ước tính giới tính
    ///   - Phát hiện hội chứng di truyền theo quy tắc
    ///   - Đánh giá mức độ rủi ro
    ///
    /// Logic này hoàn toàn độc lập với model AI — chạy trên kết quả detection.
    /// </summary>
    public static class ChromosomeAnalyzer
    {
        // ── Denver grouping thresholds (area normalized, max=1.0) ─────────────
        private static readonly (string Group, string Desc, float MinArea)[] DenverThresholds =
        {
            ("A", "NST 1–3 (lớn nhất)",  0.75f),
            ("B", "NST 4–5",              0.60f),
            ("C", "NST 6–12, X",          0.40f),
            ("D", "NST 13–15 (tâm đầu)",  0.28f),
            ("E", "NST 16–18",            0.20f),
            ("F", "NST 19–20",            0.13f),
            ("G", "NST 21–22, Y (nhỏ)",   0.00f),
        };

        // ── Expected Denver counts (normal karyotype 46,XX or 46,XY) ─────────
        private static readonly Dictionary<string, int> ExpectedCounts = new()
        {
            {"A", 6}, {"B", 4}, {"C", 16}, {"D", 6}, {"E", 6}, {"F", 4}, {"G", 4}
        };

        // ── Syndrome detection rules ──────────────────────────────────────────
        private static readonly List<(Func<Dictionary<string,int>,int,bool> Cond, string Name, string Desc)>
            SyndromeRules = new()
        {
            (
                (g, t) => g.GetValueOrDefault("G") >= 5 && t == 47,
                "Trisomy 21 — Hội chứng Down (hoặc 47,XYY)",
                "Thừa 1 NST nhóm G"
            ),
            (
                (g, t) => g.GetValueOrDefault("D") >= 7,
                "Trisomy 13 — Hội chứng Patau",
                "Thừa 1 NST nhóm D"
            ),
            (
                (g, t) => g.GetValueOrDefault("E") >= 7,
                "Trisomy 18 — Hội chứng Edwards",
                "Thừa 1 NST nhóm E"
            ),
            (
                (g, t) => g.GetValueOrDefault("C") <= 13 && t <= 45,
                "Monosomy X — Hội chứng Turner (45,X)",
                "Thiếu NST nhóm C, nghi mất NST X"
            ),
            (
                (g, t) => g.GetValueOrDefault("C") >= 17 && t >= 47,
                "47,XXX hoặc Klinefelter (47,XXY)",
                "Thừa NST nhóm C, nghi thêm NST X"
            ),
        };

        // ── Main entry ────────────────────────────────────────────────────────

        public static ChromosomeAnalysisInfo Analyze(
            List<DetectionBox> boxes,
            int normalCount = 46,
            int tolerance   = 1)
        {
            int total    = boxes.Count;
            bool isNormal = Math.Abs(total - normalCount) <= tolerance;

            // Normalize areas
            float maxArea = boxes.Count > 0 ? boxes.Max(b => b.Area) : 1f;
            if (maxArea < 1e-6f) maxArea = 1f;

            var normalizedAreas = boxes.Select(b => b.Area / maxArea).ToList();

            // Phân nhóm Denver
            var groups = new Dictionary<string, int>
                { {"A",0}, {"B",0}, {"C",0}, {"D",0}, {"E",0}, {"F",0}, {"G",0} };

            for (int i = 0; i < normalizedAreas.Count; i++)
            {
                var group = AssignDenverGroup(normalizedAreas[i]);
                groups[group]++;
            }

            // Giới tính (ước tính từ nhóm C)
            var (sex, sexConf) = EstimateSex(groups);

            // Hội chứng
            var syndromes = DetectSyndromes(groups, total, normalCount, tolerance);

            // Rủi ro
            string risk;
            if (!isNormal && syndromes.Count > 0)
                risk = "Nguy cơ cao";
            else if (!isNormal || syndromes.Count > 0)
                risk = "Cần theo dõi";
            else
                risk = "Bình thường";

            // Stats
            float meanArea = normalizedAreas.Count > 0 ? normalizedAreas.Average() : 0;
            float stdArea  = normalizedAreas.Count > 1
                ? (float)Math.Sqrt(normalizedAreas.Average(a => Math.Pow(a - meanArea, 2)))
                : 0;
            float cv = meanArea > 1e-6f ? stdArea / meanArea * 100f : 0f;

            return new ChromosomeAnalysisInfo
            {
                TotalCount      = total,
                NormalCount     = normalCount,
                Tolerance       = tolerance,
                IsNormalCount   = isNormal,
                DenverGroups    = groups,
                SexEstimation   = sex,
                SexConfidence   = sexConf,
                SyndromeFlags   = syndromes,
                RiskLevel       = risk,
                NormalizedAreas = normalizedAreas,
                MeanArea        = meanArea,
                StdArea         = stdArea,
                CvPercent       = cv,
            };
        }

        // ── Denver grouping ───────────────────────────────────────────────────

        private static string AssignDenverGroup(float areaNorm)
        {
            foreach (var (group, _, minArea) in DenverThresholds)
                if (areaNorm >= minArea) return group;
            return "G";
        }

        // ── Sex estimation ────────────────────────────────────────────────────

        private static (string Sex, string Confidence) EstimateSex(Dictionary<string, int> g)
        {
            int c = g.GetValueOrDefault("C");
            int gCount = g.GetValueOrDefault("G");

            // C=16 → XX (nữ), C=15 → XY (nam), G>=5 → có NST Y
            if (c >= 16 && gCount <= 4)
                return ("XX — Nữ (ước tính)", "Trung bình");
            if (c == 15 || gCount >= 5)
                return ("XY — Nam (ước tính)", "Trung bình");
            return ($"Không xác định (nhóm C: {c})", "Thấp");
        }

        // ── Syndrome detection ────────────────────────────────────────────────

        private static List<string> DetectSyndromes(
            Dictionary<string, int> groups, int total, int normal, int tolerance)
        {
            var flags = new List<string>();
            foreach (var (cond, name, desc) in SyndromeRules)
            {
                try { if (cond(groups, total)) flags.Add($"{name} ({desc})"); }
                catch { /* ignore malformed rule */ }
            }

            // Generic aneuploidy if no specific syndrome
            int diff = Math.Abs(total - normal);
            if (diff > tolerance && flags.Count == 0)
            {
                var dir = total > normal ? "thừa" : "thiếu";
                flags.Add($"Lệch bội {dir}: {total} NST ({dir} {diff} so với chuẩn {normal})");
            }

            return flags;
        }

        // ── Report builder ────────────────────────────────────────────────────

        public static string BuildPlainReport(ChromosomeAnalysisInfo info)
        {
            var diff  = Math.Abs(info.TotalCount - info.NormalCount);
            var dir   = info.TotalCount > info.NormalCount ? "thừa" : "thiếu";
            var countLabel = info.IsNormalCount
                ? "Bình thường"
                : $"Bất thường — {dir} {diff} NST";

            var groupStr = string.Join(", ",
                "ABCDEFG".Select(g => $"{g}:{info.DenverGroups.GetValueOrDefault(g.ToString())}"));

            var syndromeStr = info.SyndromeFlags.Count > 0
                ? string.Join("\n  • ", info.SyndromeFlags)
                : "Không phát hiện hội chứng đặc trưng";

            return string.Join("\n", new[]
            {
                $"1. Số NST đếm được: {info.TotalCount}",
                $"2. Đánh giá: {countLabel} (chuẩn {info.NormalCount} ± {info.Tolerance})",
                $"3. Mức độ rủi ro: {info.RiskLevel}",
                $"4. Giới tính ước tính: {info.SexEstimation} (Độ tin cậy: {info.SexConfidence})",
                $"5. Phân nhóm Denver: {groupStr}",
                $"6. Hội chứng nghi ngờ:",
                $"  • {syndromeStr}",
                "",
                $"Thống kê kích thước:",
                $"  Diện tích trung bình: {info.MeanArea:F3}",
                $"  Độ lệch chuẩn: {info.StdArea:F3}",
                $"  Hệ số biến thiên: {info.CvPercent:F1}%",
                "",
                "⚠ Kết quả AI hỗ trợ chẩn đoán — cần karyotype chuyên sâu để xác nhận.",
            });
        }

        // ── Denver expected comparison (for UI display) ────────────────────────
        public static Dictionary<string, int> GetExpected() => ExpectedCounts;
    }

    public class ChromosomeAnalysisInfo
    {
        public int TotalCount     { get; set; }
        public int NormalCount    { get; set; }
        public int Tolerance      { get; set; }
        public bool IsNormalCount { get; set; }
        public Dictionary<string, int> DenverGroups { get; set; } = new();
        public string SexEstimation  { get; set; } = "";
        public string SexConfidence  { get; set; } = "";
        public List<string> SyndromeFlags { get; set; } = new();
        public string RiskLevel      { get; set; } = "";
        public List<float> NormalizedAreas { get; set; } = new();
        public float MeanArea   { get; set; }
        public float StdArea    { get; set; }
        public float CvPercent  { get; set; }
    }
}
