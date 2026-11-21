using MauiBle.Sample.Bluetooth;
using Microsoft.Extensions.Logging;

namespace MauiBle.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if ANDROID
        builder.Services.AddSingleton<IBleTransport, BleTransport>();
#else
        builder.Services.AddSingleton<IBleTransport, NullBleTransport>();
#endif
        builder.Services.AddSingleton<LobbySession>();
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
