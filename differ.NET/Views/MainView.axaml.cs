using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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