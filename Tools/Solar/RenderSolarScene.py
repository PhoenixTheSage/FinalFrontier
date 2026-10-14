"""Execute with Blender MCP's CLI tool against SolarLaboratory.blend."""
import bpy
import time
from pathlib import Path
if bpy.app.is_job_running('RENDER'): raise RuntimeError('A render is already running.')

scene=bpy.data.scenes['Final Frontier - Solar Laboratory']
bpy.context.window.scene=scene
scene.frame_set(globals().get('SOLAR_FRAME',49))
scene.render.resolution_x=scene.render.resolution_y=globals().get('SOLAR_RENDER_SIZE',1024)
scene.render.filepath=str(Path(bpy.data.filepath).parent/globals().get('SOLAR_RENDER_NAME','SolarBlender-preview.png'))
if bpy.app.background:
    bpy.ops.render.render(write_still=True)
    result={'render':scene.render.filepath,'frame':scene.frame_current,'resolution':scene.render.resolution_x}
else:
    started=time.time()
    bpy.ops.render.render('INVOKE_DEFAULT',write_still=True)
    def check_is_finished():
        if bpy.app.is_job_running('RENDER'): return None
        path=Path(scene.render.filepath)
        if not path.exists() or path.stat().st_mtime < started:
            raise RuntimeError('Render did not produce a new image.')
        return {'render':str(path),'exists':True}
