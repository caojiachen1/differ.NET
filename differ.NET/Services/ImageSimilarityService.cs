using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace differ.NET.Services;

/// <summary>
/// 图片相似度计算服务 - 仅使用DINOv3深度学习模型
/// </summary>
public class ImageSimilarityService : IDisposable
{
    // DINOv3 特征提取器（单例模式）
    private static readonly Lazy<DinoV3FeatureExtractor> _dinoExtractor = 
        new(() => new DinoV3FeatureExtractor(), isThreadSafe: true);

    private static bool _dinoInitialized;
    private static readonly object _initLock = new();

    /// <summary>
    /// 获取 DINOv3 提取器实例
    /// </summary>
    public static DinoV3FeatureExtractor DinoExtractor => _dinoExtractor.Value;

    /// <summary>
    /// 异步初始化 DINOv3 模型（如果模型不存在会自动下载）
    /// </summary>
    /// <param name="modelPath">可选的模型路径</param>
    /// <param name="progressCallback">下载进度回调</param>
    /// <returns>是否初始化成功</returns>
    public static async Task<bool> InitializeDinoAsync(string? modelPath = null, Action<string, double>? progressCallback = null)
    {
        Console.WriteLine($"[ImageSimilarity] InitializeDinoAsync called, _dinoInitialized={_dinoInitialized}");
        
        if (_dinoInitialized && _dinoExtractor.Value.IsInitialized)
        {
            Console.WriteLine($"[ImageSimilarity] Already initialized, returning true");
            return true;
        }

        // 订阅下载进度事件
        if (progressCallback != null)
        {
            _dinoExtractor.Value.DownloadProgressChanged += progressCallback;
        }

        try
        {
            Console.WriteLine($"[ImageSimilarity] Calling DinoExtractor.InitializeAsync...");
            var initResult = await _dinoExtractor.Value.InitializeAsync(modelPath);
            Console.WriteLine($"[ImageSimilarity] DinoExtractor.InitializeAsync returned: {initResult}");
            
            if (initResult)
            {
                _dinoInitialized = true;
                var success = "DINOv3 Successfully initialized (async)";
                Console.WriteLine($"[ImageSimilarity] {success}");
                ErrorLogService.LogInfo(success);
            }
            else
            {
                Console.WriteLine($"[ImageSimilarity] DINOv3 async initialization failed!");
                if (!string.IsNullOrEmpty(_dinoExtractor.Value.LastError))
                {
                    Console.WriteLine($"[ImageSimilarity] Last error: {_dinoExtractor.Value.LastError}");
                }
            }
            
            return initResult;
        }
        finally
        {
            // 取消订阅
            if (progressCallback != null)
            {
                _dinoExtractor.Value.DownloadProgressChanged -= progressCallback;
            }
        }
    }

    /// <summary>
    /// 初始化 DINOv3 模型
    /// </summary>
    /// <param name="modelPath">可选的模型路径</param>
    /// <returns>是否初始化成功</returns>
    public static bool InitializeDino(string? modelPath = null)
    {
        Console.WriteLine($"[ImageSimilarity] InitializeDino called, _dinoInitialized={_dinoInitialized}, IsValueCreated={_dinoExtractor.IsValueCreated}");
        
        if (_dinoInitialized)
        {
            var result = _dinoExtractor.Value.IsInitialized;
            Console.WriteLine($"[ImageSimilarity] Already initialized, returning: {result}");
            return result;
        }

        lock (_initLock)
        {
            Console.WriteLine($"[ImageSimilarity] In lock, _dinoInitialized={_dinoInitialized}");
            
            if (_dinoInitialized)
            {
                var result = _dinoExtractor.Value.IsInitialized;
                Console.WriteLine($"[ImageSimilarity] Already initialized (in lock), returning: {result}");
                return result;
            }

            _dinoInitialized = true;
            Console.WriteLine($"[ImageSimilarity] Calling DinoExtractor.Initialize...");
            var initResult = _dinoExtractor.Value.Initialize(modelPath);
            Console.WriteLine($"[ImageSimilarity] DinoExtractor.Initialize returned: {initResult}");
            
            if (!initResult)
            {
                Console.WriteLine($"[ImageSimilarity] DINOv3 initialization failed!");
                if (!string.IsNullOrEmpty(_dinoExtractor.Value.LastError))
                {
                    Console.WriteLine($"[ImageSimilarity] Last error: {_dinoExtractor.Value.LastError}");
                }
            }
            else
            {
                var success = $"DINOv3 Successfully initialized";
                Console.WriteLine($"[ImageSimilarity] {success}");
                ErrorLogService.LogInfo(success);
            }
            
            return initResult;
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
            var message = "DINOv3 not available, attempting initialization...";
            Console.WriteLine($"[ImageSimilarity] {message}");
            ErrorLogService.LogInfo(message);
            InitializeDino();
        }

        if (!IsDinoAvailable)
        {
            var error = "DINOv3 initialization failed, cannot extract features";
            Console.WriteLine($"[ImageSimilarity] {error}");
            ErrorLogService.LogError(error);
            return null;
        }

        try
        {
            Console.WriteLine($"[ImageSimilarity] Starting DINOv3 feature extraction for: {imagePath}");
            var features = _dinoExtractor.Value.ExtractFeatures(imagePath);
            
            if (features == null)
            {
                var error = $"DINOv3 feature extraction failed for {imagePath}";
                Console.WriteLine($"[ImageSimilarity] {error}");
                ErrorLogService.LogError(error);
                
                if (!string.IsNullOrEmpty(_dinoExtractor.Value.LastError))
                {
                    Console.WriteLine($"[ImageSimilarity] DINOv3 detailed error: {_dinoExtractor.Value.LastError}");
                    ErrorLogService.LogError($"DINOv3 detailed error: {_dinoExtractor.Value.LastError}");
                }
            }
            else
            {
                Console.WriteLine($"[ImageSimilarity] Successfully extracted {features.Length} DINOv3 features from {imagePath}");
                Console.WriteLine($"[ImageSimilarity] Feature vector sample (first 5 values): {string.Join(", ", features.Take(5))}");
                var info = $"Successfully extracted DINOv3 features from {imagePath}";
                ErrorLogService.LogInfo(info);
            }
            
            return features;
        }
        catch (Exception ex)
        {
            var error = $"DINOv3 feature extraction error for {imagePath}: {ex.Message}";
            Console.WriteLine($"[ImageSimilarity] {error}");
            ErrorLogService.LogError(error, ex);
            return null;
        }
    }

    /// <summary>
    /// 计算两个图片的相似度 - 仅使用DINOv3特征
    /// </summary>
    /// <param name="features1">图片1的 DINO 特征</param>
    /// <param name="features2">图片2的 DINO 特征</param>
    /// <returns>相似度 (0-100)</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CalculateSimilarity(float[]? features1, float[]? features2)
    {
        Console.WriteLine($"[ImageSimilarity] CalculateSimilarity called");
        Console.WriteLine($"[ImageSimilarity] Features1: {(features1 != null ? $"Length {features1.Length}" : "null")}");
        Console.WriteLine($"[ImageSimilarity] Features2: {(features2 != null ? $"Length {features2.Length}" : "null")}");
        
        // 只使用 DINOv3 特征进行计算
        if (features1 != null && features2 != null && features1.Length == features2.Length)
        {
            Console.WriteLine($"[ImageSimilarity] Both features valid, calculating cosine similarity");
            Console.WriteLine($"[ImageSimilarity] Feature1 sample (first 5): {string.Join(", ", features1.Take(5))}");
            Console.WriteLine($"[ImageSimilarity] Feature2 sample (first 5): {string.Join(", ", features2.Take(5))}");
            
            var similarity = DinoV3FeatureExtractor.CalculateCosineSimilarity(features1, features2);
            
            Console.WriteLine($"[ImageSimilarity] Raw cosine similarity: {similarity}");
            var info = $"Using DINOv3 similarity: {similarity:F2}%";
            Console.WriteLine($"[ImageSimilarity] {info}");
            ErrorLogService.LogInfo(info);
            return similarity;
        }

        // 如果DINOv3特征不可用，这是一个严重错误
        var error = $"DINOv3 features not available for similarity calculation. Features1: {(features1 != null ? features1.Length : 0)}, Features2: {(features2 != null ? features2.Length : 0)}";
        Console.WriteLine($"[ImageSimilarity] {error}");
        ErrorLogService.LogError(error);
        return 0;
    }

    public void Dispose()
    {
        if (_dinoExtractor.IsValueCreated)
        {
            _dinoExtractor.Value.Dispose();
        }
    }
}
