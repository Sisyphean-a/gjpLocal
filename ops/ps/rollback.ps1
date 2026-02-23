param(
    [string]$ServiceName = "SwcsScanner",
    [string]$PublishDir = "C:\Services\SwcsScanner",
    [string]$BackupStamp
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    throw "Please run this script in an elevated PowerShell window (Run as administrator)."
}

$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$backupRoot = Join-Path $resolvedPublishDir "_rollback"
if (-not (Test-Path $backupRoot)) {
    throw "Rollback root not found: $backupRoot"
}

$backupDir = $null
if ([string]::IsNullOrWhiteSpace($BackupStamp)) {
    $backupDir = Get-ChildItem -Path $backupRoot -Directory |
        Sort-Object Name -Descending |
        Select-Object -First 1
}
else {
    $candidate = Join-Path $backupRoot $BackupStamp
    if (Test-Path $candidate) {
        $backupDir = Get-Item -Path $candidate
    }
}

if ($null -eq $backupDir) {
    throw "No rollback backup was found."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    throw "Service '$ServiceName' was not found."
}

if ($service.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
}

Get-ChildItem -Path $resolvedPublishDir -Force |
    Where-Object { $_.Name -ne "_rollback" } |
    Remove-Item -Recurse -Force

Copy-Item -Path (Join-Path $backupDir.FullName "*") -Destination $resolvedPublishDir -Recurse -Force

Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
$status = Get-Service -Name $ServiceName

Write-Host "Rollback source: $($backupDir.Name)"
Write-Host "Service status : $($status.Status)"
