# RHUD 3D map

Press **Ctrl+Shift+F2** in gameplay to open Final Frontier's client-side map.
The binding is editable in Pulsar's Final Frontier settings (**Map toggle**);
right-click its key button to clear it. RHUD's **Anomaly Shaders → Final Frontier
→ Map** page contains live status, range, wireframe and RGBA palette controls.
The map works independently of the celestial background's Enabled setting.
Load updated Final Frontier and Anomaly together through the normal Pulsar
source launch, with Rich HUD Master in the world.

## Interaction

The centered window starts at 80% of the screen, adjusted for RHUD high DPI.
Drag its header to move it or its edges to resize it. Wheel over the map zooms,
right-drag orbits, middle-drag pans and left-click selects a proxy. The right
panel scrolls independently and virtualizes its object rows. Center on player,
Focus selection and Reset view are available above the filters.

Escape, the toggle chord and Close dismiss the map. A non-rendering GUI focus
screen and RHUD's temporary blacklist capture gameplay input while it is open;
opening the map does not pause the world. Chat, loading, another GUI modal or
the RHUD terminal block the opening chord. The Open map settings button closes
the known config/terminal UI and waits up to ten seconds for gameplay focus.
Dependency loss, world unload and Master reset close the map and release capture.

## Visibility

Ordinary mode uses the player's position, independently of map camera panning.
For grids, asteroids and generic voxels, the range fraction defaults to 1 and is
capped at the session view distance.
Distance is measured to the nearest oriented bound, or a planet's maximum-radius
sphere. A planet's center may be far away while its nearby surface is in range.
All client-known planets and moons are included regardless of range or admin
permissions, subject to the Planets / moons filter. Use the object list and Focus
selection to inspect distant planets outside the current camera view. This is
entirely client-side; it does not discover planets the server has not supplied.

Grids owned by the player's identity or with a major owner in the same faction
show their names and bounds. Other nearby replicated grids show **Unknown grid
contact**, type, distance and bounds. Their name, owner and faction identifiers
are excluded before the snapshot reaches rendering, rows or selection details.
Faction and grid ownership changes invalidate the snapshot; identity and admin
permission are checked every update. There is no additional line-of-sight scan.

**Admin reveal** is available only to Admin/Owner users, requires its explicit
checkbox, defaults off, and is never serialized. It bypasses range and name
restrictions, retaining the type filters. Losing admin permission revokes it on
the next game update. Its status reads **All client-known objects**: this client
plugin cannot fetch distant unreplicated server entities. No server companion is
included.

Planets/moons use cached icosahedra; asteroids use oriented diamond wireframes
(six-tip octahedra, with optional translucent faces). Grids and generic voxel
bodies retain oriented bounding boxes. Non-boulder MyVoxelMap roots are classified
as asteroids; boulders and other voxel roots retain the generic representation.
Asteroids share the voxel RGBA palette and Asteroids / voxels filter.
Voxel roots deduplicate planet/asteroid chunks. Preview
and closed entities, characters, debris and particles are excluded. Decorative
catalogue stars, the directional sun, navigation/GPS actions and exact hulls are
outside this version.

## Rendering and persistence

Entity add/remove events maintain the registry after a single session seed.
While open, game-thread sampling is capped at 0.75 ms and 1,024 visits per update,
targeting 5 Hz for moving grids and 1 Hz for static bodies. Under heavy load these
are targets, not freshness guarantees. Only sanitized value snapshots reach the
CPU projector; HUD draw callbacks never enumerate or read game entities.

Projection subtracts the map origin in double precision, clips near-plane and
viewport crossings, sorts translucent faces by depth, and reuses geometry buffers.
Budgets are 4,096 lines, 1,024 face triangles and 128 labels. Selected and nearby
objects receive detail first; small/distant proxies become markers, crowded
markers carry aggregate counts, and overflow carries a visible aggregate. The
full eligible object list remains available regardless of drawing LOD.

Line/face RGB and alpha are independent for own grids, faction grids, contacts,
planets and voxels. Wireframe only hides faces. Settings use the existing cached
serializer and deferred save worker; callbacks never write files. Map properties
do not dirty the celestial shader uniform bridge. Camera position, selection and
admin reveal are session state. Closing clears published data before reopening.

The reusable window, widgets, clipping, billboard submission and input capture
live in Anomaly. Final Frontier binds its v1 interface once by reflection, using
only primitive CLR arrays and delegates. It does not vendor RHUD or patch the
game renderer. See Anomaly's `Docs/InteractiveOverlays.md` for the pack contract.

## Validation

Run `Tests/Run-MapTests.ps1`. It builds both plugin runtimes without deployment,
uses a unique temporary config directory, and runs policy, redaction, range,
large-coordinate projection, clipping, picking, budgets, voxel-index lifecycle,
config XML and reflection-contract checks. The .NET 10 run additionally exercises
arrays/delegates through an isolated assembly load context.

Measurements on October 2, 2026: a warmed 5,000-object snapshot, 200 camera-changing
rebuilds, 1,200×800 viewport, CPU projection only. The initial optimized p95 was
1.650 ms on net48 and 0.693 ms on net10.0. The final build measured 2.836 ms and
0.699 ms respectively; other runs measured 1.677–1.751 ms on net48. The observed
variation means the 2 ms target is not yet accepted. These are development-machine
results, not GTX 1070 Ti hardware acceptance or full game-thread/GPU timings.

After the asteroid/planet update on October 3, 2026, both target builds and
81/82 map checks passed. The same synthetic benchmark measured p95 3.651 ms on
net48 and 2.455 ms on net10.0 in the final run; hardware acceptance remains pending.

Pending in-game acceptance:

- Single-player and multiplayer: own, faction, unknown and admin contacts;
  ownership/faction changes, permission revoke, creation/removal and world reload.
- Verify asteroid diamonds versus generic voxel boxes, and distant planets with
  admin reveal off and a reduced map range. Toggle the planet filter and focus
  a distant planet from the object list.
- Toggle rebinding/unbinding, raw Escape without opening the pause menu, no
  movement/building/camera input while captured, resize, side-panel scrolling,
  selection and focus of distant admin-visible objects.
- RHUD absent/reset and dependency loss; verify that the input guard is removed.
- 1080p/high DPI, SDR/HDR, native, DLSS/FSR and frame generation; verify UI colors,
  translucent faces, clipping and label layering.
- Populated-world total added p95 CPU below 2 ms and GPU below 3 ms at 1080p on
  GTX 1070 Ti. Record entity sampling, snapshot/redaction, projection, RHUD
  submission and GPU time separately. No GPU/hardware pass is claimed yet.

For bugs, include Pulsar runtime, game version, RHUD availability, a screenshot,
map status, relevant logs and whether admin reveal was enabled.
