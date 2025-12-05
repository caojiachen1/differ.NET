using System;
using System.Linq;

namespace differ.NET.Services;

/// <summary>
/// 缓存的DINOv3图片特征数据
/// </summary>
public class CachedImageFeatures
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long LastModified { get; set; }
    public byte[] DinoFeaturesBytes { get; set; } = Array.Empty<byte>();
    public int DinoFeatureLength { get; set; }

    /// <summary>
    /// 获取DINO特征向量
    /// </summary>
    public float[] GetDinoFeatures()
    {
        Console.WriteLine($"[CachedImageFeatures] GetDinoFeatures called");
        Console.WriteLine($"[CachedImageFeatures] DinoFeaturesBytes length: {DinoFeaturesBytes?.Length ?? 0}");
        Console.WriteLine($"[CachedImageFeatures] DinoFeatureLength: {DinoFeatureLength}");
        
        if (DinoFeaturesBytes == null || DinoFeatureLength == 0 || DinoFeaturesBytes.Length == 0)
        {
            Console.WriteLine($"[CachedImageFeatures] Returning empty array");
            return Array.Empty<float>();
        }

        Console.WriteLine($"[CachedImageFeatures] Converting bytes to float array");
        var features = new float[DinoFeatureLength];
        Buffer.BlockCopy(DinoFeaturesBytes, 0, features, 0, DinoFeaturesBytes.Length);
        
        Console.WriteLine($"[CachedImageFeatures] Converted {features.Length} features");
        Console.WriteLine($"[CachedImageFeatures] Feature sample (first 5): {string.Join(", ", features.Take(5))}");
        
        return features;
    }
}
