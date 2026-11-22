using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.VisualBasic.FileIO;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using Velopack;
using Velopack.Sources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.System;

namespace GaleriaDesktop;

public sealed partial class MainWindow : Window
{
    private readonly Ustawienia _ustawienia = Ustawienia.Wczytaj();
    private List<ElementGalerii> _wszystkie = [];
    private List<ElementGalerii> _widoczne = [];
    private int _indeksPodgladu;
    private int _numerPodgladu;   // rośnie przy każdej zmianie pliku/obrotu — porzucamy wyniki starszych wczytań
    private int _obrot;           // obrót w podglądzie: ćwierćobroty w prawo (0–3)
    private bool _widokUlubionych;
    private bool _ulubioneZmienione;
    private ItemsWrapGrid? _panelSiatki;
    private readonly HashSet<string> _zaznaczone = new(StringComparer.OrdinalIgnoreCase); // ścieżki zaznaczonych plików
    private bool _odswiezanieSiatki; // podmiana ItemsSource — ignorujemy zdarzenia SelectionChanged
    private bool _eksport;
    private List<StorageFile> _doUdostepnienia = [];

    public MainWindow()
    {
        UstawKolorPrzewodni(_ustawienia.KolorPrzewodni); // przed InitializeComponent, żeby XAML od razu wziął kolor
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Galeria.ico"));
        Sortowanie.SelectedIndex = 0;
        Sortowanie.SelectionChanged += (_, _) => OdswiezWidok();
        WyborKolumn.SelectedIndex = _ustawienia.Kolumny switch { 3 => 1, 5 => 2, 7 => 3, _ => 0 };
        WyborKolumn.SelectionChanged += WyborKolumn_SelectionChanged;
        WyborMotywu.SelectedIndex = _ustawienia.Motyw switch { "Jasny" => 1, "Ciemny" => 2, _ => 0 };
        WyborMotywu.SelectionChanged += WyborMotywu_SelectionChanged;
        UstawMotyw();
        SuwakPowiekszenia.Value = 100;
        SuwakPowiekszenia.ValueChanged += SuwakPowiekszenia_ValueChanged;
        EdytorZdjec.Zapisano += DodajZapisanaKopie;
        PrzelacznikAktualizacji.IsOn = _ustawienia.Autoaktualizacja;
        PrzelacznikAktualizacji.Toggled += (_, _) =>
        {
            _ustawienia.Autoaktualizacja = PrzelacznikAktualizacji.IsOn;
            _ustawienia.Zapisz();
        };
        OknoKoloru.Opening += (_, _) => WyborKoloru.Color = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];

        // Udostępnianie: systemowe okno Windows dostaje pliki przygotowane w Udostepnij_Click.
        var okno = WinRT.Interop.WindowNative.GetWindowHandle(this);
        DataTransferManagerInterop.GetForWindow(okno).DataRequested += (_, args) =>
        {
            args.Request.Data.Properties.Title = $"Zdjęcia z Galerii ({_doUdostepnienia.Count})";
            args.Request.Data.SetStorageItems(_doUdostepnienia);
        };

        // Po restarcie zapamiętane foldery wczytują się same.
        _ = WczytajBiblioteke();
        _ = SprawdzAktualizacje();
    }

    // Jeśli na GitHub Releases jest nowsza wersja: przy włączonej autoaktualizacji pobierz, zainstaluj
    // i uruchom ponownie; przy wyłączonej tylko pokaż informację na górnym pasku.
    private async Task SprawdzAktualizacje()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource("https://github.com/dev-tk-pl/galeria", null, false));
            if (!mgr.IsInstalled) return; // dotnet run albo exe spoza instalatora
            var nowa = await mgr.CheckForUpdatesAsync();
            if (nowa == null) return;

            var wersja = nowa.TargetFullRelease.Version;
            StanAktualizacji.Visibility = Visibility.Visible;
            KoloAktualizacji.Visibility = Widocznosc(_ustawienia.Autoaktualizacja);
            if (!_ustawienia.Autoaktualizacja)
            {
                TekstAktualizacji.Text = $"Dostępna wersja {wersja}";
                return;
            }
            TekstAktualizacji.Text = $"Pobieranie wersji {wersja}… nie zamykaj okna";
            await mgr.DownloadUpdatesAsync(nowa);
            mgr.ApplyUpdatesAndRestart(nowa);
        }
        catch (Exception ex)
        {
            // Brak sieci itp. — spróbujemy przy następnym uruchomieniu
            StanAktualizacji.Visibility = Visibility.Collapsed;
            Debug.WriteLine($"Aktualizacja: {ex.Message}");
        }
    }

    private async void DodajFolder_Click(object sender, RoutedEventArgs e)
    {
        var wynik = await new FolderPicker(AppWindow.Id).PickSingleFolderAsync();
        if (wynik is null || _ustawienia.Foldery.Contains(wynik.Path, StringComparer.OrdinalIgnoreCase)) return;

        _ustawienia.Foldery.Add(wynik.Path);
        _ustawienia.Zapisz();
        await WczytajBiblioteke();
    }

    private void OdlaczFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = (FolderZrodlowy)((FrameworkElement)sender).DataContext;
        _ustawienia.Foldery.Remove(folder.Sciezka);
        _ustawienia.Zapisz();
        _ = WczytajBiblioteke();
    }

    private void OdlaczWszystkie_Click(object sender, RoutedEventArgs e)
    {
        _ustawienia.Foldery.Clear();
        _ustawienia.Zapisz();
        _ = WczytajBiblioteke();
    }

    // Po każdej zmianie listy folderów skanujemy wszystkie od nowa — prościej niż scalać foldery zagnieżdżone.
    private async Task WczytajBiblioteke()
    {
        Naglowek.Visibility = StanPusty.Visibility = PasekNarzedzi.Visibility = Visibility.Collapsed;
        Siatka.Visibility = BrakWynikow.Visibility = Visibility.Collapsed;
        StanWczytywania.Visibility = Visibility.Visible;

        var foldery = _ustawienia.Foldery.Where(Directory.Exists).ToList();
        _wszystkie = await Task.Run(() => foldery
            .SelectMany(Skanuj)
            .DistinctBy(e => e.Sciezka, StringComparer.OrdinalIgnoreCase)
            .ToList());

        StanWczytywania.Visibility = Visibility.Collapsed;
        var n = _ustawienia.Foldery.Count;
        OpisFolderu.Text = n == 0 ? "Dodaj folder ze zdjęciami" : $"{n} {(n == 1 ? "podłączony" : "podłączone")} · zarządzaj";
        OdswiezListeFolderow();
        OdswiezWidok();
    }

    private void OdswiezListeFolderow()
    {
        ListaFolderow.ItemsSource = _ustawienia.Foldery
            .Select(f => new FolderZrodlowy(f, _wszystkie.Count(e => e.Folder == f)))
            .ToList();
        BrakFolderow.Visibility = Widocznosc(_ustawienia.Foldery.Count == 0);
        OdlaczWszystkie.Visibility = Widocznosc(_ustawienia.Foldery.Count > 0);
    }

    private async void Ustawienia_Click(object sender, RoutedEventArgs e)
    {
        OknoUstawien.XamlRoot = Content.XamlRoot;
        // Wersja z <Version> w GaleriaDesktop.csproj
        WersjaAplikacji.Text = $"Wersja: {typeof(App).Assembly.GetName().Version!.ToString(3)}";
        await OknoUstawien.ShowAsync();
    }

    private void WyborMotywu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ustawienia.Motyw = (string)WyborMotywu.SelectedItem;
        _ustawienia.Zapisz();
        UstawMotyw();
    }

    private void UstawMotyw()
    {
        var motyw = _ustawienia.Motyw switch { "Jasny" => ElementTheme.Light, "Ciemny" => ElementTheme.Dark, _ => ElementTheme.Default };
        Korzen.RequestedTheme = motyw;
        OknoUstawien.RequestedTheme = motyw; // okno dialogowe jest w osobnej warstwie i nie dziedziczy motywu
        AppWindow.TitleBar.PreferredTheme = motyw switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    // Rekurencyjnie, jak w PWA.
    private static List<ElementGalerii> Skanuj(string folder)
    {
        var opcje = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return new DirectoryInfo(folder).EnumerateFiles("*", opcje)
            .Select(plik => (plik, film: ElementGalerii.RozszerzeniaFilmow.Contains(plik.Extension)))
            .Where(x => x.film || ElementGalerii.RozszerzeniaZdjec.Contains(x.plik.Extension))
            .Select(x => NowyElement(x.plik, folder, x.film))
            .ToList();
    }

    private static ElementGalerii NowyElement(FileInfo plik, string folder, bool film) => new()
    {
        Sciezka = plik.FullName,
        Folder = folder,
        SciezkaWzgledna = Path.GetRelativePath(folder, plik.FullName).Replace('\\', '/'),
        CzyFilm = film,
        DataModyfikacji = plik.LastWriteTime,
        Rozmiar = plik.Length,
    };

    private void Edytuj_Click(object sender, RoutedEventArgs e)
        => _ = EdytorZdjec.Otworz(_widoczne[_indeksPodgladu].Sciezka, AppWindow.Id);

    // Kopia z edytora zapisana w podłączonym folderze trafia od razu do biblioteki i do podglądu.
    private void DodajZapisanaKopie(string sciezka)
    {
        var folder = _ustawienia.Foldery.FirstOrDefault(f => sciezka.StartsWith(f.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        if (folder is null) return;
        _wszystkie.RemoveAll(e => e.Sciezka.Equals(sciezka, StringComparison.OrdinalIgnoreCase)); // nadpisany plik
        _wszystkie.Add(NowyElement(new FileInfo(sciezka), folder, film: false));
        OdswiezWidok();
        var indeks = _widoczne.FindIndex(e => e.Sciezka.Equals(sciezka, StringComparison.OrdinalIgnoreCase));
        if (indeks >= 0) _ = PokazWPodgladzie(indeks);
    }

    // Widok (wszystkie / ulubione) + filtr wyszukiwania (nazwa i ścieżka) + sortowanie, jak w PWA.
    private void OdswiezWidok()
    {
        var fraza = Szukaj.Text.Trim();
        var wynik = _wszystkie.Where(e => (!_widokUlubionych || _ustawienia.Ulubione.Contains(e.Sciezka))
            && (fraza.Length == 0 || ElementGalerii.Polski.CompareInfo.IndexOf(e.SciezkaWzgledna, fraza, CompareOptions.IgnoreCase) >= 0));
        _widoczne = (Sortowanie.SelectedIndex switch
        {
            1 => wynik.OrderBy(e => e.DataModyfikacji),
            2 => wynik.OrderBy(e => e.Nazwa, StringComparer.Create(ElementGalerii.Polski, ignoreCase: true)),
            _ => wynik.OrderByDescending(e => e.DataModyfikacji),
        }).ToList();

        var trybZaznaczania = Siatka.SelectionMode == ListViewSelectionMode.Multiple;
        foreach (var element in _widoczne)
        {
            element.Ulubiony = _ustawienia.Ulubione.Contains(element.Sciezka);
            element.TrybZaznaczania = trybZaznaczania;
            element.Zaznaczony = _zaznaczone.Contains(element.Sciezka);
        }
        _odswiezanieSiatki = true;
        Siatka.ItemsSource = _widoczne;
        if (Siatka.SelectionMode == ListViewSelectionMode.Multiple) // zaznaczenie przeżywa wyszukiwanie i sortowanie
            foreach (var element in _widoczne.Where(e => _zaznaczone.Contains(e.Sciezka))) Siatka.SelectedItems.Add(element);
        _odswiezanieSiatki = false;
        OdswiezZaznaczenie();
        LiczbaPlikow.Text = Pliki(_widoczne.Count);
        NotkaDolna.Text = $"Przeglądasz lokalne pliki · {Pliki(_wszystkie.Count)} multimedialnych w bibliotece";
        LicznikWszystkich.Text = _wszystkie.Count.ToString();
        LicznikUlubionych.Text = _wszystkie.Count(e => _ustawienia.Ulubione.Contains(e.Sciezka)).ToString();

        var saPliki = _wszystkie.Count > 0;
        Naglowek.Visibility = StanPusty.Visibility = Widocznosc(!saPliki);
        PasekNarzedzi.Visibility = Widocznosc(saPliki);
        Siatka.Visibility = Widocznosc(_widoczne.Count > 0);
        BrakWynikow.Visibility = Widocznosc(saPliki && _widoczne.Count == 0);
        var brakUlubionych = _widokUlubionych && fraza.Length == 0;
        BrakWynikowIkona.Glyph = brakUlubionych ? "" : "";
        BrakWynikowTytul.Text = brakUlubionych ? "Nie masz jeszcze ulubionych" : "Nie znaleziono plików";
        BrakWynikowOpis.Text = brakUlubionych ? "Kliknij serduszko na zdjęciu, aby dodać je do ulubionych." : "Zmień wyszukiwaną frazę i spróbuj ponownie.";
    }

    private void NawWszystkie_Click(object sender, RoutedEventArgs e) => UstawWidok(ulubione: false);
    private void NawUlubione_Click(object sender, RoutedEventArgs e) => UstawWidok(ulubione: true);

    private void UstawWidok(bool ulubione)
    {
        _widokUlubionych = ulubione;
        var zaznaczenie = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        NawWszystkie.Background = ulubione ? null : zaznaczenie;
        NawUlubione.Background = ulubione ? zaznaczenie : null;
        TytulWidoku.Text = ulubione ? "Ulubione" : "Wszystkie zdjęcia";
        OdswiezWidok();
    }

    private static string Pliki(int n) => $"{n} {(n == 1 ? "plik" : "plików")}";
    private static Visibility Widocznosc(bool widoczny) => widoczny ? Visibility.Visible : Visibility.Collapsed;

    private void Szukaj_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => OdswiezWidok();

    private void Szukaj_Skrot(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Szukaj.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void WyborKolumn_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ustawienia.Kolumny = WyborKolumn.SelectedIndex switch { 1 => 3, 2 => 5, 3 => 7, _ => 0 };
        _ustawienia.Zapisz();
        UstawKolumny();
    }

    // Kolumny: wybór użytkownika (3/5/7) albo „Domyślna” jak w PWA: 5 / 4 / 3 (progi okna 1250 / 950 px
    // minus pasek boczny i marginesy). Miniatury 4:3.
    private void PanelSiatki_Loaded(object sender, RoutedEventArgs e)
    {
        _panelSiatki = (ItemsWrapGrid)sender;
        UstawKolumny();
    }

    private void Siatka_SizeChanged(object sender, SizeChangedEventArgs e) => UstawKolumny();

    private void UstawKolumny()
    {
        var szerokosc = Siatka.ActualWidth;
        if (_panelSiatki is null || szerokosc <= 0) return;
        var kolumny = _ustawienia.Kolumny > 0 ? _ustawienia.Kolumny : szerokosc >= 900 ? 5 : szerokosc >= 600 ? 4 : 3;
        _panelSiatki.ItemWidth = Math.Floor(szerokosc / kolumny);
        _panelSiatki.ItemHeight = Math.Floor((_panelSiatki.ItemWidth - 17) * 3 / 4) + 9 + 46 + 22; // miniatura + odstęp + podpis + margines
    }

    // Miniatury ładowane leniwie, tylko dla widocznych kafelków; zwalniane przy recyklingu kontenera.
    private async void Siatka_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var element = (ElementGalerii)args.Item;
        element.NaEkranie = !args.InRecycleQueue;
        if (args.InRecycleQueue)
        {
            element.Miniatura = null;
            return;
        }
        if (element.Miniatura is not null) return;

        try
        {
            // Miniatura pobierana poza wątkiem UI: wywołana na wątku UI powodowała losową natywną awarię
            // XAML (0xc000027b) w ~połowie uruchomień; w tle — 0 awarii na 20 prób.
            using var miniatura = await Task.Run(async () =>
            {
                // Przy szybkim przewijaniu kafelek mógł już zniknąć — pomijamy, żeby widoczne nie czekały w kolejce.
                if (!element.NaEkranie) return null;
                var plik = await StorageFile.GetFileFromPathAsync(element.Sciezka);
                return await plik.GetThumbnailAsync(ThumbnailMode.SingleItem, 320);
            });
            if (miniatura is null || !element.NaEkranie) return;

            var obraz = new BitmapImage();
            await obraz.SetSourceAsync(miniatura);
            element.Miniatura = obraz;
        }
        catch
        {
            // Windows nie umie zrobić miniatury — kafelek zostaje pusty.
        }
    }

    private void Siatka_ItemClick(object sender, ItemClickEventArgs e)
        => _ = PokazWPodgladzie(_widoczne.IndexOf((ElementGalerii)e.ClickedItem));

    // ---------- Zaznaczanie i eksport (jak w PWA) ----------

    private void WybierzPliki_Click(object sender, RoutedEventArgs e) => UstawTrybZaznaczania(true);
    private void Gotowe_Click(object sender, RoutedEventArgs e) => UstawTrybZaznaczania(false);

    // W trybie zaznaczania kafelki mają pola wyboru, a kliknięcie zaznacza zamiast otwierać podgląd.
    private void UstawTrybZaznaczania(bool tryb)
    {
        _zaznaczone.Clear();
        WybierzPliki.Visibility = PoleSortowania.Visibility = PoleSiatki.Visibility = Widocznosc(!tryb);
        PanelZaznaczania.Visibility = Widocznosc(tryb);
        _odswiezanieSiatki = true;
        Siatka.SelectionMode = tryb ? ListViewSelectionMode.Multiple : ListViewSelectionMode.None;
        _odswiezanieSiatki = false;
        Siatka.IsItemClickEnabled = !tryb;
        foreach (var element in _widoczne)
        {
            element.TrybZaznaczania = tryb;
            element.Zaznaczony = false;
        }
        OdswiezZaznaczenie();
    }

    private void Siatka_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_odswiezanieSiatki) return;
        foreach (ElementGalerii element in e.AddedItems)
        {
            _zaznaczone.Add(element.Sciezka);
            element.Zaznaczony = true;
        }
        foreach (ElementGalerii element in e.RemovedItems)
        {
            _zaznaczone.Remove(element.Sciezka);
            element.Zaznaczony = false;
        }
        OdswiezZaznaczenie();
    }

    private List<ElementGalerii> Zaznaczone() => _wszystkie.Where(e => _zaznaczone.Contains(e.Sciezka)).ToList();
    private bool WszystkieWidoczneZaznaczone() => _widoczne.Count > 0 && _widoczne.All(e => _zaznaczone.Contains(e.Sciezka));

    private void OdswiezZaznaczenie()
    {
        var n = Zaznaczone().Count;
        LiczbaZaznaczonych.Text = $"{n} zazn.";
        ZapiszDo.IsEnabled = PobierzZip.IsEnabled = Udostepnij.IsEnabled = n > 0 && !_eksport;
        ZaznaczWidoczne.Content = WszystkieWidoczneZaznaczone() ? "Odznacz widoczne" : "Zaznacz widoczne";
    }

    private void ZaznaczWidoczne_Click(object sender, RoutedEventArgs e)
    {
        if (WszystkieWidoczneZaznaczone()) Siatka.SelectedItems.Clear();
        else Siatka.SelectAll();
    }

    // Wszystkie pliki trafiają luzem do wskazanego folderu.
    // Klasyczny systemowy wybór folderu: ten z Windows App SDK nie pozwalał wybrać Pulpitu ani katalogu
    // głównego pendrive'a („Brak obiektu dla monikera” / „Nazwa folderu jest nieprawidłowa”).
    private async void ZapiszDo_Click(object sender, RoutedEventArgs e)
    {
        var okno = new Windows.Storage.Pickers.FolderPicker { CommitButtonText = "Zapisz" };
        okno.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(okno, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await okno.PickSingleFolderAsync();
        if (folder is null) return;
        var pliki = Zaznaczone();
        await Eksportuj(() =>
        {
            var zajete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plik in pliki)
                File.Copy(plik.Sciezka, Path.Combine(folder.Path, WolnaNazwa(plik.Nazwa, zajete, folder.Path)));
        }, $"Zapisano {Pliki(pliki.Count)} w folderze {folder.Path}");
    }

    private async void PobierzZip_Click(object sender, RoutedEventArgs e)
    {
        var pliki = Zaznaczone();
        var okno = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = $"Galeria-{DateTime.Now:yyyy-MM-dd}-{pliki.Count}-plikow",
            DefaultFileExtension = ".zip",
        };
        okno.FileTypeChoices.Add("Archiwum ZIP", [".zip"]);
        var wynik = await okno.PickSaveFileAsync();
        if (wynik is null) return;
        await Eksportuj(() =>
        {
            // Bez kompresji: zdjęcia i filmy są już skompresowane, a tak jest dużo szybciej.
            using var zip = new ZipArchive(File.Create(wynik.Path), ZipArchiveMode.Create);
            var zajete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plik in pliki)
                zip.CreateEntryFromFile(plik.Sciezka, WolnaNazwa(plik.Nazwa, zajete), CompressionLevel.NoCompression);
        }, $"Zapisano archiwum {Path.GetFileName(wynik.Path)} ({Pliki(pliki.Count)})");
    }

    // Systemowe okno udostępniania Windows (Poczta, Phone Link, Udostępnianie w pobliżu…).
    private async void Udostepnij_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _doUdostepnienia = [.. await Task.WhenAll(Zaznaczone().Select(p => StorageFile.GetFileFromPathAsync(p.Sciezka).AsTask()))];
            DataTransferManagerInterop.ShowShareUIForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch (Exception ex)
        {
            PokazKomunikat($"Nie udało się udostępnić plików: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task Eksportuj(Action praca, string sukces)
    {
        _eksport = true;
        OdswiezZaznaczenie();
        try
        {
            await Task.Run(praca);
            PokazKomunikat(sukces, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            PokazKomunikat($"Nie udało się: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _eksport = false;
            OdswiezZaznaczenie();
        }
    }

    private void PokazKomunikat(string tekst, InfoBarSeverity rodzaj)
    {
        Komunikat.Severity = rodzaj;
        Komunikat.Message = tekst;
        Komunikat.IsOpen = true;
    }

    // „zdjecie.jpg”, „zdjecie (2).jpg”… — nazwa nieużyta w tym eksporcie i (gdy podano folder) nieistniejąca na dysku.
    private static string WolnaNazwa(string nazwa, HashSet<string> zajete, string? folder = null)
    {
        var kandydat = nazwa;
        for (var i = 2; zajete.Contains(kandydat) || folder is not null && File.Exists(Path.Combine(folder, kandydat)); i++)
            kandydat = $"{Path.GetFileNameWithoutExtension(nazwa)} ({i}){Path.GetExtension(nazwa)}";
        zajete.Add(kandydat);
        return kandydat;
    }

    // ---------- Kolor przewodni ----------

    private void ZastosujKolor_Click(object sender, RoutedEventArgs e)
    {
        var c = WyborKoloru.Color;
        ZmienKolorPrzewodni($"#{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    private void KolorDomyslny_Click(object sender, RoutedEventArgs e) => ZmienKolorPrzewodni(null);

    private void ZmienKolorPrzewodni(string? kolor)
    {
        OknoKoloru.Hide();
        _ustawienia.KolorPrzewodni = kolor;
        _ustawienia.Zapisz();
        UstawKolorPrzewodni(kolor);
        // Pędzle z ThemeResource przeliczają kolor dopiero przy zmianie motywu — przełączamy go tam i z powrotem.
        var odwrotny = Korzen.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Korzen.RequestedTheme = OknoUstawien.RequestedTheme = odwrotny;
        UstawMotyw();
    }

    private const string DomyslnyKolor = "#138AE2";

    // Nadpisuje kolor akcentu Windows (SystemAccentColor i jego jaśniejsze/ciemniejsze odcienie); null = kolor domyślny.
    private static void UstawKolorPrzewodni(string? kolor)
    {
        kolor ??= DomyslnyKolor;
        var zasoby = Application.Current.Resources;
        string[] klucze = ["SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                           "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"];
        var baza =Windows.UI.Color.FromArgb(255, Convert.ToByte(kolor[1..3], 16), Convert.ToByte(kolor[3..5], 16), Convert.ToByte(kolor[5..7], 16));
        zasoby[klucze[0]] = baza;
        for (var i = 1; i <= 3; i++)
        {
            zasoby[klucze[i]] = Wymieszaj(baza, Colors.White, 0.15 * i);
            zasoby[klucze[i + 3]] = Wymieszaj(baza, Colors.Black, 0.15 * i);
        }
    }

    private static Windows.UI.Color Wymieszaj(Windows.UI.Color a, Windows.UI.Color b, double t) => Windows.UI.Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private void Poprzedni_Click(object sender, RoutedEventArgs e) => PrzejdzO(-1);
    private void Nastepny_Click(object sender, RoutedEventArgs e) => PrzejdzO(1);

    // ←/→ w podglądzie przechwytywane na poziomie okna — inaczej strzałki zjada siatka, która ma fokus pod podglądem.
    private void Okno_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (EdytorZdjec.Visibility == Visibility.Visible)
        {
            // W edytorze Esc zamyka tylko edytor; strzałki zostają dla suwaków.
            if (e.Key != VirtualKey.Escape) return;
            EdytorZdjec.Zamknij();
            e.Handled = true;
            return;
        }
        if (Podglad.Visibility != Visibility.Visible) return;
        if (e.Key == VirtualKey.Left) PrzejdzO(-1);
        else if (e.Key == VirtualKey.Right) PrzejdzO(1);
        else return;
        e.Handled = true;
    }

    // Strzałki zawijają się na końcach listy, jak w PWA.
    private void PrzejdzO(int krok)
    {
        if (_widoczne.Count < 2) return;
        _ = PokazWPodgladzie((_indeksPodgladu + krok + _widoczne.Count) % _widoczne.Count);
    }

    private async Task PokazWPodgladzie(int indeks)
    {
        var numer = ++_numerPodgladu;
        _indeksPodgladu = indeks;
        _obrot = 0;
        var element = _widoczne[indeks];
        PodgladNazwa.Text = element.Nazwa;
        PodgladData.Text = element.DataTekst;
        PodgladRozmiar.Text = element.RozmiarTekst;
        PodgladSciezka.Text = element.SciezkaWzgledna;
        PodgladLicznik.Text = $"{indeks + 1} / {_widoczne.Count}";
        ZnaczekWideo.Visibility = Widocznosc(element.CzyFilm);
        NarzedziaZdjecia.Visibility = Powiekszenie.Visibility = Widocznosc(!element.CzyFilm);
        UstawSerce(element);
        WyczyscPodglad();
        Podglad.Visibility = Visibility.Visible;

        if (element.CzyFilm)
        {
            var plik = await StorageFile.GetFileFromPathAsync(element.Sciezka);
            if (numer != _numerPodgladu) return; // w międzyczasie przełączono na inny plik
            // Kodeki Windows (Media Foundation), bez konwersji.
            PodgladFilmu.Source = MediaSource.CreateFromStorageFile(plik);
            PodgladFilmu.Visibility = Visibility.Visible;
            return;
        }

        var exif = Task.Run(() => CzytajExif(element.Sciezka));
        await WyswietlZdjecie(element, numer);
        var pola = await exif;
        if (numer != _numerPodgladu) return;
        ListaExif.ItemsSource = pola;
        PasekExif.Visibility = Widocznosc(pola.Count > 0);
    }

    // Dekodowanie przez BitmapDecoder: uwzględnia orientację EXIF i pozwala obrócić obraz o 90°
    // tak, by po obrocie dalej mieścił się w oknie.
    private async Task WyswietlZdjecie(ElementGalerii element, int numer)
    {
        var obrot = (BitmapRotation)_obrot;
        using var bitmapa = await Task.Run(() => Obrazy.Dekoduj(element.Sciezka, obrot));
        if (numer != _numerPodgladu) return;
        var zrodlo = new SoftwareBitmapSource();
        await zrodlo.SetBitmapAsync(bitmapa);
        if (numer != _numerPodgladu) return;
        PodgladZdjecia.Source = zrodlo;
        PrzewijanieZdjecia.ChangeView(0, 0, 1, disableAnimation: true);
        PrzewijanieZdjecia.Visibility = Visibility.Visible;
    }

    private static readonly string[] KluczeExif =
    [
        "System.Photo.CameraManufacturer", "System.Photo.CameraModel", "System.Photo.FocalLength",
        "System.Photo.FNumber", "System.Photo.ExposureTime", "System.Photo.ISOSpeed",
    ];

    // Metadane z systemowych właściwości pliku (te same co w Eksploratorze → Właściwości → Szczegóły).
    private static async Task<List<PoleExif>> CzytajExif(string sciezka)
    {
        var plik = await StorageFile.GetFileFromPathAsync(sciezka);
        var w = await plik.Properties.RetrievePropertiesAsync(KluczeExif);
        object? Wartosc(string klucz) => w.TryGetValue(klucz, out var v) ? v : null;

        var pola = new List<PoleExif>();
        var producent = (Wartosc("System.Photo.CameraManufacturer") as string)?.Trim();
        var model = (Wartosc("System.Photo.CameraModel") as string)?.Trim();
        // „NIKON CORPORATION” + „NIKON D5100” → „NIKON D5100”; „Canon” + „EOS 600D” → „Canon EOS 600D”.
        var aparat = string.IsNullOrEmpty(producent) || string.IsNullOrEmpty(model)
                     || model.StartsWith(producent.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
            ? model ?? producent
            : $"{producent} {model}";
        if (!string.IsNullOrEmpty(aparat)) pola.Add(new("APARAT / TELEFON", aparat));
        if (Wartosc("System.Photo.FocalLength") is double ogniskowa)
            pola.Add(new("OGNISKOWA", $"{ogniskowa.ToString("0.#", ElementGalerii.Polski)} mm"));
        if (Wartosc("System.Photo.FNumber") is double przyslona)
            pola.Add(new("PRZYSŁONA", $"f/{przyslona.ToString("0.#", CultureInfo.InvariantCulture)}"));
        if (Wartosc("System.Photo.ExposureTime") is double czas && czas > 0)
            pola.Add(new("CZAS", czas < 1 ? $"1/{Math.Round(1 / czas)} s" : $"{czas.ToString("0.#", ElementGalerii.Polski)} s"));
        if (Wartosc("System.Photo.ISOSpeed") is { } iso)
            pola.Add(new("ISO", $"ISO {iso}"));
        return pola;
    }

    private void Obroc_Click(object sender, RoutedEventArgs e)
    {
        _obrot = (_obrot + 1) % 4;
        _ = WyswietlZdjecie(_widoczne[_indeksPodgladu], ++_numerPodgladu);
    }

    private void Ulubione_Click(object sender, RoutedEventArgs e)
    {
        var element = _widoczne[_indeksPodgladu];
        PrzelaczUlubione(element);
        _ulubioneZmienione = true;
        UstawSerce(element);
    }

    // Serce na kafelku; w widoku Ulubionych odznaczony plik od razu znika z listy.
    private void SerceKafelka_Click(object sender, RoutedEventArgs e)
    {
        PrzelaczUlubione((ElementGalerii)((FrameworkElement)sender).DataContext);
        if (_widokUlubionych) OdswiezWidok();
    }

    private void Kafelek_PointerEntered(object sender, PointerRoutedEventArgs e) => UstawNajechanie(sender, true);
    private void Kafelek_PointerExited(object sender, PointerRoutedEventArgs e) => UstawNajechanie(sender, false);

    private static void UstawNajechanie(object kafelek, bool najechany)
    {
        if (((FrameworkElement)kafelek).DataContext is ElementGalerii element) element.Najechany = najechany;
    }

    private void PrzelaczUlubione(ElementGalerii element)
    {
        if (!_ustawienia.Ulubione.Remove(element.Sciezka)) _ustawienia.Ulubione.Add(element.Sciezka);
        _ustawienia.Zapisz();
        element.Ulubiony = _ustawienia.Ulubione.Contains(element.Sciezka);
        LicznikUlubionych.Text = _wszystkie.Count(el => _ustawienia.Ulubione.Contains(el.Sciezka)).ToString();
    }

    private void UstawSerce(ElementGalerii element)
    {
        var ulubiony = _ustawienia.Ulubione.Contains(element.Sciezka);
        IkonaUlubione.Glyph = ulubiony ? "" : "";
        if (ulubiony) IkonaUlubione.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        else IkonaUlubione.ClearValue(FontIcon.ForegroundProperty);
        ToolTipService.SetToolTip(PrzyciskUlubione, ulubiony ? "Usuń z ulubionych" : "Dodaj do ulubionych");
    }

    private async void Usun_Click(object sender, RoutedEventArgs e)
    {
        var element = _widoczne[_indeksPodgladu];
        var potwierdzenie = NoweOkno("Przenieść do Kosza?",
            $"Plik „{element.Nazwa}” zostanie przeniesiony do Kosza Windows. Możesz go stamtąd przywrócić.");
        potwierdzenie.PrimaryButtonText = "Przenieś do Kosza";
        if (await potwierdzenie.ShowAsync() != ContentDialogResult.Primary) return;

        var indeks = _indeksPodgladu;
        WyczyscPodglad(); // zwalnia plik (film trzyma go otwartym)
        try
        {
            FileSystem.DeleteFile(element.Sciezka, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception)
        {
            // Windows pokazał już własny komunikat błędu — wracamy do podglądu pliku.
            await PokazWPodgladzie(indeks);
            return;
        }

        _wszystkie.Remove(element);
        if (_ustawienia.Ulubione.Remove(element.Sciezka)) _ustawienia.Zapisz();
        OdswiezWidok();
        if (_widoczne.Count == 0) Zamknij_Click(sender, e);
        else await PokazWPodgladzie(Math.Min(indeks, _widoczne.Count - 1));
    }

    private void Drukuj_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Systemowe okno „Drukowanie obrazów”.
            Process.Start(new ProcessStartInfo(_widoczne[_indeksPodgladu].Sciezka) { UseShellExecute = true, Verb = "print" });
        }
        catch (Win32Exception)
        {
            _ = NoweOkno("Nie można wydrukować", "Windows nie ma programu do drukowania plików tego typu.").ShowAsync();
        }
    }

    private ContentDialog NoweOkno(string tytul, string tresc) => new()
    {
        XamlRoot = Content.XamlRoot,
        RequestedTheme = Korzen.RequestedTheme,
        Title = tytul,
        Content = tresc,
        CloseButtonText = "Anuluj",
        DefaultButton = ContentDialogButton.Close,
    };

    // Zoom: ScrollViewer (także Ctrl + kółko) zsynchronizowany z suwakiem 100–400%.
    private void PrzewijanieZdjecia_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RamkaZdjecia.Width = e.NewSize.Width;
        RamkaZdjecia.Height = e.NewSize.Height;
    }

    private void PrzewijanieZdjecia_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate) SuwakPowiekszenia.Value = Math.Round(PrzewijanieZdjecia.ZoomFactor * 100);
    }

    private void SuwakPowiekszenia_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        WartoscPowiekszenia.Text = $"{e.NewValue:0}%";
        var sv = PrzewijanieZdjecia;
        if (Math.Abs(sv.ZoomFactor * 100 - e.NewValue) < 1) return;
        // Powiększamy względem środka widoku.
        var z = (float)(e.NewValue / 100);
        var srodekX = (sv.HorizontalOffset + sv.ViewportWidth / 2) / sv.ZoomFactor;
        var srodekY = (sv.VerticalOffset + sv.ViewportHeight / 2) / sv.ZoomFactor;
        sv.ChangeView(srodekX * z - sv.ViewportWidth / 2, srodekY * z - sv.ViewportHeight / 2, z);
    }

    private void Zamknij_Click(object sender, RoutedEventArgs e)
    {
        Podglad.Visibility = Visibility.Collapsed;
        WyczyscPodglad();
        // W widoku Ulubionych odświeżamy listę dopiero po zamknięciu, żeby nie przestawiać numeracji w trakcie przeglądania.
        if (_widokUlubionych && _ulubioneZmienione) OdswiezWidok();
        _ulubioneZmienione = false;
    }

    private void WyczyscPodglad()
    {
        PodgladFilmu.Source = null; // zatrzymuje odtwarzanie
        PodgladFilmu.Visibility = Visibility.Collapsed;
        PodgladZdjecia.Source = null;
        PrzewijanieZdjecia.Visibility = Visibility.Collapsed;
        PasekExif.Visibility = Visibility.Collapsed;
    }
}
