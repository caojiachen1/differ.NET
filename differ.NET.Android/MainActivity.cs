using Android.App;
using Android.Content.PM;
using System;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using differ.NET.Android.ViewModels;

namespace differ.NET.Android;

[Activity(
    Label = "differ.NET.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .AfterSetup(async b =>
            {
                // 设置存储提供程序给ViewModel
                if (b.Instance is App app)
                {
                    var lifetime = app.ApplicationLifetime as ISingleViewApplicationLifetime;
                    if (lifetime?.MainView is Views.MainView mainView)
                    {
                        if (mainView.DataContext is MainViewModel viewModel)
                        {
                            try
                            {
                                // 尝试通过 MainView 的 TopLevel 获取 StorageProvider 并设置给 ViewModel
                                var top = TopLevel.GetTopLevel(mainView);
                                if (top != null)
                                {
                                    viewModel.SetStorageProvider(top.StorageProvider);
                                    Console.WriteLine("[MainActivity] StorageProvider set on MainViewModel via TopLevel");
                                }
                                else
                                {
                                    Console.WriteLine("[MainActivity] TopLevel for MainView is null; cannot set StorageProvider yet.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[MainActivity] Failed to set StorageProvider: {ex.Message}");
                            }
                        }
                    }
                }
            });
    }
}