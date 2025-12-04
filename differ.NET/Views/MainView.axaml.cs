using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using differ.NET.Models;
using differ.NET.ViewModels;

namespace differ.NET.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
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
            if (sender is Border border && border.ContextFlyout is MenuFlyout flyout)
            {
                flyout.ShowAt(border);
                e.Handled = true;
            }
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
}