namespace MedVisionAI.Services;
using System.IO;

    /// <summary>
    /// Định dạng file model AI được hỗ trợ.
    /// </summary>
    public enum ModelFormat
    {
        Unknown,
        Onnx,       // .onnx  — C# native via Microsoft.ML.OnnxRuntime
        PyTorch,    // .pt / .pth — cần Python bridge
        Keras,      // .h5        — cần Python bridge
        TfSavedModel, // thư mục saved_model hoặc .pb
    }

    public static class ModelFormatDetector
    {
        /// <summary>
        /// Phát hiện định dạng model từ đường dẫn file.
        /// App không cần biết model được train như thế nào —
        /// chỉ nhìn vào extension để chọn runtime phù hợp.
        /// </summary>
        public static ModelFormat Detect(string? path)
        {
            if (string.IsNullOrEmpty(path)) return ModelFormat.Unknown;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".onnx"        => ModelFormat.Onnx,
                ".pt" or ".pth" => ModelFormat.PyTorch,
                ".h5"          => ModelFormat.Keras,
                ".pb"          => ModelFormat.TfSavedModel,
                _              => ModelFormat.Unknown,
            };
        }
    }

