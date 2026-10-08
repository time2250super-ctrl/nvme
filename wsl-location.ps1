$ErrorActionPreference = 'SilentlyContinue'

Write-Host '=== WSL DISTRO REGISTRATIONS (BasePath = where the distro disk lives) ==='
$lxss = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss'
Get-ChildItem $lxss | ForEach-Object {
    $p = Get-ItemProperty $_.PSPath
    [pscustomobject]@{
        Distro   = $p.DistributionName
        Version  = $p.Version
        Flags    = $p.Flags
        BasePath = $p.BasePath
    }
} | Format-Table -AutoSize

Write-Host ''
Write-Host '=== ACTUAL ext4.vhdx FILES AT THOSE PATHS ==='
Get-ChildItem $lxss | ForEach-Object {
    $bp = (Get-ItemProperty $_.PSPath).BasePath
    if ($bp) {
        $bpClean = $bp -replace '^\\\\\?\\',''
        $vhdx = Join-Path $bpClean 'ext4.vhdx'
        if (Test-Path -LiteralPath $vhdx) {
            $fi = Get-Item -LiteralPath $vhdx
            [pscustomobject]@{ Path = $vhdx; SizeGB = [math]::Round($fi.Length/1GB,2); OnDriveD = ($bpClean -like 'D:*') }
        } else {
            [pscustomobject]@{ Path = "$bpClean (ext4.vhdx not found)"; SizeGB = $null; OnDriveD = ($bpClean -like 'D:*') }
        }
    }
} | Format-Table -AutoSize

Write-Host ''
Write-Host '=== DOCKER DESKTOP VM DISK LOCATION (if any) ==='
$ddData = Join-Path $env:LOCALAPPDATA 'Docker\wsl'
if (Test-Path $ddData) {
    Get-ChildItem -Path $ddData -Recurse -Force -Include *.vhdx -ErrorAction SilentlyContinue |
        Select-Object @{n='SizeGB';e={[math]::Round($_.Length/1GB,2)}}, FullName | Format-Table -AutoSize
} else {
    Write-Host "No Docker WSL data at $ddData"
}

Write-Host ''
Write-Host 'DONE'
