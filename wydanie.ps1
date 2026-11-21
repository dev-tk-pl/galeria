# Buduje instalator Velopack w wersji z <Version> w GaleriaDesktop.csproj (wynik: Releases\).
# Z przełącznikiem -Wyslij publikuje go też na GitHub Releases; token w $env:GITHUB_TOKEN.
param([switch]$Wyslij)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$repo = 'https://github.com/dev-tk-pl/galeria'
$wersja = ([xml](Get-Content GaleriaDesktop.csproj)).Project.PropertyGroup.Version

# Osobny folder kompilacji, żeby uruchomiona Galeria z bin\Release nie blokowała publikacji
dotnet publish -c Release --self-contained -p:OutputPath=bin\Velopack\ -o bin\velopack-publish
if ($LASTEXITCODE) { exit 1 }

vpk pack -u GaleriaDesktop -v $wersja -p bin\velopack-publish -e Galeria.exe --packTitle Galeria -o Releases
if ($LASTEXITCODE) { exit 1 }

if ($Wyslij) {
    vpk upload github -o Releases --repoUrl $repo --token $env:GITHUB_TOKEN --publish --releaseName "Galeria $wersja" --tag "v$wersja"
    if ($LASTEXITCODE) { exit 1 }
}
