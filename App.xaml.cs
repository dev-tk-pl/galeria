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
        // Zainstalowana wersja z włączoną autoaktualizacją: najpierw okienko sprawdzania aktualizacji.
        var mgr = new UpdateManager(new GithubSource("https://github.com/dev-tk-pl/galeria", null, false));
        var ustawienia = Ustawienia.Wczytaj();
        _window = mgr.IsInstalled && ustawienia.Autoaktualizacja ? new OknoStartowe(mgr, ustawienia) : new MainWindow();
        _window.Activate();
    }
}
