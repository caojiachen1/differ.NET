using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace differ.NET.Services;

/// <summary>
/// DINOv3 特征提取服务，使用 ONNX 模型进行图像特征提取
/// 支持 GPU 加速，失败时回退到 CPU
/// </summary>
public class DinoV3FeatureExtractor : IDisposable
{
    private InferenceSession? _session;
    private readonly object _sessionLock = new();
    private bool _isInitialized;
    private bool _disposed;

    // DINOv3 模型输入尺寸 (通常是 224x224 或 518x518)
    private const int InputWidth = 518;
    private const int InputHeight = 518;

    // ImageNet 标准化参数
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    private string? _modelPath;

    /// <summary>
    /// 获取模型是否已成功初始化
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// 初始化 ONNX 模型
    /// </summary>
    /// <param name="modelPath">模型文件路径</param>
    /// <returns>是否初始化成功</returns>
    public bool Initialize(string? modelPath = null)
    {
        if (_isInitialized)
            return true;

        lock (_sessionLock)
        {
            if (_isInitialized)
                return true;

            try
            {
                _modelPath = modelPath ?? FindModelPath();
                if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
                {
                    Console.WriteLine($"[DinoV3] Model file not found: {_modelPath}");
                    return false;
                }

                // 尝试使用 GPU，失败则回退到 CPU
                try
                {
                    var gpuOptions = new SessionOptions();
                    gpuOptions.AppendExecutionProvider_CUDA(0);
                    gpuOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    _session = new InferenceSession(_modelPath, gpuOptions);
                    Console.WriteLine("[DinoV3] Initialized with CUDA GPU acceleration");
                }
                catch
                {
                    // GPU 不可用，使用 CPU
                    var cpuOptions = new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        InterOpNumThreads = Environment.ProcessorCount,
                        IntraOpNumThreads = Environment.ProcessorCount
                    };
                    _session = new InferenceSession(_modelPath, cpuOptions);
                    Console.WriteLine("[DinoV3] Initialized with CPU");
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DinoV3] Failed to initialize: {ex.Message}");
                _session?.Dispose();
                _session = null;
                return false;
            }
        }
    }

    /// <summary>
    /// 查找模型文件路径
    /// </summary>
    private static string? FindModelPath()
    {
        // 尝试多个可能的路径
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "model_q4.onnx"),
            Path.Combine(AppContext.BaseDirectory, "Model", "model_q4.onnx"),
            Path.Combine(Directory.GetCurrentDirectory(), "Model", "model_q4.onnx"),
            // 开发时的相对路径
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "differ.NET", "Model", "model_q4.onnx"),
        };

        foreach (var path in possiblePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (File.Exists(normalizedPath))
            {
                Console.WriteLine($"[DinoV3] Found model at: {normalizedPath}");
                return normalizedPath;
            }
        }

        return possiblePaths[0]; // 返回默认路径用于错误消息
    }

    /// <summary>
    /// 提取图像特征向量
    /// </summary>
    /// <param name="imagePath">图像文件路径</param>
    /// <returns>特征向量，失败返回 null</returns>
    public float[]? ExtractFeatures(string imagePath)
    {
        if (!_isInitialized || _session == null)
            return null;

        try
        {
            // 加载和预处理图像
            using var image = Image.Load<Rgb24>(imagePath);

            // 调整尺寸到模型输入大小
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(InputWidth, InputHeight),
                Mode = ResizeMode.Stretch
            }));

            // 准备输入张量 (NCHW 格式: batch, channels, height, width)
            var inputTensor = PreprocessImage(image);

            // 运行推理
            var inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            using var results = _session.Run(inputs);
            var output = results.First().AsTensor<float>();

            // 提取特征向量并进行 L2 归一化
            var features = output.ToArray();
            NormalizeL2(features);

            return features;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DinoV3] Feature extraction failed for {imagePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 预处理图像为模型输入张量
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static DenseTensor<float> PreprocessImage(Image<Rgb24> image)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, InputHeight, InputWidth });

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < InputHeight; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < InputWidth; x++)
                {
                    var pixel = row[x];
                    // 归一化到 [0, 1] 然后应用 ImageNet 标准化
                    tensor[0, 0, y, x] = (pixel.R / 255f - Mean[0]) / Std[0]; // R
                    tensor[0, 1, y, x] = (pixel.G / 255f - Mean[1]) / Std[1]; // G
                    tensor[0, 2, y, x] = (pixel.B / 255f - Mean[2]) / Std[2]; // B
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// L2 归一化
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void NormalizeL2(Span<float> vector)
    {
        float sumSquares = 0;
        
        // 使用 SIMD 加速计算平方和
        int vectorSize = Vector<float>.Count;
        int i = 0;

        if (Vector.IsHardwareAccelerated && vector.Length >= vectorSize)
        {
            var sumVector = Vector<float>.Zero;
            for (; i <= vector.Length - vectorSize; i += vectorSize)
            {
                var v = new Vector<float>(vector.Slice(i, vectorSize));
                sumVector += v * v;
            }
            sumSquares = Vector.Sum(sumVector);
        }

        // 处理剩余元素
        for (; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        float norm = MathF.Sqrt(sumSquares);
        if (norm > float.Epsilon)
        {
            float invNorm = 1f / norm;
            for (i = 0; i < vector.Length; i++)
            {
                vector[i] *= invNorm;
            }
        }
    }

    /// <summary>
    /// 计算两个特征向量的余弦相似度
    /// </summary>
    /// <param name="features1">特征向量1</param>
    /// <param name="features2">特征向量2</param>
    /// <returns>相似度 (0-100)</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double CalculateCosineSimilarity(ReadOnlySpan<float> features1, ReadOnlySpan<float> features2)
    {
        if (features1.Length != features2.Length)
            return 0;

        float dotProduct = 0;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        // 使用 SIMD 加速点积计算
        if (Vector.IsHardwareAccelerated && features1.Length >= vectorSize)
        {
            var sumVector = Vector<float>.Zero;
            for (; i <= features1.Length - vectorSize; i += vectorSize)
            {
                var v1 = new Vector<float>(features1.Slice(i, vectorSize));
                var v2 = new Vector<float>(features2.Slice(i, vectorSize));
                sumVector += v1 * v2;
            }
            dotProduct = Vector.Sum(sumVector);
        }

        // 处理剩余元素
        for (; i < features1.Length; i++)
        {
            dotProduct += features1[i] * features2[i];
        }

        // 由于特征向量已经 L2 归一化，点积即为余弦相似度
        // 将 [-1, 1] 映射到 [0, 100]
        return (dotProduct + 1) * 50;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _session?.Dispose();
        _session = null;
        _isInitialized = false;
    }
}
