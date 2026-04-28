using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace MedVisionAI.Services
{
    /// <summary>
    /// Chạy inference trực tiếp bằng ONNX Runtime trong C#.
    /// Không cần Python. Hỗ trợ mọi model được xuất sang .onnx
    /// (PyTorch → torch.onnx.export, TF → tf2onnx, Keras → tf2onnx, v.v.)
    ///
    /// Tự động thích nghi với output shape của model:
    ///   - Detection (YOLO-style): [1, num_boxes, 5+classes] hoặc [1, 5+classes, num_boxes]
    ///   - Segmentation:           output thêm masks tensor
    ///   - Classification:         [1, num_classes]
    /// </summary>
    public class OnnxInferenceEngine : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly int[]  _inputShape;   // [batch, channel, H, W]
        private readonly int    _inputH;
        private readonly int    _inputW;

        public string ModelPath { get; }
        public OnnxOutputType OutputType { get; }

        public OnnxInferenceEngine(string modelPath)
        {
            ModelPath = modelPath;

            var opts = new SessionOptions();
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            // Ưu tiên GPU nếu có, fallback về CPU
            try { opts.AppendExecutionProvider_CUDA(); } catch { /* no GPU, dùng CPU */ }

            _session   = new InferenceSession(modelPath, opts);

            // Lấy thông tin input đầu tiên
            var inputMeta = _session.InputMetadata.First();
            _inputName  = inputMeta.Key;
            _inputShape = inputMeta.Value.Dimensions;

            // Xác định H, W từ shape
            // Thông thường: [batch=1, C=3, H, W] hoặc [batch=1, H, W, C=3]
            if (_inputShape.Length == 4)
            {
                // NCHW format (PyTorch default)
                if (_inputShape[1] is 1 or 3)
                {
                    _inputH = _inputShape[2] > 0 ? _inputShape[2] : 640;
                    _inputW = _inputShape[3] > 0 ? _inputShape[3] : 640;
                }
                // NHWC format (TF/Keras default)
                else
                {
                    _inputH = _inputShape[1] > 0 ? _inputShape[1] : 640;
                    _inputW = _inputShape[2] > 0 ? _inputShape[2] : 640;
                }
            }
            else
            {
                _inputH = 640;
                _inputW = 640;
            }

            // Phát hiện loại output
            OutputType = DetectOutputType();
        }

        // ── Detect output type ────────────────────────────────────────────────

        private OnnxOutputType DetectOutputType()
        {
            var outputs = _session.OutputMetadata;

            // Có 2+ outputs → segmentation (boxes + masks)
            if (outputs.Count >= 2)
                return OnnxOutputType.Segmentation;

            // 1 output: nhìn vào shape
            var outShape = outputs.First().Value.Dimensions;
            if (outShape.Length == 2)
                return OnnxOutputType.Classification;  // [batch, num_classes]

            return OnnxOutputType.Detection;   // [batch, num_boxes, ...] hoặc tương tự
        }

        // ── Main inference ────────────────────────────────────────────────────

        /// <summary>
        /// Chạy inference. Trả về raw output tensors.
        /// InferenceService sẽ parse kết quả tuỳ theo OutputType.
        /// </summary>
        public IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(Mat image)
        {
            var tensor = PreprocessImage(image);
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            return _session.Run(inputs);
        }

        // ── Preprocessing ─────────────────────────────────────────────────────

        /// <summary>
        /// Resize + normalize ảnh thành float tensor [1, 3, H, W].
        /// Dùng mean/std chuẩn ImageNet vì hầu hết model đều dùng.
        /// </summary>
        private DenseTensor<float> PreprocessImage(Mat src)
        {
            using var resized = new Mat();
            Cv2.Resize(src, resized, new OpenCvSharp.Size(_inputW, _inputH));

            using var rgb = new Mat();
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            // Normalize về [0,1] rồi áp ImageNet mean/std
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std  = { 0.229f, 0.224f, 0.225f };

            int h = _inputH, w = _inputW;
            var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var px = rgb.At<Vec3b>(y, x);
                    tensor[0, 0, y, x] = (px.Item0 / 255f - mean[0]) / std[0]; // R
                    tensor[0, 1, y, x] = (px.Item1 / 255f - mean[1]) / std[1]; // G
                    tensor[0, 2, y, x] = (px.Item2 / 255f - mean[2]) / std[2]; // B
                }
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
}
