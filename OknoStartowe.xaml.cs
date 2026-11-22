using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Velopack;

namespace GaleriaDesktop;

// Okienko przed głównym oknem: jest nowa wersja → pobranie, instalacja i uruchomienie nowej wersji;
// nie ma (albo brak sieci) → otwiera Galerię.
public sealed partial class OknoStartowe : Window
{
    private readonly UpdateManager _mgr;

    public OknoStartowe(UpdateManager mgr, Ustawienia ustawienia)
    {
        _mgr = mgr;
        MainWindow.UstawKolorPrzewodni(ustawienia.KolorPrzewodni);
        InitializeComponent();
        Korzen.RequestedTheme = ustawienia.Motyw switch { "Jasny" => ElementTheme.Light, "Ciemny" => ElementTheme.Dark, _ => ElementTheme.Default };
        var ikona = Path.Combine(AppContext.BaseDirectory, "Assets", "Galeria.ico");
        AppWindow.SetIcon(ikona);
        Ikona.Source = new BitmapImage(new Uri(ikona));

        // Bez paska tytułu, stały rozmiar, na środku ekranu.
        var prezenter = OverlappedPresenter.Create();
        prezenter.IsResizable = prezenter.IsMaximizable = prezenter.IsMinimizable = false;
        prezenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(prezenter);
        var skala = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        int szer = (int)(420 * skala), wys = (int)(170 * skala);
        var obszar = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new(obszar.X + (obszar.Width - szer) / 2, obszar.Y + (obszar.Height - wys) / 2, szer, wys));

        _ = Sprawdz();
    }

    private async Task Sprawdz()
    {
        try
        {
            // Limit czasu: przy wolnej/zawieszonej sieci nie blokujemy startu.
            var sprawdzenie = _mgr.CheckForUpdatesAsync();
            if (await Task.WhenAny(sprawdzenie, Task.Delay(TimeSpan.FromSeconds(10))) == sprawdzenie
                && await sprawdzenie is { } nowa)
            {
                var wersja = nowa.TargetFullRelease.Version;
                Tekst.Text = $"Pobieranie wersji {wersja}…";
                Pasek.IsIndeterminate = false;
                await _mgr.DownloadUpdatesAsync(nowa, p => DispatcherQueue.TryEnqueue(() =>
                {
                    Pasek.Value = p;
                    Tekst.Text = $"Pobieranie wersji {wersja}… {p}%";
                }));
                _mgr.ApplyUpdatesAndRestart(nowa); // kończy proces i uruchamia nową wersję
                return;
            }
        }
        catch (Exception ex)
        {
            // Brak sieci, przerwane pobieranie itp. — spróbujemy przy następnym uruchomieniu
            Debug.WriteLine($"Aktualizacja: {ex.Message}");
        }
        new MainWindow().Activate();
        Close();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
