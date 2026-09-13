"""
generate_worm_boss.py
------------------------------------------------------------------
Procedurally generates an enormous segmented worm-boss enemy for Repsyche: a
brain-tissue-skinned body tapering from a wide toothed circular maw to a
whip-thin tail, lined its full length with uniform tendril-arms, bristling
with randomly-sized obsidian spikes. Built entirely from closed, watertight
primitive geometry (rings, cones, small pyramids) - the same discipline
established for the desert terrain and thorn spikes: every piece is either a
genuinely sealed manifold volume on its own, or (body + throat + teeth)
forms one continuous closed manifold where every edge is shared by exactly
two faces, which is what makes bmesh.ops.recalc_face_normals reliable.

Follows this project's existing PSX pipeline: no UV unwrapping. The Brutalist
assets and PSXAesthetic.shader already establish that convention here -
Unity's Custom/PSXAesthetic shader projects a texture triplanar from object
space instead of using UVs, so the mesh deliberately has none. The actual
Unity-side materials (using the Higgsfield-generated, quantized-palette
WormBoss_Flesh.png / WormBoss_Obsidian.png textures) are wired up separately
by Assets/Editor/SetupWormBoss.cs after import - this script's own Blender
materials exist only for previewing the result here.

USAGE
    blender --background --python generate_worm_boss.py
------------------------------------------------------------------
"""

import bpy
import bmesh
import math
import os
import random

from mathutils import Vector, noise

SEED = 2044

EXPORT_PATH = "J:/Repsyche/Repsyche/Assets/Models/WormBoss.fbx"
BLEND_SAVE_PATH = "E:/Blender/Projects/repsycheclaude.blend"
FLESH_TEXTURE = "J:/Repsyche/Repsyche/Assets/Textures/WormBoss_Flesh.png"
OBSIDIAN_TEXTURE = "J:/Repsyche/Repsyche/Assets/Textures/WormBoss_Obsidian.png"

COLLECTION_NAME = "WormBoss"

BASE_RADIUS = 1.7
BODY_LENGTH = 16.0
N_SEGMENTS = 22
SIDES = 10

N_ARMS = 48
N_SPIKES = 36

MAW_ROWS = 4
MAW_TEETH_PER_ROW = 14


# ============================================================
# Shared helpers
# ============================================================

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


def image_material(name, image_path, roughness=0.75):
    """Preview-only material (no UVs, no triplanar) using Generated coordinates -
    just enough to sanity-check proportions/placement when rendering here. The
    real Unity material is built separately by SetupWormBoss.cs."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex_coord = nt.nodes.new("ShaderNodeTexCoord")
    image_tex = nt.nodes.new("ShaderNodeTexImage")
    try:
        image_tex.image = bpy.data.images.load(image_path, check_existing=True)
    except RuntimeError:
        pass
    nt.links.new(tex_coord.outputs["Generated"], image_tex.inputs["Vector"])
    nt.links.new(image_tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = roughness
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def _basis_from_dir(d):
    d = d.normalized()
    ref = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
    side = d.cross(ref).normalized()
    up = side.cross(d).normalized()
    return side, up


def add_pyramid(bm, base_center, right, up, direction, length, width):
    """A tiny closed pyramid (tooth): a square base cap plus 4 triangular
    sides to an apex, fully sealed on its own."""
    hw = width * 0.5
    b0 = bm.verts.new(base_center + right * hw + up * hw)
    b1 = bm.verts.new(base_center - right * hw + up * hw)
    b2 = bm.verts.new(base_center - right * hw - up * hw)
    b3 = bm.verts.new(base_center + right * hw - up * hw)
    tip = bm.verts.new(base_center + direction * length)
    bm.faces.new((b0, b1, b2, b3))
    bm.faces.new((b0, b3, tip))
    bm.faces.new((b3, b2, tip))
    bm.faces.new((b2, b1, tip))
    bm.faces.new((b1, b0, tip))


# ============================================================
# 1. Main body: segmented brain-tissue tube + nested toothed maw
# ============================================================

def body_radius(t):
    """0 = head/maw end, 1 = tail tip."""
    taper = 1.0 - 0.88 * (t ** 0.85)
    segment_bulge = 0.08 * math.sin(t * math.pi * 5.0)
    return max(0.05, taper + segment_bulge)


def wrinkle_offset(t, ang, seed_offset):
    """Layered noise approximating convoluted brain-fold ridges wrapped
    around the body."""
    p = Vector((math.cos(ang) * 2.2, math.sin(ang) * 2.2, t * 9.0 + seed_offset))
    ridge = noise.noise(p * 1.6) * 0.5
    fine = noise.noise(p * 5.0 + Vector((50, 0, 0))) * 0.18
    return ridge + fine


def build_body():
    random.seed(SEED)
    seed_offset = random.uniform(0, 1000)
    bm = bmesh.new()

    total_rings = N_SEGMENTS
    rings = []
    for i in range(total_rings + 1):
        t = i / total_rings
        x = t * BODY_LENGTH
        r = body_radius(t) * BASE_RADIUS
        path_y = math.sin(t * math.pi * 1.3) * BODY_LENGTH * 0.05
        path_z = math.sin(t * math.pi * 2.1 + 0.6) * BODY_LENGTH * 0.035

        ring = []
        for s in range(SIDES):
            ang = (s / SIDES) * math.tau
            wrinkle = 1.0 + wrinkle_offset(t, ang, seed_offset) * 0.22
            rr = max(0.03, r * wrinkle)
            y = path_y + math.cos(ang) * rr
            z = path_z + math.sin(ang) * rr
            ring.append(bm.verts.new((x, y, z)))
        rings.append(ring)
    bm.verts.ensure_lookup_table()

    for i in range(total_rings):
        r0, r1 = rings[i], rings[i + 1]
        for s in range(SIDES):
            s2 = (s + 1) % SIDES
            bm.faces.new((r0[s], r0[s2], r1[s2], r1[s]))

    # tail: taper to a whip-thin closed point
    tail_ring = rings[-1]
    tail_center = Vector((0, 0, 0))
    for v in tail_ring:
        tail_center += v.co
    tail_center /= SIDES
    tail_tip = bm.verts.new(tail_center + Vector((BASE_RADIUS * 0.4, 0, 0)))
    for s in range(SIDES):
        s2 = (s + 1) % SIDES
        bm.faces.new((tail_ring[s], tail_ring[s2], tail_tip))

    # --- nested maw: shares rings[0] as the mouth's outer lip, then recedes
    # INTO the head (same +X direction the body extends) via progressively
    # smaller tooth-ring rows, ending in a sealed dark throat cap. This stays
    # a valid closed manifold because every edge of the shared lip ring is
    # still used by exactly two faces total: one from the outer body's first
    # band, one from the throat's first band. ---
    throat_rings = [rings[0]]
    for row in range(1, MAW_ROWS + 1):
        depth_t = (row / (MAW_ROWS + 1)) * 0.10
        x = depth_t * BODY_LENGTH
        shrink = 1.0 - (row / (MAW_ROWS + 1)) * 0.8
        r = body_radius(0.0) * BASE_RADIUS * shrink
        ring = []
        for s in range(SIDES):
            ang = (s / SIDES) * math.tau
            ring.append(bm.verts.new((x, math.cos(ang) * r, math.sin(ang) * r)))
        throat_rings.append(ring)

    for i in range(len(throat_rings) - 1):
        r0, r1 = throat_rings[i], throat_rings[i + 1]
        for s in range(SIDES):
            s2 = (s + 1) % SIDES
            bm.faces.new((r0[s], r0[s2], r1[s2], r1[s]))

    last_throat = throat_rings[-1]
    throat_center = Vector((0, 0, 0))
    for v in last_throat:
        throat_center += v.co
    throat_center /= SIDES
    throat_cap = bm.verts.new(throat_center + Vector((BASE_RADIUS * 0.15, 0, 0)))
    for s in range(SIDES):
        s2 = (s + 1) % SIDES
        bm.faces.new((last_throat[s], last_throat[s2], throat_cap))

    # teeth: rows of small pyramids at each throat ring (not the outer lip),
    # pointing inward toward the throat's central axis and slightly deeper -
    # based right at the wall surface and growing into already-empty interior
    # space, so they don't plunge into (and self-intersect with) solid geometry
    # the way an earlier design mistake with the thorn-spike barbs did.
    for row_ring in throat_rings[1:]:
        ring_center = Vector((row_ring[0].co.x, 0, 0))
        for v in row_ring:
            outward = (v.co - ring_center)
            outward.x = 0
            if outward.length < 1e-6:
                continue
            inward = -outward.normalized()
            axial = Vector((1, 0, 0))
            direction = (inward * 0.75 + axial * 0.35).normalized()
            side, up = _basis_from_dir(direction)
            tooth_len = BASE_RADIUS * random.uniform(0.10, 0.16)
            tooth_w = BASE_RADIUS * random.uniform(0.05, 0.08)
            add_pyramid(bm, v.co, side, up, direction, tooth_len, tooth_w)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new("WormBoss_Body")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    for p in mesh.polygons:
        p.use_smooth = True

    obj = bpy.data.objects.new("WormBoss_Body", mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(image_material("M_WormBoss_Flesh_Preview", FLESH_TEXTURE, roughness=0.8))
    return obj


# ============================================================
# 2. Uniform tendril-arms lining the body
# ============================================================

def build_arm(name, base_r, length):
    bm = bmesh.new()
    rings_n = 3
    sides = 5
    rings = []
    for i in range(rings_n + 1):
        t = i / rings_n
        r = base_r * (1.0 - t) ** 1.2
        r = max(0.015, r)
        curl = (t ** 1.6) * length * 0.35
        ring = []
        for s in range(sides):
            ang = (s / sides) * math.tau
            ring.append(bm.verts.new((curl, math.cos(ang) * r, math.sin(ang) * r - t * length)))
        rings.append(ring)
    base_center = bm.verts.new((0, 0, 0))
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((base_center, rings[0][s2], rings[0][s]))
    for i in range(rings_n):
        r0, r1 = rings[i], rings[i + 1]
        for s in range(sides):
            s2 = (s + 1) % sides
            bm.faces.new((r0[s], r0[s2], r1[s2], r1[s]))
    tip_ring = rings[-1]
    tip_center = Vector((0, 0, 0))
    for v in tip_ring:
        tip_center += v.co
    tip_center /= sides
    tip = bm.verts.new(tip_center + Vector((0, 0, -length * 0.15)))
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((tip_ring[s], tip_ring[s2], tip))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    md = bpy.data.meshes.new(name)
    bm.to_mesh(md)
    bm.free()
    md.update()
    for p in md.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, md)
    bpy.context.collection.objects.link(ob)
    return ob


# ============================================================
# 3. Random obsidian spikes
# ============================================================

def build_spike(name, height, base_radius, sides=6):
    bm = bmesh.new()
    rings_n = 5
    rings = []
    for i in range(rings_n + 1):
        t = i / rings_n
        r = max(0.02, base_radius * (1.0 - t) ** 1.4)
        jag = 1.0 + random.uniform(-0.15, 0.15)
        ring = []
        for s in range(sides):
            ang = (s / sides) * math.tau
            rr = r * (1.0 + 0.08 * math.sin(ang * 3 + i))
            ring.append(bm.verts.new((math.cos(ang) * rr * jag, math.sin(ang) * rr * jag, height * t)))
        rings.append(ring)
    base_center = bm.verts.new((0, 0, 0))
    for s in range(sides):
        s2 = (s + 1) % sides
        bm.faces.new((base_center, rings[0][s2], rings[0][s]))
    for i in range(rings_n):
        r0, r1 = rings[i], rings[i + 1]
        for s in range(sides):
            s2 = (s + 1) % sides
            bm.faces.new((r0[s], r0[s2], r1[s2], r1[s]))
    last = rings[-1]
    tip = bm.verts.new((0, 0, height * 1.05))
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


# ============================================================
# 4. Assemble, export, save
# ============================================================

def surface_point_and_normal(t, ang):
    x = t * BODY_LENGTH
    r = body_radius(t) * BASE_RADIUS
    path_y = math.sin(t * math.pi * 1.3) * BODY_LENGTH * 0.05
    path_z = math.sin(t * math.pi * 2.1 + 0.6) * BODY_LENGTH * 0.035
    wrinkle = 1.0 + wrinkle_offset(t, ang, 0.0) * 0.22
    rr = max(0.03, r * wrinkle)
    y = path_y + math.cos(ang) * rr
    z = path_z + math.sin(ang) * rr
    point = Vector((x, y, z))
    normal = Vector((0, math.cos(ang), math.sin(ang))).normalized()
    return point, normal


def main():
    # Headless Blender still loads its default startup scene (Cube, Camera, Light) even
    # with no file argument - clear_collection() only manages the WormBoss collection,
    # so without this the default Cube sits at the world origin, right where the head
    # ends up (this is exactly what a stray white box in the first render turned out to
    # be). Other generator scripts in this project clear the whole scene explicitly for
    # the same reason.
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    random.seed(SEED)
    coll = clear_collection(COLLECTION_NAME)

    body = build_body()
    link_only(body, coll)

    obsidian_mat = image_material("M_WormBoss_Obsidian_Preview", OBSIDIAN_TEXTURE, roughness=0.15)

    for i in range(N_ARMS):
        t = 0.12 + 0.85 * (i / max(1, N_ARMS - 1))
        ang = random.choice([math.radians(a) for a in (110, 140, 160, 200, 220, 250)])
        ang += random.uniform(-0.15, 0.15)
        point, normal = surface_point_and_normal(t, ang)

        arm = build_arm("WormBoss_Arm_{0}".format(i), BASE_RADIUS * 0.06, BASE_RADIUS * random.uniform(1.1, 1.4))
        arm.data.materials.append(image_material("M_WormBoss_Arm_{0}".format(i), FLESH_TEXTURE, roughness=0.85))

        arm.location = point
        arm.rotation_euler = normal.to_track_quat('-Z', 'Y').to_euler()
        arm.rotation_euler.rotate_axis('Z', random.uniform(-0.3, 0.3))
        link_only(arm, coll)

    for i in range(N_SPIKES):
        t = random.uniform(0.04, 0.96)
        ang = random.uniform(0, math.tau)
        point, normal = surface_point_and_normal(t, ang)

        height = BASE_RADIUS * random.uniform(0.4, 1.6)
        base_r = height * random.uniform(0.12, 0.22)
        spike = build_spike("WormBoss_Spike_{0}".format(i), height, base_r)
        spike.data.materials.append(obsidian_mat)

        spike.location = point
        jitter = Vector((random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3)))
        spike.rotation_euler = (normal + jitter).normalized().to_track_quat('Z', 'Y').to_euler()
        link_only(spike, coll)

    for o in bpy.data.objects:
        o.select_set(o.name in coll.objects)
    bpy.context.view_layer.objects.active = body

    os.makedirs(os.path.dirname(EXPORT_PATH), exist_ok=True)
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
        add_leaf_bones=False,
        bake_anim=False,
    )
    print("[generate_worm_boss] Exported {0} objects to {1}".format(len(coll.objects), EXPORT_PATH))

    os.makedirs(os.path.dirname(BLEND_SAVE_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_SAVE_PATH)
    print("[generate_worm_boss] Saved working file to {0}".format(BLEND_SAVE_PATH))


if __name__ == "__main__":
    main()
