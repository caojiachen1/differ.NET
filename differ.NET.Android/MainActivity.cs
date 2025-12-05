using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
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
                            // Storage provider is handled differently on Android
                            // viewModel.SetStorageProvider(StorageProvider);
                        }
                    }
                }
            });
    }
}