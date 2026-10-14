# Star Dust

The **Anomaly Shaders → Final Frontier → Star Dust** page controls Keen's movement
dust. `Vanilla` is the default for new and existing configs. Select `Enhanced` to
use the custom controls, or `Off` to suppress movement dust. These modes are
independent of Final Frontier's celestial-background toggle.

Restart the normal Pulsar source launch to load this code. Mode and settings
changes then apply live. Appearance acceptance is pending; the headless checks
do not verify the picture in the game.

## What Keen actually renders

The prepared client source records `SE_VERSION=1210014` (1.210.014). The relevant
source path is
`SpaceEngineers.Game/SpaceEngineers/Game/World/Environment/MyEnvironmentalParticleLogicSpace.cs`.

- `UpdateBeforeSimulation` obtains the controlled character or the topmost
  cockpit/remote-control grid and reads its physical velocity. Spectator cameras
  are excluded. The native effect starts at 10 m/s, suppresses particles when
  the camera overlaps a large-grid bounding box, and rejects natural gravity
  unless `MyFakes.ENABLE_STARDUST_ON_PLANET` is set.
- Spawn locations are CPU-generated ahead of the camera. Particles remain at
  fixed world positions; the camera moving past them supplies the motion.
- `Draw` sends line billboards opposite velocity. Space streaks are 0.025 m
  thick and `clamp(speed / 50, 0, 1)` metres long. Planetary drawing, when
  enabled, multiplies width by 1.5 and length by 3 and uses planetary colour.
- `Sandbox.Game/Sandbox/Game/World/MyEnvironmentalParticleLogic.cs` owns the reusable active
  and inactive particle lists and performs native expiry/distance cleanup.
  `Sandbox.Game/Sandbox/Game/SessionComponents/MyEnvironmentalParticles.cs` constructs these
  handlers from the world's environmental particle settings.
- `Content/Data/Environment.sbc` specifies `Stardust`, a 10 m spawn radius,
  15 m despawn radius, density 0.0000235, lifetime 1500 ms and a 128-particle pool.
  Worlds can override these definitions. `TransparentMaterials.sbc` supplies
  `Textures/Particles/Stardust.dds`, depth testing and native alpha misting.

There is no separate star-dust movement shader. The material enters Keen's
`VRage.Game/MyTransparentGeometry.AddLineBillboard` API and
`VRageRender.MyBillboardRenderer.RenderLDR`. The shared
`Content/Shaders/Transparent/Billboards.hlsl` samples the texture, applies
billboard colour and, for applicable shader variants, soft-particle depth fading.
The LDR pass uses premultiplied alpha blending and the scene depth stencil.

## Enhanced implementation

`ClientPlugin/StarDust/StarDustRuntime.cs` installs three owner-scoped Harmony
prefixes on **only** the space handler's before-update, after-update and draw
methods. Vanilla mode delegates to the original methods. Enhanced mode replaces
that producer and cleanup; Off suppresses it. Other environmental particles
continue to run their original methods.

A draw postfix observes the number of queued billboards in both Vanilla and
Enhanced. RHUD and Show Status report **active particles**, **billboards submitted
last draw** and **draw calls** separately. Active particles prove spawning;
submitted billboards prove queuing, not successful GPU visibility. Counts are
last-draw observations rather than render-frame telemetry.

Enhanced reuses the original bounded pool, each particle's space/planet material,
and its world-defined base colour. It retains Keen's LDR billboard submission.
It does not patch shared shader compilation, edit game content, change the global
planet-dust flag, create a new render pass or require an Anomaly rendering API.
Anomaly and Rich HUD supply the optional folder page.

### Invisible dust follow-up (2026-10-02)

The user reports both modes invisible while Enhanced reports 80 active particles
at 100 m/s. A concrete shared rendering defect was found: DLSS/FSR output-depth
preparation clears the context before the LDR billboard draw. Keen's
`BindResourcesCommon` restores vertex frame constants/samplers but inherits their
pixel counterparts. With missing PS frame b0, the native SOFT_PARTICLE shader's
depth fade erases the particles. Fullscreen/upscaler state can also leave the
atlas sampler missing or changed. The earlier AfterAtmosphere restoration runs
before this later context clear and cannot cover it.

Anomaly now restores frame b0, standard pixel samplers and the shadow sampler
at the shared `BindResourcesCommon` consumer boundary, including calls inside
DLSS/FSR's replacement LDR path. This preserves native draw order, materials,
foreground depth and Vanilla delegation. Both updated Anomaly and FinalFrontier
must load through the normal Pulsar source launch. No density/brightness defaults
or renderer-wide enable flags are changed to hide the defect.

Anomaly's `Tests/TransparentBindings/Run.ps1` compiles the installed Keen
`Transparent/Billboards.hlsl` SOFT_PARTICLE pixel shader. A synthetic atlas,
billboard record and vertex shader reproduce invisible output with missing
bindings and recover exact healthy pixels after the production restoration;
soft foreground clipping still works. There are 43 assertions per runtime,
including the original binding probes and actual Harmony hook installation.
These exercise immediate/deferred D3D11 WARP contexts, not the full game or
upscaler implementation. The exact live scene still needs acceptance.

The new producer adds:

- A uniform front-hemisphere distribution with a 6-degree central exclusion.
- Double-precision world positions held separately from Keen's float particle
  position. Final billboard geometry receives those double positions.
- Spawn-rate integration in simulation seconds, calibrated to Keen's nominal
  60 Hz average and the **world's actual density**. It scales with the density
  control and radius squared. The native 8.5 m/s spawn-rate subtraction tracks
  85% of the chosen minimum speed, so thresholds below 10 m/s also work.
- Smooth speed visibility and density ramps, adjustable reference speed and
  speed stretch, plus a hard streak-length cap after random size variation.
- Stable per-particle size variation, RGB tint and brightness, opacity,
  lifetime fade-in/out and a fade near the outer despawn boundary.
- Immediate pool cleanup on mode changes, expiry, distance culling, live budget
  reductions and enhanced teardown. Pool saturation discards emission debt to
  avoid a later catch-up burst. Handler state is weakly held across worlds.

The enhanced budget is 8–128 and cannot exceed the world's preallocated pool.
No particle pool is enlarged. Despawn distance is 1.5 times the spawn radius;
the enhanced lifetime control replaces the native particle lifetime. Brightness
is an LDR colour multiplier, not physical lighting or an HDR emissive value.

Subtle, Cinematic and High Speed presets adjust density, range, lifetime, speed
response and appearance. Presets retain Mode and both location gates. Manual
changes customize the selected preset; **Reapply preset** restores its values.
**Reset dust settings** selects Vanilla and resets the enhanced look to Subtle,
with near-planet dust disabled and large-grid suppression enabled.

Settings use `ConfigStorage.Save` through the existing property-change handler.
It marks dirty; the existing 400 ms quiet period and worker write handle disk
persistence. The RHUD bridge refreshes at most once per second and supplies
explicit fractional steps for every float slider. It binds through Anomaly's
page interface and never assigns Rich HUD `CustomValueGetter`.

Missing types/fields/methods or a runtime exception produce a logged
`Vanilla fallback` status. Partial installation is removed. A runtime fault
delegates subsequent calls to Keen until the next plugin initialization; even
Off cannot suppress dust after such a fault. This is visible in the page and
Final Frontier's Status dialog. A world with no space-dust handler cannot be
given dust by these hooks; the enhanced status remains waiting for the handler.

## Validation

Run `Tests/Run-StarDustSmokeTests.ps1`. It builds both plugin targets without
deploying, then uses a .NET 10 headless host with the installed game assemblies
to validate the published page construction and install/remove the actual
Harmony hooks. It checks config bounds, old XML compatibility, XML round trips,
preset gates, slider steps, speed/lifetime/length behaviour, emission scaling,
double-position culling, pool cleanup and mode/fallback delegation. Synthetic
pooled particles avoid starting a game or depending on its clock. It does not
execute the full spawn/draw loop with a live controlled entity or a GPU.
The expanded suite passes 84 checks, including draw observer installation and
headless diagnostic behavior. Rendering validation is the separate Anomaly suite
above. Builds used disabled deployment.

Before changing the default from Vanilla, verify in game:

1. Load the page, select Enhanced and try all three presets while flying in
   open space. Check the threshold, speed fade, colour, opacity and length cap.
2. Lower the threshold below 10 m/s. Test a character, cockpit and remote control;
   spectator views should stay clear. At rest, dust should disappear.
3. Toggle Enhanced → Off → Vanilla while moving and while paused. Watch for
   retained particles or bursts. Change the budget/radius/lifetime while flying.
4. Test the large-grid gate in a cockpit and a room, then near a planet with the
   planet gate both off and on. The native bounds test is intentionally coarse.
5. Check streaks behind opaque objects and near the camera. Repeat at distant
   world coordinates and after teleporting or changing worlds.
6. Drag several sliders and restart to verify persistence. Test without Rich HUD;
   the Pulsar dialog still exposes Mode and retains the saved enhanced settings.
7. Compare native rendering, supported DLSS/FSR configurations and frame
   generation. Check temporal ghosts, depth edges, motion appearance and CPU/GPU
   cost. No upscaler/frame-generation visual acceptance is claimed yet.

A future unique dust texture, GPU particle simulation, HDR lighting or separate
temporal treatment would require a distinct material/rendering design. That
shared rendering capability belongs in Anomaly rather than a pack patch to
the shared billboard shader.
