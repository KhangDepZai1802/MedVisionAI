namespace MedVisionAI.Models
{
    public enum MedicalClassifierKind
    {
        BloodCancer,
        Malaria
    }

    public class MedicalClassifierResult
    {
        public string ModuleTitle { get; set; } = "";
        public string Label { get; set; } = "";
        public string DisplayLabel { get; set; } = "";
        public float Confidence { get; set; }
        public int ClassIndex { get; set; }
        public bool IsNormal { get; set; }
        public string RiskLevel { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public byte[]? AnnotatedImageBytes { get; set; }
        public string ReportPlain { get; set; } = "";
    }
}
