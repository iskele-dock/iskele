# Iskele kurulum dosyasını (IskeleSetup-<sürüm>.exe) üretir.
# Gerekenler: .NET 9 SDK. Kurulum programı Windows'un yerleşik .NET Framework derleyicisiyle (csc.exe) derlenir.
#   ./installer/build.ps1            -> dist/IskeleSetup-1.0.0.exe
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src\Iskele.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$work = Join-Path $projectRoot 'build'
$payload = Join-Path $work 'payload'
$dist = Join-Path $projectRoot 'dist'

# Verify the build target before clearing generated files.
if ([IO.Path]::GetFullPath($work) -ne [IO.Path]::GetFullPath((Join-Path $projectRoot 'build'))) { throw 'Beklenmeyen build yolu' }
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload, $dist | Out-Null

& dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish başarısız' }

$zip = Join-Path $work 'payload.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -Force

$setup = Join-Path $dist "IskeleSetup-$version.exe"
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
# Approved branding embedded in setup and its Windows file icon.
& $compiler ('/win32icon:' + (Join-Path $projectRoot 'src\Assets\Brand\iskele.ico')) ('/resource:' + (Join-Path $projectRoot 'src\Assets\Brand\iskele.png') + ',iskele-brand.png') /nologo /codepage:65001 /optimize+ /target:winexe ('/out:' + $setup) `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    ('/resource:' + (Join-Path $projectRoot 'docs\screenshots\dock-serit.png') + ',dock-preview.png') `
    ('/resource:' + $zip + ',payload.zip') (Join-Path $PSScriptRoot 'Setup.cs')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw 'Kurulum programı derlenemedi' }

$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
"$hash  $(Split-Path $setup -Leaf)" | Out-File -LiteralPath "$setup.sha256" -Encoding ascii
Write-Output $setup
Write-Output "SHA256: $hash"
