param(
    [string]$ServiceName = "SwcsScanner",
    [string]$InstallDir = "C:\Services\SwcsScanner",
    [string]$Environment = "Production",
    [switch]$BuildFrontend = $true,
    [switch]$NoBuild = $false
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$deployScript = Join-Path $scriptDir "deploy.ps1"

& $deployScript `
    -Operation Deploy `
    -ServiceName $ServiceName `
    -PublishDir $InstallDir `
    -Environment $Environment `
    -BuildFrontend:$BuildFrontend `
    -NoBuild:$NoBuild
