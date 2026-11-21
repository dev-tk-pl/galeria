using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.Storage.Pickers;
using Windows.Foundation;

namespace GaleriaDesktop;

public record PodgladFiltra(string Nazwa, ImageSource Podglad);

public sealed partial class Edytor : UserControl
{
    // Filtry z PWA (FILTERS): kolejne funkcje CSS jako macierze kolorów, w tej samej kolejności co w CSS.
    private static readonly (string Nazwa, Matrix5x4[] Macierze)[] Filtry =
    [
        ("Oryginalny", []),
        ("Perforacja", [Sepia(.22), Kontrast(1.12), Nasycenie(.88)]),
        ("Złoty", [Sepia(.35), Nasycenie(1.25), Kontrast(1.05)]),
        ("Promienie", [Jasnosc(1.12), Nasycenie(1.12), Sepia(.12)]),
        ("Ciepły kontrast", [Sepia(.18), Nasycenie(1.2), Kontrast(1.22)]),
        ("Spokojny", [Nasycenie(.72), Kontrast(.96), Jasnosc(1.04)]),
        ("Chłodne światło", [Barwa(8), Nasycenie(.9), Jasnosc(1.08)]),
        ("Żywy chłodny", [Barwa(12), Nasycenie(1.45), Kontrast(1.08)]),
        ("Dramatyczny chłodny", [Barwa(16), Nasycenie(.82), Kontrast(1.35)]),
        ("Czarno-biały", [Nasycenie(0)]),
        ("Czarno-białe – chłodne", [Nasycenie(0), Barwa(190), Kontrast(1.08)]),
        ("Czarno-białe – ciepłe", [Nasycenie(0), Sepia(.28)]),
        ("Czarno-biały – duży kontrast", [Nasycenie(0), Kontrast(1.55)]),
        ("Nagrywanie", [Sepia(.24), Kontrast(1.08), Nasycenie(.78)]),
        ("Film", [Kontrast(1.12), Nasycenie(.82), Sepia(.12)]),
        ("Klasyczny", [Sepia(.12), Kontrast(1.08), Nasycenie(.92)]),
    ];
    private const int FiltrAutoUlepszenia = 3; // „Promienie”, jak w PWA
    private const double MaksKat = 45;
    private const double PikseliNaStopien = 15;

    // Proporcje kadru jak CROP_RATIOS w PWA; 0 = Dowolny, -1 = Oryginalny.
    private static readonly (string Nazwa, double Wartosc)[] Proporcje =
    [
        ("Dowolny", 0), ("Oryginalny", -1), ("Kwadrat", 1),
        ("9 : 16", 9.0 / 16), ("16 : 9", 16.0 / 9), ("4 : 5", 4.0 / 5), ("5 : 4", 5.0 / 4),
        ("3 : 4", 3.0 / 4), ("4 : 3", 4.0 / 3), ("2 : 3", 2.0 / 3), ("3 : 2", 3.0 / 2),
        ("5 : 7", 5.0 / 7), ("7 : 5", 7.0 / 5), ("1 : 2", 1.0 / 2), ("2 : 1", 2.0),
    ];
    private static readonly string[] SuwakiSwiatlaNazwy = ["Jasność", "Ekspozycja", "Kontrast", "Rozjaśnienia", "Cienie", "Winieta"];
    private static readonly string[] SuwakiKoloruNazwy = ["Nasycenie", "Ciepło", "Odcień", "Ostrość"];
    // Uchwyty ramki: nazwa → położenie względem ramki (0–1).
    private static readonly (string Nazwa, double X, double Y)[] Uchwyty =
    [
        ("nw", 0, 0), ("n", 0.5, 0), ("ne", 1, 0), ("e", 1, 0.5),
        ("se", 1, 1), ("s", 0.5, 1), ("sw", 0, 1), ("w", 0, 0.5),
    ];
    private const double MinRozmiarKadru = 0.05;

    private readonly Dictionary<string, Slider> _suwaki = [];
    private readonly Dictionary<string, Rectangle> _uchwyty = [];

    private string _sciezka = "";
    private Microsoft.UI.WindowId _oknoId;
    private CanvasBitmap? _podglad;       // zmniejszona kopia do podglądu na żywo; zapis liczy pełną rozdzielczość
    private int _obrot;                   // ćwierćobroty w prawo (0–3)
    private bool _odbicieX, _odbicieY;
    private Rect _kadr = new(0, 0, 1, 1); // względem obróconego obrazu, 0–1
    private double _proporcja;            // szer./wys. w pikselach; 0 = dowolna
    private string _nazwaProporcji = "Dowolny";
    private double _kat;                  // prostowanie w stopniach (−45…45), w prawo dodatnie
    private int _filtr;                   // indeks w Filtry
    private Rect _obszarObrazu;           // gdzie obraz leży na płótnie (do ramki kadrowania)
    private string? _chwyt;               // przeciągany uchwyt albo „srodek”
    private Point _startChwytu;
    private Rect _kadrStart;
    private double? _startLinijki;        // X początku przeciągania linijki; null = nie przeciągana
    private double _katStart;

    // Historia do Cofnij/Ponów: kolejne zatwierdzone stany edycji.
    private record Stan(int Obrot, bool OdbicieX, bool OdbicieY, Rect Kadr, string NazwaProporcji, double Proporcja,
                        double Kat, int Filtr, double[] Korekta);
    private readonly List<Stan> _historia = [];
    private int _pozycjaHistorii;
    private bool _przywracanie;

    public event Action<string>? Zapisano;

    public Edytor()
    {
        InitializeComponent();
        foreach (var nazwa in SuwakiSwiatlaNazwy) SuwakiSwiatla.Children.Add(NowySuwak(nazwa));
        foreach (var nazwa in SuwakiKoloruNazwy) SuwakiKoloru.Children.Add(NowySuwak(nazwa));
        foreach (var (nazwa, wartosc) in Proporcje)
        {
            var pozycja = new MenuFlyoutItem { Text = nazwa };
            pozycja.Click += (_, _) =>
            {
                UstawProporcje(nazwa, wartosc);
                Zatwierdz();
            };
            MenuProporcji.Items.Add(pozycja);
        }
        ActualThemeChanged += (_, _) => Linijka.Invalidate();
        foreach (var (nazwa, _, _) in Uchwyty)
        {
            var uchwyt = new Rectangle { Width = 14, Height = 14, Fill = new SolidColorBrush(Colors.White), RadiusX = 3, RadiusY = 3 };
            _uchwyty[nazwa] = uchwyt;
            NakladkaKadru.Children.Add(uchwyt);
        }
    }

    private FrameworkElement NowySuwak(string nazwa)
    {
        var wartosc = new TextBlock { Text = "0", HorizontalAlignment = HorizontalAlignment.Right };
        var suwak = new Slider { Minimum = -100, Maximum = 100, StepFrequency = 1 };
        suwak.ValueChanged += (_, e) =>
        {
            wartosc.Text = $"{e.NewValue:0}";
            Plotno.Invalidate();
        };
        // Do historii trafia wartość po puszczeniu suwaka, a nie każdy krok przeciągania.
        suwak.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => Zatwierdz()), true);
        suwak.AddHandler(KeyUpEvent, new KeyEventHandler((_, _) => Zatwierdz()), true);
        _suwaki[nazwa] = suwak;
        var naglowek = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        naglowek.Children.Add(new TextBlock { Text = nazwa });
        naglowek.Children.Add(wartosc);
        var panel = new StackPanel();
        panel.Children.Add(naglowek);
        panel.Children.Add(suwak);
        return panel;
    }

    private double Wartosc(string nazwa) => _suwaki[nazwa].Value;

    public async Task Otworz(string sciezka, Microsoft.UI.WindowId oknoId)
    {
        _sciezka = sciezka;
        _oknoId = oknoId;
        NazwaPliku.Text = System.IO.Path.GetFileName(sciezka);
        foreach (var suwak in _suwaki.Values) suwak.Value = 0;
        _obrot = 0;
        _odbicieX = _odbicieY = false;
        _kadr = new Rect(0, 0, 1, 1);
        UstawProporcje("Dowolny", 0);
        UstawKat(0);
        _filtr = 0;
        _historia.Clear();
        _historia.Add(BiezacyStan());
        _pozycjaHistorii = 0;
        OdswiezHistorie();
        UstawZakladke(ZakladkaPrzytnij);
        ListaFiltrow.ItemsSource = null;
        _podglad?.Dispose();
        _podglad = null;
        Visibility = Visibility.Visible;

        using var bitmapa = await Task.Run(() => Obrazy.Dekoduj(sciezka, maksBok: 2048));
        _podglad = CanvasBitmap.CreateFromSoftwareBitmap(CanvasDevice.GetSharedDevice(), bitmapa);
        ListaFiltrow.ItemsSource = UtworzPodgladyFiltrow(_podglad);
        ListaFiltrow.SelectedIndex = _filtr;
        Plotno.Invalidate();
    }

    // Miniatury filtrów: zdjęcie zmniejszone do 160 px z każdym filtrem.
    private static List<PodgladFiltra> UtworzPodgladyFiltrow(CanvasBitmap zdjecie)
    {
        var skala = (float)(160 / Math.Max(zdjecie.Size.Width, zdjecie.Size.Height));
        var male = new ScaleEffect { Source = zdjecie, Scale = new Vector2(skala) };
        var (w, h) = ((float)zdjecie.Size.Width * skala, (float)zdjecie.Size.Height * skala);
        return [.. Filtry.Select(filtr =>
        {
            var podglad = new CanvasImageSource(CanvasDevice.GetSharedDevice(), w, h, 96);
            using (var ds = podglad.CreateDrawingSession(Colors.Transparent))
                ds.DrawImage(ZastosujFiltr(male, filtr.Macierze));
            return new PodgladFiltra(filtr.Nazwa, podglad);
        })];
    }

    public void Zamknij()
    {
        Visibility = Visibility.Collapsed;
        _podglad?.Dispose();
        _podglad = null;
    }

    private void Anuluj_Click(object sender, RoutedEventArgs e) => Zamknij();

    private void Zakladka_Click(object sender, RoutedEventArgs e) => UstawZakladke((ToggleButton)sender);

    private bool TrybKadrowania => ZakladkaPrzytnij.IsChecked == true;

    private void UstawZakladke(ToggleButton zakladka)
    {
        ZakladkaPrzytnij.IsChecked = zakladka == ZakladkaPrzytnij;
        ZakladkaKorekta.IsChecked = zakladka == ZakladkaKorekta;
        ZakladkaFiltry.IsChecked = zakladka == ZakladkaFiltry;
        NakladkaKadru.Visibility = PasekKadrowania.Visibility = Widocznosc(zakladka == ZakladkaPrzytnij);
        PanelKorekty.Visibility = Widocznosc(zakladka == ZakladkaKorekta);
        PanelFiltrow.Visibility = Widocznosc(zakladka == ZakladkaFiltry);
        Plotno.Invalidate();
    }

    private static Visibility Widocznosc(bool widoczny) => widoczny ? Visibility.Visible : Visibility.Collapsed;

    // ---------- Cofnij / Ponów ----------

    private Stan BiezacyStan() => new(_obrot, _odbicieX, _odbicieY, _kadr, _nazwaProporcji, _proporcja, _kat, _filtr,
                                      [.. _suwaki.Values.Select(s => s.Value)]);

    // Dopisuje bieżący stan do historii, jeśli różni się od ostatnio zatwierdzonego.
    private void Zatwierdz()
    {
        if (_przywracanie || _historia.Count == 0) return;
        var stan = BiezacyStan();
        var poprzedni = _historia[_pozycjaHistorii];
        if (stan with { Korekta = poprzedni.Korekta } == poprzedni && stan.Korekta.SequenceEqual(poprzedni.Korekta)) return;
        _historia.RemoveRange(_pozycjaHistorii + 1, _historia.Count - _pozycjaHistorii - 1);
        _historia.Add(stan);
        _pozycjaHistorii++;
        OdswiezHistorie();
    }

    private void Cofnij_Click(object sender, RoutedEventArgs e)
    {
        if (_pozycjaHistorii > 0) PrzywrocStan(_pozycjaHistorii - 1);
    }

    private void Ponow_Click(object sender, RoutedEventArgs e)
    {
        if (_pozycjaHistorii < _historia.Count - 1) PrzywrocStan(_pozycjaHistorii + 1);
    }

    private void PrzywrocStan(int pozycja)
    {
        var s = _historia[pozycja];
        _pozycjaHistorii = pozycja;
        _przywracanie = true;
        (_obrot, _odbicieX, _odbicieY, _kadr) = (s.Obrot, s.OdbicieX, s.OdbicieY, s.Kadr);
        (_nazwaProporcji, _proporcja) = (s.NazwaProporcji, s.Proporcja);
        WyborProporcji.Content = $"Proporcje: {s.NazwaProporcji}";
        UstawKat(s.Kat);
        var i = 0;
        foreach (var suwak in _suwaki.Values) suwak.Value = s.Korekta[i++];
        _filtr = s.Filtr;
        ListaFiltrow.SelectedIndex = s.Filtr;
        _przywracanie = false;
        OdswiezHistorie();
        Plotno.Invalidate();
    }

    private void OdswiezHistorie()
    {
        PrzyciskCofnij.IsEnabled = _pozycjaHistorii > 0;
        PrzyciskPonow.IsEnabled = _pozycjaHistorii < _historia.Count - 1;
    }

    // ---------- Filtry ----------

    private void ListaFiltrow_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaFiltrow.SelectedIndex < 0 || _przywracanie) return;
        _filtr = ListaFiltrow.SelectedIndex;
        Plotno.Invalidate();
        Zatwierdz();
    }

    // Jak w PWA: filtr „Promienie” + lekka korekta światła i koloru.
    private void AutoUlepszenie_Click(object sender, RoutedEventArgs e)
    {
        foreach (var suwak in _suwaki.Values) suwak.Value = 0;
        _suwaki["Jasność"].Value = 8;
        _suwaki["Kontrast"].Value = 8;
        _suwaki["Nasycenie"].Value = 12;
        ListaFiltrow.SelectedIndex = FiltrAutoUlepszenia;
        Zatwierdz();
    }

    private static ICanvasImage ZastosujFiltr(ICanvasImage obraz, Matrix5x4[] macierze)
        => macierze.Aggregate(obraz, (o, m) => new ColorMatrixEffect { Source = o, ColorMatrix = m, ClampOutput = true });

    // Macierze funkcji CSS filter wg specyfikacji Filter Effects — dzięki temu filtry wyglądają jak w PWA.
    private static Matrix5x4 Macierz(double[] m, double przesuniecie = 0) => new()
    {
        M11 = (float)m[0], M21 = (float)m[1], M31 = (float)m[2],
        M12 = (float)m[3], M22 = (float)m[4], M32 = (float)m[5],
        M13 = (float)m[6], M23 = (float)m[7], M33 = (float)m[8],
        M44 = 1,
        M51 = (float)przesuniecie, M52 = (float)przesuniecie, M53 = (float)przesuniecie,
    };

    private static Matrix5x4 Jasnosc(double b) => Macierz([b, 0, 0, 0, b, 0, 0, 0, b]);

    private static Matrix5x4 Kontrast(double c) => Macierz([c, 0, 0, 0, c, 0, 0, 0, c], 0.5 - 0.5 * c);

    private static Matrix5x4 Nasycenie(double s) => Macierz([
        0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s,
        0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s,
        0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s]);

    private static Matrix5x4 Sepia(double a)
    {
        var p = 1 - a;
        return Macierz([
            0.393 + 0.607 * p, 0.769 - 0.769 * p, 0.189 - 0.189 * p,
            0.349 - 0.349 * p, 0.686 + 0.314 * p, 0.168 - 0.168 * p,
            0.272 - 0.272 * p, 0.534 - 0.534 * p, 0.131 + 0.869 * p]);
    }

    private static Matrix5x4 Barwa(double stopnie)
    {
        var (s, c) = Math.SinCos(stopnie * Math.PI / 180);
        return Macierz([
            0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928,
            0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283,
            0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072]);
    }

    // ---------- Prostowanie ----------

    private void UstawKat(double kat)
    {
        _kat = Math.Clamp(Math.Round(kat, 1), -MaksKat, MaksKat);
        KatTekst.Text = $"{_kat:0.#} °";
        Linijka.Invalidate();
        Plotno.Invalidate();
    }

    // Kropka co 1°, kreska co 5°; linijka gaśnie przy krawędziach, znacznik zostaje na środku.
    private void Linijka_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var (w, h) = ((float)sender.ActualWidth, (float)sender.ActualHeight);
        var kolor = ActualTheme == ElementTheme.Light ? Colors.Black : Colors.White;
        for (var stopien = -MaksKat; stopien <= MaksKat; stopien++)
        {
            var x = w / 2 + (float)((stopien - _kat) * PikseliNaStopien);
            var widocznosc = Math.Clamp(Math.Min(x, w - x) / 60f, 0, 1);
            if (widocznosc <= 0) continue;
            var kreska = stopien % 5 == 0;
            var c = kolor with { A = (byte)(255 * widocznosc * (kreska ? 0.7 : 0.4)) };
            if (kreska) ds.FillRoundedRectangle(x - 2, h / 2 - 8, 4, 16, 2, 2, c);
            else ds.FillCircle(x, h / 2, 2, c);
        }
        ds.FillRoundedRectangle(w / 2 - 5, h / 2 - 14, 10, 28, 5, 5, kolor);
    }

    private void Linijka_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var obszar = (UIElement)sender;
        _startLinijki = e.GetCurrentPoint(obszar).Position.X;
        _katStart = _kat;
        obszar.CapturePointer(e.Pointer);
        UstawNakladke();
    }

    private void Linijka_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_startLinijki is not double start) return;
        // Przeciągnięcie linijki w lewo przesuwa pod znacznik większe kąty.
        UstawKat(_katStart - (e.GetCurrentPoint((UIElement)sender).Position.X - start) / PikseliNaStopien);
    }

    private void Linijka_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_startLinijki is null) return;
        _startLinijki = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        UstawNakladke();
        Zatwierdz();
    }

    // ---------- Łańcuch efektów Win2D (wspólny dla podglądu i zapisu) ----------

    // Zwraca obraz po obrocie/odbiciu i korekcie, przycięty do kadru (zKadrem) albo cały; granice w DIP źródła.
    private ICanvasImage ZbudujObraz(CanvasBitmap zrodlo, bool zKadrem, out Rect granice)
    {
        var (w, h) = ((float)zrodlo.Size.Width, (float)zrodlo.Size.Height);
        var (ow, oh) = _obrot % 2 == 1 ? (h, w) : (w, h);
        // Prostowanie: obraz obrócony o kąt i powiększony tak, żeby pokrył całą ramkę ow×oh — bez pustych rogów.
        var kat = (float)(_kat * Math.PI / 180);
        var (sin, cos) = (MathF.Abs(MathF.Sin(kat)), MathF.Cos(kat));
        var powiekszenie = MathF.Max(cos + oh / ow * sin, cos + ow / oh * sin);
        var macierz = Matrix3x2.CreateTranslation(-w / 2, -h / 2)
                      * Matrix3x2.CreateRotation(_obrot * MathF.PI / 2)
                      * Matrix3x2.CreateScale(_odbicieX ? -1 : 1, _odbicieY ? -1 : 1)
                      * Matrix3x2.CreateRotation(kat)
                      * Matrix3x2.CreateScale(powiekszenie)
                      * Matrix3x2.CreateTranslation(ow / 2, oh / 2);
        ICanvasImage obraz = new Transform2DEffect { Source = zrodlo, TransformMatrix = macierz, BorderMode = EffectBorderMode.Hard };
        obraz = ZastosujFiltr(obraz, Filtry[_filtr].Macierze);

        var jasnosc = (float)(Wartosc("Jasność") / 250);
        obraz = new ExposureEffect { Source = obraz, Exposure = (float)(Wartosc("Ekspozycja") / 50) };
        obraz = new ColorMatrixEffect
        {
            Source = obraz,
            ColorMatrix = new Matrix5x4 { M11 = 1, M22 = 1, M33 = 1, M44 = 1, M51 = jasnosc, M52 = jasnosc, M53 = jasnosc },
        };
        obraz = new ContrastEffect { Source = obraz, Contrast = (float)(Wartosc("Kontrast") / 100) };
        obraz = new HighlightsAndShadowsEffect
        {
            Source = obraz,
            Highlights = (float)(Wartosc("Rozjaśnienia") / 100),
            Shadows = (float)(Wartosc("Cienie") / 100),
        };
        obraz = new SaturationEffect { Source = obraz, Saturation = (float)(1 + Wartosc("Nasycenie") / 100) };
        obraz = new TemperatureAndTintEffect
        {
            Source = obraz,
            Temperature = (float)(Wartosc("Ciepło") / 100),
            Tint = (float)(Wartosc("Odcień") / 100),
        };
        var ostrosc = Wartosc("Ostrość");
        if (ostrosc > 0) obraz = new SharpenEffect { Source = obraz, Amount = (float)(ostrosc / 10) };
        else if (ostrosc < 0) obraz = new GaussianBlurEffect { Source = obraz, BlurAmount = (float)(-ostrosc / 25), BorderMode = EffectBorderMode.Hard };

        var kadr = zKadrem ? _kadr : new Rect(0, 0, 1, 1);
        granice = new Rect(kadr.X * ow, kadr.Y * oh, kadr.Width * ow, kadr.Height * oh);
        obraz = new CropEffect { Source = obraz, SourceRectangle = granice };

        // Winieta po kadrowaniu — wyśrodkowana na tym, co zostanie zapisane.
        var winieta = Wartosc("Winieta");
        if (winieta != 0)
            obraz = new VignetteEffect { Source = obraz, Amount = (float)(Math.Abs(winieta) / 100), Color = winieta > 0 ? Colors.Black : Colors.White };
        return obraz;
    }

    private void Plotno_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_podglad is null) return;
        // Przytnij: cały obraz z ramką; Korekta: już przycięty wynik.
        var obraz = ZbudujObraz(_podglad, zKadrem: !TrybKadrowania, out var granice);
        var skala = Math.Min(sender.ActualWidth / granice.Width, sender.ActualHeight / granice.Height);
        var x = (sender.ActualWidth - granice.Width * skala) / 2;
        var y = (sender.ActualHeight - granice.Height * skala) / 2;
        args.DrawingSession.Transform = Matrix3x2.CreateTranslation(-(float)granice.X, -(float)granice.Y)
                                        * Matrix3x2.CreateScale((float)skala)
                                        * Matrix3x2.CreateTranslation((float)x, (float)y);
        args.DrawingSession.DrawImage(obraz);

        _obszarObrazu = new Rect(x, y, granice.Width * skala, granice.Height * skala);
        if (TrybKadrowania) UstawNakladke();
    }

    // ---------- Kadrowanie ----------

    private Size RozmiarObroconego => _podglad is null ? new Size(1, 1)
        : _obrot % 2 == 1 ? new Size(_podglad.Size.Height, _podglad.Size.Width) : _podglad.Size;

    private Rect KadrNaEkranie => new(
        _obszarObrazu.X + _kadr.X * _obszarObrazu.Width, _obszarObrazu.Y + _kadr.Y * _obszarObrazu.Height,
        _kadr.Width * _obszarObrazu.Width, _kadr.Height * _obszarObrazu.Height);

    private void UstawNakladke()
    {
        var r = KadrNaEkranie;
        Canvas.SetLeft(RamkaKadru, r.X);
        Canvas.SetTop(RamkaKadru, r.Y);
        RamkaKadru.Width = r.Width;
        RamkaKadru.Height = r.Height;
        var przyciemnienie = new GeometryGroup { FillRule = FillRule.EvenOdd };
        przyciemnienie.Children.Add(new RectangleGeometry { Rect = _obszarObrazu });
        przyciemnienie.Children.Add(new RectangleGeometry { Rect = r });
        Przyciemnienie.Data = przyciemnienie;
        foreach (var (nazwa, ux, uy) in Uchwyty)
        {
            Canvas.SetLeft(_uchwyty[nazwa], r.X + ux * r.Width - 7);
            Canvas.SetTop(_uchwyty[nazwa], r.Y + uy * r.Height - 7);
        }

        // Siatka 9×9 w ramce podczas prostowania (jak w PWA) — ułatwia wyrównanie horyzontu.
        GeometryGroup? siatka = null;
        if (_startLinijki is not null)
        {
            siatka = new GeometryGroup();
            for (var i = 1; i < 9; i++)
            {
                var (x, y) = (r.X + r.Width * i / 9, r.Y + r.Height * i / 9);
                siatka.Children.Add(new LineGeometry { StartPoint = new Point(x, r.Top), EndPoint = new Point(x, r.Bottom) });
                siatka.Children.Add(new LineGeometry { StartPoint = new Point(r.Left, y), EndPoint = new Point(r.Right, y) });
            }
        }
        SiatkaProstowania.Data = siatka;
    }

    private void Nakladka_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(NakladkaKadru).Position;
        var r = KadrNaEkranie;
        _chwyt = Uchwyty
            .Where(u => Math.Abs(r.X + u.X * r.Width - p.X) < 14 && Math.Abs(r.Y + u.Y * r.Height - p.Y) < 14)
            .Select(u => u.Nazwa)
            .FirstOrDefault() ?? (r.Contains(p) ? "srodek" : null);
        if (_chwyt is null) return;
        _startChwytu = p;
        _kadrStart = _kadr;
        NakladkaKadru.CapturePointer(e.Pointer);
    }

    private void Nakladka_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_chwyt is null || _obszarObrazu.Width <= 0) return;
        var p = e.GetCurrentPoint(NakladkaKadru).Position;
        var dx = (p.X - _startChwytu.X) / _obszarObrazu.Width;
        var dy = (p.Y - _startChwytu.Y) / _obszarObrazu.Height;
        _kadr = PrzesunKadr(_kadrStart, _chwyt, dx, dy);
        UstawNakladke();
    }

    private void Nakladka_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _chwyt = null;
        NakladkaKadru.ReleasePointerCapture(e.Pointer);
        Zatwierdz();
    }

    private Rect PrzesunKadr(Rect k, string chwyt, double dx, double dy)
    {
        if (chwyt == "srodek")
            return new Rect(Math.Clamp(k.X + dx, 0, 1 - k.Width), Math.Clamp(k.Y + dy, 0, 1 - k.Height), k.Width, k.Height);

        double l = k.Left, t = k.Top, r = k.Right, b = k.Bottom;
        if (chwyt.Contains('w')) l = Math.Clamp(l + dx, 0, r - MinRozmiarKadru);
        if (chwyt.Contains('e')) r = Math.Clamp(r + dx, l + MinRozmiarKadru, 1);
        if (chwyt.Contains('n')) t = Math.Clamp(t + dy, 0, b - MinRozmiarKadru);
        if (chwyt.Contains('s')) b = Math.Clamp(b + dy, t + MinRozmiarKadru, 1);
        var nowy = new Rect(l, t, r - l, b - t);
        return _proporcja > 0 ? ZachowajProporcje(nowy, chwyt) ?? k : nowy;
    }

    // Dopasowuje kadr do wybranych proporcji, trzymając przeciwległy róg/bok; null = nie mieści się w obrazie.
    private Rect? ZachowajProporcje(Rect r, string chwyt)
    {
        var o = RozmiarObroconego;
        var q = _proporcja * o.Height / o.Width; // proporcja w jednostkach 0–1
        double w = r.Width, h = r.Height;
        if (chwyt is "n" or "s") w = h * q; else h = w / q;
        var x = chwyt.Contains('w') ? r.Right - w : chwyt.Contains('e') ? r.Left : r.X + (r.Width - w) / 2;
        var y = chwyt.Contains('n') ? r.Bottom - h : chwyt.Contains('s') ? r.Top : r.Y + (r.Height - h) / 2;
        const double tolerancja = 1e-6;
        return x >= -tolerancja && y >= -tolerancja && x + w <= 1 + tolerancja && y + h <= 1 + tolerancja
            ? new Rect(Math.Max(0, x), Math.Max(0, y), Math.Min(w, 1), Math.Min(h, 1))
            : null;
    }

    private void UstawProporcje(string nazwa, double wartosc)
    {
        WyborProporcji.Content = $"Proporcje: {nazwa}";
        _nazwaProporcji = nazwa;
        var o = RozmiarObroconego;
        _proporcja = wartosc < 0 ? o.Width / o.Height : wartosc;
        if (_proporcja > 0)
        {
            // Największy wyśrodkowany kadr o tych proporcjach.
            var q = _proporcja * o.Height / o.Width;
            double w = 1, h = 1 / q;
            if (h > 1) { h = 1; w = q; }
            _kadr = new Rect((1 - w) / 2, (1 - h) / 2, w, h);
        }
        Plotno.Invalidate();
    }

    private void ObrocWPrawo_Click(object sender, RoutedEventArgs e) => Obroc(wPrawo: true);
    private void ObrocWLewo_Click(object sender, RoutedEventArgs e) => Obroc(wPrawo: false);

    private void Obroc(bool wPrawo)
    {
        // Przy pojedynczym odbiciu obrót w macierzy działa odwrotnie — kompensujemy, żeby na ekranie było zgodnie z przyciskiem.
        var krok = (wPrawo ? 1 : -1) * (_odbicieX ^ _odbicieY ? -1 : 1);
        _obrot = (_obrot + krok + 4) % 4;
        var k = _kadr;
        _kadr = wPrawo ? new Rect(1 - k.Y - k.Height, k.X, k.Height, k.Width) : new Rect(k.Y, 1 - k.X - k.Width, k.Height, k.Width);
        if (_proporcja > 0) _proporcja = 1 / _proporcja;
        Plotno.Invalidate();
        Zatwierdz();
    }

    // Odbicie zmienia kierunek obrotu, więc kąt prostowania zmienia znak — wyprostowany horyzont zostaje prosty.
    private void OdbijPoziomo_Click(object sender, RoutedEventArgs e)
    {
        _odbicieX = !_odbicieX;
        _kadr = new Rect(1 - _kadr.X - _kadr.Width, _kadr.Y, _kadr.Width, _kadr.Height);
        UstawKat(-_kat);
        Zatwierdz();
    }

    private void OdbijPionowo_Click(object sender, RoutedEventArgs e)
    {
        _odbicieY = !_odbicieY;
        _kadr = new Rect(_kadr.X, 1 - _kadr.Y - _kadr.Height, _kadr.Width, _kadr.Height);
        UstawKat(-_kat);
        Zatwierdz();
    }

    private void ResetujKadr_Click(object sender, RoutedEventArgs e)
    {
        _obrot = 0;
        _odbicieX = _odbicieY = false;
        _kadr = new Rect(0, 0, 1, 1);
        UstawKat(0);
        UstawProporcje("Dowolny", 0);
        Zatwierdz();
    }

    private void ResetujKorekte_Click(object sender, RoutedEventArgs e)
    {
        foreach (var suwak in _suwaki.Values) suwak.Value = 0;
        Zatwierdz();
    }

    // ---------- Zapis kopii ----------

    private void ZapiszJpg_Click(object sender, RoutedEventArgs e) => _ = Zapisz(jpg: true);
    private void ZapiszPng_Click(object sender, RoutedEventArgs e) => _ = Zapisz(jpg: false);

    private async Task Zapisz(bool jpg)
    {
        var rozszerzenie = jpg ? ".jpg" : ".png";
        var okno = new FileSavePicker(_oknoId)
        {
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_sciezka) + "-edytowane",
            SuggestedFolder = System.IO.Path.GetDirectoryName(_sciezka),
            DefaultFileExtension = rozszerzenie,
        };
        okno.FileTypeChoices.Add(jpg ? "Obraz JPG" : "Obraz PNG", [rozszerzenie]);
        var wynik = await okno.PickSaveFileAsync();
        if (wynik is null) return;

        try
        {
            OpcjeZapisu.IsEnabled = false;
            var urzadzenie = CanvasDevice.GetSharedDevice();
            using var bitmapa = await Task.Run(() => Obrazy.Dekoduj(_sciezka)); // pełna rozdzielczość
            using var pelny = CanvasBitmap.CreateFromSoftwareBitmap(urzadzenie, bitmapa);
            var obraz = ZbudujObraz(pelny, zKadrem: true, out var granice);
            // DPI jak w źródle, żeby liczba pikseli wyniku odpowiadała oryginałowi.
            using var cel = new CanvasRenderTarget(urzadzenie, (float)granice.Width, (float)granice.Height, pelny.Dpi);
            using (var ds = cel.CreateDrawingSession())
            {
                ds.Clear(jpg ? Colors.Black : Colors.Transparent);
                ds.DrawImage(obraz, -(float)granice.X, -(float)granice.Y);
            }
            await cel.SaveAsync(wynik.Path, jpg ? CanvasBitmapFileFormat.Jpeg : CanvasBitmapFileFormat.Png, 0.92f);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Nie udało się zapisać kopii",
                Content = ex.Message,
                CloseButtonText = "OK",
            }.ShowAsync();
            return;
        }
        finally
        {
            OpcjeZapisu.IsEnabled = true;
        }

        Zamknij();
        Zapisano?.Invoke(wynik.Path);
    }
}
