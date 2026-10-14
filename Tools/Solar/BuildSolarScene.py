"""Run via Blender MCP execute_blender_code. Self-contained native animation.

Creates a separate scene; keeps the existing scene and objects. No images,
external assets, scripted frame handlers, or simulation caches are required.
"""
import bpy
import math
import random
from pathlib import Path
from mathutils import Vector

OUTPUT = Path(globals().get('SOLAR_OUTPUT',Path(__file__).resolve().parents[2]/'Artifacts'/'SolarBlender'))
OUTPUT.mkdir(parents=True, exist_ok=True)
NAME = "Final Frontier - Solar Laboratory"
if NAME in bpy.data.scenes:
    raise RuntimeError("Solar scene already exists; edit it instead of overwriting.")
scene = bpy.data.scenes.new(NAME)
bpy.context.window.scene = scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = scene.render.resolution_y = 1024
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_depth = '16'
scene.render.fps = 24
scene.frame_start, scene.frame_end = 1, 240
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = -2.6
scene.world = bpy.data.worlds.new('Solar - black space')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0,0,0,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0

def node(tree, kind, label, x=0, y=0):
    n = tree.nodes.new(kind); n.label = label; n.name = label
    n.location = (x,y); return n

def wire(tree, value, socket):
    if isinstance(value, bpy.types.NodeSocket): tree.links.new(value,socket)
    else: socket.default_value = value

def mathnode(tree, op, a, b=0, label=None):
    n = node(tree,'ShaderNodeMath',label or op)
    n.operation = op; wire(tree,a,n.inputs[0]); wire(tree,b,n.inputs[1]); return n.outputs[0]

def vectornode(tree, op, a, b, label):
    n=node(tree,'ShaderNodeVectorMath',label); n.operation=op
    wire(tree,a,n.inputs[0]); wire(tree,b,n.inputs['Scale'] if op=='SCALE' else n.inputs[1]); return n.outputs['Vector']

def noise(tree, co, scale, detail, rough, speed, label):
    n=node(tree,'ShaderNodeTexNoise',label); n.noise_dimensions='4D'
    wire(tree,co,n.inputs['Vector']); n.inputs['Scale'].default_value=scale
    n.inputs['Detail'].default_value=detail; n.inputs['Roughness'].default_value=rough
    n.inputs['W'].driver_add('default_value').driver.expression=f'frame*{speed}/24'
    return n

def ramp(tree, value, colors, label):
    n=node(tree,'ShaderNodeValToRGB',label); wire(tree,value,n.inputs[0])
    cr=n.color_ramp; cr.interpolation='EASE'
    cr.elements.remove(cr.elements[1])
    for i,(pos,color) in enumerate(colors):
        e=cr.elements[0] if i==0 else cr.elements.new(pos)
        e.position=pos; e.color=(*color,1)
    return n.outputs['Color']

mat=bpy.data.materials.new('Solar - advected plasma / emission only'); mat.use_nodes=True
tree=mat.node_tree; tree.nodes.clear()
coords=node(tree,'ShaderNodeTexCoord','Sphere object space').outputs['Object']
warp=noise(tree,coords,4.3,5,.72,.055,'4D convection - domain warp')
offset=vectornode(tree,'SUBTRACT',warp.outputs['Color'],(.5,.5,.5),'Centered convection')
offset=vectornode(tree,'SCALE',offset,.24,'Warp amplitude')
flow=vectornode(tree,'ADD',coords,offset,'Advected coordinates')
convection=noise(tree,flow,8,7,.72,.045,'Multifractal convection - 7 octaves')
meso=noise(tree,flow,38,5,.68,.085,'Mesoscale turbulent filaments')
absridge=mathnode(tree,'ABSOLUTE',mathnode(tree,'SUBTRACT',mathnode(tree,'MULTIPLY',meso.outputs['Fac'],2),1))
ridge=mathnode(tree,'POWER',mathnode(tree,'SUBTRACT',1,absridge),9,label='Thin magnetic ridges')
gabor=node(tree,'ShaderNodeTexGabor','Fine field-aligned fibrils')
gabor.gabor_type='3D'; wire(tree,flow,gabor.inputs['Vector'])
gabor.inputs['Scale'].default_value=95; gabor.inputs['Frequency'].default_value=2.4
gabor.inputs['Anisotropy'].default_value=.78
for socket in gabor.inputs:
    if socket.name=='Orientation' and socket.type=='VECTOR': wire(tree,offset,socket)
fibrils=mathnode(tree,'POWER',gabor.outputs['Intensity'],3)
grain=noise(tree,flow,320,4,.76,.11,'Subpixel granulation - 320 scale')
cell=node(tree,'ShaderNodeTexVoronoi','Distorted cellular granulation')
cell.feature='DISTANCE_TO_EDGE'; cell.inputs['Scale'].default_value=240; wire(tree,flow,cell.inputs['Vector'])
cellnet=mathnode(tree,'EXPONENT',mathnode(tree,'MULTIPLY',cell.outputs['Distance'],-28))
active=mathnode(tree,'POWER',mathnode(tree,'MULTIPLY',convection.outputs['Fac'],1.5),3)
contrast=mathnode(tree,'POWER',mathnode(tree,'MULTIPLY',convection.outputs['Fac'],1.7),4)
energy=mathnode(tree,'ADD',.7,mathnode(tree,'MULTIPLY',contrast,3.4))
granules=mathnode(tree,'POWER',mathnode(tree,'MULTIPLY',grain.outputs['Fac'],1.7),4)
energy=mathnode(tree,'ADD',energy,mathnode(tree,'MULTIPLY',granules,2.7))
energy=mathnode(tree,'ADD',energy,mathnode(tree,'MULTIPLY',cellnet,.8))
energy=mathnode(tree,'ADD',energy,mathnode(tree,'MULTIPLY',fibrils,1.2))
energy=mathnode(tree,'ADD',energy,mathnode(tree,'MULTIPLY',mathnode(tree,'MULTIPLY',ridge,active),4.0))
heat=mathnode(tree,'MULTIPLY',energy,.11)
color=ramp(tree,heat,[(0,(1,.20,.006)),(.42,(1,.40,.012)),(.72,(1,.65,.035)),(1,(1,.91,.27))],'Luminous amber to hot gold - no black spots')
layer=node(tree,'ShaderNodeLayerWeight','Tight chromosphere')
limb=mathnode(tree,'POWER',layer.outputs['Facing'],7)
energy=mathnode(tree,'ADD',energy,mathnode(tree,'MULTIPLY',limb,3.0))
em=node(tree,'ShaderNodeEmission','HDR solar emission'); wire(tree,color,em.inputs['Color']); wire(tree,energy,em.inputs['Strength'])
out=node(tree,'ShaderNodeOutputMaterial','Output'); tree.links.new(em.outputs[0],out.inputs['Surface'])
# Lay out the native node graph for inspection.
for i,n in enumerate(tree.nodes): n.location=(i%7*220,-(i//7)*240)

bpy.ops.mesh.primitive_uv_sphere_add(segments=256,ring_count=128,radius=1)
sphere=bpy.context.object; sphere.name='Photosphere - continuous procedural detail'
sphere.data.materials.append(mat)
for p in sphere.data.polygons: p.use_smooth=True
sphere.rotation_euler[2]=0
sphere.driver_add('rotation_euler',1).driver.expression='frame/24*0.018'
sphere.rotation_euler[2]=math.radians(12)

flaremat=bpy.data.materials.new('Solar - particle trail fibrils'); flaremat.use_nodes=True
nt=flaremat.node_tree; nt.nodes.clear()
co=node(nt,'ShaderNodeTexCoord','Strand object coordinates').outputs['Object']
mapping=vectornode(nt,'MULTIPLY',co,(35,35,9),'Flow stretched along magnetic tubes')
tex=noise(nt,mapping,3,3,.72,.65,'4D travelling plasma packets')
fade=mathnode(nt,'POWER',tex.outputs['Fac'],2.0)
strength=mathnode(nt,'ADD',.8,mathnode(nt,'MULTIPLY',fade,32))
color=ramp(nt,tex.outputs['Fac'],[(0,(1,.21,.008)),(.5,(1,.55,.025)),(.82,(1,.91,.20)),(1,(1,.99,.7))],'Hot strands')
em=node(nt,'ShaderNodeEmission','Strand emission'); wire(nt,color,em.inputs[0]); wire(nt,strength,em.inputs[1])
out=node(nt,'ShaderNodeOutputMaterial','Output'); nt.links.new(em.outputs[0],out.inputs['Surface'])
for i,n in enumerate(nt.nodes): n.location=(i%5*220,-(i//5)*240)

# A deterministic ensemble of thin particle-like trails following evolving
# magnetic fields. Shape-key animation is native and remains active on reopen.
rng=random.Random(83071)
curve=bpy.data.curves.new('Magnetic field bundles - animated strand cache','CURVE')
curve.dimensions='3D'; curve.resolution_u=1; curve.bevel_depth=.00065
curve.bevel_resolution=2; curve.use_fill_caps=True
trails=bpy.data.objects.new('Prominences - evolving filament bundles',curve)
scene.collection.objects.link(trails); curve.materials.append(flaremat)
trails.rotation_euler=sphere.rotation_euler
trails.driver_add('rotation_euler',1).driver.expression='frame/24*0.018'
definitions=[]
for i in range(48):
    if i<20:
        theta=(i*.61803398875 % 1)*math.tau
        z=rng.uniform(-.10,.23); xy=math.sqrt(1-z*z)
        anchor=Vector((math.cos(theta)*xy,math.sin(theta)*xy,z))
    else:
        z=rng.uniform(-.3,1); theta=rng.random()*math.tau; xy=math.sqrt(1-z*z)
        anchor=Vector((math.cos(theta)*xy,math.sin(theta)*xy,z))
    tangent=anchor.cross(Vector((0,0,1))).normalized()
    side=anchor.cross(tangent).normalized()
    spin=rng.uniform(-1.5,1.5); tangent=(tangent*math.cos(spin)+side*math.sin(spin)).normalized(); side=anchor.cross(tangent)
    width=rng.uniform(.025,.11); height=rng.uniform(.018,.20) if i<20 else rng.uniform(.008,.07)
    phase=rng.random()*math.tau; twist=rng.uniform(-2,2); skew=rng.uniform(-.55,.55)
    for strand in range(rng.randint(10,21)):
        spread=rng.uniform(-1,1); jitter=rng.random()*math.tau
        definitions.append((anchor,tangent,side,width,height,phase,twist,skew,spread,jitter))
        spline=curve.splines.new('POLY'); spline.points.add(64)
        for j,p in enumerate(spline.points): p.co=(0,0,0,1); p.radius=.3+.85*math.sin(math.pi*j/64)**.6

def strand_point(d,u,seconds):
    n,t,b,w,h,phase,twist,skew,spread,jitter=d
    # Each field line has a different apex and foot separation; projected
    # bundles retain volume at the limb rather than collapsing into one wire.
    w*=1+spread*.22
    h*=1+spread*.38
    life=.5+.5*math.sin(seconds*.5+phase)
    growth=.12+.88*life**1.6
    # Unequal legs, serpentine apex, cross-field twist, and staggered birth.
    arc=math.sin(math.pi*u)**(.65+.45*math.sin(phase))
    along=w*(2*u-1+skew*math.sin(math.pi*u))
    rise=h*arc*growth*(1+.15*math.sin(9*u+seconds*.7+phase))
    strand=spread*w*.20*math.sin(math.pi*u)
    cross=strand*math.cos(twist*5*u+seconds*.45)+w*.11*arc*math.sin(8*u+seconds*.5+jitter)
    along+=w*.035*arc*math.sin(23*u+seconds*.9+jitter)
    base=(n+t*along+b*cross).normalized()
    return base*(.9995+rise)

for spline,d in zip(curve.splines,definitions):
    for j,p in enumerate(spline.points): p.co=(*strand_point(d,j/64,0),1)
trails.shape_key_add(name='Basis')
for k in range(1,17):
    seconds=(k-1)*.625
    key=trails.shape_key_add(name=f'Flux phase {k:02}')
    flat=0
    for d in definitions:
        for j in range(65): key.data[flat].co=strand_point(d,j/64,seconds); flat+=1
    key.value=0; key.keyframe_insert('value',frame=1+(k-2)*15)
    key.value=1; key.keyframe_insert('value',frame=1+(k-1)*15)
    key.value=0; key.keyframe_insert('value',frame=1+k*15)
    key.value=0; key.keyframe_insert('value',frame=241)
    key.value=0
# Animation is a study, not a falsely mirrored or looping simulation.

camdata=bpy.data.cameras.new('Solar inspection camera'); camera=bpy.data.objects.new('Solar inspection camera',camdata)
scene.collection.objects.link(camera); camera.location=(0,0,5); camera.rotation_euler=(0,0,0)
camdata.type='ORTHO'; camdata.ortho_scale=2.75; scene.camera=camera
scene.use_nodes=True
comp=bpy.data.node_groups.new('Solar - radiance driven round bloom','CompositorNodeTree')
scene.compositing_node_group=comp
comp.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
rl=node(comp,'CompositorNodeRLayers','HDR solar render',0,0); rl.scene=scene
glow=node(comp,'CompositorNodeGlare','Compact optical bloom - no halo card',250,0)
glow.inputs['Type'].default_value='Fog Glow'; glow.inputs['Quality'].default_value='High'
glow.inputs['Threshold'].default_value=1.3; glow.inputs['Strength'].default_value=.45; glow.inputs['Size'].default_value=.065
comp.links.new(rl.outputs['Image'],glow.inputs['Image'])
output=node(comp,'NodeGroupOutput','Final image',500,0); comp.links.new(glow.outputs['Image'],output.inputs['Image'])
scene['Design']='Continuous 4D emissive plasma, irregular evolving 3D flux strands; compact radiance-driven bloom; no sunspot mask.'
scene['Trail count']=len(definitions)
scene['Sources']='FireDrum procedural VFX lesson; CBaileyFilm procedural solar flares; Codesign Projects staggered flare animation. See Docs/SolarPlasma.md.'
text=bpy.data.texts.new('Solar laboratory - read me')
text.write('Final Frontier solar study.\nFrame range 1-240 / 24fps.\nProcedural object-space material; native shape keys evolve the flux bundles.\nAll strands are 3D and hidden by the photosphere when behind it.\nRender 4096 square for inspection. Surface closeup: camera at (.28,.22,5), ortho .6.\nBloom comes from HDR emission, no stretched billboard.\nThis .blend is the art reference; the game uses Assets/Celestial/Sun.hlsli.\n')
scene.frame_set(49)
scene.render.filepath=str(OUTPUT/'SolarBlender-preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/'SolarLaboratory.blend'))
result={'file':bpy.data.filepath,'scene':scene.name,'strand_count':len(definitions),'frames':240,'saved':True}
