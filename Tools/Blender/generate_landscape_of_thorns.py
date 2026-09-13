"""
generate_landscape_of_thorns.py
------------------------------------------------------------------
Procedural "Landscape of Thorns" generator for Repsyche.

Concept: the 1993 Sandia National Laboratories "Human Interference Task
Force" nuclear semiotics proposal (Michael Brill) for marking buried
nuclear waste across deep time - a vast, flat field bristling with
megalithic concrete spikes thrust out of the ground at chaotic,
aggressive angles, radiating outward from a central "burst" so the
whole site reads, wordlessly and across any culture or language, as
"something here is dangerous, do not dig."

Everything is procedural bmesh geometry + legacy Blender Texture
displacement + vertex colors - no external assets. The desert plane
is silvery sand; the spikes are angular, beveled, faceted "polished
concrete" megaliths, individually tilted and leaning away from a
central burst point, some bristling with smaller secondary barbs.

USAGE
    Blender GUI: Scripting tab -> Text -> Open -> this file -> Run Script.
    Headless:    blender --background --python generate_landscape_of_thorns.py

Re-running deletes and rebuilds the "LandscapeOfThorns" collection.
------------------------------------------------------------------
"""

import bpy
import bmesh
import math
import random

from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

# ============================================================
# CONFIG
# ============================================================

SEED = 2027

EXPORT_PATH = "J:/Repsyche/Repsyche/Assets/Models/LandscapeOfThorns.fbx"
COLLECTION_NAME = "LandscapeOfThorns"

# --- desert plane (metres) ---
DESERT_SIZE = 220.0      # vast: 220m x 220m
DESERT_RES = 130         # grid subdivisions per side - higher, to resolve steep dune faces cleanly
DUNE_MACRO_AMPLITUDE = 14.0   # immense broad rolling dune mass
DUNE_RIDGE_AMPLITUDE = 11.0   # sharpened directional ridge crests - the "heavy wave angles"
DUNE_RIDGE_FREQUENCY = 0.085  # ridge wavelength ~ 1/freq metres
DUNE_RIDGE_ANGLE_DEG = 35     # dominant orientation of the ridge lines
DUNE_RIDGE_SHARPNESS = 0.45   # < 1 sharpens the sine into steep-sided ridges instead of smooth rolling waves
GRAIN_AMPLITUDE = 0.4         # fine sand-grain ripple, scaled up to match the now-much-bigger dunes

# --- megalithic spikes ---
N_SPIKES = 74
BURST_CENTER = Vector((0.0, 0.0))
BURST_RADIUS = 106.0            # spikes scattered across the FULL plane (half-width is 110m)
BURST_RADIUS_MIN = 6.0          # keep a small clearing at the very center
SPIKE_HEIGHT_RANGE = (18.0, 95.0)   # heavily increased - true megalithic scale
SPIKE_BASE_RADIUS_FACTOR = 0.12  # base radius as a fraction of height
TILT_MIN_DEG = 18
TILT_MAX_DEG = 62
BARB_CHANCE = 0.4


def smoothstep(a, b, x):
    if b == a:
        return 0.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def clear_collection(name):
    coll = bpy.data.collections.get(name)
    if coll:
        for obj in list(coll.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(coll)
    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)
    return coll


def link_only(obj, collection):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    collection.objects.link(obj)


def unhide_collection(coll):
    coll.hide_viewport = False
    coll.hide_render = False
    for layer_coll in bpy.context.view_layer.layer_collection.children:
        if layer_coll.collection == coll:
            layer_coll.exclude = False
            layer_coll.hide_viewport = False
    for obj in coll.objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.hide_render = False


def frame_view_on_collection(coll):
    for obj in bpy.data.objects:
        obj.select_set(False)
    for obj in coll.objects:
        obj.select_set(True)
    if coll.objects:
        bpy.context.view_layer.objects.active = next(iter(coll.objects))

    framed = False
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type != 'VIEW_3D':
                continue
            region = next((r for r in area.regions if r.type == 'WINDOW'), None)
            if region is None:
                continue
            with bpy.context.temp_override(window=window, area=area, region=region):
                bpy.ops.view3d.view_selected()
            framed = True
    return framed


def new_vertex_color_material(name, saturation=1.0, roughness=0.4):
    """Blender-side preview material only (Unity gets its own material via
    the vertex-color shader). Low saturation/desaturated by default - this
    is a silvery concrete/sand palette, not the saturated clay look."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    hs = nt.nodes.new("ShaderNodeHueSaturation")
    hs.inputs["Saturation"].default_value = saturation
    nt.links.new(vc.outputs["Color"], hs.inputs["Color"])
    nt.links.new(hs.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = roughness
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def add_vertex_colors_point(mesh_data, color_fn):
    attr = mesh_data.color_attributes.new(name="Col", type='FLOAT_COLOR', domain='POINT')
    for v in mesh_data.vertices:
        c = color_fn(v)
        attr.data[v.index].color = (c[0], c[1], c[2], 1.0)
    mesh_data.color_attributes.active_color = attr
    return attr


def add_displace(obj, name, tex_type, strength, scale, mid=0.5, **texkw):
    tex = bpy.data.textures.new(name + "Tex", type=tex_type)
    for k, val in texkw.items():
        setattr(tex, k, val)
    tex.noise_scale = scale
    mod = obj.modifiers.new(name=name, type='DISPLACE')
    mod.texture = tex
    mod.strength = strength
    mod.mid_level = mid
    mod.texture_coords = 'LOCAL'
    return mod


def apply_modifier(obj, name):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=name)


# ============================================================
# 1. Vast silvery desert plane
# ============================================================

def dune_height(x, y):
    """Immense dune elevation with sharpened directional ridge crests ("heavy wave
    angles") rather than smooth rolling hills: a broad macro noise mass carries the
    overall dune bulk, a sine-based ridge field sharpened via a fractional power
    (preserving sign) gives each ridge a steep, angular crest, and a small amount of
    fine grain noise textures the surface."""
    macro = noise.noise(Vector((x * 0.006, y * 0.006, 0.0))) * DUNE_MACRO_AMPLITUDE

    ang = math.radians(DUNE_RIDGE_ANGLE_DEG)
    xr = x * math.cos(ang) - y * math.sin(ang)
    yr = x * math.sin(ang) + y * math.cos(ang)
    # meander the ridge lines instead of perfectly straight sine bands
    meander = noise.noise(Vector((x * 0.02, y * 0.02, 10.0))) * 2.2
    raw_wave = math.sin(xr * DUNE_RIDGE_FREQUENCY + meander + yr * 0.01)
    sharp = math.copysign(abs(raw_wave) ** DUNE_RIDGE_SHARPNESS, raw_wave)
    ridge = sharp * DUNE_RIDGE_AMPLITUDE

    grain = noise.noise(Vector((x * 0.35, y * 0.35, 5.0))) * GRAIN_AMPLITUDE
    return macro + ridge + grain


def build_desert_plane():
    random.seed(SEED)
    half = DESERT_SIZE / 2.0
    n = DESERT_RES

    bm = bmesh.new()
    grid = [[None] * (n + 1) for _ in range(n + 1)]
    for iy in range(n + 1):
        for ix in range(n + 1):
            x = -half + DESERT_SIZE * (ix / n)
            y = -half + DESERT_SIZE * (iy / n)
            z = dune_height(x, y)
            grid[iy][ix] = bm.verts.new((x, y, z))

    bm.verts.ensure_lookup_table()
    for iy in range(n):
        for ix in range(n):
            v00 = grid[iy][ix]
            v10 = grid[iy][ix + 1]
            v11 = grid[iy + 1][ix + 1]
            v01 = grid[iy + 1][ix]
            bm.faces.new((v00, v10, v11, v01))

    # Seal the plane into a closed, watertight solid: an open heightmap sheet is normally
    # fine for a game terrain collider, but it is genuinely non-manifold, and
    # bmesh.ops.recalc_face_normals has no reliable "outward" reference on a non-manifold
    # shape (this exact ambiguity previously made DesertGround's top surface invisible
    # from above until the shader was set to Cull Off). Closing it with a flat bottom and
    # perimeter walls removes the ambiguity at the source: recalc_face_normals is only
    # reliable on a genuinely closed manifold volume, which this now is.
    bottom_z = min(v.co.z for v in bm.verts) - 20.0
    boundary = []
    boundary.extend(grid[0][ix] for ix in range(n + 1))
    boundary.extend(grid[iy][n] for iy in range(1, n + 1))
    boundary.extend(grid[n][ix] for ix in range(n - 1, -1, -1))
    boundary.extend(grid[iy][0] for iy in range(n - 1, 0, -1))

    bottom_verts = [bm.verts.new((v.co.x, v.co.y, bottom_z)) for v in boundary]
    m = len(boundary)
    for i in range(m):
        i2 = (i + 1) % m
        bm.faces.new((boundary[i], boundary[i2], bottom_verts[i2], bottom_verts[i]))

    bottom_center = bm.verts.new((0.0, 0.0, bottom_z))
    for i in range(m):
        i2 = (i + 1) % m
        bm.faces.new((bottom_verts[i2], bottom_verts[i], bottom_center))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new("DesertGround")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new("DesertGround", mesh)
    bpy.context.collection.objects.link(obj)
    for p in mesh.polygons:
        p.use_smooth = True

    # --- silvery vertex colors with subtle warm/cool patchiness ---
    silver = (0.72, 0.735, 0.77)

    def sand_color(v):
        n1 = noise.noise(v.co * 0.05) * 0.06          # broad tonal patches
        n2 = noise.noise(v.co * 0.6 + Vector((50, 0, 0))) * 0.03  # fine speckle
        warm_cool = noise.noise(v.co * 0.03 + Vector((0, 0, 100))) * 0.04
        r = silver[0] + n1 + n2 + warm_cool
        g = silver[1] + n1 + n2
        b = silver[2] + n1 + n2 - warm_cool * 0.5
        return (max(0.0, min(1.0, r)), max(0.0, min(1.0, g)), max(0.0, min(1.0, b)))

    add_vertex_colors_point(mesh, sand_color)
    mesh.materials.append(new_vertex_color_material("M_DesertSand", saturation=0.5, roughness=0.55))
    return obj


# ============================================================
# 2. Megalithic thorn spikes (polished concrete)
# ============================================================

def _basis_from_dir(d):
    """Orthonormal basis (side, up2) perpendicular to direction d, used to
    build a barb's local cross-section frame."""
    d = d.normalized()
    ref = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    side = d.cross(ref).normalized()
    up2 = side.cross(d).normalized()
    return side, up2


def build_barb(name, attach_point, shaft_dir, out_deg, twist_rad, barb_height, base_r, sides=5):
    """A small secondary barb thorn jutting outward+upward from a point partway up the
    main shaft, built as its own separate closed object rather than appended into the
    parent shaft's bmesh. It visually embeds into the shaft (its base sits inside the
    shaft's solid geometry) exactly as before, but overlapping geometry between two
    separate objects is completely normal - it's only self-intersection *within one
    mesh* that Unity's FBX importer flags and discards, which is what actually caused
    the holes: barbs merged into the shaft mesh created exactly that. A capped base
    keeps this object itself closed and watertight too, even though that cap ends up
    buried inside the shaft and is never seen.
    """
    side, up2 = _basis_from_dir(shaft_dir)
    horiz = (side * math.cos(twist_rad) + up2 * math.sin(twist_rad)).normalized()
    barb_dir = (shaft_dir * math.cos(math.radians(out_deg)) + horiz * math.sin(math.radians(out_deg))).normalized()
    b_side, b_up = _basis_from_dir(barb_dir)

    bm = bmesh.new()
    rings_n = 3
    rings = []
    for ri in range(rings_n + 1):
        t = ri / rings_n
        r = max(0.02, base_r * (1.0 - t) ** 1.4)
        center = attach_point + barb_dir * (barb_height * t)
        ring = []
        for s in range(sides):
            ang = (s / sides) * math.tau
            offset = (b_side * math.cos(ang) + b_up * math.sin(ang)) * r
            ring.append(bm.verts.new(center + offset))
        rings.append(ring)
    tip = bm.verts.new(attach_point + barb_dir * (barb_height * 1.08))

    base_ring = rings[0]
    base_center = bm.verts.new(attach_point)
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((base_center, base_ring[s2], base_ring[s]))

    for ri in range(rings_n):
        r0, r1 = rings[ri], rings[ri + 1]
        for s in range(sides):
            s2 = (s + 1) % sides
            bm.faces.new((r0[s], r0[s2], r1[s2], r1[s]))
    last = rings[rings_n]
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((last[s], last[s2], tip))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    md = bpy.data.meshes.new(name)
    bm.to_mesh(md)
    bm.free()
    md.update()
    ob = bpy.data.objects.new(name, md)
    bpy.context.collection.objects.link(ob)
    return ob


def build_spike(name, height, base_radius, sides, jaggedness, twist_per_ring, n_barbs):
    bm = bmesh.new()
    rings_n = 9
    rings = []
    kink_dir = Vector((random.uniform(-1, 1), random.uniform(-1, 1))).normalized()
    kink_strength = height * random.uniform(0.05, 0.14)

    for ri in range(rings_n + 1):
        t = ri / rings_n
        radius = max(0.03, base_radius * (1.0 - t) ** 1.35)
        z = height * t
        drift = kink_dir * (kink_strength * (t ** 1.7))
        ring = []
        twist = ri * twist_per_ring
        for s in range(sides):
            ang = (s / sides) * math.tau + twist
            jag = 1.0 + random.uniform(-1, 1) * jaggedness * (1.0 - t * 0.5)
            r = radius * jag
            x = math.cos(ang) * r + drift.x
            y = math.sin(ang) * r + drift.y
            ring.append(bm.verts.new((x, y, z)))
        rings.append(ring)
    bm.verts.ensure_lookup_table()

    # wide jagged base cap
    base_center = bm.verts.new((0, 0, 0))
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((base_center, rings[0][s2], rings[0][s]))

    for ri in range(rings_n):
        r0, r1 = rings[ri], rings[ri + 1]
        for s in range(sides):
            s2 = (s + 1) % sides
            bm.faces.new((r0[s], r0[s2], r1[s2], r1[s]))

    # sharp tip
    last_ring = rings[rings_n]
    last_center = Vector((0, 0, 0))
    for v in last_ring:
        last_center += v.co
    last_center /= sides
    tip = bm.verts.new(last_center + Vector((0, 0, height * 0.06)))
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((last_ring[s], last_ring[s2], tip))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # --- modifiers: bevel for a cast/formed concrete edge, fine noise for grain ---
    md = bpy.data.meshes.new(name)
    bm.to_mesh(md)
    bm.free()
    md.update()
    ob = bpy.data.objects.new(name, md)
    bpy.context.collection.objects.link(ob)

    bpy.context.view_layer.objects.active = ob
    for o in bpy.data.objects:
        o.select_set(False)
    ob.select_set(True)

    bevel = ob.modifiers.new(name="ConcreteBevel", type='BEVEL')
    bevel.width = max(0.02, base_radius * 0.06)
    bevel.segments = 2
    apply_modifier(ob, "ConcreteBevel")

    grain = add_displace(ob, "ConcreteGrain", 'DISTORTED_NOISE', 0.03, 0.6)
    apply_modifier(ob, "ConcreteGrain")

    # deliberately NOT smooth-shaded: flat facets read as cast/quarried
    # concrete rather than an organic clay blob.

    # secondary barbs bristling off the mid-shaft - separate closed objects (see
    # build_barb's doc comment for why), built in the same local space as the shaft
    # so the caller can apply the identical world transform to both.
    barb_objs = []
    for bi in range(n_barbs):
        ri = random.randint(2, rings_n - 2)
        t = ri / rings_n
        attach = Vector((0, 0, height * t)) + kink_dir.to_3d() * (kink_strength * (t ** 1.7))
        barb_objs.append(build_barb(
            name + "_Barb{0}".format(bi), attach, Vector((0, 0, 1)),
            out_deg=random.uniform(35, 75),
            twist_rad=random.uniform(0, math.tau),
            barb_height=height * random.uniform(0.18, 0.32),
            base_r=base_radius * (1.0 - t) * random.uniform(0.35, 0.55),
        ))

    return ob, barb_objs


# ============================================================
# 3. Scatter + tilt spikes outward from the burst center, export
# ============================================================

def main():
    random.seed(SEED)
    coll = clear_collection(COLLECTION_NAME)

    ground = build_desert_plane()
    link_only(ground, coll)

    bm_t = bmesh.new()
    bm_t.from_mesh(ground.data)
    bvh = BVHTree.FromBMesh(bm_t)

    def raycast_down(x, y):
        # raised well above the tallest possible dune + spike combination now that
        # both have been scaled up heavily
        origin = Vector((x, y, 700.0))
        loc, normal, idx, dist = bvh.ray_cast(origin, Vector((0, 0, -1)), 1400.0)
        return loc

    concrete = (0.55, 0.565, 0.60)
    concrete_dark = (0.42, 0.43, 0.47)

    for i in range(N_SPIKES):
        # bias toward the burst radius with sqrt for even area coverage
        r = BURST_RADIUS_MIN + (BURST_RADIUS - BURST_RADIUS_MIN) * math.sqrt(random.random())
        a = random.uniform(0, math.tau)
        x, y = BURST_CENTER.x + math.cos(a) * r, BURST_CENTER.y + math.sin(a) * r
        loc = raycast_down(x, y)
        if loc is None:
            continue

        # weighted toward mid-height with occasional towering megaliths
        h_min, h_max = SPIKE_HEIGHT_RANGE
        height = h_min + (h_max - h_min) * (random.random() ** 1.8)
        base_r = height * SPIKE_BASE_RADIUS_FACTOR * random.uniform(0.8, 1.25)
        sides = random.randint(5, 8)
        jaggedness = random.uniform(0.12, 0.24)
        twist_per_ring = random.uniform(-0.22, 0.22)
        n_barbs = random.randint(1, 3) if random.random() < BARB_CHANCE else 0

        ob, barb_objs = build_spike(
            "ThornSpike_{0}".format(i), height, base_r, sides, jaggedness, twist_per_ring, n_barbs
        )

        t = random.random()
        base_col = tuple(concrete_dark[c] * (1 - t) + concrete[c] * t for c in range(3))

        def spike_color(v, base_col=base_col, height=height):
            h = max(0.0, min(1.0, v.co.z / height))
            n = noise.noise(v.co * 0.4) * 0.05
            # slightly cleaner/lighter toward the tip, grimier at the base
            shade = base_col[0] + h * 0.12 + n, base_col[1] + h * 0.12 + n, base_col[2] + h * 0.12 + n
            return tuple(max(0.0, min(1.0, c)) for c in shade)

        add_vertex_colors_point(ob.data, spike_color)
        ob.data.materials.append(new_vertex_color_material("M_Thorn_{0}".format(i), saturation=0.7, roughness=0.35))

        # tilt: lean outward, away from the burst center, plus chaotic jitter
        radial_dir = Vector((math.cos(a), math.sin(a), 0))
        lean_deg = random.uniform(TILT_MIN_DEG, TILT_MAX_DEG)
        jitter_axis = Vector((random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3), 0))
        lean_axis = (Vector((-radial_dir.y, radial_dir.x, 0)) + jitter_axis).normalized()
        rot = Matrix.Rotation(math.radians(lean_deg), 4, lean_axis)
        spin = Matrix.Rotation(random.uniform(0, math.tau), 4, Vector((0, 0, 1)))
        world_transform = Matrix.Translation(loc) @ rot @ spin
        sink = Vector((0, 0, base_r * 0.4))

        # sink the base into the sand so it reads as bursting through, not resting on top
        ob.matrix_world = world_transform
        ob.location -= sink
        link_only(ob, coll)

        for barb_ob in barb_objs:
            add_vertex_colors_point(barb_ob.data, spike_color)
            barb_ob.data.materials.append(ob.data.materials[0])
            # Same rigid transform as the parent shaft - barbs were built in that same
            # local space, so this reproduces the identical tilt/position/sink.
            barb_ob.matrix_world = world_transform
            barb_ob.location -= sink
            link_only(barb_ob, coll)

    bm_t.free()

    # --- export the whole collection as FBX into the Unity Assets folder ---
    import os
    os.makedirs(os.path.dirname(EXPORT_PATH), exist_ok=True)
    for o in bpy.data.objects:
        o.select_set(False)
    for o in coll.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = next(iter(coll.objects), None)

    bpy.ops.export_scene.fbx(
        filepath=EXPORT_PATH,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        object_types={'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        colors_type='SRGB',
        add_leaf_bones=False,
        bake_anim=False,
    )

    unhide_collection(coll)
    bbox_max_z = max((o.matrix_world.translation.z + 40.0) for o in coll.objects) if coll.objects else 0.0
    framed = frame_view_on_collection(coll)

    print("=" * 70)
    print("[generate_landscape_of_thorns] SUCCESS")
    print("[generate_landscape_of_thorns] {0} objects in collection '{1}'".format(len(coll.objects), COLLECTION_NAME))
    print("[generate_landscape_of_thorns] FBX exported to: {0}".format(EXPORT_PATH))
    if not framed:
        print("[generate_landscape_of_thorns] No open 3D Viewport found to auto-frame "
              "(expected in --background mode).")
    print("=" * 70)


if __name__ == "__main__":
    main()
