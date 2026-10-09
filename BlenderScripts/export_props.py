"""
Vending machine + coins for Unity (headless Blender; the originals in ~/Downloads are not touched).

  Blender -b --python export_props.py -- <out_dir> <materials_json_out>

Machine: floor plate, cameras and lights dropped; the small parts are merged into ONE mesh (one object instead of ~190),
except the ones the game animates: Holograma, Baliza, Antena_Punta, Pantalla (kept as their own objects).
Coins: three separate FBX (Oro / Platino / Elite), each standing upright facing -Y (= Unity +Z), pivot at the centre, 0.6 m wide.
Also writes the material data (emission / roughness / metal / alpha) in the format of Assets/Editor/OrbitRushMaterials.json.
"""
import bpy, sys, os, json, math, mathutils
import numpy as np

SRC = os.path.expanduser("~/Downloads")
out_dir, mat_json = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(out_dir, exist_ok=True)
entries = {}

def collect_mats():
    for m in bpy.data.materials:
        if not m.use_nodes: continue
        b = m.node_tree.nodes.get("Principled BSDF")
        if not b or m.name == "Material": continue
        e = list(b.inputs["Emission Color"].default_value[:3])
        entries[m.name] = dict(name=m.name, emit=[round(x, 4) for x in e], strength=round(b.inputs["Emission Strength"].default_value, 3),
                               rough=round(b.inputs["Roughness"].default_value, 3), metal=round(b.inputs["Metallic"].default_value, 3),
                               alpha=round(b.inputs["Alpha"].default_value, 3))

def export(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(out_dir, name)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
        axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
        path_mode='STRIP', embed_textures=False, add_leaf_bones=False, bake_anim=False)
    print("DONE", path, os.path.getsize(path))

# ── Machine ──────────────────────────────────────────────────────────────
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, "maquina.fbx"))
collect_mats()
for o in [o for o in bpy.data.objects if o.type in ('CAMERA', 'LIGHT')]: bpy.data.objects.remove(o)
for o in [o for o in bpy.data.objects if o.type == 'MESH' and o.name == "Piso"]: bpy.data.objects.remove(o)
root = bpy.data.objects.get("Maquina_Expendedora")
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:                      # bake the hierarchy into world space
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
if root: bpy.data.objects.remove(root)
KEEP = ("Holograma", "Baliza", "Antena_Punta", "Pantalla")
loose = [o for o in meshes if o.name in KEEP]
body = [o for o in meshes if o not in loose]
bpy.ops.object.select_all(action='DESELECT')
for o in body: o.select_set(True)
bpy.context.view_layer.objects.active = body[0]
bpy.ops.object.join()
main = bpy.context.view_layer.objects.active; main.name = "Maquina_Cuerpo"
parts = [main] + loose
for o in parts:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in loose:                       # pivot at their own centre so they can spin / pulse in place
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
pts = np.array([tuple(o.matrix_world @ mathutils.Vector(c)) for o in parts for c in o.bound_box])
print("MACHINE bbox", [round(float(v), 2) for v in pts.max(axis=0) - pts.min(axis=0)], "min", [round(float(v), 2) for v in pts.min(axis=0)],
      "polys", sum(len(o.data.polygons) for o in parts), "objects", len(parts))
export(parts, "Maquina.fbx")

# ── Coins ────────────────────────────────────────────────────────────────
for coin in ("Oro", "Platino", "Elite"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, "monedas.fbx"))
    collect_mats()
    want = {"Oro": "Moneda_Principal_Oro", "Platino": "Moneda_Especial_Platino", "Elite": "Moneda_Premium_Elite"}[coin]
    for o in [o for o in bpy.data.objects if o.name != want]: bpy.data.objects.remove(o)
    o = bpy.data.objects[want]
    for a in list(bpy.data.actions): bpy.data.actions.remove(a)
    o.animation_data_clear()
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = o.data
    P = np.array([tuple(v.co) for v in me.vertices]); ext = P.max(axis=0) - P.min(axis=0)
    thin = int(np.argmin(ext)); print("COIN", coin, "extent", [round(float(v), 3) for v in ext], "thin axis", "XYZ"[thin])
    # thin axis -> Blender Y (the coin faces -Y = Unity +Z, standing upright)
    if thin == 2: rot = mathutils.Matrix.Rotation(math.radians(90), 4, 'X')
    elif thin == 0: rot = mathutils.Matrix.Rotation(math.radians(90), 4, 'Z')
    else: rot = mathutils.Matrix.Identity(4)
    me.transform(rot); me.update()
    P = np.array([tuple(v.co) for v in me.vertices]); mn, mx = P.min(axis=0), P.max(axis=0)
    diameter = max(mx[0] - mn[0], mx[2] - mn[2])
    k = 0.6 / diameter
    c = (mn + mx) / 2
    me.transform(mathutils.Matrix.Translation((-c[0], -c[1], -c[2])))
    me.transform(mathutils.Matrix.Scale(k, 4)); me.update()
    o.location = (0, 0, 0)
    o.name = f"Moneda_{coin}"
    P = np.array([tuple(v.co) for v in me.vertices]); print("COIN final", coin, [round(float(v), 3) for v in P.max(axis=0) - P.min(axis=0)])
    export([o], f"Moneda_{coin}.fbx")

json.dump({"items": list(entries.values())}, open(mat_json, "w"), indent=1)
print("MATS", len(entries))
