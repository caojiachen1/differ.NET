using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using differ.NET.Models;
using differ.NET.Services;

namespace differ.NET.Android.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _currentFolder = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ImageItem> _images = new();

    [ObservableProperty]
    private ObservableCollection<ImageItem> _similarImages = new();

    [ObservableProperty]
    private ImageItem? _selectedImage;

    [ObservableProperty]
    private ImageItem? _sourceImage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = "Select a folder to browse images";

    [ObservableProperty]
    private double _similarityThreshold = 70;

    [ObservableProperty]
    private bool _includeSubfolders = true;

    [ObservableProperty]
    private string _compareFolder = string.Empty;

    [ObservableProperty]
    private bool _useCompareFolder;

    [ObservableProperty]
    private bool _compareIncludeSubfolders = true;

    [ObservableProperty]
    private ObservableCollection<ImageItem> _compareImages = new();

    [ObservableProperty]
    private int _totalImages;

    [ObservableProperty]
    private int _processedImages;

    [ObservableProperty]
    private bool _isDinoAvailable;

    [ObservableProperty]
    private bool _useDatabaseCache = true;

    [ObservableProperty]
    private string _cacheStatus = string.Empty;

    private IStorageProvider? _storageProvider;

    public MainViewModel()
    {
        // 异步初始化 DINOv3 模型（如果模型不存在会自动下载）
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine($"[MainViewModel] Starting DINOv3 initialization...");
                
                // 使用异步初始化，支持自动下载模型
                var success = await ImageSimilarityService.InitializeDinoAsync(
                    progressCallback: (fileName, progress) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            StatusText = $"Downloading {fileName}: {progress:F1}%";
                        });
                    });
                
                Console.WriteLine($"[MainViewModel] DINOv3 initialization result: {success}");
                Console.WriteLine($"[MainViewModel] IsDinoAvailable after init: {ImageSimilarityService.IsDinoAvailable}");
                
                Dispatcher.UIThread.Post(() =>
                {
                    IsDinoAvailable = success && ImageSimilarityService.IsDinoAvailable;
                    Console.WriteLine($"[MainViewModel] Set IsDinoAvailable to: {IsDinoAvailable}");
                    
                    if (success)
                    {
                        StatusText = "DINOv3 model loaded. Select a folder to browse images.";
                    }
                    else
                    {
                        StatusText = "DINOv3 initialization failed. Please check the model file.";
                    }
                });
            }
            catch (Exception ex)
            {
                var error = $"Failed to initialize DINOv3 model: {ex.Message}";
                Console.WriteLine($"[MainViewModel] {error}");
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = "DINOv3 initialization failed. Please check the error logs.";
                    IsDinoAvailable = false;
                });
            }
        });
    }

    public void SetStorageProvider(IStorageProvider provider)
    {
        _storageProvider = provider;
    }

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        if (_storageProvider == null)
            return;

        var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Image Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            CurrentFolder = folder.Path.LocalPath;
            await LoadImagesFromFolderAsync(CurrentFolder);
        }
    }

    private async Task LoadImagesFromFolderAsync(string folderPath)
    {
        IsLoading = true;
        StatusText = "Scanning folder...";
        Images.Clear();
        SimilarImages.Clear();
        SourceImage = null;
        ImageLoaderService.ClearCache();

        try
        {
            var imageFiles = ImageLoaderService.GetImagesInFolder(folderPath, IncludeSubfolders);
            TotalImages = imageFiles.Length;
            ProcessedImages = 0;

            if (TotalImages == 0)
            {
                StatusText = "No images found in folder.";
                return;
            }

            // 检查DINOv3是否可用
            if (!ImageSimilarityService.IsDinoAvailable)
            {
                StatusText = "DINOv3 model is not available. Please check the model file.";
                IsLoading = false;
                return;
            }

            StatusText = $"Found {TotalImages} images. Processing with DINOv3...";

            // 简化处理流程，适应移动端性能
            foreach (var filePath in imageFiles)
            {
                try
                {
                    var imageItem = new ImageItem(filePath);
                    
                    // 获取 DINOv3 特征
                    imageItem.DinoFeatures = ImageSimilarityService.ExtractDinoFeatures(filePath);
                    
                    if (imageItem.DinoFeatures == null)
                    {
                        continue; // 跳过该图片
                    }

                    // 加载缩略图（较小的尺寸以适应移动屏幕）
                    imageItem.Thumbnail = await ImageLoaderService.LoadThumbnailAsync(imageItem.FilePath, 120);
                    
                    Images.Add(imageItem);
                    ProcessedImages = Images.Count;
                    
                    if (ProcessedImages % 10 == 0 || ProcessedImages == TotalImages)
                    {
                        StatusText = $"Loading images: {ProcessedImages}/{TotalImages}";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process image {filePath}: {ex.Message}");
                }
            }

            StatusText = $"Loaded {Images.Count} images. Tap and hold to set as search source.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    internal async Task SetAsSourceAsync(ImageItem? image)
    {
        if (image == null)
            return;

        SourceImage = image;
        await FindSimilarImagesAsync();
    }

    [RelayCommand]
    private async Task FindSimilarImagesAsync()
    {
        if (SourceImage == null)
        {
            StatusText = "Please select a source image first";
            return;
        }

        // 检查DINOv3是否可用
        if (!ImageSimilarityService.IsDinoAvailable)
        {
            StatusText = "DINOv3 model is not available. Cannot perform similarity search.";
            return;
        }

        // 检查源图片是否具有DINO特征
        if (!SourceImage.HasDinoFeatures)
        {
            StatusText = "Source image does not have DINOv3 features.";
            return;
        }

        IsSearching = true;
        StatusText = "Finding similar images...";
        SimilarImages.Clear();

        try
        {
            await Task.Run(() =>
            {
                var sourceFeatures = SourceImage.DinoFeatures;

                var similar = Images
                    .Where(img => img.FilePath != SourceImage.FilePath && img.HasDinoFeatures)
                    .Select(img =>
                    {
                        // 使用 DINOv3 特征进行相似度计算
                        img.Similarity = ImageSimilarityService.CalculateSimilarity(sourceFeatures, img.DinoFeatures);
                        return img;
                    })
                    .Where(img => img.Similarity >= SimilarityThreshold)
                    .OrderByDescending(img => img.Similarity)
                    .ToList();

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var img in similar)
                    {
                        SimilarImages.Add(img);
                    }
                });
            });

            StatusText = $"Found {SimilarImages.Count} similar images (similarity >= {SimilarityThreshold:F0}%)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error during similarity search: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void ClearSource()
    {
        SourceImage = null;
        SimilarImages.Clear();
        StatusText = Images.Count > 0 
            ? $"Loaded {Images.Count} images. Tap and hold to set as search source."
            : "Select a folder to browse images";
    }

    partial void OnSimilarityThresholdChanged(double value)
    {
        if (SourceImage != null && !IsSearching)
        {
            _ = FindSimilarImagesAsync();
        }
    }
}