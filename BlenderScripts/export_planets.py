"""
Exports one planet .blend (from ~/Desktop/spacekill_map) to an FBX for Unity, WITHOUT modifying the .blend:

  Blender -b <planet>.blend --python export_planets.py -- <bosque|ciudad|desierto|sol> <out.fbx>

What it does besides exporting:
  * bosque / desierto: the ground is a coarse sphere (23-30 m faces). Unity walks on a perfect sphere collider, so between vertices
    the visible ground sat up to ~0.5 m below it and the player seemed to float. The ground faces (material slot 0) are subdivided to
    ~4 m edges and every ground vertex is projected onto the sphere (error ~1-2 cm), then the objects standing on it are re-seated.
  * ciudad: the ground ("Suelo") is joined into the mesh as material slot 0 (GalaxySetup treats submesh 0 as the terrain).
  * sol: the light is dropped (Unity ignores it; SunLight aims the scene's directional light).
The Blender axes / unit settings below are the ones the Unity pipeline (GalaxyLoader / GalaxySetup) was calibrated with.
"""
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

kind, out = sys.argv[sys.argv.index("--") + 1], sys.argv[sys.argv.index("--") + 2]
objs = bpy.data.objects

# Subdivision cuts per planet (edge length / (cuts + 1)); target ~4 m edges
CUTS = {"bosque": 6, "desierto": 5}
FLAT, SINK_FRAC, SINK_MIN = 1.5, 0.004, 0.05

# Desierto size: the whole planet (ground, mesas, rocks, cacti) is scaled about its centre, and the cacti shrink extra on their own.
# 1.0 = the Blender size (radius 360). Change these two numbers to resize it.
DESERT_SCALE = 0.6        # 360 -> 216 m radius
CACTUS_EXTRA = 0.5        # cacti end up 0.6 * 0.5 = 0.3 of their Blender size (~22 m -> ~7 m)


def unparent_keep(o):
    m = o.matrix_world.copy(); o.parent = None; o.matrix_world = m

def delete(o): bpy.data.objects.remove(o, do_unlink=True)


def islands_of(bm):
    seen = set(); out = []
    for v in bm.verts:
        if v.index in seen: continue
        st = [v]; comp = []; seen.add(v.index)
        while st:
            x = st.pop(); comp.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen: seen.add(y.index); st.append(y)
        out.append(comp)
    return out


def ground_tree(bm, ground_faces):
    bm.verts.ensure_lookup_table()
    coords = [v.co.copy() for v in bm.verts]
    polys = [[v.index for v in f.verts] for f in ground_faces]
    return BVHTree.FromPolygons(coords, polys)


def deviation(tree, R, n=3000):
    """Max |hit radius - R| and the deepest dip below the sphere, over n random directions from the planet centre."""
    rng = random.Random(7); worst = 0.0; dip = 0.0
    for _ in range(n):
        d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
        hit = tree.ray_cast(Vector((0, 0, 0)), d, 4 * R)
        if hit[0] is None: continue
        dev = hit[0].length - R
        worst = max(worst, abs(dev)); dip = min(dip, dev)
    return worst, dip


def refine_ground(obj, cuts):
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    gf = [f for f in bm.faces if f.material_index == 0]
    gv = {v for f in gf for v in f.verts}
    R = sum(v.co.length for v in gv) / len(gv)
    w0, dip0 = deviation(ground_tree(bm, gf), R)
    print(f"GROUND before: faces={len(gf)} verts={len(gv)} R={R:.3f} max|dev|={w0:.3f} deepest dip={dip0:.3f}")

    edges = list({e for f in gf for e in f.edges})
    if cuts is None:                                    # aim for ~4 m edges whatever the planet's current size
        avg = sum(e.calc_length() for e in edges) / len(edges)
        cuts = max(1, math.ceil(avg / 4.0) - 1)
        print(f"GROUND average edge {avg:.1f} m -> {cuts} cuts")
    bmesh.ops.subdivide_edges(bm, edges=edges, cuts=cuts, use_grid_fill=True)
    bm.faces.ensure_lookup_table()
    gf = [f for f in bm.faces if f.material_index == 0]
    gv = {v for f in gf for v in f.verts}
    for v in gv: v.co = v.co.normalized() * R             # exactly on the sphere
    for f in gf: f.smooth = True
    bm.normal_update()
    w1, dip1 = deviation(ground_tree(bm, gf), R)
    print(f"GROUND after:  faces={len(gf)} verts={len(gv)} R={R:.3f} max|dev|={w1:.3f} deepest dip={dip1:.3f}")
    bm.to_mesh(me); bm.free(); me.update()
    return R


def smooth(t): return t * t * (2 - t)


def scale_mesh(obj, factor):
    """Scales every vertex about the object's origin (= the planet's centre)."""
    me = obj.data
    for v in me.vertices: v.co = v.co * factor
    me.update()


def scale_cacti(obj, extra):
    """Shrinks each cactus (trunk + arms are separate pieces) about its own base point."""
    from mathutils.kdtree import KDTree
    me = obj.data
    names = [m.name if m else "" for m in me.materials]
    cactus_slot = names.index("Material.003")
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    cverts = {v.index for f in bm.faces if f.material_index == cactus_slot for v in f.verts}
    pieces = [c for c in islands_of(bm) if c[0].index in cverts]
    allv = [v for c in pieces for v in c]
    pid = {}
    for i, c in enumerate(pieces):
        for v in c: pid[v.index] = i
    kd = KDTree(len(allv))
    for i, v in enumerate(allv): kd.insert(v.co, i)
    kd.balance()
    parent = list(range(len(pieces)))
    def find(a):
        while parent[a] != a: parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for i, v in enumerate(allv):
        for (_, j, _) in kd.find_range(v.co, 3.0):
            a, b = find(pid[v.index]), find(pid[allv[j].index])
            if a != b: parent[a] = b
    groups = {}
    for i, c in enumerate(pieces): groups.setdefault(find(i), []).extend(c)
    for verts in groups.values():
        rs = sorted(verts, key=lambda v: v.co.length)
        base = sum((v.co for v in rs[:max(3, len(rs) // 10)]), Vector()) / max(3, len(rs) // 10)
        for v in verts: v.co = base + (v.co - base) * extra
    print(f"CACTI scaled: {len(groups)} cacti x{extra}")
    bm.to_mesh(me); bm.free(); me.update()


def reseat(obj, R):
    """Stand every object on the refined ground again (lowest base points on the sphere, sunk a hair): rigid moves only."""
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    gf = [f for f in bm.faces if f.material_index == 0]
    gset = {v.index for f in gf for v in f.verts}
    tree = ground_tree(bm, gf)
    center = Vector((0, 0, 0))

    def gr(d):
        h = tree.ray_cast(center, d, 5 * R)
        return h[0].length if h[0] is not None else None

    pieces = [c for c in islands_of(bm) if not any(v.index in gset for v in c)]
    moved = 0; skipped = 0; maxshift = 0.0
    for comp in pieces:
        P = np.array([tuple(v.co) for v in comp]); n = len(comp)
        rs = np.linalg.norm(P, axis=1)
        height = rs.max() - rs.min()
        if height < FLAT: skipped += 1; continue
        order = np.argsort(rs)
        g0 = gr(Vector(P[order[0]]).normalized())
        if g0 is not None and rs.max() < g0 + 0.5: skipped += 1; continue        # buried on purpose: leave it
        # A wide flat base (the big mesas) can't be re-seated rigidly: its edges were already blended into the old ground
        # and the new ground is only a few cm to ~1.5 m higher, so they just end up slightly buried. Only narrow bases move.
        base0 = order[:max(3, int(0.12 * n))]
        if float((P[base0].max(axis=0) - P[base0].min(axis=0)).max()) >= 25.0: skipped += 1; continue
        total = 0.0
        for _ in range(2):
            P = np.array([tuple(v.co) for v in comp]); rs = np.linalg.norm(P, axis=1); order = np.argsort(rs)
            base_idx = order[:max(3, int(0.12 * n))]
            hs = []
            for i in base_idx:
                g = gr(comp[i].co.normalized())
                if g is not None: hs.append(rs[i] - g)
            if not hs: break
            h = max(hs)
            d = Vector(P[base_idx].mean(axis=0)).normalized()
            sink = max(SINK_MIN, SINK_FRAC * height)
            delta = -h - sink
            gl = gr(Vector(P[order[0]]).normalized())
            if gl is not None:
                lowgap = rs.min() - gl; cap = max(0.5, min(0.1 * height, 8.0))
                delta = max(delta, -cap - lowgap)
            for v in comp: v.co = v.co + d * delta
            total += delta
        moved += 1; maxshift = max(maxshift, abs(total))
        if abs(total) > 2.0:
            print(f"BIGSHIFT verts={n} height={height:.1f} shift={total:.2f} base_gap_before_first_pass={'?'} ext={float((P.max(axis=0) - P.min(axis=0)).max()):.1f}")
    print(f"RESEAT: pieces moved={moved} skipped={skipped} max shift={maxshift:.3f} m")
    bm.to_mesh(me); bm.free(); me.update()


def drop_floaters(obj, min_gap=0.3, sink=0.05, cap=40.0):
    """Ciudad: pieces hovering over the relief ground are lowered (rigidly, along their radial) until their lowest base point
    touches it. Only pieces with NOTHING under them but ground move: parts resting on other pieces (building storeys) stay."""
    from mathutils.bvhtree import BVHTree
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    bm.faces.index_update()
    tree = BVHTree.FromBMesh(bm)
    is_ground = [f.material_index == 0 for f in bm.faces]
    gset = {v.index for f in bm.faces if f.material_index == 0 for v in f.verts}
    moved = 0; maxdrop = 0.0; hist = []
    for comp in [c for c in islands_of(bm) if not any(v.index in gset for v in c)]:
        P = np.array([tuple(v.co) for v in comp]); n = len(comp)
        rs = np.linalg.norm(P, axis=1); order = np.argsort(rs)
        base = order[:max(3, int(0.12 * n))]
        gaps = []; ok = True
        for i in base:
            d = comp[i].co.normalized()
            h = tree.ray_cast(comp[i].co - d * 0.02, -d, 200.0)
            if h[0] is None: continue
            if not is_ground[h[2]]: ok = False; break
            gaps.append(h[3] - 0.02)
        if not ok or not gaps: continue
        gap = min(gaps)
        if gap <= min_gap or n != 8 or float(rs.max()-rs.min()) < 1.5: continue      # loose debris boxes only (8 vertices)
        if os.environ.get('DIAG'): hist.append((round(float(gap),1), round(float(rs.max()-rs.min()),1), round(float((P.max(axis=0)-P.min(axis=0)).max()),1), n)); continue
        delta = -min(gap + sink, cap)
        d = Vector(P[base].mean(axis=0)).normalized()
        for v in comp: v.co = v.co + d * delta
        moved += 1; maxdrop = max(maxdrop, -delta); hist.append(round(-delta, 1))
    print(f"FLOATERS lowered={moved} max drop={maxdrop:.2f} m  n={len(hist)} drops={[h for h in sorted(hist) if h[1]>=1.5 and h[3]<=40][-40:] if os.environ.get('DIAG') else sorted(hist)[-15:]}")
    bm.to_mesh(me); bm.free(); me.update()


# ── Per-planet preparation ────────────────────────────────────────────────
if kind == "bosque":
    unparent_keep(objs["Bosque"]); delete(objs["Bosque_Pos"])
    R = refine_ground(objs["Bosque_Malla"], CUTS["bosque"])
    reseat(objs["Bosque_Malla"], R)          # seat everything on the refined ground first; moving objects by turning them about the centre keeps them seated
    if os.environ.get("NO_ROUTES") != "1":
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import bosque_map, json
        zones = bosque_map.build(objs["Bosque_Malla"], R, islands_of, {})
        json.dump(zones, open(os.path.splitext(out)[0] + "_zones.json", "w"), indent=1)
elif kind == "desierto":
    unparent_keep(objs["Desierto.001"]); delete(objs["Desierto_Pos"])
    objs["Desierto.001"].name = "Desierto"; objs["Desierto_Malla.001"].name = "Desierto_Malla"
    scale_mesh(objs["Desierto_Malla"], DESERT_SCALE)
    scale_cacti(objs["Desierto_Malla"], CACTUS_EXTRA)
    R = refine_ground(objs["Desierto_Malla"], None); reseat(objs["Desierto_Malla"], R)
elif kind == "ciudad":
    unparent_keep(objs["Ciudad_Destruida"]); delete(objs["Ciudad_Destruida_Pos"])
    malla, suelo = objs["Ciudad_Destruida_Malla"], objs["Ciudad_Destruida_Suelo"]
    bpy.ops.object.select_all(action='DESELECT')
    suelo.select_set(True); malla.select_set(True)
    bpy.context.view_layer.objects.active = malla
    bpy.ops.object.join()                       # one mesh: the ground must be material slot 0
    idx = [i for i, s in enumerate(malla.material_slots) if s.material and s.material.name == "CD_Suelo_Bake"][0]
    malla.active_material_index = idx
    while malla.active_material_index > 0:
        bpy.ops.object.material_slot_move(direction='UP')
    print("GROUNDSLOT", malla.material_slots[0].material.name, "slots", len(malla.material_slots))
    drop_floaters(malla)
elif kind == "sol":
    for o in list(objs):
        if o.type == 'LIGHT': delete(o)

for o in bpy.data.objects:
    print("EXPORT", o.name, o.type, o.parent.name if o.parent else None, tuple(round(v, 1) for v in o.matrix_world.translation))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'EMPTY', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', bake_space_transform=False,
    axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
    colors_type='NONE', use_custom_props=False, path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
print("DONE", out, os.path.getsize(out))
