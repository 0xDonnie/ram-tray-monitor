<#
  uninstall.ps1 - ferma il programma e toglie l'avvio automatico.
  Non cancella bin\ ne' i CSV degli allarmi.
#>
$ErrorActionPreference = 'Continue'
Get-Process ClaudeRamTray -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ClaudeRamTray' -ErrorAction SilentlyContinue
Write-Host 'Fermato e tolto dall avvio automatico.' -ForegroundColor Green
Read-Host 'INVIO per chiudere'
