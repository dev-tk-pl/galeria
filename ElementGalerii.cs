using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GaleriaDesktop;

// Jeden plik (zdjęcie albo film) w siatce galerii.
public class ElementGalerii : INotifyPropertyChanged
{
    // Rozszerzenia przeniesione z PWA (zdjecia-pwa/src/App.tsx).
    public static readonly HashSet<string> RozszerzeniaZdjec = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif", ".svg", ".heic", ".heif", ".tif", ".tiff"
    };
    public static readonly HashSet<string> RozszerzeniaFilmow = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".webm", ".ogv", ".ogg", ".3gp", ".3g2", ".avi", ".mkv", ".mpeg", ".mpg", ".wmv", ".mts", ".m2ts", ".ts"
    };

    public static readonly CultureInfo Polski = new("pl-PL");

    public required string Sciezka { get; init; }
    public required string Folder { get; init; }          // podłączony folder, z którego pochodzi plik
    public required string SciezkaWzgledna { get; init; } // względem Folder, z „/” jak w PWA
    public required bool CzyFilm { get; init; }
    public required DateTime Data { get; init; }          // data wykonania (EXIF / data nagrania), gdy brak — modyfikacji
    public required long Rozmiar { get; init; }

    public string Nazwa => Path.GetFileName(Sciezka);
    public string DataTekst => Data.ToString("d MMMM yyyy", Polski);
    public string RozmiarTekst => Rozmiar < 1024 * 1024
        ? $"{Math.Max(1, Math.Round(Rozmiar / 1024.0))} KB"
        : $"{(Rozmiar / (1024.0 * 1024)).ToString("0.0", Polski)} MB";
    public Visibility WidocznoscIkonyFilmu => CzyFilm ? Visibility.Visible : Visibility.Collapsed;

    // Czy kafelek jest teraz na ekranie — odczytywane też z wątku w tle przy ładowaniu miniatur.
    public volatile bool NaEkranie;

    private ImageSource? _miniatura;
    public ImageSource? Miniatura
    {
        get => _miniatura;
        set
        {
            _miniatura = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Miniatura)));
        }
    }

    // Serce na kafelku — ustawiane z listy ulubionych w Ustawieniach.
    private bool _ulubiony;
    public bool Ulubiony
    {
        get => _ulubiony;
        set
        {
            _ulubiony = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ulubiony)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WidocznoscSerca)));
        }
    }

    // Kursor nad kafelkiem — serce widać po najechaniu albo zawsze przy ulubionym (jak w PWA).
    private bool _najechany;
    public bool Najechany
    {
        get => _najechany;
        set
        {
            _najechany = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WidocznoscSerca)));
        }
    }
    public Visibility WidocznoscSerca => Ulubiony || Najechany ? Visibility.Visible : Visibility.Collapsed;

    // Własne pole wyboru na kafelku (wbudowanego w GridView nie da się odsunąć od rogu).
    private bool _trybZaznaczania;
    public bool TrybZaznaczania
    {
        get => _trybZaznaczania;
        set
        {
            _trybZaznaczania = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WidocznoscPolaWyboru)));
        }
    }
    public Visibility WidocznoscPolaWyboru => TrybZaznaczania ? Visibility.Visible : Visibility.Collapsed;

    private bool _zaznaczony;
    public bool Zaznaczony
    {
        get => _zaznaczony;
        set
        {
            _zaznaczony = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WidocznoscZaznaczenia)));
        }
    }
    public Visibility WidocznoscZaznaczenia => Zaznaczony ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}

// Jedno pole w pasku metadanych pod zdjęciem, np. („PRZYSŁONA”, „f/2.8”).
public record PoleExif(string Etykieta, string Wartosc);

// Pozycja listy „Wybrane foldery” w Ustawieniach.
public record FolderZrodlowy(string Sciezka, int LiczbaPlikow)
{
    public string Nazwa => Path.GetFileName(Sciezka.TrimEnd('\\')) is { Length: > 0 } nazwa ? nazwa : Sciezka;
    public string Opis => $"{Sciezka} · plików: {LiczbaPlikow}";
}
