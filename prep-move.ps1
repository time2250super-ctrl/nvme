$ErrorActionPreference = 'SilentlyContinue'

Write-Host '=== TOP-LEVEL FOLDER SIZES ON D: ==='
$folders = 'digit-lab','Docker','local-ai','models',"SuperDave's repo"
$sizes = foreach ($f in $folders) {
    $p = Join-Path 'D:\' $f
    if (Test-Path -LiteralPath $p) {
        $files = Get-ChildItem -LiteralPath $p -Recurse -Force -File -ErrorAction SilentlyContinue
        $bytes = ($files | Measure-Object -Property Length -Sum).Sum
        [pscustomobject]@{ Folder = $f; SizeGB = [math]::Round(($bytes/1GB),2); Files = ($files | Measure-Object).Count }
    }
}
$sizes | Format-Table -AutoSize

Write-Host ''
Write-Host '=== G: ROOT CONTENTS (check for name conflicts) ==='
Get-ChildItem -LiteralPath 'G:\' -Force -ErrorAction SilentlyContinue |
    Select-Object Mode, Name | Format-Table -AutoSize

Write-Host ''
Write-Host '=== SHUTTING DOWN WSL (distros already stopped; ensures no handles) ==='
wsl.exe --shutdown 2>&1
Start-Sleep -Seconds 2
wsl.exe -l -v 2>&1

Write-Host ''
Write-Host 'DONE'
