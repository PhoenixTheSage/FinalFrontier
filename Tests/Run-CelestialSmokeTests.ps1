param(
    [string]$AnomalyRoot = (Join-Path $PSScriptRoot '..\..\Anomaly'),
    [string]$GameShaders,
    [string]$Fxc
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $GameShaders) {
    [xml]$localProps = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props.user')
    $gameBin = [string]$localProps.Project.PropertyGroup.Bin64
    $GameShaders = Join-Path (Split-Path $gameBin -Parent) 'Content\Shaders'
}
if (-not $Fxc) {
    $Fxc = Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter fxc.exe -Recurse |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Fxc) { throw 'fxc.exe not found; supply -Fxc.' }
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('FinalFrontier-' + [guid]::NewGuid().ToString('N'))
$outputs = @("$scratch-main.cso", "$scratch-probe.cso", "$scratch-vs.cso")
try {
    $includes = Join-Path $AnomalyRoot 'Assets\Shaders'
    $provider = Join-Path $projectRoot 'Assets\Celestial\Background.hlsl'
    foreach ($probe in 0, 1) {
        & $Fxc /nologo /T ps_5_0 /E __pixel_shader /D "ANOMALY_CELESTIAL_PROBE=$probe" /I $includes /I $GameShaders /Fo $outputs[$probe] $provider
        if ($LASTEXITCODE -ne 0) { throw "Celestial shader compilation failed (probe=$probe)." }
    }
    & $Fxc /nologo /T vs_5_0 /E __vertex_shader /Fo $outputs[2] (Join-Path $includes 'Fullscreen.hlsl')
    if ($LASTEXITCODE -ne 0) { throw 'Fullscreen vertex compilation failed.' }
    & dotnet build (Join-Path $PSScriptRoot 'CelestialSmokeTests.csproj') -c Release -p:UseSharedCompilation=false -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Smoke harness build failed.' }
    & (Join-Path $PSScriptRoot 'bin\Release\net48\CelestialSmokeTests.exe') @outputs (Join-Path $projectRoot 'Assets/Celestial/Hipparcos.bin')
    if ($LASTEXITCODE -ne 0) { throw 'D3D11 WARP checks failed.' }
}
finally { foreach ($file in $outputs) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } } }
