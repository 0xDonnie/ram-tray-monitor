<#
  install.ps1 - compila, registra l'avvio automatico e lancia il programma.
  NON serve amministratore.
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1')
$exe = Join-Path $root 'bin\ClaudeRamTray.exe'
if (-not (Test-Path $exe)) { Read-Host 'Build fallita. INVIO per chiudere'; exit 1 }

$runK = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $runK -Name 'ClaudeRamTray' -Value "`"$exe`"" -PropertyType String -Force | Out-Null
Write-Host 'Registrato in avvio automatico (HKCU Run).'

Start-Process $exe
Start-Sleep -Seconds 2
if (Get-Process ClaudeRamTray -ErrorAction SilentlyContinue) {
    Write-Host 'In esecuzione. Guarda vicino all orologio.' -ForegroundColor Green
} else {
    Write-Host 'Compilato ma non partito.' -ForegroundColor Yellow
}
Read-Host 'INVIO per chiudere'
