using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace differ.NET.Services;

/// <summary>
/// 图片加载服务
/// </summary>
public static class ImageLoaderService
{
    private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    /// <summary>
    /// 检查文件是否为支持的图片格式
    /// </summary>
    public static bool IsSupportedImage(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return Array.Exists(SupportedExtensions, e => e == ext);
    }

    /// <summary>
    /// 异步加载缩略图
    /// </summary>
    public static async Task<Bitmap?> LoadThumbnailAsync(string filePath, int maxSize = 200)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var image = SixLabors.ImageSharp.Image.Load(filePath);
                
                // 计算缩放比例
                double scale = Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);

                image.Mutate(x => x.Resize(newWidth, newHeight));

                using var ms = new MemoryStream();
                image.SaveAsPng(ms);
                ms.Position = 0;

                return new Bitmap(ms);
            });
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
        if (!Directory.Exists(folderPath))
            return Array.Empty<string>();

        var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(folderPath, "*.*", searchOption);
        return Array.FindAll(files, IsSupportedImage);
    }
}
