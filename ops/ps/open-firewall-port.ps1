param(
    [int]$Port = 5001,
    [string]$RuleName = "SWCS Scanner HTTPS"
)

$ErrorActionPreference = "Stop"

$existingRule = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
if ($existingRule) {
    Set-NetFirewallRule -DisplayName $RuleName -Enabled True -Action Allow -Profile Any | Out-Null
    Write-Host "已更新防火墙规则：$RuleName"
    return
}

New-NetFirewallRule `
    -DisplayName $RuleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $Port `
    -Profile Any | Out-Null

Write-Host "已创建防火墙规则：$RuleName（TCP $Port）"
