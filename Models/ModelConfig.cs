namespace MedVisionAI.Models
{
    /// <summary>
    /// Cấu hình model AI và thông số phân tích cho module NST.
    /// </summary>
    public class ModelConfig
    {
        /// <summary>Đường dẫn model phân đoạn NST (YOLO/detection). Bắt buộc.</summary>
        public string? SegmentationModelPath { get; set; }

        /// <summary>Đường dẫn model phân tách NST chồng lấn. Tuỳ chọn.</summary>
        public string? OverlapModelPath { get; set; }

        /// <summary>Đường dẫn model xác định bất thường số lượng NST. Tuỳ chọn.</summary>
        public string? AnomalyModelPath { get; set; }

        /// <summary>Số NST chuẩn (2n). Mặc định 46.</summary>
        public int NormalChromosomeCount { get; set; } = 46;

        /// <summary>Sai số cho phép (±). Mặc định 1.</summary>
        public int Tolerance { get; set; } = 1;

        public ModelConfig Clone() => new()
        {
            SegmentationModelPath = SegmentationModelPath,
            OverlapModelPath      = OverlapModelPath,
            AnomalyModelPath      = AnomalyModelPath,
            NormalChromosomeCount = NormalChromosomeCount,
            Tolerance             = Tolerance,
        };
    }
}
