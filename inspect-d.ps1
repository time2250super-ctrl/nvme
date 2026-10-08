$ErrorActionPreference = 'SilentlyContinue'

Write-Host '=== D: TOP-LEVEL CONTENTS (including hidden/system) ==='
Get-ChildItem -LiteralPath 'D:\' -Force |
    Select-Object Mode, @{n='SizeMB';e={ if ($_.PSIsContainer) { '' } else { [math]::Round($_.Length/1MB,2) } }}, Name |
    Format-Table -AutoSize

Write-Host ''
Write-Host '=== CAPACITY ==='
Get-Volume | Where-Object { $_.DriveLetter -in @('D','G') } |
    Select-Object DriveLetter, FileSystemLabel, FileSystem,
        @{n='UsedGB';e={ [math]::Round(($_.Size - $_.SizeRemaining)/1GB,2) }},
        @{n='FreeGB';e={ [math]::Round($_.SizeRemaining/1GB,2) }},
        @{n='SizeGB';e={ [math]::Round($_.Size/1GB,2) }} |
    Format-Table -AutoSize

Write-Host ''
Write-Host '=== PROCESSES WHOSE EXE IS ON D: ==='
Get-Process | Where-Object { $_.Path -like 'D:\*' } |
    Select-Object Id, ProcessName, Path | Format-Table -AutoSize

Write-Host ''
Write-Host '=== PROCESSES WITH A LOADED MODULE/DLL FROM D: ==='
$modHits = foreach ($p in Get-Process) {
    $mods = $p.Modules | Where-Object { $_.FileName -like 'D:\*' }
    if ($mods) {
        foreach ($m in $mods) {
            [pscustomobject]@{ Id = $p.Id; ProcessName = $p.ProcessName; Module = $m.FileName }
        }
    }
}
if ($modHits) { $modHits | Format-Table -AutoSize } else { Write-Host 'None' }

Write-Host ''
Write-Host '=== HANDLE-BASED OPEN FILES ON D: (if handle.exe present) ==='
$handle = Get-Command handle.exe -ErrorAction SilentlyContinue
$handle64 = Get-Command handle64.exe -ErrorAction SilentlyContinue
if ($handle64) { & $handle64.Source -nobanner -accepteula D:\ }
elseif ($handle) { & $handle.Source -nobanner -accepteula D:\ }
else { Write-Host 'handle.exe not installed (Sysinternals). Will use fallback checks.' }

Write-Host ''
Write-Host 'DONE'
