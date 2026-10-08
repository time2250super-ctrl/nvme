$ErrorActionPreference = 'SilentlyContinue'

Write-Host '=== RELEVANT RUNNING PROCESSES (docker/wsl/vm/ai/ollama/python/node) ==='
$names = 'docker','com.docker','dockerd','vpnkit','wsl','wslservice','vmmem','vmmemwsl','ollama','localai','local-ai','python','pythonw','node','lmstudio','jan'
Get-Process | Where-Object {
    $n = $_.ProcessName
    ($names | Where-Object { $n -like "*$_*" }) -ne $null
} | Select-Object Id, ProcessName, Path | Sort-Object ProcessName | Format-Table -AutoSize

Write-Host ''
Write-Host '=== WSL DISTROS ==='
wsl.exe -l -v 2>&1

Write-Host ''
Write-Host '=== .vhdx / .vhd FILES ANYWHERE ON D: (WSL/Docker/Hyper-V disks) ==='
Get-ChildItem -LiteralPath 'D:\' -Recurse -Force -Include *.vhdx,*.vhd -ErrorAction SilentlyContinue |
    Select-Object @{n='SizeGB';e={[math]::Round($_.Length/1GB,2)}}, FullName | Format-Table -AutoSize

Write-Host ''
Write-Host '=== SERVICES WITH BINARY PATH ON D: ==='
Get-CimInstance Win32_Service | Where-Object { $_.PathName -like '*D:\*' } |
    Select-Object Name, State, StartMode, PathName | Format-Table -AutoSize

Write-Host ''
Write-Host '=== SECOND-LEVEL LISTING OF KEY FOLDERS ==='
foreach ($d in 'Docker','local-ai','models','digit-lab') {
    $p = Join-Path 'D:\' $d
    if (Test-Path -LiteralPath $p) {
        Write-Host ("--- D:\{0} ---" -f $d)
        Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue |
            Select-Object Mode, @{n='SizeMB';e={ if ($_.PSIsContainer) { '' } else { [math]::Round($_.Length/1MB,2) } }}, Name |
            Format-Table -AutoSize
    }
}

Write-Host ''
Write-Host '=== DOCKER CONTEXT / DATA-ROOT (if docker CLI present) ==='
$docker = Get-Command docker.exe -ErrorAction SilentlyContinue
if ($docker) {
    & $docker.Source info --format 'DataRoot={{.DockerRootDir}} Containers={{.Containers}} Running={{.ContainersRunning}}' 2>&1
} else {
    Write-Host 'docker CLI not found on PATH'
}

Write-Host ''
Write-Host 'DONE'
