namespace MedVisionAI.Models
{
    /// <summary>
    /// Kết quả phân tích NST trả về từ InferenceService.
    /// </summary>
    public class AnalysisResult
    {
        // ── Kết quả cơ bản ──────────────────────────────────────────────────
        /// <summary>Tổng số NST đếm được.</summary>
        public int ChromosomeCount { get; set; }

        /// <summary>True nếu số NST nằm trong ngưỡng bình thường.</summary>
        public bool IsNormalCount { get; set; }

        /// <summary>Mức rủi ro: "Bình thường" | "Cần theo dõi" | "Nguy cơ cao"</summary>
        public string RiskLevel { get; set; } = "Bình thường";

        // ── Phân nhóm Denver ────────────────────────────────────────────────
        /// <summary>Số NST theo từng nhóm Denver A-G.</summary>
        public Dictionary<string, int> DenverGroups { get; set; } = new();

        // ── Giới tính ────────────────────────────────────────────────────────
        public string SexEstimation  { get; set; } = "Không xác định";
        public string SexConfidence  { get; set; } = "Thấp";

        // ── Hội chứng ────────────────────────────────────────────────────────
        public List<string> SyndromeFlags { get; set; } = new();

        // ── Bounding boxes & masks (raw output từ model) ─────────────────────
        /// <summary>Danh sách bounding box [x1,y1,x2,y2,confidence].</summary>
        public List<float[]> BoundingBoxes { get; set; } = new();

        /// <summary>Danh sách area tương đối của từng NST (0.0 – 1.0).</summary>
        public List<float> NormalizedAreas { get; set; } = new();

        // ── Ảnh kết quả ──────────────────────────────────────────────────────
        /// <summary>Ảnh annotated (BGR PNG bytes) để hiển thị trong UI.</summary>
        public byte[]? AnnotatedImageBytes { get; set; }

        // ── Báo cáo text ─────────────────────────────────────────────────────
        /// <summary>Báo cáo plain text để lưu file / CSV.</summary>
        public string ReportPlain { get; set; } = "";

        /// <summary>Báo cáo HTML để hiển thị chi tiết (dùng cho WebView2 sau).</summary>
        public string ReportHtml  { get; set; } = "";

        // ── Thống kê kích thước ──────────────────────────────────────────────
        public float MeanArea   { get; set; }
        public float StdArea    { get; set; }
        public float CvPercent  { get; set; }
    }

    /// <summary>Item trong lịch sử phân tích (hiển thị ở HomeWindow).</summary>
    public class HistoryItem
    {
        public string FileName    { get; set; } = "";
        public string CountText   { get; set; } = "";
        public string Result      { get; set; } = "";
        public string Dot         { get; set; } = "●";
        public string DotColor    { get; set; } = "#9090A8";
        public string ResultColor { get; set; } = "#9090A8";
    }
}
