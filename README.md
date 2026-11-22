# Galeria

Lokalna galeria zdjęć i krótkich filmów z aparatu/telefonu dla Windows 11.
Wskazujesz folder, a aplikacja pokazuje w jednej siatce wszystkie zdjęcia i filmy z niego i z podfolderów.
Pliki nigdy nie opuszczają komputera: nie ma żadnego serwera ani wysyłania danych.

Filmy odtwarza systemowy odtwarzacz Windows (kodeki Media Foundation), bez konwersji.
Dzięki temu działają też stare formaty, których przeglądarki nie obsługują, np. AVI z aparatów
(MJPEG + PCM) i 3GP z telefonów (MPEG-4 + AMR-NB).

> Status: prototyp. Działa wybór folderu, siatka miniatur, zapamiętanie folderu i podgląd zdjęć/filmów.
> Aktualizacje (Velopack + GitHub Releases): przy starcie okienko „Sprawdzanie dostępności aktualizacji…” —
> jeśli jest nowa wersja, pobiera ją, instaluje i uruchamia; jeśli nie, otwiera Galerię. Po wyłączeniu
> w Ustawieniach okienka nie ma, a o nowej wersji informuje tylko napis na górnym pasku.

## Wymagania

- Windows 11, x64
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)

Windows App SDK jest dołączany do aplikacji (`WindowsAppSDKSelfContained`), więc nie trzeba go osobno instalować.
Pakiety NuGet pobierają się przy pierwszym buildzie. Plik `nuget.config` w projekcie dodaje źródło nuget.org,
więc build działa także na maszynie bez skonfigurowanych źródeł NuGet.

## Uruchomienie

W katalogu projektu (PowerShell):

```powershell
dotnet build              # kompilacja (Debug)
dotnet run                # kompilacja + uruchomienie
dotnet build -c Release   # kompilacja wersji Release
```

Gotowy plik wykonywalny po buildzie:

```
bin\Debug\net10.0-windows10.0.19041.0\win-x64\Galeria.exe
bin\Release\net10.0-windows10.0.19041.0\win-x64\Galeria.exe
```

Ten exe nie działa samodzielnie: potrzebuje plików z tego samego folderu i zainstalowanego .NET 10.

### Jeden plik exe na inny komputer

```powershell
dotnet publish -p:PublishProfile=JedenPlik
```

Wynik: `bin\publish\Galeria.exe` (~230 MB). To jeden plik z .NET i Windows App SDK w środku. Wystarczy go
skopiować na inny komputer z Windows 11 x64 i uruchomić, bez instalowania czegokolwiek (`Galeria.pdb` obok
nie jest potrzebny). Ustawienia są w profilu `Properties\PublishProfiles\JedenPlik.pubxml`.

- Przy pierwszym uruchomieniu exe rozpakowuje się do `%TEMP%\.net\Galeria\` (~220 MB), więc pierwszy start
  trwa dłużej. Kolejne korzystają z rozpakowanych plików.
- Publikacja kompiluje do osobnego `bin\JedenPlik\`, więc Galeria uruchomiona z `bin\Release` jej nie blokuje.

Testów automatycznych na razie nie ma. Jedyną automatyczną weryfikacją jest `dotnet build`.

## Wydanie nowej wersji

Aktualizacje idą przez [Velopack](https://velopack.io) i GitHub Releases. Sam `git push` niczego nie
aktualizuje — zainstalowana Galeria widzi nową wersję dopiero po wysłaniu wydania skryptem `wydanie.ps1`.
Potrzebne: narzędzie `vpk` (`dotnet tool install -g vpk`) i token GitHuba z prawem zapisu do repozytorium
(klasyczny z zakresem `repo` albo fine-grained z **Contents: Read and write**).

Przykład dla wersji 0.0.6:

1. W `GaleriaDesktop.csproj` zmień `<Version>0.0.5</Version>` na `<Version>0.0.6</Version>`.
2. `dotnet build` — musi przejść bez błędów.
3. Commit i `git push` (kod w repozytorium).
4. Wydanie:

   ```powershell
   $env:GITHUB_TOKEN = 'ghp_...'   # tylko w tej sesji PowerShella
   .\wydanie.ps1 -Wyslij
   ```

   Skrypt kompiluje aplikację, w folderze `Releases\` tworzy `GaleriaDesktop-0.0.6-full.nupkg`,
   `GaleriaDesktop-0.0.6-delta.nupkg` (różnica względem 0.0.5, dlatego nie kasuj `Releases\`),
   instalator `GaleriaDesktop-win-Setup.exe` i `releases.win.json`, a potem zakłada na GitHubie
   wydanie **Galeria 0.0.6** z tagiem `v0.0.6` i wysyła te pliki.
   Bez `-Wyslij` tylko buduje paczki lokalnie (do sprawdzenia przed publikacją).
5. Sprawdź na [github.com/dev-tk-pl/galeria/releases](https://github.com/dev-tk-pl/galeria/releases),
   że wydanie 0.0.6 jest na górze (opublikowane, nie „Draft”) i ma pliki z punktu 4.
6. Uruchom **zainstalowaną** Galerię (menu Start albo skrót na pulpicie — nie `dotnet run`, bo wersja
   z `bin\` nie jest zainstalowana i nie sprawdza aktualizacji):
   - **Aktualizacje: Instaluj automatycznie** → okienko „Sprawdzanie dostępności aktualizacji…”, pobieranie,
     ponowne uruchomienie. Po starcie w **Ustawieniach** powinna być wersja 0.0.6.
   - **Aktualizacje: Tylko informuj** → na górnym pasku pojawia się napis „Dostępna wersja 0.0.6”. Żeby ją
     zainstalować, przełącz na **Instaluj automatycznie** i uruchom Galerię ponownie.

Nowe osoby instalują aplikację z `GaleriaDesktop-win-Setup.exe` z najnowszego wydania (instalacja
w `%LocalAppData%`, bez uprawnień administratora).

## Jak używać

Układ okna jest wzorowany na PWA: pasek boczny (Wszystkie zdjęcia, Ulubione, Wybrane foldery),
pasek narzędzi nad siatką, karty z nazwą i datą pliku. Kolory są brane z Windows.

1. Kliknij **Dodaj folder** i wskaż folder ze zdjęciami (np. `D:\Zdjęcia`). Skanowane są też
   wszystkie podfoldery. Można podłączyć kilka folderów.
2. **Ustawienia** (⚙ albo karta **Wybrane foldery**): motyw Systemowy / Jasny / Ciemny, **kolor przewodni**
   (**Wybierz kolor** otwiera paletę barw z polami RGB i hex; **Kolor domyślny** wraca do niebieskiego `#138AE2`), lista
   folderów z liczbą plików, **Odłącz** (pliki zostają na dysku), **Dodaj folder**, **Odłącz wszystkie**,
   przełącznik **Aktualizacje** (Instaluj automatycznie / Tylko informuj) i numer wersji.
3. **Sortuj**: Najnowsze, Najstarsze, Nazwa. Data to **data wykonania**, jak w „Zdjęciach” Windows: zdjęcie —
   z EXIF, film — data nagrania z metadanych; gdy jej nie ma, data modyfikacji pliku (nie data utworzenia, bo
   Windows ustawia ją przy kopiowaniu). Ta sama data jest na kafelku i w podglądzie.
   **Siatka**: Domyślna, 3, 5 albo 7 kolumn.
   **Wybierz pliki** włącza zaznaczanie (pola wyboru na kafelkach): **Zaznacz widoczne**, **Zapisz do…**
   (kopie wszystkich plików luzem w wybranym folderze; powtórzone nazwy dostają „(2)”, „(3)”…),
   **Pobierz ZIP**, **Udostępnij** (systemowe okno udostępniania Windows), **Gotowe**.
   **♥** w lewym górnym rogu kafelka (widoczne po najechaniu kursorem, przy ulubionych zawsze) dodaje plik
   do ulubionych albo go z nich usuwa.
4. Kliknij kafelek, żeby otworzyć podgląd:
   - u góry nazwa, data, rozmiar, ścieżka i licznik; pod zdjęciem aparat/telefon, ogniskowa, przysłona, czas, ISO;
   - **Obróć** (tylko widok, plik bez zmian), **Usuń** (do Kosza Windows, po potwierdzeniu), **Drukuj**
     (systemowe okno drukowania), **♥** (ulubione), suwak powiększenia 100–400% (albo kółko myszy —
     powiększa od miejsca pod kursorem); powiększone zdjęcie przesuwa się przeciąganiem myszą;
   - **←/→** albo przyciski po bokach przechodzą do poprzedniego/następnego pliku;
   - **Esc** albo **Zamknij** zamyka podgląd.
5. **Edytuj** (w podglądzie zdjęcia) otwiera edytor:
   - **Przytnij**: ramka z uchwytami (przeciąganie rogów, boków i całej ramki), **Proporcje** (Dowolny,
     Oryginalny, Kwadrat, 16:9, 4:3, 3:2 …), obrót o 90° w lewo/prawo, odbicie w poziomie/pionie, **Resetuj**;
     **prostowanie** −45°…45° — przeciągnij linijkę pod zdjęciem (w trakcie widać siatkę), obraz sam się
     powiększa, żeby w rogach nie było pustych miejsc;
   - **Korekta**: Jasność, Ekspozycja, Kontrast, Rozjaśnienia, Cienie, Winieta, Nasycenie, Ciepło, Odcień,
     Ostrość (−100…100) z podglądem na żywo, **Resetuj korektę**;
   - **Filtry**: 16 filtrów z PWA (Perforacja, Złoty, Czarno-biały …) z miniaturami i **Automatyczne ulepszenie**;
   - **Cofnij / Ponów** (strzałki obok nazwy pliku albo **Ctrl+Z** / **Ctrl+Y**) — obejmuje kadr, obrót, odbicia,
     prostowanie, suwaki i filtr;
   - **Opcje zapisywania** → zapis kopii JPG/PNG w pełnej rozdzielczości (okno zapisu podpowiada folder
     oryginału i nazwę „…-edytowane”). Oryginał zostaje bez zmian. **Anuluj** albo **Esc** zamyka edytor.
6. **Prawy przycisk** na kafelku albo na otwartym zdjęciu/filmie otwiera menu pliku (jak w „Zdjęciach” Windows):
   Otwórz, Edytuj, Dodaj do / Usuń z ulubionych, **Rozpocznij pokaz slajdów**, Drukuj, **Kopiuj** (plik do schowka —
   Ctrl+V w Eksploratorze), **Kopiuj jako ścieżkę**, Udostępnij, **Przenieś/kopiuj** (do wskazanego folderu),
   **Zmień nazwę**, **Otwórz w Eksploratorze plików**, **Tworzenie klipu wideo w programie Microsoft Clipchamp**,
   Usuń (do Kosza). Dla filmu nie ma Edytuj i Drukuj. Menu działa na klikniętym pliku, także w trybie zaznaczania.
   - Clipchamp to osobna aplikacja Microsoftu (wymaga konta Microsoft). Film otwiera się w nim od razu; zdjęć
     Clipchamp nie przyjmuje w ten sposób, więc dla zdjęcia tylko się uruchamia. Bez Clipchampa otwiera się
     jego strona w Microsoft Store.
   - Pokaz slajdów: pełny ekran od wybranego pliku, zdjęcie co 5 s, film do końca; ←/→ przełącza ręcznie,
     **Esc** albo kliknięcie kończy pokaz.

Foldery, motyw, kolor przewodni, liczba kolumn, ulubione i ustawienie aktualizacji są zapamiętywane.

### Obsługiwane pliki

- Zdjęcia: `jpg jpeg png gif webp bmp avif svg heic heif tif tiff`
- Filmy: `mp4 m4v mov webm ogv ogg 3gp 3g2 avi mkv mpeg mpg wmv mts m2ts ts`

Pozostałe pliki (np. `NEF`, `PSD`) są pomijane. Czy film się odtworzy, zależy od kodeków zainstalowanych w Windows.

### Gdzie są ustawienia

`%LOCALAPPDATA%\Galeria\ustawienia.json`, np.:

```json
{"Foldery":["D:\\Zdjęcia"],"Motyw":"Systemowy","Kolumny":0,"Ulubione":["D:\\Zdjęcia\\a.jpg"],"Autoaktualizacja":true,"KolorPrzewodni":null}
```

Usunięcie tego pliku przywraca stan „pierwsze uruchomienie”. Stary format (`OstatniFolder`)
jest przy wczytaniu przenoszony do listy `Foldery`.

## Znane ograniczenia

- Miniatury tworzy Windows (te same, których używa Eksplorator). Przy pierwszym oglądaniu dużej liczby
  zdjęć z wolnego dysku pojawiają się z opóźnieniem (nawet 20–30 s na ekran po szybkim przewinięciu).
  Potem są w pamięci podręcznej Windows i wyświetlają się od razu.
- Zapisana kopia z edytora nie zawiera metadanych EXIF oryginału (aparat, ISO itd.).
- Podgląd GIF pokazuje tylko pierwszą klatkę, a SVG się nie wyświetla (dekoder obrazów Windows).

## Struktura projektu

| Plik | Co zawiera |
|---|---|
| `App.xaml`, `App.xaml.cs` | Start aplikacji |
| `OknoStartowe.xaml`, `OknoStartowe.xaml.cs` | Okienko przy starcie: sprawdzenie i pobranie aktualizacji |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Okno: pasek boczny, wyszukiwanie i sortowanie, siatka miniatur, podgląd |
| `ElementGalerii.cs` | Jeden plik w siatce + listy obsługiwanych rozszerzeń |
| `Ustawienia.cs` | Odczyt/zapis `ustawienia.json` |
| `Edytor.xaml`, `Edytor.xaml.cs` | Edytor zdjęcia: kadrowanie i korekta (efekty Win2D), zapis kopii |
| `Obrazy.cs` | Dekodowanie zdjęć z orientacją EXIF (podgląd i edytor) |
| `Assets\Galeria.ico` | Ikona aplikacji (exe, okno, instalator) |
| `GaleriaDesktop.csproj` | Konfiguracja projektu (.NET 10, WinUI 3, Windows App SDK 2.7, bez MSIX) |
| `Properties\PublishProfiles\JedenPlik.pubxml` | Profil publikacji: jeden samodzielny `Galeria.exe` |

## Technologie

C# / .NET 10, WinUI 3 (Windows App SDK 2.7) w wariancie bez MSIX (`WindowsPackageType=None`).
Edytor korzysta z Win2D (`Microsoft.Graphics.Win2D`) — efekty obrazu liczone na karcie graficznej.
Dlaczego nie Electron/Tauri/PWA: Chromium nie odtwarzał około 1/3 filmów z archiwum, a kodeki Windows radzą sobie z prawie wszystkimi.
