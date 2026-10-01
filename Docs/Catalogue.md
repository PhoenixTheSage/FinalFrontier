# Catalogue implementation and validation

Phase 2 replaces diagnostic markers with 41,394 Hipparcos entries through
Johnson V magnitude 8. Default visibility is magnitude 6.5. Brightness, limiting
magnitude and approximate B-V colour strength update live without rebuilding
GPU data. Sun rendering and gameplay rotation are unchanged.

The checked-in `Tools/Catalogue/Hipparcos.tsv` is a normalized snapshot of the
official CDS VizieR query recorded in `Assets/Celestial/Hipparcos.json`. Query
comments and whitespace were removed; rows were sorted by HIP ID. Of 41,411
query rows, 17 have no position and are excluded. The source hash is checked
before conversion. Run `python Tools/Catalogue/build_catalogue.py` offline to
reproduce the binary, HIP-ID mapping and manifest. No runtime downloads occur.
The data and source snapshot carry the separate ESA CC BY-NC 3.0 IGO terms in
`Assets/Celestial/DATA-LICENSE.md`; the code license does not replace them.

Coordinates are fixed ICRS at epoch J1991.25, mapped to +X at RA 0, +Y celestial
north, +Z at RA 90. There is no proper-motion or distance/parallax simulation.
The catalogue supplies Earth constellation geometry. Stick figures follow the
IAU / Alan MacRobert line patterns (same traditional Western figures used on
public IAU charts and educational atlases such as go-astronomy), baked from
`constellation_lines_iau.dat` via `python Tools/Catalogue/import_iau_figures.py`
then `python Tools/Catalogue/build_constellations.py`. HIP endpoints absent from
the V≤8 catalogue snapshot drop that edge only. Main-view guides draw highlight
rings and connecting lines with a soft per-segment pulse; fantasy mode brightens
member stars with a softer glow. Optional look-at name headers (semi-transparent
bitmap glyphs) and
colored fantasy art (`Assets/Celestial/Art`, RGBA8 slices via Anomaly
`SetArt`) fade in when the view aims at a figure and out when looking away.
Lines, fantasy, names and art all default on and are excluded from probes.
The colour conversion is an artistic tint, not calibrated RGB.

Binary format: little-endian `FFSC`, uint32 version 1, uint32 float4-record count,
then float4 records. Record 0 is (32, star count, 2, 1). The next 32 cubed records
are (absolute start record, star count, 0, 0), ordered x+32*(y+32*z). Empty cells
use zero/zero. Each star has direction/relative flux then linear RGB/magnitude.
Positions are quantized to float32 before assigning cells. Loader validation
checks sizes, contiguous ranges, finite values, unit directions and membership.
The complete buffer is about 1.77 MiB, below Anomaly's 16 MiB cap.

The shader searches all intersecting cells within a conservative PSF support
box, including neighbours and rounding tolerance; there is no per-cell star
truncation or RA/pole seam. It integrates a triangular profile over each pixel
using Anomaly's pre-discard direction derivatives. Magnitude controls relative
flux. The display normalization is artistic; high-zoom stars stay unresolved.

Validation: D3D11 WARP checks execute both real shader variants, including
subpixel flux conservation across a cell boundary, magnitude filtering,
projection of the brightest real star, constellation lines/fantasy, and
look-at name/art fade. The independent Python suite checks
Sirius coordinates, Orion separation and spatial-query completeness against
brute-force distances. Run `python Tools/Catalogue/test_catalogue.py` and
`pwsh -File Tests/Run-CelestialSmokeTests.ps1`.

Next in-game pass: brightness/density at native resolution, slow camera pans,
extreme zoom, constellation recognition, frame cost, refreshed reflections,
and temporal/upscaler behavior. Software checks do not establish game GPU cost.

## Orange sun band: open investigation

The user validated replacement, live star brightness, high zoom, sun size and
Keen day/night tracking on 2026-09-18. An erratic orange ring changes apparent
extent with exposure. The analytic disc contributes zero outside its narrow
antialiasing edge. Keen's retained Sun glare includes warm SunBigGlow and
SunBigRays textures; these or downstream bloom/exposure are plausible sources,
not yet isolated. Sun-only glare ownership remains phase 4. No global flare
switch or speculative bloom changes are included in the catalogue update.

## FSR motion brightness and orange-ring investigation (2026-09-18)

The new screenshot shows a sharply stepped, irregular warm ring around the
retained glare. The user reports stars brighten during camera movement and
settle after stopping, with FSR active. The live profile also has HDR output
and star brightness 5 enabled. These observations do not identify one proven
root cause. FSR's accumulation code changes history contribution with velocity;
the star shader has no velocity-dependent intensity. Motion-vector reconstruction
has not been changed speculatively.

A live **Star profile width** control now spans 0.85 (the original profile)
to 2.5 render pixels, default 1.5. Its normalized pixel integral preserves
flux; the spatial search expands with support. This is a temporal-stability
mitigation candidate, not a confirmed FSR fix. Twenty WARP checks pass,
including subpixel flux at all three widths. They do not execute FSR history.

**Star core sharpness** (default 3) raises the same flux-conserving CDF to a
power: 1 recovers the original triangle; higher values concentrate energy into
a pin-prick core with a rapid soft falloff. **Star twinkle strength** (0–2,
default 0.6) and **Star twinkle speed** (0–3, default 1) are independent:
strength sets linear-HDR modulation depth, speed scales the per-star tempo.
Probes stay static so reflections do not shimmer. Bright catalogue entries are
only mildly damped so depth remains visible under HDR bloom.

**Native sun glare** defaults on for comparison. Turn it off to remove only
Keen's solar glare while retaining the procedural disc and other lights.
Anomaly captures the exact solar light ID, gates suppression on a successful
main replacement in the same frame, and restores vanilla on disabled/failed/
conflicting provider or device release. No saved world definition is changed.
This brings the isolation part of phase 4 forward; replacement corona/rays and
partial-disc visibility remain deferred.

Next game checks, using updated Anomaly and FinalFrontier:
1. Toggle Native sun glare off/on with the ring visible. If it disappears only
   when off, the artifact involves the retained glare or its downstream processing.
2. Compare slow pan and rest at profile widths 0.85, 1.5 and 2.5, holding brightness
   and exposure settings fixed. Larger profiles distribute light over more pixels.
3. If brightening remains, repeat with native AA and frame generation disabled;
   that separates FSR history, FG and scene/exposure contributions.

### Follow-up: user isolated both artifacts to Frame Generation

Turning FG off removes both the orange ring and motion brightening. The earlier
FSR attribution was a hypothesis and is superseded. SE-FG now fixes demonstrated
linear-light quantization in its SDR interpolation UAV (RGBA16F replaces 8-bit
UNORM), with a failing-before/passing-after production-pipeline WARP regression.
In-game confirmation of both symptoms is pending. Profile width and native-glare
controls remain available, but are not the root-cause fix; do not disable the
sun glare as a required workaround. See SE-FG README for evidence and tests.
