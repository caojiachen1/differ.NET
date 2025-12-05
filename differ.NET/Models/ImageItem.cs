using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using differ.NET.Services;

namespace differ.NET.Models;

public partial class ImageItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private double _similarity;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// DINOv3 特征向量，用于深度学习相似度计算
    /// </summary>
    public float[]? DinoFeatures { get; set; }

    /// <summary>
    /// 是否有有效的 DINOv3 特征
    /// </summary>
    public bool HasDinoFeatures => DinoFeatures != null && DinoFeatures.Length > 0;

    /// <summary>
    /// 文件大小（用于缓存验证）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件最后修改时间（用于缓存验证）
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// 是否从缓存加载
    /// </summary>
    public bool IsFromCache { get; set; }

    public ImageItem(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
        
        // 初始化文件信息
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            FileSize = fileInfo.Length;
            LastModified = fileInfo.LastWriteTimeUtc;
        }
        else
        {
            throw new FileNotFoundException($"Image file not found: {filePath}");
        }
    }

    /// <summary>
    /// 从缓存数据创建ImageItem
    /// </summary>
    public static ImageItem FromCachedFeatures(CachedImageFeatures cached)
    {
        var item = new ImageItem(cached.FilePath)
        {
            DinoFeatures = cached.GetDinoFeatures(),
            FileSize = cached.FileSize,
            LastModified = DateTime.FromFileTimeUtc(cached.LastModified),
            IsFromCache = true
        };
        
        return item;
    }
}
