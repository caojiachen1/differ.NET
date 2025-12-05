using Android.Content;
using Android.Content.Res;
using Android.Util;
using Android.Views;

namespace differ.NET.Android;

/// <summary>
/// 屏幕适配工具类，提供屏幕尺寸、密度相关的适配功能
/// </summary>
public static class ScreenAdapter
{
    private static DisplayMetrics? _displayMetrics;
    private static float _density = 1.0f;
    private static int _screenWidthDp;
    private static int _screenHeightDp;
    private static DeviceSizeCategory _deviceSizeCategory;

    /// <summary>
    /// 设备尺寸分类
    /// </summary>
    public enum DeviceSizeCategory
    {
        SmallPhone,     // 小屏手机 (< 360dp)
        NormalPhone,    // 普通手机 (360dp - 600dp)
        SmallTablet,    // 小平板 (600dp - 720dp)
        LargeTablet     // 大平板 (> 720dp)
    }

    /// <summary>
    /// 初始化屏幕适配器
    /// </summary>
    public static void Initialize(Context context)
    {
        _displayMetrics = context.Resources?.DisplayMetrics;
        if (_displayMetrics != null)
        {
            _density = _displayMetrics.Density;
            _screenWidthDp = (int)(_displayMetrics.WidthPixels / _density);
            _screenHeightDp = (int)(_displayMetrics.HeightPixels / _density);
            
            // 根据屏幕宽度确定设备分类
            _deviceSizeCategory = ClassifyDeviceSize(_screenWidthDp);
        }
    }

    /// <summary>
    /// 获取设备尺寸分类
    /// </summary>
    public static DeviceSizeCategory GetDeviceSizeCategory()
    {
        return _deviceSizeCategory;
    }

    /// <summary>
    /// 获取屏幕密度
    /// </summary>
    public static float GetDensity()
    {
        return _density;
    }

    /// <summary>
    /// 获取屏幕宽度(dp)
    /// </summary>
    public static int GetScreenWidthDp()
    {
        return _screenWidthDp;
    }

    /// <summary>
    /// 获取屏幕高度(dp)
    /// </summary>
    public static int GetScreenHeightDp()
    {
        return _screenHeightDp;
    }

    /// <summary>
    /// dp转px
    /// </summary>
    public static int DpToPx(float dp)
    {
        return (int)(dp * _density + 0.5f);
    }

    /// <summary>
    /// px转dp
    /// </summary>
    public static float PxToDp(int px)
    {
        return px / _density;
    }

    /// <summary>
    /// 获取图片网格列数
    /// </summary>
    public static int GetGridColumnCount()
    {
        return _deviceSizeCategory switch
        {
            DeviceSizeCategory.SmallPhone => 3,
            DeviceSizeCategory.NormalPhone => 3,
            DeviceSizeCategory.SmallTablet => 4,
            DeviceSizeCategory.LargeTablet => 5,
            _ => 3
        };
    }

    /// <summary>
    /// 获取图片网格项尺寸
    /// </summary>
    public static (int width, int height) GetGridItemSize()
    {
        int width = _deviceSizeCategory switch
        {
            DeviceSizeCategory.SmallPhone => 90,
            DeviceSizeCategory.NormalPhone => 100,
            DeviceSizeCategory.SmallTablet => 120,
            DeviceSizeCategory.LargeTablet => 140,
            _ => 100
        };

        int height = (int)(width * 1.2); // 高度为宽度的1.2倍
        return (DpToPx(width), DpToPx(height));
    }

    /// <summary>
    /// 获取缩略图尺寸
    /// </summary>
    public static int GetThumbnailSize()
    {
        return _deviceSizeCategory switch
        {
            DeviceSizeCategory.SmallPhone => DpToPx(75),
            DeviceSizeCategory.NormalPhone => DpToPx(80),
            DeviceSizeCategory.SmallTablet => DpToPx(105),
            DeviceSizeCategory.LargeTablet => DpToPx(130),
            _ => DpToPx(80)
        };
    }

    /// <summary>
    /// 获取工具栏高度
    /// </summary>
    public static int GetToolbarHeight()
    {
        return _deviceSizeCategory switch
        {
            DeviceSizeCategory.SmallPhone => DpToPx(48),
            DeviceSizeCategory.NormalPhone => DpToPx(56),
            DeviceSizeCategory.SmallTablet => DpToPx(64),
            DeviceSizeCategory.LargeTablet => DpToPx(72),
            _ => DpToPx(56)
        };
    }

    /// <summary>
    /// 获取文字大小
    /// </summary>
    public static float GetTextSize(float baseSize)
    {
        float multiplier = _deviceSizeCategory switch
        {
            DeviceSizeCategory.LargeTablet => 1.2f,
            DeviceSizeCategory.SmallTablet => 1.1f,
            _ => 1.0f
        };

        return baseSize * multiplier;
    }

    /// <summary>
    /// 获取按钮高度
    /// </summary>
    public static int GetButtonHeight()
    {
        return _deviceSizeCategory switch
        {
            DeviceSizeCategory.LargeTablet => DpToPx(56),
            DeviceSizeCategory.SmallTablet => DpToPx(48),
            _ => DpToPx(40)
        };
    }

    /// <summary>
    /// 是否为平板设备
    /// </summary>
    public static bool IsTablet()
    {
        return _deviceSizeCategory == DeviceSizeCategory.SmallTablet || 
               _deviceSizeCategory == DeviceSizeCategory.LargeTablet;
    }

    /// <summary>
    /// 是否为横屏
    /// </summary>
    public static bool IsLandscape(Context context)
    {
        var orientation = context.Resources?.Configuration?.Orientation;
        return orientation == global::Android.Content.Res.Orientation.Landscape;
    }

    /// <summary>
    /// 根据屏幕宽度分类设备
    /// </summary>
    private static DeviceSizeCategory ClassifyDeviceSize(int widthDp)
    {
        if (widthDp >= 720)
            return DeviceSizeCategory.LargeTablet;
        else if (widthDp >= 600)
            return DeviceSizeCategory.SmallTablet;
        else if (widthDp >= 360)
            return DeviceSizeCategory.NormalPhone;
        else
            return DeviceSizeCategory.SmallPhone;
    }

    /// <summary>
    /// 获取屏幕信息字符串
    /// </summary>
    public static string GetScreenInfo()
    {
        return $"Device: {_deviceSizeCategory}, " +
               $"Size: {_screenWidthDp}x{_screenHeightDp}dp, " +
               $"Density: {_density:F2}, " +
               $"Grid: {GetGridColumnCount()} columns";
    }
}