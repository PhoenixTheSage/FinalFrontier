param(
    [string]$AnomalyRoot = (Join-Path $PSScriptRoot '..\..\Anomaly'),
    [string]$GameShaders,
    [string]$Fxc,
    [string]$SolarPreviewDirectory,
    [string]$HdrRoot
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
$outputs = @("$scratch-main.cso", "$scratch-probe.cso", "$scratch-vs.cso", "$scratch-glow.cso", "$scratch-hdr.cso", "$scratch-sdr.cso")
try {
    $includes = Join-Path $AnomalyRoot 'Assets\Shaders'
    $provider = Join-Path $projectRoot 'Assets\Celestial\Background.hlsl'
    foreach ($probe in 0, 1) {
        & $Fxc /nologo /T ps_5_0 /E __pixel_shader /D "ANOMALY_CELESTIAL_PROBE=$probe" /I $includes /I $GameShaders /Fo $outputs[$probe] $provider
        if ($LASTEXITCODE -ne 0) { throw "Celestial shader compilation failed (probe=$probe)." }
    }
    & $Fxc /nologo /T vs_5_0 /E __vertex_shader /Fo $outputs[2] (Join-Path $includes 'Fullscreen.hlsl')
    if ($LASTEXITCODE -ne 0) { throw 'Fullscreen vertex compilation failed.' }
    & $Fxc /nologo /T ps_5_0 /E __pixel_shader /I $includes /I $GameShaders /Fo $outputs[3] (Join-Path $PSScriptRoot 'SolarAureole.hlsl')
    if ($LASTEXITCODE -ne 0) { throw 'Solar optical-profile compilation failed.' }
    if ($HdrRoot) {
        & $Fxc /nologo /T ps_5_0 /E __pixel_shader /D ANOMALY_FULLSCREEN_SLOT_AFTERUPSCALE=1 /I $includes /I $GameShaders /Fo $outputs[4] (Join-Path $HdrRoot 'ClientPlugin/Assets/AnomalyPack/Fullscreen/AfterUpscale/HdrTonemap.hlsl')
        if ($LASTEXITCODE -ne 0) { throw 'HDR display shader compilation failed.' }
        & $Fxc /nologo /T ps_5_0 /E __pixel_shader /I $GameShaders /Fo $outputs[5] (Join-Path $PSScriptRoot 'SolarSdrTransfer.hlsl')
        if ($LASTEXITCODE -ne 0) { throw 'Keen SDR transfer compilation failed.' }
    }
    & dotnet build (Join-Path $PSScriptRoot 'CelestialSmokeTests.csproj') -c Release -p:UseSharedCompilation=false -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Smoke harness build failed.' }
    $testArgs = $outputs[0..2] + @((Join-Path $projectRoot 'Assets/Celestial/Hipparcos.bin'), $(if ($SolarPreviewDirectory) { $SolarPreviewDirectory } else { '-' }), $outputs[3])
    if ($HdrRoot) { $testArgs += $outputs[4..5] }
    & (Join-Path $PSScriptRoot 'bin\Release\net48\CelestialSmokeTests.exe') @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'D3D11 WARP checks failed.' }
}
finally { foreach ($file in $outputs) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } } }
