"""
Prepares the downloaded enemy models for Unity (headless Blender, never touches the originals).

  Blender -b --python export_enemies.py -- <name> <out_dir> [preview.png]

Per model: import -> bake the rest pose into the mesh and drop the rig (the game moves enemies procedurally) ->
rotate so the model faces Blender -Y (= Unity +Z with the export settings below) -> scale to a real size ->
pivot (feet for ground enemies, centre for flyers) -> shrink textures to 1024 -> export one FBX with embedded textures.
Models that arrive without textures get flat materials (names chosen so the Unity postprocessor makes the glow ones emissive).
"""
import bpy, sys, os, math, mathutils
import numpy as np

SRC = os.environ.get("ENEMY_SRC", "/private/tmp/claude-501/-Users-enger-Desktop-games-developer/1878fafe-43a4-40aa-b166-28c7021ec3d1/scratchpad/enemigos_zip/ENEMIGOS")
# name: file, ground|fly, size (m: height for ground, longest side for fly), yaw (deg about Z to face -Y), colours for untextured models
MODELS = {
    "Elemental":   dict(file="BOSQUE/x_Earth_Elemental_FBX/Earth_Elemental_FBX/Earth_Elemental_FBX.fbx", mode="ground", size=2.8, yaw=0,
                      diffuse="BOSQUE/x_Earth_Elemental_FBX/Earth_Elemental_FBX/Textures/1k/Elemental_LP_Earthen_Elemental_Diffuse.png"),
    "BattleDroid": dict(file="BOSQUE/x_fbx (1)/fbx.fbx", mode="ground", size=2.3, yaw=-90,
                        mats=[("M_En_Body", (0.16, 0.18, 0.2, 1), None, 0.7, 0.45), ]),
    "SpiderWalker": dict(file="BOSQUE/x_fbx/fbx.fbx", mode="ground", size=2.2, yaw=0,
                         mats=[("M_En_Body", (0.12, 0.05, 0.16, 1), None, 0.6, 0.5), ]),
    "SweepDrone":  dict(file="DESIERTO/sweep_drone_(FBX).fbx", mode="fly", size=1.7, yaw=0,
                        mats=[("M_En_Body", (0.55, 0.42, 0.28, 1), None, 0.5, 0.55), ]),
    "RustDrone":   dict(file="DESIERTO/old_rust_drone.fbx", mode="fly", size=2.8, yaw=0, decimate=0.18, rotors=["Cylinder.001", "Cylinder.010"]),
}

argv = sys.argv[sys.argv.index("--") + 1:]
name, out_dir = argv[0], argv[1]
preview = argv[2] if len(argv) > 2 else None
spec = MODELS[name]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, spec["file"]))

# 1. rest pose baked into the mesh, rig and animation dropped
for a in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
    a.data.pose_position = 'REST'
bpy.context.view_layer.update()
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
dg = bpy.context.evaluated_depsgraph_get()
for o in meshes:
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    mw = o.matrix_world.copy()
    o.modifiers.clear(); o.parent = None
    o.data = me; o.matrix_world = mw
for o in [o for o in bpy.data.objects if o.type != 'MESH']:
    bpy.data.objects.remove(o)
for act in list(bpy.data.actions): bpy.data.actions.remove(act)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:
    for g in list(o.vertex_groups): o.vertex_groups.remove(g)

# 2. join into one object, apply transforms (propellers stay separate objects so the game can spin them)
rotors = [o for o in meshes if o.name in spec.get("rotors", [])]
meshes = [o for o in meshes if o not in rotors]
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.name = name
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

for o in rotors:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.context.view_layer.objects.active = obj
parts = [obj] + rotors
if spec.get("decimate"):
    for o in parts:
        bpy.context.view_layer.objects.active = o
        md = o.modifiers.new("dec", 'DECIMATE'); md.ratio = spec["decimate"] if o is obj else 0.5
        bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.context.view_layer.objects.active = obj

# 3. orientation / size / pivot (applied to the body and the propellers together)
def bbox(objs=None):
    pts = np.array([tuple(o.matrix_world @ mathutils.Vector(c)) for o in (objs or parts) for c in o.bound_box])
    return pts.min(axis=0), pts.max(axis=0)

def xform(m):
    for o in parts:
        o.matrix_world = m @ o.matrix_world
        bpy.context.view_layer.update()

xform(mathutils.Matrix.Rotation(math.radians(spec["yaw"]), 4, 'Z'))
mn, mx = bbox(); ext = mx - mn
ref = ext[2] if spec["mode"] == "ground" else ext.max()
k = spec["size"] / ref
xform(mathutils.Matrix.Scale(k, 4))
mn, mx = bbox()
cx, cy = (mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2
cz = mn[2] if spec["mode"] == "ground" else (mn[2] + mx[2]) / 2
xform(mathutils.Matrix.Translation((-cx, -cy, -cz)))
for o in parts:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# propellers: pivot at their own centre so they spin in place; named for the game
for i, o in enumerate(rotors):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
    o.name = "Rotor_L" if o.location.x < 0 else "Rotor_R"
print("SIZE", name, [round(float(v), 2) for v in (bbox()[1] - bbox()[0])], "polys", sum(len(o.data.polygons) for o in parts), [(o.name, tuple(round(v, 2) for v in o.location)) for o in rotors])

# 4. materials: shrink the textures, or flat colours for models that came without
if spec.get("diffuse"):
    # the importer left the albedo unlinked: rebuild the materials (body = texture, eyes = glow, rest = dark flesh)
    img = bpy.data.images.load(os.path.join(SRC, spec["diffuse"]))
    for m in obj.data.materials:
        m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        out_n = nt.nodes.new("ShaderNodeOutputMaterial"); b = nt.nodes.new("ShaderNodeBsdfPrincipled")
        nt.links.new(b.outputs["BSDF"], out_n.inputs["Surface"])
        if "Eyes" in m.name:
            m.name = "M_Enemy_Glow"; b.inputs["Base Color"].default_value = (1, 0.15, 0.05, 1)
        elif "Tongue" in m.name:
            b.inputs["Base Color"].default_value = (0.35, 0.05, 0.06, 1); b.inputs["Roughness"].default_value = 0.6
        else:
            t = nt.nodes.new("ShaderNodeTexImage"); t.image = img
            nt.links.new(t.outputs["Color"], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.85
imgs_ok = [i for i in bpy.data.images if i.size[0] > 0]
for i in imgs_ok:
    if max(i.size) > 1024:
        f = 1024 / max(i.size); i.scale(max(1, int(i.size[0] * f)), max(1, int(i.size[1] * f)))
    i.pack()
if not imgs_ok and spec.get("mats"):
    obj.data.materials.clear()
    for (mname, col, emit, rough, metal) in spec["mats"]:
        m = bpy.data.materials.new(mname); m.use_nodes = True
        b = m.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = col; b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
        obj.data.materials.append(m)
    # every face on the first material
    for p in obj.data.polygons: p.material_index = 0

# preview: front (from -Y), side (from +X), top
if preview:
    s = bpy.context.scene; s.render.engine = 'BLENDER_WORKBENCH'
    s.display.shading.light = 'STUDIO'; s.display.shading.color_type = 'MATERIAL'
    s.render.resolution_x = s.render.resolution_y = 320
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); s.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; s.camera = cam
    mn, mx = bbox(); c = (mn + mx) / 2; cam.data.ortho_scale = float((mx - mn).max()) * 1.25
    out = []
    for i, (loc, rot) in enumerate([((0, -20, 0), (math.pi / 2, 0, 0)), ((20, 0, 0), (math.pi / 2, 0, math.pi / 2)), ((0, 0, 20), (0, 0, 0))]):
        cam.location = (c[0] + loc[0], c[1] + loc[1], c[2] + loc[2]); cam.rotation_euler = rot
        s.render.filepath = preview + f".{i}.png"; bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(preview + f".{i}.png"); out.append(np.array(im.pixels[:]).reshape(320, 320, 4))
    both = np.concatenate(out, axis=1)
    img = bpy.data.images.new("prev", 960, 320, alpha=True); img.pixels = both.flatten().tolist()
    img.filepath_raw = preview; img.file_format = 'PNG'; img.save()
    print("PREVIEW", preview)
    sys.exit(0)

os.makedirs(out_dir, exist_ok=True)
out = os.path.join(out_dir, f"Enemy_{name}.fbx")
bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
    axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
    path_mode='COPY', embed_textures=True, add_leaf_bones=False, bake_anim=False)
print("DONE", out, os.path.getsize(out))
