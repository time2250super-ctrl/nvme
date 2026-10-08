$ErrorActionPreference = 'Continue'

$dest = 'G:\NVMe-D-Contents'
$log  = Join-Path $PSScriptRoot 'robocopy-move.log'
if (Test-Path $log) { Remove-Item $log -Force }

New-Item -ItemType Directory -Path $dest -Force | Out-Null

$folders = 'digit-lab','Docker','local-ai','models',"SuperDave's repo"

foreach ($f in $folders) {
    $src = Join-Path 'D:\' $f
    if (-not (Test-Path -LiteralPath $src)) { Write-Host "SKIP (missing): $f"; continue }
    $dst = Join-Path $dest $f
    Write-Host ("==== MOVING: {0}  ->  {1} ====" -f $src, $dst)
    robocopy $src $dst /E /MOVE /R:1 /W:1 /NP /NFL /NDL /TEE /LOG+:$log
    $rc = $LASTEXITCODE
    if ($rc -ge 8) {
        Write-Host ("  !! robocopy reported errors (exit {0}) for '{1}' - source NOT fully removed" -f $rc, $f)
    } else {
        Write-Host ("  OK (robocopy exit {0})" -f $rc)
    }
}

Write-Host ''
Write-Host '=== D: AFTER MOVE (should be only system folders) ==='
Get-ChildItem -LiteralPath 'D:\' -Force | Select-Object Mode, Name | Format-Table -AutoSize

Write-Host ''
Write-Host '=== G:\NVMe-D-Contents AFTER MOVE ==='
Get-ChildItem -LiteralPath $dest -Force | Select-Object Mode, Name | Format-Table -AutoSize

Write-Host ''
Write-Host '=== CAPACITY AFTER MOVE ==='
Get-Volume | Where-Object { $_.DriveLetter -in @('D','G') } |
    Select-Object DriveLetter, FileSystemLabel,
        @{n='UsedGB';e={ [math]::Round(($_.Size - $_.SizeRemaining)/1GB,2) }},
        @{n='FreeGB';e={ [math]::Round($_.SizeRemaining/1GB,2) }} |
    Format-Table -AutoSize

Write-Host ''
Write-Host 'DONE'
