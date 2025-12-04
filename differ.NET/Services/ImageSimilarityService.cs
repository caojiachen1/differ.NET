using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace differ.NET.Services;

/// <summary>
/// 图片相似度计算服务，使用感知哈希算法(pHash)
/// 已优化：预计算余弦值、使用SIMD加速汉明距离计算
/// </summary>
public class ImageSimilarityService
{
    private const int HashSize = 8;
    private const int HighFreqFactor = 4;
    private const int Size = HashSize * HighFreqFactor; // 32

    // 预计算的余弦值表
    private static readonly float[,] CosTable;
    private static readonly float C1;
    private static readonly float C2;

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
    /// 计算图片的感知哈希值（优化版）
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
    /// 计算两个图片的相似度(0-100%)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalculateSimilarity(ulong hash1, ulong hash2)
    {
        int distance = HammingDistance(hash1, hash2);
        // 64位哈希，距离最大为64
        return (1.0 - distance / 64.0) * 100;
    }
}
