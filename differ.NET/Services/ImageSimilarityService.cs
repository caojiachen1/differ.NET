using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace differ.NET.Services;

/// <summary>
/// 图片相似度计算服务，使用感知哈希算法(pHash)
/// </summary>
public class ImageSimilarityService
{
    private const int HashSize = 8;
    private const int HighFreqFactor = 4;

    /// <summary>
    /// 计算图片的感知哈希值
    /// </summary>
    public static ulong ComputePerceptualHash(string imagePath)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imagePath);
            
            // 1. 缩小图片到32x32用于DCT
            int size = HashSize * HighFreqFactor;
            image.Mutate(x => x.Resize(size, size).Grayscale());

            // 2. 获取像素亮度值
            float[,] pixels = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y, x] = image[x, y].R;
                }
            }

            // 3. 计算DCT
            float[,] dct = ComputeDCT(pixels, size);

            // 4. 取左上角8x8的低频分量
            float[] lowFreq = new float[HashSize * HashSize];
            int idx = 0;
            for (int y = 0; y < HashSize; y++)
            {
                for (int x = 0; x < HashSize; x++)
                {
                    lowFreq[idx++] = dct[y, x];
                }
            }

            // 5. 计算中值
            float[] sorted = (float[])lowFreq.Clone();
            Array.Sort(sorted);
            float median = sorted[sorted.Length / 2];

            // 6. 生成哈希
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
    /// 简化版DCT计算
    /// </summary>
    private static float[,] ComputeDCT(float[,] pixels, int size)
    {
        float[,] dct = new float[size, size];
        float c1 = MathF.Sqrt(1.0f / size);
        float c2 = MathF.Sqrt(2.0f / size);

        for (int u = 0; u < size; u++)
        {
            for (int v = 0; v < size; v++)
            {
                float sum = 0;
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        sum += pixels[x, y] *
                               MathF.Cos((2 * x + 1) * u * MathF.PI / (2 * size)) *
                               MathF.Cos((2 * y + 1) * v * MathF.PI / (2 * size));
                    }
                }
                float cu = u == 0 ? c1 : c2;
                float cv = v == 0 ? c1 : c2;
                dct[u, v] = cu * cv * sum;
            }
        }

        return dct;
    }

    /// <summary>
    /// 计算两个哈希之间的汉明距离
    /// </summary>
    public static int HammingDistance(ulong hash1, ulong hash2)
    {
        ulong xor = hash1 ^ hash2;
        int distance = 0;
        while (xor != 0)
        {
            distance += (int)(xor & 1);
            xor >>= 1;
        }
        return distance;
    }

    /// <summary>
    /// 计算两个图片的相似度(0-100%)
    /// </summary>
    public static double CalculateSimilarity(ulong hash1, ulong hash2)
    {
        int distance = HammingDistance(hash1, hash2);
        // 64位哈希，距离最大为64
        return (1.0 - distance / 64.0) * 100;
    }
}
