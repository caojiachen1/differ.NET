using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using differ.NET.Models;
using differ.NET.Services;

namespace differ.NET.ViewModels;

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

    private IStorageProvider? _storageProvider;

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
        StatusText = "Loading images...";
        Images.Clear();
        SimilarImages.Clear();
        SourceImage = null;

        try
        {
            var imageFiles = ImageLoaderService.GetImagesInFolder(folderPath, IncludeSubfolders);
            TotalImages = imageFiles.Length;
            ProcessedImages = 0;

            foreach (var filePath in imageFiles)
            {
                var imageItem = new ImageItem(filePath);
                
                // Load thumbnail asynchronously
                imageItem.Thumbnail = await ImageLoaderService.LoadThumbnailAsync(filePath, 150);
                
                // Compute perceptual hash
                imageItem.PerceptualHash = await Task.Run(() => 
                    ImageSimilarityService.ComputePerceptualHash(filePath));

                Images.Add(imageItem);
                ProcessedImages++;
                StatusText = $"Loaded {ProcessedImages}/{TotalImages} images";
            }

            StatusText = $"Loaded {Images.Count} images. Right-click to set as search source.";
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
    private async Task SelectCompareFolderAsync()
    {
        if (_storageProvider == null)
            return;

        var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Compare Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folder = folders[0];
            CompareFolder = folder.Path.LocalPath;
            UseCompareFolder = true;
            await LoadCompareImagesAsync();
        }
    }

    private async Task LoadCompareImagesAsync()
    {
        if (string.IsNullOrEmpty(CompareFolder))
            return;

        IsLoading = true;
        StatusText = "Loading compare folder images...";
        CompareImages.Clear();

        try
        {
            var imageFiles = ImageLoaderService.GetImagesInFolder(CompareFolder, CompareIncludeSubfolders);
            var total = imageFiles.Length;
            var processed = 0;

            foreach (var filePath in imageFiles)
            {
                var imageItem = new ImageItem(filePath);
                imageItem.Thumbnail = await ImageLoaderService.LoadThumbnailAsync(filePath, 150);
                imageItem.PerceptualHash = await Task.Run(() => 
                    ImageSimilarityService.ComputePerceptualHash(filePath));

                CompareImages.Add(imageItem);
                processed++;
                StatusText = $"Loading compare folder: {processed}/{total} images";
            }

            StatusText = $"Compare folder loaded with {CompareImages.Count} images.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading compare folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearCompareFolder()
    {
        CompareFolder = string.Empty;
        UseCompareFolder = false;
        CompareImages.Clear();
        SimilarImages.Clear();
        StatusText = Images.Count > 0 
            ? $"Loaded {Images.Count} images. Right-click to set as search source."
            : "Select a folder to browse images";
    }

    [RelayCommand]
    private async Task SetAsSourceAsync(ImageItem? image)
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

        IsSearching = true;
        StatusText = "Finding similar images...";
        SimilarImages.Clear();

        try
        {
            await Task.Run(() =>
            {
                var sourceHash = SourceImage.PerceptualHash;

                // 根据是否使用对比文件夹选择搜索范围
                var searchCollection = UseCompareFolder && CompareImages.Count > 0 
                    ? CompareImages 
                    : Images;

                var similar = searchCollection
                    .Where(img => img.FilePath != SourceImage.FilePath)
                    .Select(img =>
                    {
                        img.Similarity = ImageSimilarityService.CalculateSimilarity(sourceHash, img.PerceptualHash);
                        return img;
                    })
                    .Where(img => img.Similarity >= SimilarityThreshold)
                    .OrderByDescending(img => img.Similarity)
                    .ToList();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var img in similar)
                    {
                        SimilarImages.Add(img);
                    }
                });
            });

            var searchScope = UseCompareFolder && CompareImages.Count > 0 ? "compare folder" : "library";
            StatusText = $"Found {SimilarImages.Count} similar images in {searchScope} (similarity >= {SimilarityThreshold:F0}%)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
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
            ? $"Loaded {Images.Count} images. Right-click to set as search source."
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
