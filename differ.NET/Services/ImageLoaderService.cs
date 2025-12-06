using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
#if ANDROID || __ANDROID__
using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using AndroidUri = Android.Net.Uri;

#endif

namespace differ.NET.Services;

/// <summary>
/// 图片加载服务
/// </summary>
public static class ImageLoaderService
{
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
    
    // 缩略图缓存
    private static readonly ConcurrentDictionary<string, Bitmap> ThumbnailCache = new();

    /// <summary>
    /// 检查文件是否为支持的图片格式
    /// </summary>
    public static bool IsSupportedImage(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return Array.Exists(SupportedExtensions, e => e == ext);
    }

    /// <summary>
    /// 清除缩略图缓存
    /// </summary>
    public static void ClearCache()
    {
        ThumbnailCache.Clear();
    }

    /// <summary>
    /// 异步加载缩略图（带缓存）
    /// </summary>
    public static async Task<Bitmap?> LoadThumbnailAsync(string filePath, int maxSize = 200)
    {
        // 检查缓存
        if (ThumbnailCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var image = SixLabors.ImageSharp.Image.Load(filePath);
                
                // 计算缩放比例
                double scale = Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height);
                int newWidth = Math.Max(1, (int)(image.Width * scale));
                int newHeight = Math.Max(1, (int)(image.Height * scale));

                image.Mutate(x => x.Resize(newWidth, newHeight));

                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                ms.Position = 0;

                return new Bitmap(ms);
            });

            if (bitmap != null)
            {
                ThumbnailCache.TryAdd(filePath, bitmap);
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取文件夹中所有图片
    /// </summary>
    /// <param name="folderPath">文件夹路径</param>
    /// <param name="includeSubfolders">是否包含子文件夹</param>
    public static string[] GetImagesInFolder(string folderPath, bool includeSubfolders = false)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return Array.Empty<string>();

#if ANDROID || __ANDROID__
        // 在安卓上，文件夹选择器返回的通常是 content:// URI，需要特殊处理
        var androidImages = TryGetImagesFromAndroid(folderPath, includeSubfolders);
        if (androidImages != null)
        {
            return androidImages;
        }
#endif

        return GetImagesFromFileSystem(folderPath, includeSubfolders);
    }

    /// <summary>
    /// 从本地文件系统读取图片路径
    /// </summary>
    private static string[] GetImagesFromFileSystem(string folderPath, bool includeSubfolders)
    {
        if (!Directory.Exists(folderPath))
            return Array.Empty<string>();

        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(folderPath, "*.*", searchOption);
        return Array.FindAll(files, IsSupportedImage);
    }

#if ANDROID || __ANDROID__
    /// <summary>
    /// 安卓端专用：处理 content:// URI，必要时复制到缓存目录
    /// 返回 null 表示继续使用默认文件系统路径逻辑
    /// </summary>
    private static string[]? TryGetImagesFromAndroid(string folderPath, bool includeSubfolders)
    {
        try
        {
            // 如果路径对应真实目录，交给默认逻辑处理
            if (Directory.Exists(folderPath))
                return null;

            // 尝试将 content/tree URI 解析为真实路径
            var resolvedPath = ResolveAndroidLocalPath(folderPath);
            if (!string.IsNullOrEmpty(resolvedPath) && Directory.Exists(resolvedPath))
            {
                return GetImagesFromFileSystem(resolvedPath, includeSubfolders);
            }

            // 如果是 content URI，使用 SAF 枚举并复制到缓存
            if (IsContentUri(folderPath))
            {
                var context = global::Android.App.Application.Context;
                if (context == null)
                    return Array.Empty<string>();

                var uri = AndroidUri.Parse(folderPath);
                if (uri == null)
                    return Array.Empty<string>();

                // 尝试持久化读写权限，避免后续访问被拒绝
                TryTakePersistablePermission(context, uri);

                var root = DocumentFile.FromTreeUri(context, uri) ?? DocumentFile.FromSingleUri(context, uri);
                if (root == null)
                    return Array.Empty<string>();

                var results = new List<string>();
                CollectFromDocumentTree(context, root, includeSubfolders, results);
                return results.ToArray();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageLoaderService] Android folder scan failed: {ex.Message}");
        }

        // 返回空数组意味着已经尝试过安卓路径解析，不再走默认逻辑
        return Array.Empty<string>();
    }

    /// <summary>
    /// 将 content/tree URI 尝试解析为真实文件系统路径
    /// </summary>
    private static string? ResolveAndroidLocalPath(string folderPath)
    {
        try
        {
            if (!System.Uri.TryCreate(folderPath, UriKind.Absolute, out var uri))
                return null;

            if (uri.IsFile)
                return uri.LocalPath;

            if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
                return null;

            var docId = DocumentsContract.GetTreeDocumentId(AndroidUri.Parse(folderPath));
            if (string.IsNullOrEmpty(docId))
            {
                docId = DocumentsContract.GetDocumentId(AndroidUri.Parse(folderPath));
            }

            if (string.IsNullOrEmpty(docId))
                return null;

            if (docId.StartsWith("raw:", StringComparison.OrdinalIgnoreCase))
            {
                return docId.Substring("raw:".Length);
            }

            if (docId.StartsWith("primary:", StringComparison.OrdinalIgnoreCase))
            {
                var relative = docId.Substring("primary:".Length);
                return Path.Combine("/storage/emulated/0", relative);
            }

            var parts = docId.Split(':', 2);
            if (parts.Length == 2)
            {
                var volume = parts[0];
                var relative = parts[1];
                return Path.Combine("/storage", volume, relative);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageLoaderService] ResolveAndroidLocalPath failed: {ex.Message}");
        }

        return null;
    }

    private static bool IsContentUri(string uriString)
    {
        return System.Uri.TryCreate(uriString, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 为选定的 content URI 申请持久化权限（最佳努力）
    /// </summary>
    private static void TryTakePersistablePermission(Context context, AndroidUri uri)
    {
        try
        {
            var resolver = context.ContentResolver;
            resolver?.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageLoaderService] TakePersistableUriPermission failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 使用 SAF 递归收集图片，必要时复制到应用缓存
    /// </summary>
    private static void CollectFromDocumentTree(Context context, DocumentFile folder, bool includeSubfolders, List<string> results)
    {
        foreach (var child in folder.ListFiles())
        {
            if (child == null)
                continue;

            if (child.IsDirectory && includeSubfolders)
            {
                CollectFromDocumentTree(context, child, includeSubfolders, results);
                continue;
            }

            if (child.IsFile && IsSupportedImage(child.Name ?? string.Empty))
            {
                var cachedPath = CopyToCache(context, child);
                if (!string.IsNullOrEmpty(cachedPath))
                {
                    results.Add(cachedPath);
                }
            }
        }
    }

    /// <summary>
    /// 将 DocumentFile 复制到应用缓存目录，返回可直接访问的本地路径
    /// </summary>
    private static string? CopyToCache(Context context, DocumentFile file)
    {
        try
        {
            var cacheDir = Path.Combine(context.CacheDir?.AbsolutePath ?? "/data/data", "image-cache");
            Directory.CreateDirectory(cacheDir);

            var rawName = file.Name;
            var extension = Path.GetExtension(rawName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".img";
            }

            var docId = DocumentsContract.GetDocumentId(file.Uri);
            var safeName = SanitizeFileName(!string.IsNullOrEmpty(docId) ? docId : rawName ?? Guid.NewGuid().ToString("N"));
            var targetPath = Path.Combine(cacheDir, $"{safeName}{extension}");

            // 若已有缓存且未过期，直接复用
            if (File.Exists(targetPath))
            {
                var existingInfo = new FileInfo(targetPath);
                var lastModified = file.LastModified();

                if (existingInfo.Length > 0 &&
                    (lastModified <= 0 || existingInfo.LastWriteTimeUtc >= DateTimeOffset.FromUnixTimeMilliseconds(lastModified).UtcDateTime))
                {
                    return targetPath;
                }
            }

            using var input = context.ContentResolver?.OpenInputStream(file.Uri);
            if (input == null)
                return null;

            using var output = File.Open(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            return targetPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageLoaderService] CopyToCache failed for {file.Uri}: {ex.Message}");
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
#endif
}
