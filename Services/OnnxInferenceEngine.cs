using System;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace MedVisionAI.Services
{
    public class OnnxInferenceEngine : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string           _inputName;
        private readonly int              _inputH;
        private readonly int              _inputW;
        private readonly OnnxPreprocessMode _defaultPreprocessMode;

        public OnnxOutputType OutputType { get; }
        public int InputHeight => _inputH;
        public int InputWidth  => _inputW;

        public OnnxInferenceEngine(
            string modelPath,
            OnnxPreprocessMode preprocessMode = OnnxPreprocessMode.Auto)
        {
            // modelPath must already be ASCII-safe (caller's responsibility)
            var opts = new SessionOptions();
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            // Try GPU, fall back to CPU silently
            try { opts.AppendExecutionProvider_CUDA(); } catch { }

            _session = new InferenceSession(modelPath, opts);

            var inputMeta = _session.InputMetadata.First();
            _inputName = inputMeta.Key;
            var shape  = inputMeta.Value.Dimensions;

            // Determine H/W from shape [N,C,H,W] or [N,H,W,C]
            if (shape.Length == 4)
            {
                bool nchw = shape[1] is 1 or 3;
                _inputH = (nchw ? shape[2] : shape[1]) is > 0 and var fh ? fh : 640;
                _inputW = (nchw ? shape[3] : shape[2]) is > 0 and var fw ? fw : 640;
            }
            else
            {
                _inputH = 640;
                _inputW = 640;
            }

            OutputType = DetectOutputType();
            _defaultPreprocessMode = preprocessMode == OnnxPreprocessMode.Auto
                ? InferPreprocessMode(OutputType)
                : preprocessMode;
        }

        private OnnxOutputType DetectOutputType()
        {
            var outputs = _session.OutputMetadata;
            if (outputs.Count >= 2) return OnnxOutputType.Segmentation;
            var outShape = outputs.First().Value.Dimensions;
            if (outShape.Length == 2) return OnnxOutputType.Classification;
            return OnnxOutputType.Detection;
        }

        public IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(Mat image)
        {
            var tensor = Preprocess(image, _defaultPreprocessMode);
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            return _session.Run(inputs);
        }

        private static OnnxPreprocessMode InferPreprocessMode(OnnxOutputType outputType)
            => outputType == OnnxOutputType.Classification
                ? OnnxPreprocessMode.ImageNet
                : OnnxPreprocessMode.UnitScale;

        private DenseTensor<float> Preprocess(Mat src, OnnxPreprocessMode mode)
        {
            using var resized = new Mat();
            Cv2.Resize(src, resized, new OpenCvSharp.Size(_inputW, _inputH));

            using var rgb = new Mat();
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std  = { 0.229f, 0.224f, 0.225f };

            int h = _inputH, w = _inputW;
            var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var px = rgb.At<Vec3b>(y, x);
                float r = px.Item0 / 255f;
                float g = px.Item1 / 255f;
                float b = px.Item2 / 255f;

                if (mode == OnnxPreprocessMode.ImageNet)
                {
                    r = (r - mean[0]) / std[0];
                    g = (g - mean[1]) / std[1];
                    b = (b - mean[2]) / std[2];
                }

                tensor[0, 0, y, x] = r;
                tensor[0, 1, y, x] = g;
                tensor[0, 2, y, x] = b;
            }
            return tensor;
        }

        public void Dispose() => _session.Dispose();
    }

    public enum OnnxOutputType
    {
        Detection,
        Segmentation,
        Classification,
    }

    public enum OnnxPreprocessMode
    {
        Auto,
        UnitScale,
        ImageNet,
    }
}
