using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using differ.NET.Models;
using differ.NET.ViewModels;

namespace differ.NET.Views;

public partial class MainView : UserControl
{
    private const int ScrollIdleDelayMs = 150;
    private readonly DispatcherTimer _scrollIdleTimer;
    private bool _isScrolling;
    private bool _isProcessingPending;
    private readonly Queue<ImageItem> _pendingThumbnails = new();
    private readonly HashSet<ImageItem> _pendingThumbnailSet = new();

    public MainView()
    {
        InitializeComponent();

        _scrollIdleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ScrollIdleDelayMs)
        };
        _scrollIdleTimer.Tick += OnScrollIdle;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
        // 获取顶级窗口的StorageProvider
        if (TopLevel.GetTopLevel(this) is { } topLevel && DataContext is MainViewModel vm)
        {
            vm.SetStorageProvider(topLevel.StorageProvider);
        }
    }

    private void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 处理右键点击显示菜单
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (sender is Control control && control.ContextFlyout is Flyout flyout)
            {
                // 获取鼠标相对于控件的位置，在鼠标位置显示菜单
                var position = e.GetPosition(control);
                flyout.Placement = PlacementMode.Pointer;
                flyout.ShowAt(control);
                e.Handled = true;
            }
            else if (sender is Control ctrl && ctrl.ContextFlyout is MenuFlyout menuFlyout)
            {
                menuFlyout.Placement = PlacementMode.Pointer;
                menuFlyout.ShowAt(ctrl);
                e.Handled = true;
            }
        }
    }

    private async void OnImageElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.Element?.DataContext is ImageItem item)
        {
            // 如果正在滚动，先排队，待滚动停止后再加载
            if (_isScrolling)
            {
                EnqueueThumbnail(item);
                return;
            }

            // 处理之前排队的缩略图，保持显示顺序一致
            if (_pendingThumbnails.Count > 0)
            {
                await ProcessPendingThumbnailsAsync();
            }

            if (item.Thumbnail != null)
            {
                RemovePendingThumbnail(item);
                return;
            }

            await vm.EnsureThumbnailAsync(item, 150);
        }
    }

    private void OnImageElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.Element?.DataContext is ImageItem item)
        {
            RemovePendingThumbnail(item);
            vm.CancelThumbnailLoad(item);
        }
    }

    private void OnSetAsSourceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is ImageItem imageItem)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SetAsSourceCommand.Execute(imageItem);
            }
        }
    }

    private void OnOpenFileLocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is ImageItem imageItem)
        {
            try
            {
                var folderPath = Path.GetDirectoryName(imageItem.FilePath);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{imageItem.FilePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    private void OnImageDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control ctrl && ctrl.Tag is ImageItem imageItem)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = imageItem.FilePath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore errors opening the file
            }
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _isScrolling = true;
        _scrollIdleTimer.Stop();
        _scrollIdleTimer.Start();
    }

    private async void OnScrollIdle(object? sender, EventArgs e)
    {
        _scrollIdleTimer.Stop();
        _isScrolling = false;
        await ProcessPendingThumbnailsAsync();
    }

    private void EnqueueThumbnail(ImageItem item)
    {
        if (item.Thumbnail != null)
            return;

        if (_pendingThumbnailSet.Add(item))
        {
            _pendingThumbnails.Enqueue(item);
        }
    }

    private void RemovePendingThumbnail(ImageItem item)
    {
        if (!_pendingThumbnailSet.Remove(item) || _pendingThumbnails.Count == 0)
            return;

        // 重新构建队列以移除指定项
        var remaining = _pendingThumbnails.Where(x => !ReferenceEquals(x, item)).ToList();
        _pendingThumbnails.Clear();
        foreach (var queued in remaining)
        {
            _pendingThumbnails.Enqueue(queued);
        }
    }

    private async Task ProcessPendingThumbnailsAsync()
    {
        if (_isProcessingPending)
            return;

        if (DataContext is not MainViewModel vm)
            return;

        _isProcessingPending = true;

        try
        {
            while (_pendingThumbnails.Count > 0 && !_isScrolling)
            {
                var item = _pendingThumbnails.Dequeue();
                _pendingThumbnailSet.Remove(item);

                if (item.Thumbnail != null)
                    continue;

                await vm.EnsureThumbnailAsync(item, 150);
            }
        }
        finally
        {
            _isProcessingPending = false;
        }
    }
}