# Publie la version courante (src\AssemblyInfo.cs) sur GitHub Releases.
# Les postes installes la detectent au prochain lancement.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

& (Join-Path $root 'build.ps1')

$assemblyInfo = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Raw
$version = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(\d+\.\d+\.\d+)').Groups[1].Value
$tag = "v$version"
$setup = Join-Path $root 'dist\ImprimLot-Setup.exe'

Push-Location $root
try {
    if (git status --porcelain) { throw 'Modifications non commitees : commit avant de publier.' }
    git push origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git push echoue' }
    gh release create $tag $setup "$setup.sha256" --title "ImprimLot $version" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'gh release create echoue' }
}
finally {
    Pop-Location
}
Write-Host "Publie : $tag"
