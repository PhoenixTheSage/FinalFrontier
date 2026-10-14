# Animated solar plasma

The sun was rebuilt through the official Blender MCP to address the coarse
surface, repeated semicircular arches and oval backing glow. The editable
Blender study and real-time HLSL are distinct deliverables; Blender material,
geometry and compositor nodes do not run inside Space Engineers.

## Blender authoring

The Solar Laboratory uses an emissive object-space material with animated 4D
convection, domain warping, multiscale noise, distorted cellular granulation and
directional Gabor fibrils. Amber valleys stay luminous, without a black sunspot
mask or reference-image texture. Bright active footpoints evolve with their
field regions.

Forty-eight regions carry 767 thin 3D strands. Foot spacing, leg shape, apex
height, skew, twist and lifetime vary. Native shape keys animate their geometry
and material drivers move plasma along them. This deterministic field-trail
ensemble is not a fluid or particle-physics simulation. It remains animated on
reopen without Python handlers, external textures or simulation caches.

A noisy emissive volume hugs the sphere. HDR emission feeds compact compositor
Fog Glow; no stretched glow card is used. Cycles/OptiX replaces Eevee for final
inspection because Eevee's volume grid was visible. Standard colour management
uses fixed exposure, with denoising disabled to preserve detail. The pre-existing
scene and objects are preserved.

Inspection outputs include a native 4096-square 16-bit still, a native-size crop,
a close-up render and a lossless 1536-square motion sequence. Odd frames 1–127 at
24 simulation fps encode at 12 fps, preserving 5.33 seconds at the authored
speed. The clip restarts; it is not a seamless or reversed ping-pong loop.

### Lessons consulted

- [FireDrum: Procedural Speed Trails & Burn Effects](https://www.firedrumstudios.com/blogs/procedural-speed-trails-burn-effects-in-blender-a-shader-vfx-tutorial)
  informed emission/transparency masks, evolving 4D noise and coordinate flow.
- [CBaileyFilm: Procedural Solar Flare Shader](https://www.youtube.com/watch?v=k9anTO45bCM)
  describes driver animation and position-dependent flare materials.
- [Codesign Projects: Animated Sun with Evolving Solar Flares](https://www.youtube.com/watch?v=DLtcT55qXSI)
  describes instanced flares with delayed growth and distortion. This study uses
  native shape-key trails and a spherical volume instead of a camera card.
- [L. Torres: The Sun in Blender](https://l_torres.artstation.com/projects/wJJEJ6)
  discusses noise/Voronoi emission and particle-flare tradeoffs.
- Blender's bundled current Noise, Gabor, Layer Weight and Glare manuals were
  checked against installed 5.2 sockets. See the online
  [Glare reference](https://docs.blender.org/manual/en/latest/compositing/types/filter/glare.html).

The video descriptions were accessible; full videos were not retrieved. Older
lessons were adapted to Blender 5.2. No tutorial assets were downloaded.

### Reproduce through MCP

`Tools/Solar/BlenderMcp.py` calls the installed official `blmcp` server over MCP
stdio, using its published tools rather than bypassing MCP with socket calls.
Use a Python environment containing `mcp` and `blmcp`, with the addon running.
Execute these files with `execute_blender_code`, in order:

1. `BuildSolarScene.py`: creates a separate scene and blend, refusing to replace
   an existing Solar Laboratory. Default output: `Artifacts/SolarBlender` in the
   project; override `SOLAR_OUTPUT` in the tool's Python namespace if needed.
2. `RefineSolarScene.py`: installs the detailed material and spherical gas.
3. `RefineSolarFlux.py`: regenerates flux geometry and adds active footpoints.
4. `ConfigureSolarRender.py`: selects Cycles/OptiX and 4K inspection defaults.
5. `RefineSolarTrails.py`: breaks up the strands with emission/transparency masks
   and saves the final native animation.
6. `RenderSolarScene.py`: renders a still. Namespace overrides:
   `SOLAR_RENDER_SIZE`, `SOLAR_RENDER_NAME`, `SOLAR_FRAME`.
7. `RenderSolarAnimation.py`: starts the PNG sequence. Once complete,
   `EncodeSolarMotion.py` encodes it with FFmpeg or imageio-ffmpeg.
8. `RestoreSolarInspection.py`: restores and saves 4K inspection settings and
   shows the rendered still in Blender's Rendering workspace.

Live still rendering uses MCP deferred completion. Background CLI rendering
timed out in this environment; completed outputs came from the live addon.
After motion export, restore frames 1–240, step 1, 48 samples, 4096 resolution
and frame 49. Refinements must run before starting a render job.

## Game shader

`Assets/Celestial/Sun.hlsli` adapts the design to an analytic sky shader:

- Rotating sphere coordinates and continuous animated noise. Six filtered
  octaves reveal finer granules at large size. Existing granulation values map
  to finer base density while retaining XML defaults and slider bounds.
- Thirty-two regions, four evolving strands each, twenty-four segments per
  strand: unequal legs, different apex/foot spacing, twist, growth and travelling
  packets. Bounds skip distant pixels; hemisphere depth hides back-facing parts.
  Maximum segment contributions prevent doubled joints.
- One filled, circular optical glow, with a monotonic falloff from the centre.
  It replaces Keen's solar lens flare, the former chromospheric gas ring and the
  additional smooth photosphere rim boost. Animated strands remain separate.
  The game's post-processing still supplies bloom from the resulting radiance.

Blender Gabor/Voronoi and volume rendering are approximated by noise, ridges and
analytic strands. The game uses 128 strands rather than 767; an exact Cycles match
is not established. There are no baked image atlases, new resource bindings or
raymarch passes. The existing Anomaly glare-ownership API now tolerates worker
recording before the current main-view draw; both plugins need the update.

Detail fades in from 4 to 18 pixels of apparent sun radius. Octaves fade before
exceeding the supplied pixel footprint, including limb foreshortening. Flare
widths preserve subpixel coverage. All layers multiply supplied sun radiance
and Sun Brightness. Gameplay lighting, foreground depth and reflection-probe
omission retain their existing behavior.

## Controls and validation

Under **Anomaly Shaders → Final Frontier → Settings → Solar surface and flare
bands**: Surface Detail, Solar Animation Speed, Corona and Flare Strength,
Flare Height, Granulation Scale and Golden Colour Strength. These also appear
in Pulsar, using existing debounced persistence. Speed zero holds phase zero;
corona zero removes raised strands. Uniform slots 34–39 are unchanged;
slot 40 selects the HDR display palette automatically. Slots 41–46 control the
unified glow; the existing 64-float host buffer and bindings are unchanged.

### Unified Sun glow (2026-10-02)

Under **Anomaly Shaders → Final Frontier → Settings → Sun glow**:

- **Glow enabled:** remove the smooth glow without restoring the native solar
  flare or changing the photosphere and animated strands.
- **Glow strength:** 0–2 relative to sun radiance; default 0.18.
- **Glow radius (solar radii):** 0.5–24, default 3. This is the half-strength
  distance from the centre, scaled with the sun's angular size and zoom.
- **Glow falloff:** 1–4, default 2. One gives longer wings, two a Gaussian and
  four a flatter centre with a steeper fade. None creates an inner cutout.
- **Glow atmosphere expansion:** 0–4, default 1. At full immersion, one doubles
  width without increasing central brightness; zero retains space width.
- **Glow golden tint:** 0–1, default 0.55. Blends world sunlight toward the
  solar golden palette, also respecting the surface's Golden Colour Strength.
- **Reset sun glow:** resets these controls without altering surface or strands.

The profile is `strength * exp2(-pow(angle / width, falloff))`, with
`width = sunRadius * glowRadius * (1 + air * atmosphereExpansion)`. Angular
distance uses `atan2(length(cross(ray, sunDirection)), dot(ray, sunDirection))`
to avoid a false centre offset from floating-point normalization. The glow is
additive through the centre of the disc, independent of detail level or corona
strength. Changing its settings applies live through debounced persistence.

The user's follow-up screenshot invalidated the previous shader-only glow fix:
native solar glare still appeared alongside a tight custom halo. FinalFrontier
now always claims the native solar flare while replacement is healthy. The old
`NativeSunGlare` XML property remains readable for compatibility but no longer
appears in RHUD/Pulsar or enables a second glow. Disable the whole provider to
restore vanilla. Other light flares and gameplay sunlight retain their behavior.

Keen gathers transparent flares on a worker that can precede the host's current
main sun draw. Anomaly's former same-frame-only policy therefore missed that
worker. Ownership now accepts a successful main draw from the current or
immediately previous frame and retains that certificate during the next draw.
Disabled, failed, conflicting and unregistered providers, device release and
main-pass fallback restore native handling; a certificate older than one frame
expires. First activation retains native glare until the first successful draw.
Already-recorded flares cannot be changed retroactively, so activation or failure
can have a bounded transition frame. Final in-game scheduling remains unverified.

### SDR/HDR gold

User acceptance: the solar surface looks good in-game in SDR. HDR retained the
orange-heavy linear source chromaticity that Keen's SDR filmic shoulder shifts
towards yellow. The HDR-only palette now raises green relative to red and
renormalizes each heat colour to its original luminance. This changes hue without
adding exposure; gold strength zero retains the world's colour. The procedural
surface and animated strand palette remain, but the new additive glow and removal
of the rim boost change the composite in both SDR and HDR. The previous exact SDR
pixel comparison applies to the earlier palette-only fix, not this composite.

`AnomalyBridge.ReadHdrDisplay` checks the public live status of HDR Output's
`hdr.tonemap` program. Missing/pending/off/conflicted/failed programs use SDR;
mode changes upload uniforms without a settings edit. Status shows **solar
display palette HDR gold / SDR gold**. This is a labeled Slice AI workaround,
filed in Anomaly's extensibility notes and framework gaps: Anomaly does not yet
publish a structured display encoding, so future HDR display tenants need that
shared contract rather than another hard-coded program id.

Both main/probe Shader Model 5 variants compile against installed game headers.
The 87 D3D11 WARP checks cover luminous contrast, surface/flare motion, frozen
time, corona removal, circular halo, foreground masking, probe omission,
brightness/enable controls, extreme zoom and catalogue/constellation behavior.
The optical profile itself is rendered at three air and three detail values;
monotonic decay and no annular peak in the AA disc/glow composite are checked.
Additional checks exercise glow off, zero strength, width, falloff, atmosphere
expansion and independent tint; glow off also removes the former smooth rim halo.
With `-HdrRoot <HdrRender repository>`, the harness also executes HDR Output's
actual BT.2390 shader at 600/1000/2000 nits and Keen's SDR filmic shoulder at
neutral exposure/grading. It verifies luminance preservation, a golden HDR
ratio, movement towards SDR highlight chromaticity, exact SDR restoration and
world-colour preservation with gold disabled. These are controlled GPU checks,
not a calibrated perceptual match or proof of in-game native glare/bloom/FG.
Luminance preservation also passes with two warm sunlight colours.
The constellation `PatternAlpha` compiler warning remains pre-existing.
Both plugin targets build; 84 config/RHUD/dust/display/glow integration checks
pass. Anomaly's 24 actual registry/Harmony-prefix checks pass on both net48 and
net10.0, including worker-before/after-draw freshness, exact solar identity and
restoration. They seed the successful draw certificate and frame clock rather
than running the game scheduler. See Anomaly's `Tests/CelestialGlare/README.md`.
Builds used disabled deployment.

Export actual shader frames with `Tests/Run-CelestialSmokeTests.ps1
-SolarPreviewDirectory <directory>`. Environment variables
`FINAL_FRONTIER_SOLAR_PREVIEW_SIZE` (33–4096) and
`FINAL_FRONTIER_SOLAR_PREVIEW_FRAMES` (1–240) choose size/count; defaults 513/64.
`FINAL_FRONTIER_SOLAR_PREVIEW_HDR=1` selects HDR gold for exported source frames.
When `-HdrRoot` is supplied with a preview directory, `display-*.f32` additionally
contains the SDR shoulder and actual HDR before/after results; `display.txt`
records dimensions and scRGB units.
`Tools/Solar/RenderDisplayComparison.py <directory>` makes an SDR PNG comparison
from those actual display-shader outputs, using one shared scale for both HDR
panels. This image is for comparing hue, not calibrating HDR display brightness.
`Tools/Solar/RenderPreview.py` tone-maps float32 frames at fixed exposure;
`--name` separates large stills from motion previews. These omit Keen's exposure,
bloom, native flare, atmosphere and temporal reconstruction.

Restart the normal Pulsar source launch with **both updated Anomaly and
FinalFrontier** to load the host handoff, bridge and shader together.
The SDR surface has user acceptance; revised HDR gold and filled-glow acceptance,
GPU timings (including the GTX 1070 Ti target), atmosphere and
DLSS/FSR/frame-generation stability remain unverified. The Cycles reference
does not establish those checks.
