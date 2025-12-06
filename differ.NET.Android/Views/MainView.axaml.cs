using Avalonia.Controls;
using Avalonia.Input;
using differ.NET.Android.ViewModels;
using differ.NET.Models;

namespace differ.NET.Android.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
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