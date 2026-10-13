# Compile A3Print.exe puis l'installeur dist\A3Print-Setup-<version>.exe (+ .sha256).
# Prerequis : Inno Setup 6 (winget install JRSoftware.InnoSetup). Le compilateur C# est celui de Windows.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$sumatraVersion = '3.5.2'
$sumatraUrl = "https://www.sumatrapdfreader.org/dl/rel/$sumatraVersion/SumatraPDF-$sumatraVersion-64.zip"

$assemblyInfo = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
$version = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(\d+\.\d+\.\d+)').Groups[1].Value
if (-not $version) { throw 'Version introuvable dans src\AssemblyInfo.cs' }
Write-Host "A3Print $version"

$build = Join-Path $root 'build'
$vendor = Join-Path $root 'vendor'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $build, $vendor, $dist | Out-Null

# Moteur d'impression PDF
$sumatra = Join-Path $vendor 'SumatraPDF.exe'
if (-not (Test-Path $sumatra)) {
    Write-Host "Telechargement SumatraPDF $sumatraVersion..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zip = Join-Path $vendor 'sumatra.zip'
    $extract = Join-Path $vendor 'sumatra-extract'
    Invoke-WebRequest $sumatraUrl -OutFile $zip -UseBasicParsing
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive $zip -DestinationPath $extract
    $exe = Get-ChildItem $extract -Filter 'SumatraPDF*.exe' | Select-Object -First 1
    if (-not $exe) { throw 'SumatraPDF.exe absent de l''archive' }
    Move-Item $exe.FullName $sumatra
    Remove-Item $zip, $extract -Recurse -Force
}

# Application
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "Compilateur introuvable : $csc" }
$exeOut = Join-Path $build 'A3Print.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$exeOut" `
    "/win32manifest:$(Join-Path $root 'src\app.manifest')" `
    "/win32icon:$(Join-Path $root 'assets\app.ico')" `
    "/resource:$(Join-Path $root 'assets\logo.png'),A3Print.logo.png" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    (Join-Path $root 'src\*.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation echouee' }
Copy-Item $sumatra $build -Force

# Installeur
$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup' }
& $iscc /Qp "/DAppVersion=$version" (Join-Path $root 'installer\setup.iss')
if ($LASTEXITCODE -ne 0) { throw 'Creation de l''installeur echouee' }

$setupName = "A3Print-Setup-$version.exe"
$setup = Join-Path $dist $setupName
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
Set-Content -Path "$setup.sha256" -Value "$hash  $setupName" -Encoding ascii

Write-Host "OK : $setup"
Write-Host "SHA-256 : $hash"
