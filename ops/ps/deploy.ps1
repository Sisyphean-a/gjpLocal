param(
    [ValidateSet("Deploy", "Restart")]
    [string]$Operation = "Deploy",
    [string]$ServiceName = "SwcsScanner",
    [string]$PublishDir = "C:\Services\SwcsScanner",
    [string]$Environment = "Production",
    [switch]$BuildFrontend = $true,
    [switch]$NoBuild = $false
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Command '$Name' was not found. Install it and add it to PATH."
    }
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE. Args: $($Arguments -join ' ')"
    }
}

function Invoke-Sc {
    param([Parameter(Mandatory = $true)][string]$Arguments)

    & cmd.exe /c "sc.exe $Arguments"
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed with exit code $LASTEXITCODE. Args: $Arguments"
    }
}

function Stop-ConflictingSwcsProcesses {
    Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -in @("SwcsScanner.Api.exe", "dotnet.exe") -and
            $_.CommandLine -match "SwcsScanner.Api"
        } |
        ForEach-Object {
            try {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop
            }
            catch {
                Write-Host "Skip process $($_.ProcessId): $($_.Exception.Message)"
            }
        }
}

function Backup-PublishDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$TargetPublishDir,
        [Parameter(Mandatory = $true)][string]$ServiceBinaryPath
    )

    if (-not (Test-Path $ServiceBinaryPath)) {
        return
    }

    $backupRoot = Join-Path $TargetPublishDir "_rollback"
    if (-not (Test-Path $backupRoot)) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    }

    $stamp = Get-Date -Format "yyyyMMddHHmmss"
    $backupDir = Join-Path $backupRoot $stamp
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

    Get-ChildItem -Path $TargetPublishDir -Force |
        Where-Object { $_.Name -ne "_rollback" } |
        ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $backupDir -Recurse -Force
        }

    Get-ChildItem -Path $backupRoot -Directory |
        Sort-Object Name -Descending |
        Select-Object -Skip 5 |
        Remove-Item -Recurse -Force

    Write-Host "Backup created: $backupDir"
}

if (-not (Test-Admin)) {
    throw "Please run this script in an elevated PowerShell window (Run as administrator)."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$frontendDir = Join-Path $repoRoot "frontend"
$backendDir = Join-Path $repoRoot "backend\src\SwcsScanner.Api"
$projectFile = Join-Path $backendDir "SwcsScanner.Api.csproj"
$resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$certSourceDir = Join-Path $backendDir "certs"
$exePath = Join-Path $resolvedPublishDir "SwcsScanner.Api.exe"

Assert-CommandAvailable -Name "dotnet"
if ($BuildFrontend -and -not $NoBuild -and $Operation -eq "Deploy") {
    Assert-CommandAvailable -Name "npm"
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($Operation -eq "Restart") {
    if ($null -eq $service) {
        throw "Service '$ServiceName' was not found."
    }

    Restart-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
    $status = Get-Service -Name $ServiceName
    Write-Host "Service status: $($status.Status)"
    exit 0
}

if ($null -ne $service -and $service.Status -ne "Stopped") {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $ServiceName -Force
}

Stop-ConflictingSwcsProcesses

if ($BuildFrontend -and -not $NoBuild) {
    Write-Host "Building frontend..."
    Push-Location $frontendDir
    try {
        Invoke-External -FilePath "npm" -Arguments @("run", "build")
    }
    finally {
        Pop-Location
    }

    $distDir = Join-Path $frontendDir "dist"
    $wwwrootDir = Join-Path $backendDir "wwwroot"
    if (-not (Test-Path $wwwrootDir)) {
        New-Item -ItemType Directory -Path $wwwrootDir | Out-Null
    }

    Copy-Item -Path (Join-Path $distDir "*") -Destination $wwwrootDir -Recurse -Force
}

if (-not (Test-Path $resolvedPublishDir)) {
    New-Item -ItemType Directory -Path $resolvedPublishDir -Force | Out-Null
}

Backup-PublishDirectory -TargetPublishDir $resolvedPublishDir -ServiceBinaryPath $exePath

if (-not $NoBuild) {
    Write-Host "Publishing backend..."
    Invoke-External -FilePath "dotnet" -Arguments @(
        "publish",
        $projectFile,
        "-c", "Release",
        "-o", $resolvedPublishDir
    )
}

if (Test-Path $certSourceDir) {
    $certTargetDir = Join-Path $resolvedPublishDir "certs"
    if (-not (Test-Path $certTargetDir)) {
        New-Item -ItemType Directory -Path $certTargetDir -Force | Out-Null
    }

    Copy-Item -Path (Join-Path $certSourceDir "*") -Destination $certTargetDir -Recurse -Force
}

if (-not (Test-Path $exePath)) {
    throw "Published executable not found: $exePath"
}

if ($null -ne $service) {
    Invoke-Sc -Arguments "config $ServiceName binPath= `"$exePath`" start= auto DisplayName= `"SWCS Scanner Service`""
}
else {
    Invoke-Sc -Arguments "create $ServiceName binPath= `"$exePath`" start= auto DisplayName= `"SWCS Scanner Service`""
}

Invoke-Sc -Arguments "description $ServiceName `"SWCS local barcode scanner service.`""
Invoke-Sc -Arguments "failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000"
Invoke-Sc -Arguments "failureflag $ServiceName 1"

$serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$environmentValues = @(
    "ASPNETCORE_ENVIRONMENT=$Environment",
    "DOTNET_ENVIRONMENT=$Environment"
)
New-ItemProperty -Path $serviceRegistryPath -Name Environment -PropertyType MultiString -Value $environmentValues -Force | Out-Null

Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
$status = Get-Service -Name $ServiceName

Write-Host ""
Write-Host "Service status: $($status.Status)"
Write-Host "Start type    : Automatic"
Write-Host "Binary path   : $exePath"
Write-Host "Health check  : https://localhost:5001/api/v2/health"
