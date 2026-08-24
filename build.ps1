<#
  build.ps1 - compila ClaudeRamTray.exe in bin\
  Non serve nessun SDK: usa csc.exe della .NET Framework 4, presente su ogni Windows.
  Uso:  .\build.ps1        oppure   .\build.ps1 -Avvia
#>
param([switch]$Avvia)
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src\ClaudeRamTray.cs'
$bin  = Join-Path $root 'bin'
$exe  = Join-Path $bin 'ClaudeRamTray.exe'

if (-not (Test-Path $src)) { Write-Host "Manca $src" -ForegroundColor Red; exit 1 }
New-Item -ItemType Directory -Path $bin -Force | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { Write-Host 'csc.exe non trovato' -ForegroundColor Red; exit 1 }

Get-Process ClaudeRamTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$argomenti = @('/nologo','/target:winexe','/optimize+',"/out:$exe",
               '/r:System.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Core.dll',
               '/r:System.Management.dll',$src)
# L'eseguibile vecchio va tolto PRIMA di compilare: altrimenti, se la
# compilazione fallisce, il Test-Path qui sotto trova la copia precedente e
# lo script annuncia "OK" mentre in bin\ c'e' ancora la versione di ieri.
Remove-Item $exe -Force -ErrorAction SilentlyContinue

$out = & $csc $argomenti 2>&1
if (-not (Test-Path $exe)) {
    Write-Host 'COMPILAZIONE FALLITA' -ForegroundColor Red
    $out | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "OK -> $exe" -ForegroundColor Green
if ($Avvia) { Start-Process $exe ; Write-Host 'Avviato.' }
