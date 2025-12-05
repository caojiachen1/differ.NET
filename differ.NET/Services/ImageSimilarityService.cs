using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace differ.NET.Services;

/// <summary>
/// 图片相似度计算服务
/// 主要使用 DINOv3 深度学习模型，感知哈希算法(pHash)作为失败回退
/// </summary>
public class ImageSimilarityService : IDisposable
{
    private const int HashSize = 8;
    private const int HighFreqFactor = 4;
    private const int Size = HashSize * HighFreqFactor; // 32

    // 预计算的余弦值表
    private static readonly float[,] CosTable;
    private static readonly float C1;
    private static readonly float C2;

    // DINOv3 特征提取器（单例）
    private static readonly Lazy<DinoV3FeatureExtractor> _dinoExtractor = 
        new(() => new DinoV3FeatureExtractor(), isThreadSafe: true);

    private static bool _dinoInitialized;
    private static readonly object _initLock = new();

    static ImageSimilarityService()
    {
        // 预计算余弦值，避免重复计算
        CosTable = new float[Size, Size];
        for (int i = 0; i < Size; i++)
        {
            for (int j = 0; j < Size; j++)
            {
                CosTable[i, j] = MathF.Cos((2 * i + 1) * j * MathF.PI / (2 * Size));
            }
        }
        C1 = MathF.Sqrt(1.0f / Size);
        C2 = MathF.Sqrt(2.0f / Size);
    }

    /// <summary>
    /// 获取 DINOv3 提取器实例
    /// </summary>
    public static DinoV3FeatureExtractor DinoExtractor => _dinoExtractor.Value;

    /// <summary>
    /// 初始化 DINOv3 模型
    /// </summary>
    /// <param name="modelPath">可选的模型路径</param>
    /// <returns>是否初始化成功</returns>
    public static bool InitializeDino(string? modelPath = null)
    {
        if (_dinoInitialized)
            return _dinoExtractor.Value.IsInitialized;

        lock (_initLock)
        {
            if (_dinoInitialized)
                return _dinoExtractor.Value.IsInitialized;

            _dinoInitialized = true;
            return _dinoExtractor.Value.Initialize(modelPath);
        }
    }

    /// <summary>
    /// 检查 DINOv3 是否可用
    /// </summary>
    public static bool IsDinoAvailable => _dinoExtractor.IsValueCreated && _dinoExtractor.Value.IsInitialized;

    /// <summary>
    /// 提取 DINOv3 特征向量
    /// </summary>
    /// <param name="imagePath">图片路径</param>
    /// <returns>特征向量，失败返回 null</returns>
    public static float[]? ExtractDinoFeatures(string imagePath)
    {
        if (!IsDinoAvailable)
        {
            InitializeDino();
        }

        if (!IsDinoAvailable)
            return null;

        return _dinoExtractor.Value.ExtractFeatures(imagePath);
    }

    /// <summary>
    /// 计算两个图片的相似度 - 优先使用 DINOv3，失败时回退到 pHash
    /// </summary>
    /// <param name="features1">图片1的 DINO 特征</param>
    /// <param name="features2">图片2的 DINO 特征</param>
    /// <param name="hash1">图片1的感知哈希（回退用）</param>
    /// <param name="hash2">图片2的感知哈希（回退用）</param>
    /// <returns>相似度 (0-100)</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalculateSimilarity(float[]? features1, float[]? features2, ulong hash1, ulong hash2)
    {
        // 优先使用 DINOv3 特征
        if (features1 != null && features2 != null && features1.Length == features2.Length)
        {
            return DinoV3FeatureExtractor.CalculateCosineSimilarity(features1, features2);
        }

        // 回退到 pHash
        return CalculateSimilarity(hash1, hash2);
    }

    /// <summary>
    /// 计算图片的感知哈希值（优化版）- 作为失败回退
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ulong ComputePerceptualHash(string imagePath)
    {
        try
        {
            using var image = Image.Load<L8>(imagePath); // 直接加载为灰度图，更快
            
            // 1. 缩小图片到32x32
            image.Mutate(x => x.Resize(Size, Size));

            // 2. 获取像素亮度值
            Span<float> pixels = stackalloc float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    pixels[y * Size + x] = image[x, y].PackedValue;
                }
            }

            // 3. 计算DCT（只计算左上角8x8）
            Span<float> lowFreq = stackalloc float[HashSize * HashSize];
            ComputeDCTOptimized(pixels, lowFreq);

            // 4. 计算中值
            Span<float> sorted = stackalloc float[HashSize * HashSize];
            lowFreq.CopyTo(sorted);
            sorted.Sort();
            float median = sorted[sorted.Length / 2];

            // 5. 生成哈希
            ulong hash = 0;
            for (int i = 0; i < 64; i++)
            {
                if (lowFreq[i] > median)
                {
                    hash |= (1UL << i);
                }
            }

            return hash;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 优化的DCT计算 - 只计算需要的8x8低频分量
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ComputeDCTOptimized(ReadOnlySpan<float> pixels, Span<float> result)
    {
        int idx = 0;
        for (int u = 0; u < HashSize; u++)
        {
            float cu = u == 0 ? C1 : C2;
            for (int v = 0; v < HashSize; v++)
            {
                float cv = v == 0 ? C1 : C2;
                float sum = 0;
                
                for (int x = 0; x < Size; x++)
                {
                    float cosU = CosTable[x, u];
                    int rowOffset = x * Size;
                    for (int y = 0; y < Size; y++)
                    {
                        sum += pixels[rowOffset + y] * cosU * CosTable[y, v];
                    }
                }
                
                result[idx++] = cu * cv * sum;
            }
        }
    }

    /// <summary>
    /// 计算两个哈希之间的汉明距离（使用SIMD优化）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HammingDistance(ulong hash1, ulong hash2)
    {
        return BitOperations.PopCount(hash1 ^ hash2);
    }

    /// <summary>
    /// 计算两个图片的相似度(0-100%) - 基于 pHash
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalculateSimilarity(ulong hash1, ulong hash2)
    {
        int distance = HammingDistance(hash1, hash2);
        // 64位哈希，距离最大为64
        return (1.0 - distance / 64.0) * 100;
    }

    public void Dispose()
    {
        if (_dinoExtractor.IsValueCreated)
        {
            _dinoExtractor.Value.Dispose();
        }
    }
}
