param(
    [string]$ServiceName = "SwcsScanner",
    [string]$InstallDir = "C:\Services\SwcsScanner",
    [string]$Environment = "Production",
    [switch]$BuildFrontend = $true
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
    Write-Host "Stopping legacy foreground process (if any)..."
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

if (-not (Test-Admin)) {
    throw "Please run this script in an elevated PowerShell window (Run as administrator)."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$frontendDir = Join-Path $repoRoot "frontend"
$backendDir = Join-Path $repoRoot "backend\src\SwcsScanner.Api"
$projectFile = Join-Path $backendDir "SwcsScanner.Api.csproj"
$publishDir = [System.IO.Path]::GetFullPath($InstallDir)
$certSourceDir = Join-Path $backendDir "certs"

Assert-CommandAvailable -Name "dotnet"
if ($BuildFrontend) {
    Assert-CommandAvailable -Name "npm"
}

Write-Host "Service name: $ServiceName"
Write-Host "Install dir : $publishDir"
Write-Host "Environment : $Environment"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existingService -and $existingService.Status -ne "Stopped") {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $ServiceName -Force
}

Stop-ConflictingSwcsProcesses

if ($BuildFrontend) {
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
    Write-Host "Frontend assets copied to backend wwwroot."
}

if (-not (Test-Path $publishDir)) {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
}

Write-Host "Publishing backend..."
Invoke-External -FilePath "dotnet" -Arguments @(
    "publish",
    $projectFile,
    "-c", "Release",
    "-o", $publishDir
)

if (Test-Path $certSourceDir) {
    $certTargetDir = Join-Path $publishDir "certs"
    if (-not (Test-Path $certTargetDir)) {
        New-Item -ItemType Directory -Path $certTargetDir -Force | Out-Null
    }

    Copy-Item -Path (Join-Path $certSourceDir "*") -Destination $certTargetDir -Recurse -Force
    Write-Host "Certificates copied to publish directory."
}

$exePath = Join-Path $publishDir "SwcsScanner.Api.exe"
if (-not (Test-Path $exePath)) {
    throw "Published executable not found: $exePath"
}

if ($null -ne $existingService) {
    Write-Host "Updating existing service..."
    Invoke-Sc -Arguments "config $ServiceName binPath= `"$exePath`" start= auto DisplayName= `"SWCS Scanner Service`""
}
else {
    Write-Host "Creating new service..."
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

Write-Host "Starting service..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$status = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Service status: $($status.Status)"
Write-Host "Start type    : Automatic"
Write-Host "Binary path   : $exePath"
Write-Host ""
Write-Host "Health check URL: https://localhost:5001/api/health"
