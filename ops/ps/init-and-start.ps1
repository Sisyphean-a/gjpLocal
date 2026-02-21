param(
    [switch]$BuildFrontend = $true,
    [string]$Environment = "Production",
    [switch]$SkipRuntimeChecks = $false
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$backendDir = Join-Path $repoRoot "backend\src\SwcsScanner.Api"
$startScript = Join-Path $scriptDir "start-services.ps1"
$configPath = Join-Path $backendDir ("appsettings.{0}.json" -f $Environment)
if (-not (Test-Path $configPath)) {
    $configPath = Join-Path $backendDir "appsettings.json"
}

if (-not (Test-Path $configPath)) {
    throw "No appsettings file was found."
}

if (-not (Test-Path $startScript)) {
    throw "Start script was not found: $startScript"
}

$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Add-CheckError {
    param([string]$Message)
    $errors.Add($Message) | Out-Null
}

function Add-CheckWarning {
    param([string]$Message)
    $warnings.Add($Message) | Out-Null
}

function Test-PlaceholderValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    $markers = @("ChangeMe", "PleaseReplace", "ChangeThis", "<", "TODO")
    foreach ($marker in $markers) {
        if ($Value -like "*$marker*") {
            return $true
        }
    }

    return $false
}

function Get-StringArray {
    param($Value)

    $result = @()
    if ($null -eq $Value) {
        return $result
    }

    foreach ($item in $Value) {
        $text = [string]$item
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            $result += $text
        }
    }

    return $result
}

function Resolve-FilePath {
    param(
        [string]$BaseDir,
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return Join-Path $BaseDir $PathValue
}

function Invoke-DbScalar {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Sql,
        [hashtable]$Parameters
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 5
    foreach ($key in $Parameters.Keys) {
        $parameter = $command.Parameters.Add("@$key", [System.Data.SqlDbType]::NVarChar, 256)
        $parameter.Value = [string]$Parameters[$key]
    }

    return $command.ExecuteScalar()
}

function Test-DbTableExists {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$ObjectName
    )

    $sql = "SELECT CASE WHEN OBJECT_ID(@ObjectName, 'U') IS NOT NULL THEN 1 ELSE 0 END;"
    return [int](Invoke-DbScalar -Connection $Connection -Sql $sql -Parameters @{ ObjectName = $ObjectName }) -eq 1
}

function Test-DbColumnExists {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$ObjectName,
        [string]$ColumnName
    )

    $sql = @"
SELECT COUNT(1)
FROM sys.columns c
INNER JOIN sys.objects o ON c.object_id = o.object_id
WHERE o.object_id = OBJECT_ID(@ObjectName) AND c.name = @ColumnName;
"@

    return [int](Invoke-DbScalar -Connection $Connection -Sql $sql -Parameters @{
            ObjectName = $ObjectName
            ColumnName = $ColumnName
        }) -gt 0
}

Write-Host "Loading config: $configPath"

$connectionString = [string]$config.ConnectionStrings.SwcsReadonly
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Add-CheckError "ConnectionStrings:SwcsReadonly is required."
}
elseif (Test-PlaceholderValue -Value $connectionString) {
    Add-CheckWarning "ConnectionStrings:SwcsReadonly may still be a placeholder."
}

$jwtKey = [string]$config.Jwt.Key
if ([string]::IsNullOrWhiteSpace($jwtKey) -or $jwtKey.Length -lt 32) {
    Add-CheckError "Jwt:Key must be at least 32 characters."
}
elseif (Test-PlaceholderValue -Value $jwtKey) {
    Add-CheckWarning "Jwt:Key looks like a placeholder."
}

$pfxPassword = [string]$config.Https.PfxPassword
if ([string]::IsNullOrWhiteSpace($pfxPassword)) {
    Add-CheckError "Https:PfxPassword is required."
}
elseif (Test-PlaceholderValue -Value $pfxPassword) {
    Add-CheckWarning "Https:PfxPassword may still be a placeholder."
}

$pfxPath = [string]$config.Https.PfxPath
if ([string]::IsNullOrWhiteSpace($pfxPath)) {
    Add-CheckError "Https:PfxPath is required."
}
else {
    $resolvedPfxPath = Resolve-FilePath -BaseDir $backendDir -PathValue $pfxPath
    if (-not (Test-Path $resolvedPfxPath)) {
        Add-CheckError "Certificate file was not found: $resolvedPfxPath"
    }
}

$swcs = $config.Swcs
if ($null -eq $swcs) {
    Add-CheckError "Swcs configuration section is missing."
}

if ($errors.Count -eq 0) {
    Add-Type -AssemblyName System.Data

    $connection = $null
    try {
        $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
        $connection.Open()
    }
    catch {
        Add-CheckError "Database connection failed: $($_.Exception.Message)"
    }

    if ($null -ne $connection -and $connection.State -eq [System.Data.ConnectionState]::Open -and $null -ne $swcs) {
        try {
            $productTable = [string]$swcs.ProductTable
            $productNameField = [string]$swcs.ProductNameField
            $specificationField = [string]$swcs.SpecificationField
            $barcodeTable = [string]$swcs.BarcodeTable
            $barcodeColumn = [string]$swcs.BarcodeColumn
            $priceTable = [string]$swcs.PriceTable
            $priceColumn = [string]$swcs.PriceColumn
            $barcodeFields = Get-StringArray -Value $swcs.BarcodeFields
            $priceFields = Get-StringArray -Value $swcs.PriceFields

            if (-not (Test-DbTableExists -Connection $connection -ObjectName $productTable)) {
                Add-CheckError "Product table was not found: $productTable"
            }
            else {
                if (-not (Test-DbColumnExists -Connection $connection -ObjectName $productTable -ColumnName $productNameField)) {
                    Add-CheckError "Product name field is missing: $productTable.$productNameField"
                }

                if (-not [string]::IsNullOrWhiteSpace($specificationField) -and
                    -not (Test-DbColumnExists -Connection $connection -ObjectName $productTable -ColumnName $specificationField)) {
                    Add-CheckWarning "Specification field was not found: $productTable.$specificationField. Empty specification will be returned."
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($barcodeTable)) {
                if ([string]::IsNullOrWhiteSpace($barcodeColumn)) {
                    Add-CheckError "Swcs:BarcodeColumn is required when Swcs:BarcodeTable is configured."
                }
                elseif (-not (Test-DbTableExists -Connection $connection -ObjectName $barcodeTable)) {
                    Add-CheckError "Barcode table was not found: $barcodeTable"
                }
                elseif (-not (Test-DbColumnExists -Connection $connection -ObjectName $barcodeTable -ColumnName $barcodeColumn)) {
                    Add-CheckError "Barcode field is missing: $barcodeTable.$barcodeColumn"
                }
            }
            else {
                if ($barcodeFields.Count -eq 0) {
                    Add-CheckError "Swcs:BarcodeFields must contain at least one value when Swcs:BarcodeTable is not configured."
                }
                else {
                    $existingBarcodeFields = @(
                        $barcodeFields | Where-Object {
                            Test-DbColumnExists -Connection $connection -ObjectName $productTable -ColumnName $_
                        }
                    )
                    if ($existingBarcodeFields.Count -eq 0) {
                        Add-CheckError "None of the configured barcode fields exist in $productTable. Candidates: $($barcodeFields -join ', ')"
                    }
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($priceTable)) {
                if ([string]::IsNullOrWhiteSpace($priceColumn)) {
                    Add-CheckError "Swcs:PriceColumn is required when Swcs:PriceTable is configured."
                }
                elseif (-not (Test-DbTableExists -Connection $connection -ObjectName $priceTable)) {
                    Add-CheckError "Price table was not found: $priceTable"
                }
                elseif (-not (Test-DbColumnExists -Connection $connection -ObjectName $priceTable -ColumnName $priceColumn)) {
                    Add-CheckError "Price field is missing: $priceTable.$priceColumn"
                }
            }
            else {
                if ($priceFields.Count -eq 0) {
                    Add-CheckError "Swcs:PriceFields must contain at least one value when Swcs:PriceTable is not configured."
                }
                else {
                    $existingPriceFields = @(
                        $priceFields | Where-Object {
                            Test-DbColumnExists -Connection $connection -ObjectName $productTable -ColumnName $_
                        }
                    )
                    if ($existingPriceFields.Count -eq 0) {
                        Add-CheckError "None of the configured price fields exist in $productTable. Candidates: $($priceFields -join ', ')"
                    }
                }
            }
        }
        finally {
            $connection.Dispose()
        }
    }
}

Write-Host ""
Write-Host "========== Init Check Summary =========="
if ($warnings.Count -gt 0) {
    Write-Host "Warnings:"
    foreach ($warning in $warnings) {
        Write-Host "  - $warning"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Errors:"
    foreach ($error in $errors) {
        Write-Host "  - $error"
    }
    throw "Initialization checks failed. Fix errors before start."
}

Write-Host "All checks passed. Starting services..."
& $startScript -BuildFrontend:$BuildFrontend -Environment $Environment -SkipChecks:$SkipRuntimeChecks
