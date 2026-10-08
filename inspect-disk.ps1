$ErrorActionPreference = 'SilentlyContinue'

Write-Host '=== ALL PHYSICAL DISKS ==='
Get-Disk | Select-Object Number, FriendlyName, SerialNumber, PartitionStyle,
    @{n='SizeGB';e={[math]::Round($_.Size/1GB,2)}}, BusType, OperationalStatus, IsBoot, IsSystem |
    Sort-Object Number | Format-Table -AutoSize

Write-Host ''
Write-Host '=== IDENTIFY SPCC NVMe DISK ==='
$spcc = Get-Disk | Where-Object { $_.FriendlyName -like '*SPCC*' -or $_.FriendlyName -like '*M.2*' }
if (-not $spcc) {
    # fall back: the disk that hosts volume D
    $pd = Get-Partition -DriveLetter D
    $spcc = Get-Disk -Number $pd.DiskNumber
}
$spcc | Select-Object Number, FriendlyName, SerialNumber, PartitionStyle,
    @{n='SizeGB';e={[math]::Round($_.Size/1GB,2)}}, IsBoot, IsSystem | Format-Table -AutoSize

$disk = $spcc.Number
Write-Host ("SPCC disk number = {0}" -f $disk)

Write-Host ''
Write-Host '=== PARTITIONS ON SPCC DISK ==='
Get-Partition -DiskNumber $disk |
    Select-Object PartitionNumber, DriveLetter, Type, IsBoot, IsHidden,
        @{n='SizeGB';e={[math]::Round($_.Size/1GB,2)}},
        @{n='OffsetGB';e={[math]::Round($_.Offset/1GB,2)}} |
    Sort-Object PartitionNumber | Format-Table -AutoSize

Write-Host ''
Write-Host '=== VOLUME DETAIL FOR D ==='
Get-Volume -DriveLetter D | Select-Object DriveLetter, FileSystemLabel, FileSystem,
    @{n='UsedGB';e={[math]::Round(($_.Size-$_.SizeRemaining)/1GB,2)}},
    @{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,2)}},
    @{n='SizeGB';e={[math]::Round($_.Size/1GB,2)}} | Format-Table -AutoSize

Write-Host ''
Write-Host '=== UNALLOCATED / SUPPORTED SIZES ON SPCC DISK ==='
$part = Get-Partition -DiskNumber $disk -DriveLetter D
$sup = Get-PartitionSupportedSize -DiskNumber $disk -PartitionNumber $part.PartitionNumber
[pscustomobject]@{
    CurrentSizeGB = [math]::Round($part.Size/1GB,2)
    MinShrinkToGB = [math]::Round($sup.SizeMin/1GB,2)
    MaxGrowToGB   = [math]::Round($sup.SizeMax/1GB,2)
} | Format-Table -AutoSize

Write-Host ''
Write-Host 'DONE'
