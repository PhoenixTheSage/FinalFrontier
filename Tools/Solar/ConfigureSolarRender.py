"""Select Cycles/OptiX for fine volumetric inspection without Eevee froxels."""
import bpy
if bpy.app.is_job_running('RENDER'): raise RuntimeError('Wait for the active render before changing render settings.')
scene=bpy.data.scenes['Final Frontier - Solar Laboratory']
bpy.context.window.scene=scene
scene.render.engine='CYCLES'
prefs=bpy.context.preferences.addons['cycles'].preferences
prefs.compute_device_type='OPTIX'; prefs.get_devices()
for device in prefs.devices: device.use=device.type=='OPTIX'
scene.cycles.device='GPU'; scene.cycles.samples=48
scene.cycles.use_denoising=False
scene.cycles.volume_step_rate=.5
scene.render.resolution_x=scene.render.resolution_y=4096
scene.render.image_settings.color_depth='16'
scene.camera.data.ortho_scale=2.75
scene['Inspection renderer']='Cycles / OptiX, 48 samples, no denoising. Continuous procedural surface, fine emissive volume.'
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
result={'engine':scene.render.engine,'device':scene.cycles.device,'samples':scene.cycles.samples}
