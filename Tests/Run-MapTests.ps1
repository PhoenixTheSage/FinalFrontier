param([string]$AnomalyRoot = (Join-Path $PSScriptRoot '..\..\Anomaly'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$localProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props.user')
$gameBin = [string]$localProps.Project.PropertyGroup.Bin64
& dotnet build (Join-Path $AnomalyRoot 'ClientPlugin\ClientPlugin.csproj') -c Release -p:RunPostBuildEvent=Never -p:UseSharedCompilation=false -v:q
if ($LASTEXITCODE -ne 0) { throw 'Anomaly build failed.' }
& dotnet build (Join-Path $PSScriptRoot 'Map\MapTests.csproj') -c Release -p:RunPostBuildEvent=Never -p:UseSharedCompilation=false -v:q
if ($LASTEXITCODE -ne 0) { throw 'Map harness build failed.' }
foreach ($runtime in 'net48', 'net10.0') {
    $anomalyDll = Join-Path $AnomalyRoot "ClientPlugin\bin\Release\$runtime\Anomaly.dll"
    $testDir = Join-Path $PSScriptRoot "Map\bin\Release\$runtime"
    if ($runtime -eq 'net48') { & (Join-Path $testDir 'FinalFrontier.MapTests.exe') $gameBin $anomalyDll }
    else { & dotnet (Join-Path $testDir 'FinalFrontier.MapTests.dll') $gameBin $anomalyDll }
    if ($LASTEXITCODE -ne 0) { throw "Map tests failed on $runtime." }
}
