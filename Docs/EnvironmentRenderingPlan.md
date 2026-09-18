# Final Frontier: procedural celestial environment

Planning baseline, 2026-09-18. Implementation update: shared Anomaly support and the phase-1 prototype now exist; see [IntegrationSpike.md](IntegrationSpike.md) for implemented scope, test evidence and remaining gates. Sections below retain design goals, not claims that every phase is finished.

## Direction

Build FinalFrontier as an Anomaly-dependent client plugin that owns celestial appearance: catalogued stars rendered analytically, an analytic sun disc, optional solar effects, and constellation guides. Preserve the world's sun direction, rotation, shadows, solar-power behavior, weather and atmosphere. No nebula or diffuse Milky Way component in the initial scope.

Use real star coordinates rather than random placement for the recognizable sky. “Procedural” describes how light is drawn, not where stars must be placed. A finite catalogue plus analytic rendering eliminates magnified cubemap texels; it does not promise unlimited floating-point precision or freedom from aliasing at arbitrary FOVs.

## Findings from the local source

Examined the existing FinalFrontier template, current Anomaly checkout, and the prepared game source labelled SE_VERSION=1210014, client build 0. These are source findings, not an in-game compatibility certification. Graphify located the renderer classes; direct source inspection established the shader behavior. The prepared content lacks some included HLSLI files, so their exact ray reconstruction and sun profile still need checking against the installed shader assets during the first spike.

- FinalFrontier still has template initialization, configuration and example patches. It has not registered an Anomaly asset pack. Preserve existing uncommitted setup changes when implementation begins.
- `Content/Shaders/Lighting/LightDir.hlsl:49–53` samples the sky cubemap and adds `GetSunColor` in its background-only branch. Foreground lighting is in the other branch. The disc is already shader-calculated; the cubemap and textured flare are distinct replacement targets.
- `MyLightsRendering.RenderDirectionalEnvironmentLight` binds the visible cubemap at t10 and environment probes at t11/t17. Replacing the background must leave foreground lighting and these reflection inputs intact.
- `MySector.InitSunGlare` creates a separate distant flare using the Sun definition; `UpdateSunLight` positions it along the game sun direction. Do not disable the global flare renderer to replace this one effect.
- `EnvProbe/ForwardPostprocess.hlsl` fills probe background from `SkyboxColorReflected`. `MyEnvProbeProcessing.RunForwardPostprocess` selects the indirect sky cubemap when present. Replacing only the main-view background leaves old sky content in reflections. The probe shader's explicit sun-disc addition is commented out; do not blindly add a bright sun to probes and double-count specular energy.
- `MyTransparentRendering.Render` invokes atmosphere rendering. Anomaly's `AfterLighting` prefix is before that method; an early background pass is possible. A late `AfterAtmosphere` replacement would overwrite already-composited atmosphere.
- Anomaly exposes `Lighting.Dir` and `EnvProbe` named shader stages. Existing lighting injection wraps shared lighting, but there is no demonstrated narrow background-provider contract. `ShaderBindRegistry` binds the Lighting family; named EnvProbe compile support alone does not establish a probe-time resource-binding contract.
- Anomaly exposes `RequestFolderPage` for settings. `HudOverlayRegistry` currently supplies stacked corner text, not projected star labels or world lines. Packs must not vendor another Rich HUD client.
- The game carries a distinct sky projection. The inspected `MyCamera.UpdateCamera` sends the same zoom FOV for scene and sky, but other camera paths and mods need testing. Use one explicit celestial projection policy for stars, disc, highlights and lines.
- Anomaly's camera velocity fallback reconstructs depth-zero background through inverse projection. Its correctness for infinity, translations and changing FOV is a validation item, not a confirmed bug.

## Rendering architecture

Keep game integration, GPU lifetime, pass ordering and shared contracts in Anomaly. Keep catalogue data, celestial shader logic, appearance settings and constellation definitions in FinalFrontier.

Preferred final contract: an exclusive celestial-background provider invoked within the directional-light background branch, plus a matching environment-probe background evaluation. Exact API names remain to be designed. It should receive a normalized world ray, pixel angular footprint, view kind, sky orientation and supported sun/brightness inputs, and return linear HDR radiance before existing fog and atmosphere composition. Missing resources or compilation failure must restore the original sky and sun together.

A small `Lighting.Dir` overlay that preserves the foreground branch can prove the visual path first. It must declare conflicts through Anomaly, preserve shadow/MSAA permutations, and remain a bounded prototype rather than a maintained fork of all lighting. Do not claim the whole Lighting family unnecessarily. An `AfterLighting` depth-masked pass is an alternative spike, but must preserve sky fog and avoid duplicate disc energy; it is not automatically equivalent to replacing the original background branch.

### Stars

Start with a magnitude-limited subset of ESA Hipparcos, retaining HIP identifiers, right ascension, declination, magnitude, available color information and selected names. Record the catalogue version, coordinate frame and epoch explicitly; Hipparcos source coordinates are not automatically J2000-epoch positions. Bake one consistent frame offline, with documented attribution and redistribution terms. Ship processed data locally; no runtime catalogue downloads.

Convert positions to unit directions and apply a configurable celestial-to-world quaternion. Default to a fixed Earth-view sky with no translation parallax. Do not rotate the whole sky merely because SE rotates its sun. A future astronomical simulation would require a separate model and is outside this scope.

Recommended initial GPU strategy: partition directions into spherical cells, upload star records and cell ranges as data resources, and evaluate only nearby candidates in the background shader. Account for pixel footprint and star support across cell boundaries; never silently truncate crowded cells. This avoids an all-stars-per-pixel loop and works for probe views too. Benchmark against instanced analytic star quads if candidate cost becomes excessive; do not build both renderers up front.

Render each star with an analytic, pixel-integrated point-spread profile. Magnitude controls relative flux; color data controls a restrained tint. Make exposure response and artistic visibility gain explicit. Ordinary stars remain unresolved points under normal game zoom rather than becoming arbitrary large glowing spheres. Filter subpixel energy, cap expensive work predictably, and keep positions deterministic across resolution/FOV changes. Prefer tangent-plane distance or squared vector separation to fragile `acos(dot)` calculations at tiny angles.

Do not add random faint stars initially: the real catalogue already provides a substantial field. A later synthetic extension must be optional and distinguished from catalogue objects. Omitting diffuse Milky Way artwork does not make the real distribution of stars uniform.

### Sun

Use the game's current sun direction as the single authority. Separate angular radius, disc radiance, limb darkening, surface detail, corona and lens glare. Visual size must not silently change directional light intensity or shadow softness.

The first disc should have an analytic antialiased edge and smooth limb darkening. Add band-limited granulation and optional slow surface activity only after zoom behavior is stable. Anchor noise in a stable solar coordinate frame, with consistent tangent axes as the sun moves. Avoid screen-space noise that swims and unfiltered high-frequency noise that sparkles.

Let existing bloom respond to HDR disc radiance before adding a bespoke lens flare. Corona is a spatial celestial effect; lens glare is a camera effect and needs its own visibility treatment. Keep the vanilla flare during the first disc spike, then introduce sun-specific ownership so it is suppressed only while the replacement is healthy. Partial occlusion needs sampling over the disc rather than a single center-ray visibility flag. Terrain, grids, glass and cloud transmittance all require separate validation.

### Reflections, atmosphere and temporal rendering

Evaluate the same star field for environment-probe backgrounds and allow the existing probe convolution/mip chain to filter it. Probe cubemaps remain useful lighting caches; their resolution does not bound the directly viewed sky. Decide reflected solar-disc energy explicitly after measuring the existing directional specular contribution.

Preserve sky fog and normal atmosphere/transparency ordering first. Verify daytime suppression, sunset attenuation and night visibility instead of assuming the current atmosphere provides physically complete stellar extinction. Any missing reusable transmittance input belongs in Anomaly.

Celestial motion is rotation/projection motion at infinity, not finite-distance translation. Validate sky velocity, jitter alignment, FOV changes, history reset and tiny bright-star retention with native rendering, DLSS, FSR and frame generation. Do not mark the entire sky reactive as a default workaround. Constellation/UI toggles should invalidate only the necessary history or use a dedicated overlay treatment.

## Constellations and settings

Store constellation figures as edges between stable catalogue IDs. Include curated common names and membership separately. The IAU defines 88 constellation regions, but does not prescribe official stick figures; select or author a line convention and document its license. Prototype Orion and Ursa Major before filling all 88 figures.

Use Rich HUD through Anomaly for **Anomaly Shaders → Final Frontier** pages:

- Sky: enable, star visibility/brightness, color strength, orientation and quality.
- Sun: enable, angular size, radiance, limb darkening, detail and optional corona/glare.
- Constellations: off/selected/all, constellation selector, star highlights, connecting lines, optional names, opacity and line thickness.
- Diagnostics: active provider, loaded catalogue count/version, shader status and fallback reason.

Render highlights and lines using the same directions and projection as the stars, with great-circle segments, stable pixel widths and explicit foreground occlusion. Default guides off. Exclude guides from reflection probes and ambient lighting. Labels are a later projected-HUD capability; Anomaly's corner text API is insufficient. Define whether an optional HUD mode may show through geometry rather than doing so accidentally. Clip near-plane crossings and suppress overlapping labels.

Settings apply live through render-thread snapshots. Cache serialization, mark configuration dirty and save after roughly 400 ms of quiet on a worker, following Anomaly's existing contract. Do not serialize inside HUD setters or attach per-tick host getters. Provide a basic Pulsar settings fallback when Rich HUD Master is unavailable.

## Implementation sequence and exit criteria

1. **Integration spike.** Register Pulsar named assets and Anomaly dependency; remove template example behavior deliberately. Replace only background with a few fixed analytic stars and a disc. Verify depth silhouettes, zoom, fog, atmosphere ordering, toggle/failure restoration, and foreground lighting parity. No large catalogue yet.
2. **Catalogue and filtering.** Build reproducible offline conversion, catalogue IDs and spherical index. Implement pixel-aware stars and orientation. Verify Orion/Ursa Major, seam-free rotation, stable magnitude ordering and no translation parallax. Profile GPU time before choosing a final magnitude cutoff or promising a budget.
3. **Shared environment integration.** Add the narrow Anomaly contract if the spike confirms it; support runtime uniforms/data bindings and probe views. Verify reflective surfaces, roughness filtering, weather, multiple planets, resource reload/device reset and safe rollback. Confirm sky motion with upscalers early, not after polish.
4. **Sun finish.** Add limb darkening/detail, then corona and optional glare. Establish sun-specific flare suppression and visibility. Test partial grid/planet occlusion, sun at screen edges, sunsets and exposure transitions. Keep gameplay sunlight unchanged.
5. **Constellation guide.** Settings, highlights and lines first; projected labels second. Validate screen-space alignment at narrow FOV, geometry occlusion and exclusion from probes. Expand the curated dataset to the desired coverage.
6. **Compatibility and release.** Exercise native AA, DLSS, FSR, FG, HDR/tonemapping, Anomaly atmosphere/cloud/aurora/SSGI packs, window/aspect/resolution changes, cockpit/third-person/block cameras, world reload, extreme world coordinates and custom skybox worlds. Measure CPU/GPU cost and memory on target hardware. Verify both supported Pulsar runtimes and asset packaging.

No build or game launch is required for this planning change. Visual quality, performance budgets and exact provider API remain unvalidated until the spike.

## Proposed source layout

`ClientPlugin/Integration` for Anomaly discovery and registration; `ClientPlugin/Celestial` for catalogue metadata and orientation; `ClientPlugin/Configuration` for settings snapshots/persistence; `Assets/FinalFrontier` for the pack manifest, shaders and processed data; `Tools/Catalogue` for reproducible conversion; `Docs` for provenance, design and validation records. Resolve Anomaly services by their supported well-known type names, following existing pack conventions.

## Shared framework work to track

Proposed, not shipped: narrow main/probe background ownership and rollback; stage-safe catalogue resource and live-parameter binding; view-aware sky-ray/footprint and infinity-motion validation; sun-only glare ownership; optional projected HUD labels. Existing fullscreen uniforms and corner overlays must not be presented as already solving these different stage/UI requirements. Record these in Anomaly's Slice AI gap ledger, framework wiki and existing wiki canvas before implementation.

## External data references

- ESA Hipparcos catalogue overview: https://www.cosmos.esa.int/web/Hipparcos
- ESA catalogue access: https://www.cosmos.esa.int/web/hipparcos/catalogues
- ESA coordinate/epoch notes: https://www.cosmos.esa.int/web/esdc/esasky-catalogues
- ESA common names and HIP IDs: https://www.cosmos.esa.int/web/hipparcos/common-star-names
- IAU constellation regions and figure conventions: https://iauarchive.eso.org/public/themes/constellations/
