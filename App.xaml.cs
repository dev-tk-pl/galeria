using Microsoft.UI.Xaml;
using Velopack;
using Velopack.Sources;

namespace GaleriaDesktop;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        _ = AktualizujAsync();
    }

    // Jeśli na GitHub Releases jest nowsza wersja: pobierz, zainstaluj i uruchom ponownie
    private static async Task AktualizujAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource("https://github.com/dev-tk-pl/galeria", null, false));
            if (!mgr.IsInstalled) return; // dotnet run albo exe spoza instalatora
            var nowa = await mgr.CheckForUpdatesAsync();
            if (nowa == null) return;
            await mgr.DownloadUpdatesAsync(nowa);
            mgr.ApplyUpdatesAndRestart(nowa);
        }
        catch (Exception ex)
        {
            // Brak sieci itp. — spróbujemy przy następnym uruchomieniu
            System.Diagnostics.Debug.WriteLine($"Aktualizacja: {ex.Message}");
        }
    }
}
