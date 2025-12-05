using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Graphics;
using differ.NET.Services;

namespace differ.NET.Android;

/// <summary>
/// 移动端优化的图片加载器，提供内存和性能优化
/// </summary>
public static class MobileOptimizedImageLoader
{
    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> _memoryCache = new();
    private static readonly object _cacheLock = new object();
    private const int MAX_CACHE_SIZE = 50; // 最大缓存数量
    private const int COMPRESS_QUALITY = 85; // JPEG压缩质量

    /// <summary>
    /// 加载优化后的缩略图
    /// </summary>
    /// <param name="filePath">图片文件路径</param>
    /// <param name="maxSize">最大尺寸（像素）</param>
    /// <param name="context">安卓上下文</param>
    /// <returns>优化后的Bitmap</returns>
    public static async Task<Bitmap?> LoadOptimizedThumbnailAsync(string filePath, int maxSize = 120)
    {
        try
        {
            // 检查缓存
            var cacheKey = $"{filePath}_{maxSize}";
            if (_memoryCache.TryGetValue(cacheKey, out var weakRef) && 
                weakRef.TryGetTarget(out var cachedBitmap) && 
                cachedBitmap != null && !cachedBitmap.IsRecycled)
            {
                return cachedBitmap;
            }

            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                return null;
            }

            // 加载优化后的图片
            var bitmap = await Task.Run(() => LoadAndOptimizeBitmap(filePath, maxSize));
            
            if (bitmap != null)
            {
                // 添加到缓存
                lock (_cacheLock)
                {
                    CleanupCacheIfNeeded();
                    _memoryCache[cacheKey] = new WeakReference<Bitmap>(bitmap);
                }
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MobileOptimizedImageLoader] Error loading thumbnail for {filePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 加载并优化Bitmap
    /// </summary>
    private static Bitmap? LoadAndOptimizeBitmap(string filePath, int maxSize)
    {
        try
        {
            // 首先只解码边界信息来获取原始尺寸
            var options = new BitmapFactory.Options
            {
                InJustDecodeBounds = true
            };
            BitmapFactory.DecodeFile(filePath, options);

            if (options.OutHeight == -1 || options.OutWidth == -1)
            {
                return null;
            }

            // 计算缩放比例
            var scaleFactor = CalculateInSampleSize(options, maxSize, maxSize);

            // 解码实际的Bitmap
            options.InSampleSize = scaleFactor;
            options.InJustDecodeBounds = false;
            options.InPreferredConfig = Bitmap.Config.Rgb565; // 使用16位色深减少内存占用

            var bitmap = BitmapFactory.DecodeFile(filePath, options);
            if (bitmap == null)
            {
                return null;
            }

            // 进一步调整尺寸到目标大小
            if (bitmap.Width > maxSize || bitmap.Height > maxSize)
            {
                var (targetWidth, targetHeight) = CalculateTargetSize(bitmap.Width, bitmap.Height, maxSize);
                var scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, targetWidth, targetHeight, true);
                
                // 释放原始Bitmap
                if (scaledBitmap != bitmap)
                {
                    bitmap.Recycle();
                    bitmap.Dispose();
                }
                
                bitmap = scaledBitmap;
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MobileOptimizedImageLoader] Error in LoadAndOptimizeBitmap: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 计算采样大小
    /// </summary>
    private static int CalculateInSampleSize(BitmapFactory.Options options, int reqWidth, int reqHeight)
    {
        var height = options.OutHeight;
        var width = options.OutWidth;
        var inSampleSize = 1;

        if (height > reqHeight || width > reqWidth)
        {
            var halfHeight = height / 2;
            var halfWidth = width / 2;

            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
            {
                inSampleSize *= 2;
            }
        }

        return inSampleSize;
    }

    /// <summary>
    /// 计算目标尺寸，保持宽高比
    /// </summary>
    private static (int width, int height) CalculateTargetSize(int originalWidth, int originalHeight, int maxSize)
    {
        if (originalWidth <= maxSize && originalHeight <= maxSize)
        {
            return (originalWidth, originalHeight);
        }

        var ratio = Math.Min((double)maxSize / originalWidth, (double)maxSize / originalHeight);
        var targetWidth = (int)(originalWidth * ratio);
        var targetHeight = (int)(originalHeight * ratio);

        return (targetWidth, targetHeight);
    }

    /// <summary>
    /// 保存Bitmap到文件（压缩优化）
    /// </summary>
    public static async Task<bool> SaveBitmapAsync(Bitmap bitmap, string filePath, 
        Bitmap.CompressFormat? format = null, int quality = COMPRESS_QUALITY)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            var actualFormat = format ?? Bitmap.CompressFormat.Jpeg;
            var success = await bitmap.CompressAsync(actualFormat!, quality, stream);
            await stream.FlushAsync();
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MobileOptimizedImageLoader] Error saving bitmap: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 清理内存缓存
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var kvp in _memoryCache)
            {
                if (kvp.Value.TryGetTarget(out var bitmap) && bitmap != null && !bitmap.IsRecycled)
                {
                    bitmap.Recycle();
                    bitmap.Dispose();
                }
            }
            _memoryCache.Clear();
        }
    }

    /// <summary>
    /// 清理缓存如果超出限制
    /// </summary>
    private static void CleanupCacheIfNeeded()
    {
        if (_memoryCache.Count >= MAX_CACHE_SIZE)
        {
            // 清理一半的旧缓存项
            var keysToRemove = _memoryCache.Keys.Take(MAX_CACHE_SIZE / 2).ToList();
            foreach (var key in keysToRemove)
            {
                if (_memoryCache.TryRemove(key, out var weakRef) && 
                    weakRef.TryGetTarget(out var bitmap) && 
                    bitmap != null && !bitmap.IsRecycled)
                {
                    bitmap.Recycle();
                    bitmap.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 获取内存缓存统计信息
    /// </summary>
    public static (int totalItems, int aliveItems, long estimatedMemoryUsage) GetCacheStats()
    {
        int aliveItems = 0;
        long estimatedMemory = 0;

        foreach (var kvp in _memoryCache)
        {
            if (kvp.Value.TryGetTarget(out var bitmap) && bitmap != null && !bitmap.IsRecycled)
            {
                aliveItems++;
                estimatedMemory += bitmap.ByteCount;
            }
        }

        return (_memoryCache.Count, aliveItems, estimatedMemory);
    }

    /// <summary>
    /// 获取推荐的图片网格尺寸
    /// </summary>
    public static int GetRecommendedGridSize()
    {
        var screenAdapter = ScreenAdapter.GetDeviceSizeCategory();
        return screenAdapter switch
        {
            ScreenAdapter.DeviceSizeCategory.SmallPhone => 90,
            ScreenAdapter.DeviceSizeCategory.NormalPhone => 100,
            ScreenAdapter.DeviceSizeCategory.SmallTablet => 120,
            ScreenAdapter.DeviceSizeCategory.LargeTablet => 140,
            _ => 100
        };
    }

    /// <summary>
    /// 异步批量加载图片（优化内存使用）
    /// </summary>
    public static async Task<Bitmap?[]> LoadBatchAsync(string[] filePaths, int maxSize = 120)
    {
        var results = new Bitmap?[filePaths.Length];
        
        // 分批处理以避免内存峰值
        const int BATCH_SIZE = 10;
        for (int i = 0; i < filePaths.Length; i += BATCH_SIZE)
        {
            var batch = filePaths.Skip(i).Take(BATCH_SIZE).ToArray();
            var batchResults = await Task.WhenAll(
                batch.Select(path => LoadOptimizedThumbnailAsync(path, maxSize))
            );
            
            Array.Copy(batchResults, 0, results, i, batchResults.Length);
            
            // 小延迟以避免UI线程阻塞
            if (i + BATCH_SIZE < filePaths.Length)
            {
                await Task.Delay(10);
            }
        }

        return results;
    }
}