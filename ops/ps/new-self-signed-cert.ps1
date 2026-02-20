param(
    [string]$Hostname = $env:COMPUTERNAME,
    [string]$IpAddress = "192.168.1.100",
    [string]$OutputDir = "..\..\backend\src\SwcsScanner.Api\certs",
    [string]$PfxPassword = "ChangePfxPassword!"
)

$ErrorActionPreference = "Stop"

$resolvedOutputDir = Resolve-Path -Path $OutputDir -ErrorAction SilentlyContinue
if (-not $resolvedOutputDir) {
    $resolvedOutputDir = New-Item -ItemType Directory -Path $OutputDir -Force
}

$pfxPath = Join-Path $resolvedOutputDir "swcs-scanner.pfx"
$cerPath = Join-Path $resolvedOutputDir "swcs-scanner.cer"

$sanExtension = "2.5.29.17={text}dns=$Hostname&ipaddress=$IpAddress"
$cert = New-SelfSignedCertificate `
    -Subject "CN=$Hostname" `
    -DnsName @($Hostname, $IpAddress) `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -FriendlyName "SWCS Scanner LAN HTTPS" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -TextExtension $sanExtension `
    -NotAfter (Get-Date).AddYears(3)

$securePassword = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Host "已生成证书："
Write-Host "PFX: $pfxPath"
Write-Host "CER: $cerPath"
Write-Host "请将 CER 导入手机并设为受信任证书。"
