using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    [ObservableProperty]
    private bool _useDinoModel = true;

    [ObservableProperty]
    private bool _isDinoAvailable;

    private IStorageProvider? _storageProvider;

    public MainViewModel()
    {
        // 异步初始化 DINOv3 模型
        Task.Run(() =>
        {
            var success = ImageSimilarityService.InitializeDino();
            Dispatcher.UIThread.Post(() =>
            {
                IsDinoAvailable = success;
                if (success)
                {
                    StatusText = "DINOv3 model loaded. Select a folder to browse images.";
                }
                else
                {
                    StatusText = "DINOv3 unavailable, using pHash fallback. Select a folder to browse images.";
                    UseDinoModel = false;
                }
            });
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

            var useDino = UseDinoModel && IsDinoAvailable;
            var algorithmName = useDino ? "DINOv3" : "pHash";
            StatusText = $"Found {TotalImages} images. Processing with {algorithmName} using {Environment.ProcessorCount} cores...";

            // 使用并行处理计算特征
            var processedItems = new ConcurrentBag<ImageItem>();
            var processedCount = 0;

            await Task.Run(() =>
            {
                // DINOv3 需要串行处理（模型推理通常不支持并行）
                // pHash 可以并行处理
                if (useDino)
                {
                    foreach (var filePath in imageFiles)
                    {
                        try
                        {
                            var imageItem = new ImageItem(filePath);
                            
                            // 提取 DINOv3 特征
                            imageItem.DinoFeatures = ImageSimilarityService.ExtractDinoFeatures(filePath);
                            
                            // 同时计算 pHash 作为回退
                            imageItem.PerceptualHash = ImageSimilarityService.ComputePerceptualHash(filePath);
                            
                            processedItems.Add(imageItem);
                            
                            var count = Interlocked.Increment(ref processedCount);
                            if (count % 5 == 0 || count == TotalImages)
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    ProcessedImages = count;
                                    StatusText = $"Extracting DINOv3 features: {count}/{TotalImages} images";
                                });
                            }
                        }
                        catch
                        {
                            // 跳过无法处理的图片
                        }
                    }
                }
                else
                {
                    Parallel.ForEach(imageFiles, 
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        filePath =>
                        {
                            try
                            {
                                var imageItem = new ImageItem(filePath);
                                
                                // 计算感知哈希
                                imageItem.PerceptualHash = ImageSimilarityService.ComputePerceptualHash(filePath);
                                
                                processedItems.Add(imageItem);
                                
                                var count = Interlocked.Increment(ref processedCount);
                                if (count % 10 == 0 || count == TotalImages)
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        ProcessedImages = count;
                                        StatusText = $"Computed pHash: {count}/{TotalImages} images";
                                    });
                                }
                            }
                            catch
                            {
                                // 跳过无法处理的图片
                            }
                        });
                }
            });

            // 按文件名排序并添加到集合
            var sortedItems = processedItems.OrderBy(x => x.FileName).ToList();
            
            StatusText = "Loading thumbnails...";
            ProcessedImages = 0;

            // 批量加载缩略图（使用信号量限制并发）
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
            var thumbnailTasks = sortedItems.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    item.Thumbnail = await ImageLoaderService.LoadThumbnailAsync(item.FilePath, 150);
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Images.Add(item);
                        ProcessedImages = Images.Count;
                        if (Images.Count % 20 == 0 || Images.Count == sortedItems.Count)
                        {
                            StatusText = $"Loading thumbnails: {Images.Count}/{sortedItems.Count}";
                        }
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(thumbnailTasks);

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
        StatusText = "Scanning compare folder...";
        CompareImages.Clear();

        try
        {
            var imageFiles = ImageLoaderService.GetImagesInFolder(CompareFolder, CompareIncludeSubfolders);
            var total = imageFiles.Length;

            if (total == 0)
            {
                StatusText = "No images found in compare folder.";
                return;
            }

            var useDino = UseDinoModel && IsDinoAvailable;
            var algorithmName = useDino ? "DINOv3" : "pHash";
            StatusText = $"Found {total} images. Processing with {algorithmName} using {Environment.ProcessorCount} cores...";

            // 使用并行处理计算特征
            var processedItems = new ConcurrentBag<ImageItem>();
            var processedCount = 0;

            await Task.Run(() =>
            {
                if (useDino)
                {
                    foreach (var filePath in imageFiles)
                    {
                        try
                        {
                            var imageItem = new ImageItem(filePath);
                            imageItem.DinoFeatures = ImageSimilarityService.ExtractDinoFeatures(filePath);
                            imageItem.PerceptualHash = ImageSimilarityService.ComputePerceptualHash(filePath);
                            processedItems.Add(imageItem);
                            
                            var count = Interlocked.Increment(ref processedCount);
                            if (count % 5 == 0 || count == total)
                            {
                                Dispatcher.UIThread.Post(() =>
                                {
                                    StatusText = $"Extracting DINOv3 features: {count}/{total} images";
                                });
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    Parallel.ForEach(imageFiles, 
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        filePath =>
                        {
                            try
                            {
                                var imageItem = new ImageItem(filePath);
                                imageItem.PerceptualHash = ImageSimilarityService.ComputePerceptualHash(filePath);
                                processedItems.Add(imageItem);
                                
                                var count = Interlocked.Increment(ref processedCount);
                                if (count % 10 == 0 || count == total)
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        StatusText = $"Computing pHash: {count}/{total} images";
                                    });
                                }
                            }
                            catch { }
                        });
                }
            });

            // 按文件名排序并添加到集合
            var sortedItems = processedItems.OrderBy(x => x.FileName).ToList();
            
            StatusText = "Loading thumbnails...";

            // 批量加载缩略图
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
            var thumbnailTasks = sortedItems.Select(async item =>
            {
                await semaphore.WaitAsync();
                try
                {
                    item.Thumbnail = await ImageLoaderService.LoadThumbnailAsync(item.FilePath, 150);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CompareImages.Add(item);
                        if (CompareImages.Count % 20 == 0 || CompareImages.Count == sortedItems.Count)
                        {
                            StatusText = $"Loading thumbnails: {CompareImages.Count}/{sortedItems.Count}";
                        }
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(thumbnailTasks);

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
                var sourceFeatures = SourceImage.DinoFeatures;
                var useDino = UseDinoModel && IsDinoAvailable && sourceFeatures != null;

                // 根据是否使用对比文件夹选择搜索范围
                var searchCollection = UseCompareFolder && CompareImages.Count > 0 
                    ? CompareImages 
                    : Images;

                var similar = searchCollection
                    .Where(img => img.FilePath != SourceImage.FilePath)
                    .Select(img =>
                    {
                        // 优先使用 DINOv3，回退到 pHash
                        if (useDino && img.HasDinoFeatures)
                        {
                            img.Similarity = ImageSimilarityService.CalculateSimilarity(
                                sourceFeatures, img.DinoFeatures, sourceHash, img.PerceptualHash);
                        }
                        else
                        {
                            img.Similarity = ImageSimilarityService.CalculateSimilarity(sourceHash, img.PerceptualHash);
                        }
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
            var algorithmName = UseDinoModel && IsDinoAvailable && SourceImage.HasDinoFeatures ? "DINOv3" : "pHash";
            StatusText = $"Found {SimilarImages.Count} similar images in {searchScope} using {algorithmName} (similarity >= {SimilarityThreshold:F0}%)";
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
