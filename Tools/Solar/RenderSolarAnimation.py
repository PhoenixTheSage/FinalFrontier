"""Start a lossless 1536px motion sequence via Blender MCP, keeping UI responsive.

Odd frames are 1/12 second apart in simulation. Encode at 12fps.
The sequence runs at the authored speed, without time remapping.
"""
import bpy
import json
from pathlib import Path
if bpy.app.is_job_running('RENDER'): raise RuntimeError('A render is already running.')

scene=bpy.data.scenes['Final Frontier - Solar Laboratory']
bpy.context.window.scene=scene
scene.render.resolution_x=scene.render.resolution_y=1536
scene.cycles.samples=24
scene.camera.location=(0,0,5); scene.camera.data.ortho_scale=2.75
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_depth='16'
count=64
scene.frame_start,scene.frame_end,scene.frame_step=1,count*2-1,2
directory=Path(bpy.data.filepath).parent/'MotionFinal'; directory.mkdir(exist_ok=True)
if list(directory.glob('solar-*.png')): raise RuntimeError('MotionFinal already contains frames; choose a new export directory.')
(directory/'export.json').write_text(json.dumps({'frames':count,'playback_fps':12,'first':1,'step':2}),encoding='utf-8')
scene.render.filepath=str(directory/'solar-')
scene.frame_set(1)
bpy.ops.render.render('INVOKE_DEFAULT',animation=True)
result={'started':True,'directory':str(directory),'frames':count,'size':1536,'playback_fps':12}
