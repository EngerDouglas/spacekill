"""
Kenney Survival Kit (CC0, https://kenney.nl/assets/survival-kit) -> Unity props for the Bosque landmarks.

  Blender -b --python export_kenney_props.py -- <kit "FBX format" dir> <out dir> <name> [<name> ...]

Each model is scaled from the kit's small units to metres, centred on its footprint with the base at y=0, and exported with its
texture embedded as Prop_<name>.fbx.
"""
import bpy, sys, os, mathutils
import numpy as np

SCALE = {"structure": 5.5, "structure-canvas": 5.5, "tent": 3.6, "tent-canvas": 3.6, "tent-canvas-half": 3.6,
         "fence": 3.2, "fence-doorway": 3.2, "tree-log": 3.0, "tree-log-small": 3.0}
DEFAULT = 3.0

args = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, names = args[0], args[1], args[2:]
os.makedirs(out_dir, exist_ok=True)

for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(src, name + ".fbx"))
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in meshes:                                  # keep the world transform (some nodes sit under scaled empties)
        mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in [o for o in bpy.data.objects if o.type != 'MESH']: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = "Prop_" + name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    k = SCALE.get(name, DEFAULT)
    me = obj.data
    P = np.array([tuple(v.co) for v in me.vertices]) * k
    mn, mx = P.min(axis=0), P.max(axis=0)
    off = np.array([(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, mn[2]])
    for v, p in zip(me.vertices, P): v.co = mathutils.Vector(p - off)
    me.update()
    for i in bpy.data.images:
        if i.size[0] > 0: i.pack()
    path = os.path.join(out_dir, f"Prop_{name}.fbx")
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
        axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
        path_mode='COPY', embed_textures=True, add_leaf_bones=False, bake_anim=False)
    ext = P.max(axis=0) - P.min(axis=0)
    print("PROP", name, [round(float(x), 2) for x in ext], os.path.getsize(path))
