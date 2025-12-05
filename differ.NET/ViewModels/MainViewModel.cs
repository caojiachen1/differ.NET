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
    private bool _isDinoAvailable;

    [ObservableProperty]
    private bool _useDatabaseCache = true;

    [ObservableProperty]
    private string _cacheStatus = string.Empty;

    [ObservableProperty]
    private bool _showErrorLog = false;

    [ObservableProperty]
    private ObservableCollection<ErrorLogEntry> _errorLogs = new();

    [ObservableProperty]
    private ErrorLogEntry? _selectedErrorLog;

    private IStorageProvider? _storageProvider;
    private FolderDatabaseCacheService? _cacheService;
    private string _currentCacheFolder = string.Empty;

    public MainViewModel()
    {
        // 监听错误日志事件
        ErrorLogService.OnErrorLogged += OnErrorLogged;

        // 当数据库缓存启用且用户选择文件夹时初始化

        // 异步初始化 DINOv3 模型
        Task.Run(() =>
        {
            try
            {
                Console.WriteLine($"[MainViewModel] Starting DINOv3 initialization...");
                var success = ImageSimilarityService.InitializeDino();
                Console.WriteLine($"[MainViewModel] DINOv3 initialization result: {success}");
                Console.WriteLine($"[MainViewModel] IsDinoAvailable after init: {ImageSimilarityService.IsDinoAvailable}");
                
                Dispatcher.UIThread.Post(() =>
                {
                    IsDinoAvailable = success && ImageSimilarityService.IsDinoAvailable;
                    Console.WriteLine($"[MainViewModel] Set IsDinoAvailable to: {IsDinoAvailable}");
                    
                    if (success)
                    {
                        StatusText = "DINOv3 model loaded. Select a folder to browse images.";
                        ErrorLogService.LogInfo("DINOv3 model successfully loaded");
                    }
                    else
                    {
                        StatusText = "DINOv3 initialization failed. Please check the model file.";
                        ErrorLogService.LogError("DINOv3 initialization failed - this is a critical error");
                        // 如果DINOv3初始化失败，需要一个适当的响应通知用户
                        ErrorLogService.LogCritical("Application requires DINOv3 model to function properly");
                    }
                });
            }
            catch (Exception ex)
            {
                var error = $"Failed to initialize DINOv3 model: {ex.Message}";
                Console.WriteLine($"[MainViewModel] {error}");
                ErrorLogService.LogError(error, ex);
                Dispatcher.UIThread.Post(() =>
                {
                    StatusText = "DINOv3 initialization failed. Please check the error logs.";
                    IsDinoAvailable = false;
                });
            }
        });

        // 加载现有的错误日志
        LoadErrorLogs();
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
            var newFolderPath = folder.Path.LocalPath;
            
            // 如果切换文件夹，需要重新初始化缓存
            if (CurrentFolder != newFolderPath)
            {
                // 释放旧的缓存服务
                if (_cacheService != null && _currentCacheFolder != newFolderPath)
                {
                    _cacheService.Dispose();
                    _cacheService = null;
                    _currentCacheFolder = string.Empty;
                }
                
                CurrentFolder = newFolderPath;
            }
            
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

        // 为新的文件夹初始化缓存服务
        if (UseDatabaseCache && !string.IsNullOrEmpty(folderPath))
        {
            try
            {
                // 如果缓存服务已经创建但不是当前文件夹的，释放旧的
                if (_cacheService != null && _currentCacheFolder != folderPath)
                {
                    _cacheService.Dispose();
                    _cacheService = null;
                }

                // 为新的文件夹创建缓存服务
                if (_cacheService == null)
                {
                    _cacheService = new FolderDatabaseCacheService(folderPath);
                    _currentCacheFolder = folderPath;
                    ErrorLogService.LogInfo($"Initialized folder cache for: {folderPath}");
                }
            }
            catch (Exception ex)
            {
                var error = $"Failed to initialize folder cache for {folderPath}: {ex.Message}";
                Console.WriteLine($"[MainViewModel] {error}");
                ErrorLogService.LogError(error, ex);
                UseDatabaseCache = false;
                _cacheService?.Dispose();
                _cacheService = null;
            }
        }

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

            // 检查DINOv3是否可用，如果不可用则报错
            if (!ImageSimilarityService.IsDinoAvailable)
            {
                StatusText = "DINOv3 model is not available. Please check the model file and error logs.";
                ErrorLogService.LogCritical("DINOv3 model is not available - application cannot process images");
                IsLoading = false;
                return;
            }

            var algorithmName = "DINOv3";
            var cacheInfo = UseDatabaseCache && _cacheService != null ? " (with cache)" : "";
            
            Console.WriteLine($"[LoadImages] Using DINOv3 algorithm with cache: {UseDatabaseCache}");
            StatusText = $"Found {TotalImages} images. Processing with {algorithmName}{cacheInfo} using {Environment.ProcessorCount} cores...";

            // 使用并发处理来加速
            var processedItems = new ConcurrentBag<ImageItem>();
            var processedCount = 0;
            var cacheHits = 0;
            var cacheMisses = 0;

            await Task.Run(async () =>
            {
                // 只使用DINOv3算法来提取特征，不再支持其他算法
                foreach (var filePath in imageFiles)
                {
                    try
                    {
                        ImageItem? imageItem = null;
                        
                        // 尝试从缓存加载
                        if (UseDatabaseCache && _cacheService != null)
                        {
                            var cached = await _cacheService.GetCachedFeaturesAsync(filePath);
                            if (cached != null)
                            {
                                Console.WriteLine($"[LoadImages] Cache hit for: {filePath}");
                                var dinoFeatures = cached.GetDinoFeatures();
                                Console.WriteLine($"[LoadImages] Loaded {dinoFeatures.Length} DINO features from cache");
                                Console.WriteLine($"[LoadImages] Feature sample from cache: {string.Join(", ", dinoFeatures.Take(5))}");
                                
                                // 创建新的ImageItem并从缓存加载DINO特征
                                imageItem = new ImageItem(filePath)
                                {
                                    DinoFeatures = dinoFeatures,
                                    FileSize = cached.FileSize,
                                    LastModified = DateTime.FromFileTimeUtc(cached.LastModified),
                                    IsFromCache = true
                                };
                                Interlocked.Increment(ref cacheHits);
                                Console.WriteLine($"[LoadImages] Successfully created ImageItem from cache");
                            }
                        }
                        
                        // 如果未缓存，则进行提取
                        if (imageItem == null)
                        {
                            imageItem = new ImageItem(filePath);
                            
                            // 获取 DINOv3 特征
                            imageItem.DinoFeatures = ImageSimilarityService.ExtractDinoFeatures(filePath);
                            
                            // 如果DINOv3特征提取失败，需要一个适当的响应
                            if (imageItem.DinoFeatures == null)
                            {
                                var error = $"DINOv3 feature extraction failed for {filePath}";
                                Console.WriteLine($"[LoadImages] {error}");
                                ErrorLogService.LogError(error);
                                continue; // 跳过该图片
                            }
                            
                            Interlocked.Increment(ref cacheMisses);
                            
                            // 缓存特征
                            if (UseDatabaseCache && _cacheService != null)
                            {
                                await _cacheService.CacheFeaturesAsync(filePath, imageItem.DinoFeatures);
                            }
                        }
                        
                        processedItems.Add(imageItem);
                        
                        var count = Interlocked.Increment(ref processedCount);
                        if (count % 5 == 0 || count == TotalImages)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                ProcessedImages = count;
                                var cacheStatus = cacheHits + cacheMisses > 0 ? $" (Cache: {cacheHits} hits, {cacheMisses} misses)" : "";
                                StatusText = $"Extracting DINOv3 features: {count}/{TotalImages} images{cacheStatus}";
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = $"Failed to process image {filePath}: {ex.Message}";
                        Console.WriteLine($"[LoadImages] {error}");
                        ErrorLogService.LogError(error, ex);
                    }
                }
            });

            // 按文件名排序并添加到列表
            var sortedItems = processedItems.OrderBy(x => x.FileName).ToList();
            
            StatusText = "Loading thumbnails...";
            ProcessedImages = 0;

            // 并行加载缩略图，使用信号量控制并发
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
            
            // 更新缓存状态
            await UpdateCacheStatusAsync();
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

        // 检查DINOv3是否可用
        if (!ImageSimilarityService.IsDinoAvailable)
        {
            StatusText = "DINOv3 model is not available. Cannot load compare folder.";
            IsLoading = false;
            return;
        }

        try
        {
            var imageFiles = ImageLoaderService.GetImagesInFolder(CompareFolder, CompareIncludeSubfolders);
            var total = imageFiles.Length;

            if (total == 0)
            {
                StatusText = "No images found in compare folder.";
                return;
            }

            var algorithmName = "DINOv3";
            StatusText = $"Found {total} images. Processing with {algorithmName} using {Environment.ProcessorCount} cores...";

            // 使用并发处理来加速
            var processedItems = new ConcurrentBag<ImageItem>();
            var processedCount = 0;

            await Task.Run(() =>
            {
                // 只使用DINOv3算法来提取特征，不再支持其他算法
                foreach (var filePath in imageFiles)
                {
                    try
                    {
                        var imageItem = new ImageItem(filePath);
                        
                        // 获取 DINOv3 特征
                        imageItem.DinoFeatures = ImageSimilarityService.ExtractDinoFeatures(filePath);
                        
                        // 如果DINOv3特征提取失败，需要一个适当的响应
                        if (imageItem.DinoFeatures == null)
                        {
                            ErrorLogService.LogError($"DINOv3 feature extraction failed for {filePath}");
                            continue; // 跳过该图片
                        }
                        
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
                    catch (Exception ex)
                    {
                        ErrorLogService.LogError($"Failed to process compare folder image {filePath}: {ex.Message}", ex);
                    }
                }
            });

            // 按文件名排序并添加到列表
            var sortedItems = processedItems.OrderBy(x => x.FileName).ToList();
            
            StatusText = "Loading thumbnails...";

            // 并行加载缩略图
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

        // 检查DINOv3是否可用
        if (!ImageSimilarityService.IsDinoAvailable)
        {
            StatusText = "DINOv3 model is not available. Cannot perform similarity search.";
            ErrorLogService.LogError("Cannot perform similarity search - DINOv3 model not available");
            return;
        }

        // 检查DINOv3是否可用
        if (!ImageSimilarityService.IsDinoAvailable)
        {
            StatusText = "DINOv3 model is not available. Cannot perform similarity search.";
            ErrorLogService.LogError("Cannot perform similarity search - DINOv3 model not available");
            return;
        }

        // 检查源图片是否具有DINO特征
        if (!SourceImage.HasDinoFeatures)
        {
            StatusText = "Source image does not have DINOv3 features. Please rescan the folder.";
            ErrorLogService.LogError("Source image missing DINOv3 features - need to rescan folder");
            return;
        }

        IsSearching = true;
        StatusText = "Finding similar images using DINOv3...";
        SimilarImages.Clear();

        try
        {
            await Task.Run(() =>
            {
                var sourceFeatures = SourceImage.DinoFeatures;

                // 根据是否使用比较文件夹选择搜索范围
                var searchCollection = UseCompareFolder && CompareImages.Count > 0 
                    ? CompareImages 
                    : Images;

                var similar = searchCollection
                    .Where(img => img.FilePath != SourceImage.FilePath && img.HasDinoFeatures)
                    .Select(img =>
                    {
                        // 只使用 DINOv3 特征进行相似度计算
                        img.Similarity = ImageSimilarityService.CalculateSimilarity(sourceFeatures, img.DinoFeatures);
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
            StatusText = $"Found {SimilarImages.Count} similar images in {searchScope} using DINOv3 (similarity >= {SimilarityThreshold:F0}%)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error during DINOv3 similarity search: {ex.Message}";
            ErrorLogService.LogError($"Error in FindSimilarImagesAsync: {ex.Message}", ex);
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

    /// <summary>
    /// 更新缓存状态信息
    /// </summary>
    private async Task UpdateCacheStatusAsync()
    {
        if (_cacheService == null)
        {
            CacheStatus = "Cache disabled";
            return;
        }

        try
        {
            var stats = await _cacheService.GetCacheStatisticsAsync();
            var cacheFile = Path.GetFileName(_cacheService.DatabasePath);
            CacheStatus = $"DINOv3 Cache: {stats.TotalEntries} entries - {cacheFile}";
        }
        catch (Exception ex)
        {
            CacheStatus = $"Cache error: {ex.Message}";
        }
    }

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    [RelayCommand]
    private async Task CleanCacheAsync()
    {
        if (_cacheService == null) return;

        IsLoading = true;
        StatusText = "Cleaning expired cache entries...";

        try
        {
            var cleanedCount = await _cacheService.CleanExpiredCacheAsync();
            StatusText = $"Cleaned {cleanedCount} expired DINOv3 cache entries from {Path.GetFileName(_cacheService.DatabasePath)}.";
            await UpdateCacheStatusAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Cache cleanup failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 切换数据库缓存的使用
    /// </summary>
    partial void OnUseDatabaseCacheChanged(bool value)
    {
        if (value && _cacheService == null && !string.IsNullOrEmpty(CurrentFolder))
        {
            try
            {
                _cacheService = new FolderDatabaseCacheService(CurrentFolder);
                _currentCacheFolder = CurrentFolder;
                _ = UpdateCacheStatusAsync();
                ErrorLogService.LogInfo($"Enabled folder cache for: {CurrentFolder}");
            }
            catch (Exception ex)
            {
                var error = $"Failed to initialize folder cache: {ex.Message}";
                Console.WriteLine($"[MainViewModel] {error}");
                ErrorLogService.LogError(error, ex);
                UseDatabaseCache = false;
            }
        }
        else if (!value && _cacheService != null)
        {
            _cacheService.Dispose();
            _cacheService = null;
            _currentCacheFolder = string.Empty;
            CacheStatus = "Cache disabled";
            ErrorLogService.LogInfo("Disabled folder cache");
        }
    }

    /// <summary>
    /// 处理错误日志记录事件
    /// </summary>
    private void OnErrorLogged(ErrorLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ErrorLogs.Insert(0, entry); // 添加到列表的开头
            
            // 限制日志数量
            while (ErrorLogs.Count > 1000)
            {
                ErrorLogs.RemoveAt(ErrorLogs.Count - 1);
            }
        });
    }

    /// <summary>
    /// 加载现有的错误日志
    /// </summary>
    private void LoadErrorLogs()
    {
        var logs = ErrorLogService.GetRecentLogs(100);
        foreach (var log in logs)
        {
            ErrorLogs.Add(log);
        }
    }

    /// <summary>
    /// 切换错误日志显示
    /// </summary>
    [RelayCommand]
    private void ToggleErrorLog()
    {
        ShowErrorLog = !ShowErrorLog;
    }

    /// <summary>
    /// 清除错误日志
    /// </summary>
    [RelayCommand]
    private void ClearErrorLogs()
    {
        ErrorLogService.ClearLogs();
        ErrorLogs.Clear();
    }

    /// <summary>
    /// 导出错误日志
    /// </summary>
    [RelayCommand]
    private async Task ExportErrorLogsAsync()
    {
        if (_storageProvider == null) return;

        try
        {
            var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Error Logs",
                DefaultExtension = ".txt",
                FileTypeChoices = new[] 
                { 
                    new FilePickerFileType("Text Files") 
                    { 
                        Patterns = new[] { "*.txt" } 
                    } 
                },
                SuggestedFileName = $"differ_error_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            });

            if (file != null)
            {
                var success = await ErrorLogService.ExportLogsAsync(file.Path.LocalPath);
                if (success)
                {
                    StatusText = $"Error logs exported to {file.Path.LocalPath}";
                }
                else
                {
                    StatusText = "Failed to export error logs";
                }
            }
        }
        catch (Exception ex)
        {
            var error = $"Failed to export error logs: {ex.Message}";
            StatusText = error;
            ErrorLogService.LogError(error, ex);
        }
    }

    public void Dispose()
    {
        ErrorLogService.OnErrorLogged -= OnErrorLogged;
        _cacheService?.Dispose();
    }
}
