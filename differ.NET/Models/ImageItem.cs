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

    public ImageItem(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);
    }
}
