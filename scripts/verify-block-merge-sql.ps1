param(
    [string]$SqlRepository = (Join-Path $PSScriptRoot '../../Database-totalgs'),
    [string]$ScriptDomPath = 'C:/Program Files/Microsoft SQL Server Management Studio 22/Release/Common7/IDE/Extensions/Application/Microsoft.SqlServer.TransactSql.ScriptDom.dll'
)

$ErrorActionPreference = 'Stop'
# Offline verification only: no SQL connections or execution APIs are used.
Add-Type -Path $ScriptDomPath
$parser = [Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
$objects = @(
    'sql/Tables/Tgs/TableTgsMultiCropBlockMerge.sql',
    'sql/Functions/Tgs/TgsMultiCropBlockMergeManifest.sql',
    'sql/StoredProcedures/Tgs/TgsPreviewMultiCropBlockMerge.sql',
    'sql/StoredProcedures/Tgs/TgsGetMultiCropBlockMergeStatus.sql',
    'sql/StoredProcedures/Tgs/TgsMergeMultiCropBlock.sql',
    'sql/StoredProcedures/Tgs/TgsResolveMultiCropBlockMerge.sql'
)
$releasePath = Join-Path $SqlRepository 'sql/ReleaseScripts/MetaGrowMultiCropBlockMerge.sql'
$release = [IO.File]::ReadAllText($releasePath).Replace("`r`n", "`n")
$files = @($releasePath)
foreach ($relative in $objects) {
    $path = Join-Path $SqlRepository $relative
    $source = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    if (-not $release.Contains($source)) { throw "Release does not contain the current object script: $relative" }
    $files += $path
}
$manifestPath = Join-Path $SqlRepository 'sql/Functions/Tgs/TgsMultiCropBlockMergeManifest.sql'
$manifest = [IO.File]::ReadAllText($manifestPath)
$tableNames = [regex]::Matches($manifest, "\(N'(Tgs[^']+)', N'") | ForEach-Object { $_.Groups[1].Value }
foreach ($table in $tableNames) {
    $path = Join-Path $SqlRepository "sql/Triggers/Tgs/TR_MultiCropMerge_$table.sql"
    $source = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $trigger = $source -replace 'GO\s*$', ''
    if (-not $release.Contains($trigger.Replace("'", "''"))) { throw "Release and write guard differ: $table" }
    $files += $path
}
foreach ($path in $files) {
    $reader = [IO.StringReader]::new([IO.File]::ReadAllText($path))
    $parseErrors = $null
    try { $null = $parser.Parse($reader, [ref]$parseErrors) }
    finally { $reader.Dispose() }
    if ($parseErrors.Count) {
        $messages = $parseErrors | ForEach-Object { "${path}:$($_.Line): $($_.Message)" }
        throw ($messages -join "`n")
    }
}
Write-Output "Verified $($files.Count) SQL files, $($tableNames.Count) dependency guards, and release/object consistency. No database accessed."
