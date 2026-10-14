"""Noise-masked emissive trails, retaining transparency over the photosphere."""
import bpy
if bpy.app.is_job_running('RENDER'): raise RuntimeError('Wait for the active render before refining trails.')
scene=bpy.data.scenes['Final Frontier - Solar Laboratory']
nt=bpy.data.materials['Solar - particle trail fibrils'].node_tree
if 'Transparent strand gaps' not in nt.nodes:
    tex=nt.nodes['4D travelling plasma packets']
    emission=nt.nodes['Strand emission']
    mask=nt.nodes.new('ShaderNodeMapRange'); mask.name='Noise breakup of particle trails'
    mask.interpolation_type='SMOOTHSTEP'; mask.clamp=True
    mask.inputs['From Min'].default_value=.40; mask.inputs['From Max'].default_value=.64
    nt.links.new(tex.outputs['Fac'],mask.inputs['Value'])
    transparent=nt.nodes.new('ShaderNodeBsdfTransparent'); transparent.name='Transparent strand gaps'
    mix=nt.nodes.new('ShaderNodeMixShader'); mix.name='Emission within turbulent strand mask'
    nt.links.new(mask.outputs[0],mix.inputs[0]); nt.links.new(transparent.outputs[0],mix.inputs[1]); nt.links.new(emission.outputs[0],mix.inputs[2])
    nt.links.new(mix.outputs[0],nt.nodes['Output'].inputs['Surface'])
    for i,n in enumerate(nt.nodes): n.location=(i%5*230,-(i//5)*240)
scene.cycles.transparent_max_bounces=32
scene.frame_start,scene.frame_end,scene.frame_step=1,240,1
scene.cycles.samples=48; scene.frame_set(49)
scene.render.resolution_x=scene.render.resolution_y=4096
scene.render.filepath=bpy.path.abspath('//SolarBlender-4K.png')
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
result={'noise_masked_strands':True,'native_transparency':True}
