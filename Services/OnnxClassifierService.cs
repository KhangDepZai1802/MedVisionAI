using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedVisionAI.Models;
using OpenCvSharp;

namespace MedVisionAI.Services
{
    public sealed class OnnxClassifierService
    {
        public static readonly string[] AnomalyClassNames =
        {
            "Binh thuong (46,XX)", "Binh thuong (46,XY)",
            "Trisomy 21 - Down", "Trisomy 13 - Patau", "Trisomy 18 - Edwards",
            "Monosomy X - Turner", "47,XXX", "47,XXY - Klinefelter", "47,XYY",
            "Del 5p - Cri du chat", "Del 22q11 - DiGeorge", "Del 15q - Prader-Willi/Angelman",
            "Triploidy", "Tetraploidy", "Mosaic Down", "Mosaic Turner",
            "Inv dup 15", "Ring chromosome", "Marker chromosome",
            "Del 1p36", "Dup 22q11", "Lech boi khac",
            "Chuyen doan can bang", "Chuyen doan khong can bang"
        };

        public static readonly string[] BloodCancerClassNames =
        {
            "Benign", "Early", "Pre", "Pro"
        };

        public static readonly string[] MalariaClassNames =
        {
            "Parasitized", "Uninfected"
        };

        private readonly string _modelPath;
        private readonly IReadOnlyList<string> _classNames;

        public OnnxClassifierService(
            string modelPath,
            IReadOnlyList<string> classNames)
        {
            _modelPath = modelPath;
            _classNames = classNames;
        }

        public AnomalyPrediction Predict(string imagePath, int index = 0)
        {
            string modelWork = EnsureAscii(_modelPath, out string? tempModel);
            string imageWork = EnsureAscii(imagePath, out string? tempImage);

            try
            {
                using var img = Cv2.ImRead(imageWork, ImreadModes.Color);
                if (img.Empty())
                    throw new Exception($"Cannot read image: {imagePath}");

                using var engine = new OnnxInferenceEngine(
                    modelWork, OnnxPreprocessMode.ImageNet);
                using var outputs = engine.Run(img);
                return OutputParser.ParseClassification(outputs, _classNames, index);
            }
            finally
            {
                if (tempModel != null && File.Exists(tempModel))
                    File.Delete(tempModel);
                if (tempImage != null && File.Exists(tempImage))
                    File.Delete(tempImage);
            }
        }

        public List<AnomalyPrediction> PredictBatch(IReadOnlyList<string> imagePaths)
        {
            var predictions = new List<AnomalyPrediction>();
            for (int i = 0; i < imagePaths.Count; i++)
                predictions.Add(Predict(imagePaths[i], i));
            return predictions;
        }

        private static string EnsureAscii(string path, out string? tempPath)
        {
            tempPath = null;
            if (path.All(c => c <= 127)) return path;
            string ext = Path.GetExtension(path);
            tempPath = Path.Combine(
                Path.GetTempPath(), $"mv_onnx_cls_{Guid.NewGuid():N}{ext}");
            File.Copy(path, tempPath, overwrite: true);
            return tempPath;
        }
    }
}
