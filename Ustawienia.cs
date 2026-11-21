using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaleriaDesktop;

// Ustawienia w %LOCALAPPDATA%\Galeria\ustawienia.json
// (aplikacja bez MSIX nie ma dostępu do ApplicationData.Current).
public class Ustawienia
{
    public List<string> Foldery { get; set; } = [];
    public string Motyw { get; set; } = "Systemowy"; // Systemowy / Jasny / Ciemny
    public int Kolumny { get; set; }                 // 0 = Domyślna (zależnie od szerokości okna)
    public HashSet<string> Ulubione { get; set; } = []; // pełne ścieżki plików

    // Stary format (jeden folder) — przy wczytaniu przenoszony do Foldery.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OstatniFolder { get; set; }

    private static readonly string SciezkaPliku = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Galeria", "ustawienia.json");

    public static Ustawienia Wczytaj()
    {
        Ustawienia ustawienia;
        try
        {
            ustawienia = JsonSerializer.Deserialize<Ustawienia>(File.ReadAllText(SciezkaPliku)) ?? new();
        }
        catch
        {
            return new(); // brak pliku (pierwsze uruchomienie) albo uszkodzony — ustawienia domyślne
        }

        if (ustawienia.OstatniFolder is not null)
        {
            if (!ustawienia.Foldery.Contains(ustawienia.OstatniFolder, StringComparer.OrdinalIgnoreCase))
                ustawienia.Foldery.Add(ustawienia.OstatniFolder);
            ustawienia.OstatniFolder = null;
        }
        return ustawienia;
    }

    public void Zapisz()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SciezkaPliku)!);
        File.WriteAllText(SciezkaPliku, JsonSerializer.Serialize(this));
    }
}
