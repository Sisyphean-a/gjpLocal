param(
    [switch]$BuildFrontend = $true,
    [string]$Environment = "Production",
    [switch]$SkipChecks = $false
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$frontendDir = Join-Path $repoRoot "frontend"
$backendDir = Join-Path $repoRoot "backend\src\SwcsScanner.Api"

function Assert-CommandAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Command '$Name' was not found. Install it and add it to PATH."
    }
}

if (-not $SkipChecks) {
    Assert-CommandAvailable -Name "dotnet"
    if ($BuildFrontend) {
        Assert-CommandAvailable -Name "npm"
    }
}

if ($BuildFrontend) {
    Write-Host "Building frontend..."
    Push-Location $frontendDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build failed with exit code: $LASTEXITCODE"
        }
    } catch {
        throw "Frontend build failed: $($_.Exception.Message)"
    } finally {
        Pop-Location
    }

    $distDir = Join-Path $frontendDir "dist"
    $wwwrootDir = Join-Path $backendDir "wwwroot"
    if (-not (Test-Path $distDir)) {
        throw "Frontend dist directory was not found: $distDir"
    }

    if (-not (Test-Path $wwwrootDir)) {
        New-Item -ItemType Directory -Path $wwwrootDir | Out-Null
    }

    try {
        Copy-Item -Path (Join-Path $distDir "*") -Destination $wwwrootDir -Recurse -Force
    } catch {
        throw "Failed to copy frontend assets into wwwroot: $($_.Exception.Message)"
    }

    Write-Host "Frontend assets copied to wwwroot."
}

Write-Host "Starting backend API..."
$env:ASPNETCORE_ENVIRONMENT = $Environment
dotnet run --project (Join-Path $backendDir "SwcsScanner.Api.csproj") --configuration Release --no-launch-profile
if ($LASTEXITCODE -ne 0) {
    throw "Backend failed to start. dotnet run exit code: $LASTEXITCODE"
}
