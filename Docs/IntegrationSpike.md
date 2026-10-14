# Phase 1: celestial integration prototype

Implemented 2026-09-18, following the shared Anomaly work. No game launch or installation change was performed in this session.

## What exists

- Anomaly owns an exclusive depth-masked main background pass before atmosphere/fullscreen effects and a matching probe background pass. Neither replaces foreground lighting.
- Both shader variants must compile before activation. Missing probe hook, multiple providers, explicit conflicting lighting/probe overlays, unsupported MSAA and compile/resource errors retain vanilla. Status and Retry are available.
- Shared input: scene/probe world direction, angular pixel footprint, game sun direction/radiance, 64 live floats and optional immutable float4 data (16 MiB maximum). Existing Anomaly extras are unchanged.
- FinalFrontier registers a Pulsar celestial asset, uploads 14 synthetic axis/corner star markers and draws an analytic antialiased, limb-darkened sun. Main sky fog is preserved. The same stars appear in probes; the solar disc does not.
- Configurable enable, star brightness, disc enable/brightness/angular radius. Rich HUD through Anomaly with Pulsar fallback. Saving is debounced on a worker; template example patches were removed.
- Local plugin build deployment now includes celestial assets alongside the DLL/manifest.

## Validation completed

- Anomaly and FinalFrontier Release builds on net48 and net10.0 with deployment disabled. Anomaly retains five existing unreachable-code warnings in ShaderCompileIntercept; FinalFrontier builds without warnings.
- Actual main and probe HLSL compiled with FXC shader model 5 against installed game shader headers.
- Twelve D3D11 WARP tests execute the actual compiled provider and host entry: sun aligned to expected world direction, foreground preservation, black sky away from sources, sun omitted from probes, main sky fog, fog not touching foreground, matching main/probe star directions, invariance under very large camera translation, finite extreme-zoom output, star retention at narrow FOV, rotated probe directions and live uniform response.
- These tests use a real D3D11 software device without a window. They do not load Keen or exercise Harmony, the deferred renderer, atmosphere, transparency, probe cache timing or upscalers.

Run again from this repository:

```powershell
pwsh -File Tests/Run-CelestialSmokeTests.ps1
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never -p:UseSharedCompilation=false -m:1
```

The smoke script discovers FXC and the installed shader directory; explicit `-Fxc`, `-GameShaders` and `-AnomalyRoot` overrides are available. Build Anomaly's project with the same flags before testing the plugin in-game. Omit `RunPostBuildEvent=Never` only when ready for the repositories' normal Pulsar deployment.

## Required in-game phase-1 exit checks

1. Enable both updated plugins. Check provider status and Anomaly logs. Sky changes; ships, terrain, shadows and gameplay sun behavior do not.
2. Rotate through axis/corner markers, zoom, change FOV/resolution/aspect and move large distances. Check stable direction, pixel profiles and disc alignment with the vanilla flare.
3. Check terrain/grid silhouettes, cockpit glass, atmosphere from orbit/ground, fog and clouds. The background pass precedes their composition.
4. Check a reflective surface after probes refresh. Toggle off and allow probe refresh again. Main view restores immediately on its next draw; cached reflections follow Keen's update cadence.
5. Check native AA, DLSS/FSR and FG for smear/shimmer/history artifacts. MSAA should show the explicit vanilla fallback status.
6. Test shader failure/Retry, competing provider, world unload/reload and device-resource recreation. Do not equate standalone shader compilation with these runtime checks passing.

## Held in implementation order

- Phase 2 implemented: real Hipparcos catalogue, persistent IDs, spatial indexing and pixel-integrated profiles. See [catalogue validation](Catalogue.md); in-game catalogue validation remains pending.
- Phase 3: finish real-game reflections/temporal compatibility and address any demonstrated sky motion issue. The existing depth-zero velocity code is not changed without evidence. MSAA needs a sample-aware design.
- Phase 4: surface granulation/activity and animated corona are implemented. Unified glow now owns the native solar flare; partial-disc visibility remains open. Light intensity/shadow softness are unchanged.
- Phase 5: constellation guides and projected HUD labels. Projected labels are not provided by the existing corner-text overlay API.

2026-09-18 user validation: replacement, live star brightness, high zoom, sun sizing and Keen day/night tracking passed. User approved proceeding to the catalogue. The orange exposure-dependent sun ring remains open. This does not establish the remaining reflection, device-lifecycle or temporal/upscaler checks as passed.

2026-10-02 solar plasma implementation: the analytic photosphere now has a
procedural luminous surface and sphere-anchored animated prominence bands,
with live settings and a WARP-generated preview. The Blender MCP refinement
adds a 4K Cycles reference, fine plasma and evolving noise-masked flux bundles,
with a circular halo in the game shader. All 54 expanded D3D11 checks
pass, and both plugin targets build. This is headless shader validation;
in-game bloom/exposure, hardware cost and temporal/upscaler acceptance remain
pending. See [the solar implementation and validation notes](SolarPlasma.md).

Earlier 2026-10-02 SDR/HDR follow-up (glow approach superseded below): user accepts the SDR surface. HDR now selects a
luminance-preserving golden palette while the known HDR display program is
live; SDR keeps its surface palette. The aureole's inner cutout and limb peak
are replaced with a filled, circular, outward-fading glow. Native sun glare
retains the user's setting. Seventy-eight WARP checks include radial monotonicity
and the actual HDR shader at 600/1000/2000 nit peaks; seventy-two integration
checks include live/off/failed mode detection and uniform updates with unchanged
settings. Both targets build without deployment. In-game HDR colour/glow and
temporal acceptance remain pending. The shared display-mode metadata gap is filed
in Anomaly Slice AI; the current public-status workaround is explicitly labeled.

2026-10-02 unified-glow follow-up: the user reports the hollow ring remains beside
a new tight halo, invalidating the shader-only fix. FinalFrontier now replaces
native solar glare and all former smooth rim layers with one filled, additive
profile. RHUD's **Settings → Sun glow** controls enable, strength, radius, falloff,
atmospheric expansion and tint; reset affects only glow. Legacy glare flags cannot
reenable the overlapping layer. Anomaly now accepts the current or previous
successful main draw because Keen's transparent-flare worker can record before
the current main draw. Initial activation and already-recorded work can have one
transition frame; stale readiness expires and fallback restores native handling.
Eighty-seven WARP checks, eighty-four integration checks and twenty-four actual
host registry/prefix checks on each runtime pass. Both plugins build with
deployment disabled. Restart both through the normal Pulsar source launch.
Headless checks and preview inspection do not establish in-game HDR/glow,
hardware performance or temporal acceptance. See [current solar controls and
validation](SolarPlasma.md) and Anomaly's `Tests/CelestialGlare/README.md`.
