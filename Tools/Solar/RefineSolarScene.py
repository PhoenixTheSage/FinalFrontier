"""Native node edits and a volume chromosphere, via the live Blender MCP."""
import bpy
from pathlib import Path
if bpy.app.is_job_running('RENDER'): raise RuntimeError('Wait for the active render before refining materials.')

# Reuse the material graph builder without touching the existing geometry or
# other scenes. The file is also kept as an editable text inside the blend.
source=Path(__file__).with_name('BuildSolarScene.py').read_text()
start=source.index('def node(')
end=source.index('bpy.ops.mesh.primitive_uv_sphere_add')
ns={}; exec('import bpy\n'+source[start:end],ns)
scene=bpy.data.scenes['Final Frontier - Solar Laboratory']
bpy.context.window.scene=scene
sphere=bpy.data.objects['Photosphere - continuous procedural detail']
sphere.data.materials.clear(); sphere.data.materials.append(ns['mat'])
for mat in list(bpy.data.materials):
    if mat.name.startswith('Solar - advected plasma') and mat.users==0: bpy.data.materials.remove(mat)
ns['mat'].name='Solar - advected plasma / emission only'
scene.view_settings.exposure=-2.6
glow=scene.compositing_node_group.nodes['Compact optical bloom - no halo card']
glow.inputs['Threshold'].default_value=1.3; glow.inputs['Strength'].default_value=.45; glow.inputs['Size'].default_value=.065

# A thin volume on the actual sphere provides uneven chromospheric wisps.
# The radial envelope prevents a detached circle/ellipse behind the object.
if 'Chromosphere - turbulent volume' not in bpy.data.objects:
    node,mathnode,noise,vectornode=(ns[k] for k in ('node','mathnode','noise','vectornode'))
    mat=bpy.data.materials.new('Solar - volumetric chromosphere'); mat.use_nodes=True
    nt=mat.node_tree; nt.nodes.clear()
    co=node(nt,'ShaderNodeTexCoord','Solar radius coordinates').outputs['Object']
    length=node(nt,'ShaderNodeVectorMath','Radius'); length.operation='LENGTH'; nt.links.new(co,length.inputs[0])
    normal=node(nt,'ShaderNodeVectorMath','Radial field'); normal.operation='NORMALIZE'; nt.links.new(co,normal.inputs[0])
    tex=noise(nt,normal.outputs[0],25,5,.72,.15,'Evolving chromospheric tufts')
    mask=mathnode(nt,'MAXIMUM',mathnode(nt,'SUBTRACT',tex.outputs['Fac'],.40),0)
    mask=mathnode(nt,'POWER',mathnode(nt,'MULTIPLY',mask,4),3)
    radial=mathnode(nt,'SUBTRACT',length.outputs['Value'],1.004)
    radial=mathnode(nt,'DIVIDE',radial,.012)
    radial=mathnode(nt,'EXPONENT',mathnode(nt,'MULTIPLY',mathnode(nt,'MULTIPLY',radial,radial),-1))
    emission=mathnode(nt,'MULTIPLY',mathnode(nt,'MULTIPLY',radial,mask),260)
    vol=node(nt,'ShaderNodeVolumePrincipled','Emissive turbulent gas')
    vol.inputs['Density'].default_value=0
    vol.inputs['Emission Color'].default_value=(1,.58,.018,1)
    nt.links.new(emission,vol.inputs['Emission Strength'])
    out=node(nt,'ShaderNodeOutputMaterial','Volume output'); nt.links.new(vol.outputs[0],out.inputs['Volume'])
    for i,n in enumerate(nt.nodes): n.location=(i%6*230,-(i//6)*240)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=128,ring_count=64,radius=1.09)
    obj=bpy.context.object; obj.name='Chromosphere - turbulent volume'; obj.data.materials.append(mat)
    obj.rotation_euler=sphere.rotation_euler
    obj.driver_add('rotation_euler',1).driver.expression='frame/24*0.018'
scene.frame_set(49)
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
result={'material_updated':True,'volume':True,'file':bpy.data.filepath}
