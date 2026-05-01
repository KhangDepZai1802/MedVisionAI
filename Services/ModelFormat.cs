using System.IO;

namespace MedVisionAI.Services
{
    public enum ModelFormat
    {
        Unknown,
        Onnx,
        PyTorch,
        Keras,
        TfSavedModel,
    }

    public static class ModelFormatDetector
    {
        public static ModelFormat Detect(string? path)
        {
            if (string.IsNullOrEmpty(path)) return ModelFormat.Unknown;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".onnx"          => ModelFormat.Onnx,
                ".pt" or ".pth"  => ModelFormat.PyTorch,
                ".h5"            => ModelFormat.Keras,
                ".pb"            => ModelFormat.TfSavedModel,
                _                => ModelFormat.Unknown,
            };
        }
    }
}
