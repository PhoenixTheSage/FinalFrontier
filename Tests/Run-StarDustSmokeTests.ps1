param([string]$GameBin)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $GameBin) {
    [xml]$localProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props.user')
    $GameBin = [string]$localProps.Project.PropertyGroup.Bin64
}
& dotnet build (Join-Path $projectRoot 'ClientPlugin\ClientPlugin.csproj') -c Release -p:RunPostBuildEvent=Never -v:q
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& dotnet build (Join-Path $PSScriptRoot 'StarDust\StarDustSmokeTests.csproj') -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'Star dust harness build failed.' }
& dotnet (Join-Path $PSScriptRoot 'StarDust\bin\Release\net10.0\StarDustSmokeTests.dll') $GameBin (Join-Path $projectRoot 'ClientPlugin\bin\Release\net10.0\FinalFrontier.dll')
if ($LASTEXITCODE -ne 0) { throw 'Star dust smoke checks failed.' }
