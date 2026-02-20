param(
    [switch]$BuildFrontend = $true,
    [string]$Environment = "Production"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$frontendDir = Join-Path $repoRoot "frontend"
$backendDir = Join-Path $repoRoot "backend\src\SwcsScanner.Api"

if ($BuildFrontend) {
    Write-Host "构建前端..."
    Push-Location $frontendDir
    npm run build
    Pop-Location

    $distDir = Join-Path $frontendDir "dist"
    $wwwrootDir = Join-Path $backendDir "wwwroot"
    if (-not (Test-Path $wwwrootDir)) {
        New-Item -ItemType Directory -Path $wwwrootDir | Out-Null
    }

    Copy-Item -Path (Join-Path $distDir "*") -Destination $wwwrootDir -Recurse -Force
    Write-Host "前端已复制到 wwwroot。"
}

Write-Host "启动后端 API..."
$env:ASPNETCORE_ENVIRONMENT = $Environment
dotnet run --project (Join-Path $backendDir "SwcsScanner.Api.csproj") --configuration Release --no-launch-profile
