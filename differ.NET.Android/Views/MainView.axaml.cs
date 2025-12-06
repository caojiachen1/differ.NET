using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using differ.NET.Android.ViewModels;
using differ.NET.Models;

namespace differ.NET.Android.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        try
        {
            // 在关联到视觉树时确保ViewModel拿到StorageProvider（兼容Android）
            if (TopLevel.GetTopLevel(this) is { } top && DataContext is MainViewModel vm)
            {
                vm.SetStorageProvider(top.StorageProvider);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainView.Android] Failed to set StorageProvider on attach: {ex.Message}");
        }
    }

    private async void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ImageItem imageItem)
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed || 
                e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                // 长按或右键设置为搜索源
                var viewModel = DataContext as MainViewModel;
                if (viewModel != null)
                {
                    await viewModel.SetAsSourceAsync(imageItem);
                }
            }
        }
    }

    private void OnCloseSimilarImages(object? sender, PointerPressedEventArgs e)
    {
        // 点击背景关闭相似图片结果
        var viewModel = DataContext as MainViewModel;
        if (viewModel != null)
        {
            viewModel.ClearSourceCommand.Execute(null);
        }
    }

    private void OnPopupContentPressed(object? sender, PointerPressedEventArgs e)
    {
        // 阻止事件冒泡，避免点击弹窗内容时关闭弹窗
        e.Handled = true;
    }
}