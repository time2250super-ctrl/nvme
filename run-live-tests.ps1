# Visible live test runner for DriveManager (reads REAL hardware; no simulation).
$ErrorActionPreference = 'Continue'
Set-Location -LiteralPath $PSScriptRoot

$host.UI.RawUI.WindowTitle = 'DriveManager - LIVE Multi-Drive Tests'

Write-Host ''
Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host '  DriveManager  -  LIVE MULTI-DRIVE TEST RUN (real hardware)      ' -ForegroundColor Cyan
Write-Host ('  {0}' -f (Get-Date)) -ForegroundColor DarkGray
Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host ''

Write-Host '----- STEP 1/2: All-disks health probe -----' -ForegroundColor Yellow
dotnet run --project tools\DriveManager.Probe\DriveManager.Probe.csproj -c Debug

Write-Host ''
Write-Host '----- STEP 2/2: xUnit live hardware suite -----' -ForegroundColor Yellow
dotnet test DriveManager.slnx -c Debug --nologo -l "console;verbosity=normal"

Write-Host ''
Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host '  TEST RUN COMPLETE. This window stays open for review.          ' -ForegroundColor Green
Write-Host '==================================================================' -ForegroundColor Cyan
