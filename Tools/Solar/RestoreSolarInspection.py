"""Restore the saved authoring scene after exporting motion/close-up renders."""
import bpy
from pathlib import Path
if bpy.app.is_job_running('RENDER'): raise RuntimeError('Wait for the render to finish.')
scene=bpy.data.scenes['Final Frontier - Solar Laboratory']; bpy.context.window.scene=scene
scene.frame_start,scene.frame_end,scene.frame_step=1,240,1
scene.render.resolution_x=scene.render.resolution_y=4096
scene.cycles.samples=48
scene.camera.location=(0,0,5); scene.camera.data.ortho_scale=2.75
scene.frame_set(49)
render=Path(bpy.data.filepath).parent/'SolarBlender-4K.png'
scene.render.filepath=str(render)
workspace=bpy.data.workspaces.get('Rendering')
if workspace:
    bpy.context.window.workspace=workspace
    if render.exists():
        image=bpy.data.images.load(str(render),check_existing=True)
        for area in bpy.context.screen.areas:
            if area.type=='IMAGE_EDITOR': area.spaces.active.image=image
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
result={'saved':bpy.data.filepath,'frame':49,'resolution':4096,'native_animation_frames':240}
