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
    public required DateTime DataModyfikacji { get; init; }
    public required long Rozmiar { get; init; }

    public string Nazwa => Path.GetFileName(Sciezka);
    public string DataTekst => DataModyfikacji.ToString("d MMMM yyyy", Polski);
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
