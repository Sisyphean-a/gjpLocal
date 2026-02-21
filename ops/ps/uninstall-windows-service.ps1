param(
    [string]$ServiceName = "SwcsScanner",
    [string]$InstallDir = "C:\Services\SwcsScanner",
    [switch]$RemoveInstallDir = $false
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

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    Write-Host "Service '$ServiceName' was not found."
}
else {
    if ($service.Status -ne "Stopped") {
        Write-Host "Stopping service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
    }

    Write-Host "Deleting service '$ServiceName'..."
    & sc.exe delete $ServiceName | Out-Host
}

if ($RemoveInstallDir) {
    $resolvedInstallDir = [System.IO.Path]::GetFullPath($InstallDir)
    if (Test-Path $resolvedInstallDir) {
        Write-Host "Removing install directory: $resolvedInstallDir"
        Remove-Item -Path $resolvedInstallDir -Recurse -Force
    }
}

Write-Host "Done."
