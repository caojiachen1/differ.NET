using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

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

    [ObservableProperty]
    private ulong _perceptualHash;

    /// <summary>
    /// DINOv3 特征向量，用于深度学习相似度计算
    /// </summary>
    public float[]? DinoFeatures { get; set; }

    /// <summary>
    /// 是否有有效的 DINOv3 特征
    /// </summary>
    public bool HasDinoFeatures => DinoFeatures != null && DinoFeatures.Length > 0;

    public ImageItem(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
    }
}
