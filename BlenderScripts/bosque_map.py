"""
Bosque: routes and unique places (called by export_planets.py after the ground is refined; the .blend is never touched).

The planet is a sphere, so an object can be moved anywhere by turning it about the planet's centre: it stays seated on the
ground. Steps:
  1. six places (zones) are laid out around the pond and joined by a loop path plus two shortcuts (great-circle arcs)
  2. everything standing on the path or inside a clearing is moved out to the verges ("walls" along the path)
  3. each place gets its own theme, built from objects that already exist (stumps+logs, the tallest trees, giant mushrooms, rocks)
  4. the ground along the path is subdivided and painted with its own material (Suelo_Camino)
Writes the zone list for Unity (Planeta_Bosque_zones.json).
"""
import math, json
import numpy as np
import bmesh, bpy
from mathutils import Vector, Quaternion

PATH_HALF = 3.2          # half width of the painted path (m)
CORRIDOR = 9.0           # nothing taller than a flower stands closer than this to the path axis (m)
VERGE = 28.0             # moved objects land within this band beyond the corridor
ZONES = [   # name, colatitude from the pond (deg), azimuth (deg), clearing radius (m), role
    ("Claro de inicio",   75, 250, 34, "inicio"),
    ("Aserradero",        70, 330, 38, "combate"),
    ("Estanque",           0,   0,  0, "agua"),
    ("Arboleda gigante",  80,  60, 16, "jefe"),
    ("Cueva de hongos",  115, 130, 14, "horda"),
    ("Mirador de peñascos", 105, 200, 14, "captura"),
]
ARCS = [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (5, 0), (2, 5), (1, 3)]    # loop + two shortcuts


def unit(v): v = np.asarray(v, float); return v / np.linalg.norm(v)

def basis(c):
    a = np.array([0, 0, 1.0]) if abs(c[2]) < 0.9 else np.array([1.0, 0, 0])
    e1 = unit(np.cross(c, a)); e2 = np.cross(c, e1)
    return e1, e2

def around(c, dist_m, az, R):
    e1, e2 = basis(c); t = dist_m / R
    return unit(math.cos(t) * c + math.sin(t) * (math.cos(az) * e1 + math.sin(az) * e2))

def arc_dist(D, A, B, R):
    """Distance (m) from each unit vector in D (n,3) to the great-circle arc A-B."""
    n = unit(np.cross(A, B)); ang = math.acos(np.clip(A @ B, -1, 1))
    proj = D - np.outer(D @ n, n); pn = np.linalg.norm(proj, axis=1, keepdims=True); pn[pn < 1e-9] = 1; proj = proj / pn
    t = np.arctan2(np.einsum('ij,j->i', np.cross(A, proj), n), proj @ A)
    inside = (t >= 0) & (t <= ang)
    side = np.arcsin(np.clip(np.abs(D @ n), 0, 1)) * R
    ends = np.minimum(np.arccos(np.clip(D @ A, -1, 1)), np.arccos(np.clip(D @ B, -1, 1))) * R
    return np.where(inside, side, ends)

def rotate_island(verts, src, dst, spin):
    q = Vector(src).rotation_difference(Vector(dst))
    q = Quaternion(Vector(dst), spin) @ q
    for v in verts: v.co = q @ v.co


def build(obj, R, islands_of, report):
    rng = np.random.default_rng(2024)
    me = obj.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    names = [m.name if m else "" for m in me.materials]
    slot = {n: i for i, n in enumerate(names)}
    gset = {v.index for f in bm.faces if f.material_index == 0 for v in f.verts}
    vmat = {}
    for f in bm.faces:
        for v in f.verts: vmat.setdefault(v.index, f.material_index)

    # ── islands ──
    items = []
    for comp in islands_of(bm):
        if any(v.index in gset for v in comp): continue
        P = np.array([tuple(v.co) for v in comp]); rs = np.linalg.norm(P, axis=1)
        order = np.argsort(rs); base = P[order[:max(1, int(0.12 * len(comp)))]]
        bdir = unit(base.mean(axis=0)); ext = P.max(0) - P.min(0)
        bext = (base.max(0) - base.min(0)).max()
        mname = names[vmat.get(comp[0].index, 0)]
        items.append(dict(id=len(items), v=comp, dir=bdir, mat=mname, height=float(rs.max() - rs.min()), crown=float(ext.max() / 2),
                          foot=max(0.6, float(bext / 2)), base=base.mean(axis=0)))
    def cat(it):
        m = it["mat"]
        if m.startswith("Agua") or m.startswith("Lodo"): return "water"
        if m == "Pasto_Mata": return "grass"
        if m.startswith("Flor"): return "flower"
        if m == "Hongo_Tallo": return "stalk"
        if m == "Hongo_Sombrero": return "cap"
        if m == "Junco": return "reed"
        if m.startswith("Rock"): return "rock"
        if m in ("Corteza", "Madera_Corte"): return "log"
        if m == "_12_tree.001": return "stump"
        if m == "tre__fin_.001": return "deadtree"
        return "tree"
    for it in items: it["cat"] = cat(it)
    print("BOSQUE islands", len(items), {c: sum(1 for i in items if i["cat"] == c) for c in sorted({i["cat"] for i in items})})

    # ── zones ──
    waters = [i for i in items if i["mat"].startswith("Agua")]
    pond = max(waters, key=lambda i: i["crown"])
    P0 = unit(pond["dir"]); e1, e2 = basis(P0)
    zdir = []
    for (nm, th, ph, clr, role) in ZONES:
        if th == 0: zdir.append(P0); continue
        t = math.radians(th); p = math.radians(ph)
        zdir.append(unit(math.cos(t) * P0 + math.sin(t) * (math.cos(p) * e1 + math.sin(p) * e2)))
    for a, b in ARCS:
        print(f"BOSQUE arc {ZONES[a][0]} -> {ZONES[b][0]}: {math.degrees(math.acos(zdir[a] @ zdir[b])) * R * math.pi / 180:.0f} m")
    arcs = [(zdir[a], zdir[b]) for a, b in ARCS]
    def d_path(D): return np.min([arc_dist(D, A, B, R) for A, B in arcs], axis=0)
    def d_zone(D, k): return np.arccos(np.clip(D @ zdir[k], -1, 1)) * R

    # ── occupancy ──
    occ_d = []; occ_r = []
    def occupy(d, r): occ_d.append(np.asarray(d)); occ_r.append(r)
    def free(d, r, gap=1.0):
        if not occ_d: return True
        O = np.array(occ_d); rr = np.array(occ_r)
        return bool(np.all(np.arccos(np.clip(O @ d, -1, 1)) * R > rr + r + gap))
    def usable(d, r):
        """Not on the path, not inside a clearing, not in the pond."""
        if d_path(d[None])[0] - r < CORRIDOR: return False
        for k, z in enumerate(ZONES):
            if z[3] and d_zone(d[None], k)[0] - r < z[3]: return False
        return True

    movers = [i for i in items if i["cat"] not in ("water",)]
    themed = set()

    def place(it, d, spin=None, scale_about=None, k=1.0):
        if k != 1.0:
            for v in it["v"]: v.co = Vector(scale_about) + (v.co - Vector(scale_about)) * k
        rotate_island(it["v"], it["dir"], d, rng.uniform(0, 2 * math.pi) if spin is None else spin)
        it["dir"] = unit(d); occupy(d, it["foot"] * (k if k != 1.0 else 1.0))

    def ring_spot(k, r0, r1, foot, tries=80):
        for _ in range(tries):
            d = around(zdir[k], rng.uniform(r0, r1), rng.uniform(0, 2 * math.pi), R)
            if free(d, foot) and d_path(d[None])[0] - foot > PATH_HALF + 2.0: return d
        return None

    # water stays; it blocks everything
    for it in items:
        if it["cat"] == "water": occupy(it["dir"], it["crown"])

    # ── themed pools: choose first, occupy what stays, then place ──
    def take(cat_, n, key=None):
        pool = [i for i in movers if i["cat"] == cat_ and i["id"] not in themed]
        if key: pool.sort(key=key, reverse=True)
        else: rng.shuffle(pool)
        got = pool[:n]
        themed.update(i["id"] for i in got)
        return got

    t_stump0 = take("stump", 5); t_log0 = take("log", 2)
    t_stump1 = take("stump", 18); t_log1 = take("log", 4); t_dead1 = take("deadtree", 14)
    t_trees3 = take("tree", 22, key=lambda i: i["height"])
    stalks = take("stalk", 999); caps = take("cap", 999)
    t_rock4 = take("rock", 8); t_rock5 = take("rock", 99)

    for it in movers:                      # what stays where it is (not on the path / in a clearing) claims its space first
        if it["id"] not in themed and usable(it["dir"], max(it["foot"], 0.3 * it["crown"])): occupy(it["dir"], it["foot"])

    def fill(group, k, r0, r1, extra=0.0, tries=80):
        for it in group:
            d = ring_spot(k, r0, r1, it["foot"] + extra, tries)
            if d is not None: place(it, d)

    fill(t_stump0, 0, 9, 14); fill(t_log0, 0, 6, 10)                      # 0 Claro de inicio: stumps + logs around the campfire
    fill(t_stump1, 1, 6, 30); fill(t_log1, 1, 3, 12); fill(t_dead1, 1, 14, 33)    # 1 Aserradero
    fill(t_trees3, 3, 20, 52, extra=4, tries=120)                         # 3 Arboleda gigante: the tallest trees in a ring
    # 4 Cueva de hongos: every mushroom, grown to giant size, in a ring; rocks form the entrance
    capdirs = np.array([c["dir"] for c in caps]) if caps else None
    used = set()
    for s_ in stalks:
        k = 4.5
        d = ring_spot(4, 6, 38, s_["foot"] * k)
        if d is None: continue
        j = None
        if capdirs is not None and len(used) < len(caps):
            dist = np.arccos(np.clip(capdirs @ s_["dir"], -1, 1)); dist[list(used)] = 9
            j = int(np.argmin(dist)); used.add(j)
        spin = rng.uniform(0, 2 * math.pi); base = s_["base"]
        for part in ([s_] + ([caps[j]] if j is not None else [])):
            for v in part["v"]: v.co = Vector(base) + (v.co - Vector(base)) * k
            rotate_island(part["v"], s_["dir"], d, spin)
            part["dir"] = unit(d)
        occupy(d, s_["foot"] * k)
    fill(t_rock4, 4, 34, 44)
    fill(t_rock5, 5, 16, 40)                                              # 5 Mirador de peñascos

    # ── everything else: keep it where it is, unless it stands on the path or in a clearing ──
    rng.shuffle(movers)
    moved = 0; failed = 0
    arc_len = np.array([math.acos(np.clip(A @ B, -1, 1)) * R for A, B in arcs])
    for it in movers:
        if it["id"] in themed: continue
        if usable(it["dir"], max(it["foot"], 0.3 * it["crown"])): continue
        done = False
        for _ in range(120):
            a = int(rng.choice(len(arcs), p=arc_len / arc_len.sum())); A, B = arcs[a]
            ang = math.acos(np.clip(A @ B, -1, 1)); t = rng.uniform(0, ang)
            n = unit(np.cross(A, B)); pt = unit(math.sin(ang - t) / math.sin(ang) * A + math.sin(t) / math.sin(ang) * B)
            off = CORRIDOR + it["foot"] + rng.uniform(0, VERGE) * (0.35 if it["cat"] in ("grass", "flower") else 1.0)
            s = (1 if rng.random() < 0.5 else -1) * off / R
            d = unit(math.cos(s) * pt + math.sin(s) * n)
            if usable(d, max(it["foot"], 0.3 * it["crown"])) and free(d, it["foot"], 0.5):
                place(it, d); moved += 1; done = True; break
        if not done: failed += 1
    print(f"BOSQUE moved {moved} objects off the path/clearings, {failed} could not be placed, themed {len(themed)}")
    report["moved"] = moved; report["failed"] = failed
    # check: how many solid objects still stand on the path or in a clearing
    bad = [i for i in items if i["cat"] in ("tree", "deadtree", "rock", "stump", "log", "stalk")
           and (d_path(i["dir"][None])[0] - max(i["foot"], 0.3 * i["crown"]) < PATH_HALF + 1.0
                or any(z[3] and d_zone(i["dir"][None], k)[0] < z[3] * 0.8 and i["id"] not in themed for k, z in enumerate(ZONES)))]
    print(f"BOSQUE check: {len(bad)} solid objects still on the path or in a clearing", [(b["cat"], round(b["foot"], 1)) for b in bad[:8]])

    bm.to_mesh(me); bm.free(); me.update()

    # ── path on the ground ──
    paint_path(obj, R, arcs, d_path)

    zones = dict(radius=R, pond=dict(dir=list(map(float, P0)), radius=float(pond["crown"])),
                 zones=[dict(name=ZONES[k][0], role=ZONES[k][4], dir=list(map(float, zdir[k])), clear=ZONES[k][3]) for k in range(len(ZONES))],
                 arcs=[dict(a=ZONES[a][0], b=ZONES[b][0]) for a, b in ARCS], pathHalfWidth=PATH_HALF,
                 # Unity finds the axis mapping by matching these against the vertices of the named materials
                 anchors=[dict(material="Agua_Estanque", dir=list(map(float, P0))),
                          dict(material="Hongo_Sombrero", dir=list(map(float, zdir[4]))),
                          dict(material="Rock_1_", dir=list(map(float, zdir[5])))])
    return zones


def paint_path(obj, R, arcs, d_path):
    """Subdivide the ground along the path (so the edges are smooth) and give those faces their own material."""
    me = obj.data
    mat = bpy.data.materials.get("Suelo_Camino") or bpy.data.materials.new("Suelo_Camino")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.20, 0.12, 0.07, 1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.95
    if mat.name not in [m.name for m in me.materials if m]: me.materials.append(mat)
    pslot = [m.name if m else "" for m in me.materials].index("Suelo_Camino")

    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    def near(faces, dist):
        C = np.array([tuple(f.calc_center_median()) for f in faces]); D = C / np.linalg.norm(C, axis=1, keepdims=True)
        return d_path(D) < dist
    for _ in range(2):                                  # two passes: ~4 m -> 2 m -> 1 m cells near the path
        gf = [f for f in bm.faces if f.material_index == 0]
        sel = [f for f, ok in zip(gf, near(gf, PATH_HALF + 6.0)) if ok]
        edges = list({e for f in sel for e in f.edges})
        old = len(bm.verts)
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True)
        bm.verts.ensure_lookup_table()
        for v in bm.verts[old:]: v.co = v.co.normalized() * R
        bm.faces.ensure_lookup_table()
    gf = [f for f in bm.faces if f.material_index == 0]
    C = np.array([tuple(f.calc_center_median()) for f in gf]); D = C / np.linalg.norm(C, axis=1, keepdims=True)
    dist = d_path(D)
    rng = np.random.default_rng(7)
    jitter = rng.uniform(-0.5, 0.5, len(gf))            # ragged edge
    painted = 0
    for f, d, j in zip(gf, dist, jitter):
        if d < PATH_HALF + j: f.material_index = pslot; painted += 1
    print(f"BOSQUE path: {painted} ground faces painted, ground faces total {len(gf)}")
    bm.to_mesh(me); bm.free(); me.update()
