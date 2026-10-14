# FinalFrontier changelog

## 1.2.1 — 2026-10-14

- Register a dedicated renderer-neutral Status page independently of celestial, map, and atmosphere initialization.
- Consolidate map, star-dust, atmosphere, and celestial diagnostics outside settings pages; include renderer and status-registration health in the report.

## 1.2.0 — previous release

Release 1.2.0 map, star dust, and solar plasma

Version: 1.1.0 -> 1.2.0 (minor).

Changelog:
- Add a client-side Rich HUD map on Ctrl+Shift+F2, with orbit, pan, zoom, selection, and a settings page. The binding is editable and the map does not require the celestial background to be enabled.
- Add Star Dust modes Vanilla, Enhanced, and Off for Keen movement dust. Those modes are independent of the celestial-background toggle.
- Rebuild the sun as animated solar plasma: convection, granulation, and a field-trail ensemble. The real-time HLSL stays separate from the Blender study.
- Read the Anomaly scene through the guest route so the celestial pack can run when Vulkan is the renderer.

Co-authored-by: Cursor <cursoragent@cursor.com>
