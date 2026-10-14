"""Regenerate only the authored flux geometry and add bright active footpoints."""
import bpy
import math
import random
from mathutils import Vector
from pathlib import Path
if bpy.app.is_job_running('RENDER'): raise RuntimeError('Wait for the active render before refining flux geometry.')

scene=bpy.data.scenes['Final Frontier - Solar Laboratory']; bpy.context.window.scene=scene
sphere=bpy.data.objects['Photosphere - continuous procedural detail']
flaremat=bpy.data.materials['Solar - particle trail fibrils']
obj=bpy.data.objects.get('Prominences - evolving filament bundles')
if obj:
    data=obj.data; bpy.data.objects.remove(obj,do_unlink=True)
    if data.users==0: bpy.data.curves.remove(data)
source=Path(__file__).with_name('BuildSolarScene.py').read_text()
exec(source[source.index('rng=random.Random'):source.index('camdata=bpy.data.cameras.new')])

ns={}; exec('import bpy\n'+source[source.index('def node('):source.index('mat=bpy.data.materials.new')],ns)
node,mathnode,wire=(ns[k] for k in ('node','mathnode','wire'))
mat=sphere.data.materials[0]; nt=mat.node_tree
energy=nt.nodes['HDR solar emission'].inputs['Strength'].links[0].from_socket
coords=nt.nodes['Sphere object space'].outputs['Object']
previous=None; chosen=0
for d in definitions:
    n,t,side,w,h,phase,twist,skew,spread,jitter=d
    if previous is not None and (n-previous).length<.001: continue
    previous=n
    if n.z<.05 or chosen>=14: continue
    chosen+=1
    for sign in [-1,1]:
        foot=(n+t*w*sign).normalized()
        distance=node(nt,'ShaderNodeVectorMath',f'Active footpoint {chosen}:{sign}'); distance.operation='DISTANCE'
        wire(nt,coords,distance.inputs[0]); wire(nt,tuple(foot),distance.inputs[1])
        radius=mathnode(nt,'DIVIDE',distance.outputs['Value'],w*.19)
        field=mathnode(nt,'EXPONENT',mathnode(nt,'MULTIPLY',mathnode(nt,'MULTIPLY',radius,radius),-1))
        # Intensity evolves with each region's native geometry phase.
        value=node(nt,'ShaderNodeValue',f'Footpoint lifetime {chosen}:{sign}')
        value.outputs[0].driver_add('default_value').driver.expression=f'6*(0.35+0.65*(0.5+0.5*sin(frame/24*0.5+{phase})))'
        energy=mathnode(nt,'ADD',energy,mathnode(nt,'MULTIPLY',field,value.outputs[0]))
wire(nt,energy,nt.nodes['HDR solar emission'].inputs['Strength'])
scene['Trail count']=len(definitions)
scene.frame_set(49)
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
result={'bundles_refined':True,'active_regions':chosen,'strands':len(definitions)}
