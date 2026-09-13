"""
generate_player_character.py
------------------------------------------------------------------
Generates a new low-poly humanoid player character for Repsyche, replacing
PlayerBody.fbx (a single 48-vertex static mesh with zero bones/vertex
groups - confirmed by reimporting it in Blender - which is why no real
animation was possible on it).

Built as separate limb objects (Hips, Torso, Head, ArmL/R, LegL/R) each a
closed, watertight box - not one merged mesh - so:
  1. Normals are reliably correct. bmesh.ops.recalc_face_normals is only
     reliable on a genuinely closed manifold volume; the desert terrain
     plane (open, non-manifold) hit exactly this ambiguity earlier and
     needed Cull Off in the shader to become visible from above. A sealed
     box has no such ambiguity.
  2. Unity can rotate each limb independently around its own local pivot
     (shoulder/hip joint) for a real per-limb walk cycle - swinging legs
     and arms - instead of bobbing the whole body as one rigid unit.

Also saves the working .blend directly into the user's own open Blender
project file (BLEND_SAVE_PATH) so the result is visible there without a
separate export/import step - the previous generator scripts only ever
wrote the FBX, never that project file, which is why it kept showing
stale content.

USAGE
    blender --background --python generate_player_character.py
------------------------------------------------------------------
"""

import bpy
import bmesh
import os

EXPORT_PATH = "J:/Repsyche/Repsyche/Assets/Models/PlayerCharacter.fbx"
BLEND_SAVE_PATH = "E:/Blender/Projects/repsycheclaude.blend"

SKIN_COLOR = (0.85, 0.64, 0.49)
OUTFIT_COLOR = (0.22, 0.32, 0.52)


def add_box(bm, x0, x1, y0, y1, z0, z1):
    """A closed, watertight box (all 6 faces), built in LOCAL space relative
    to whatever pivot the caller has in mind - the object's own .location is
    set separately to place that pivot in world space."""
    v = {}
    for xi, x in enumerate((x0, x1)):
        for yi, y in enumerate((y0, y1)):
            for zi, z in enumerate((z0, z1)):
                v[(xi, yi, zi)] = bm.verts.new((x, y, z))
    faces = [
        (v[(0, 0, 0)], v[(1, 0, 0)], v[(1, 1, 0)], v[(0, 1, 0)]),
        (v[(0, 0, 1)], v[(0, 1, 1)], v[(1, 1, 1)], v[(1, 0, 1)]),
        (v[(0, 0, 0)], v[(0, 1, 0)], v[(0, 1, 1)], v[(0, 0, 1)]),
        (v[(1, 0, 0)], v[(1, 0, 1)], v[(1, 1, 1)], v[(1, 1, 0)]),
        (v[(0, 0, 0)], v[(0, 0, 1)], v[(1, 0, 1)], v[(1, 0, 0)]),
        (v[(0, 1, 0)], v[(1, 1, 0)], v[(1, 1, 1)], v[(0, 1, 1)]),
    ]
    for f in faces:
        bm.faces.new(f)


def new_vertex_color_material(name, color, saturation=1.1, roughness=0.55):
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


def build_part(name, half_w, half_d, length, color, pivot_at_top=True):
    """A single sealed box limb, built pivot-relative: pivot_at_top hangs the
    box downward from local Z=0 (shoulders/hips -> limb hanging below);
    pivot_at_top=False grows it upward from local Z=0 (torso/head stacking
    on top of what's below them)."""
    bm = bmesh.new()
    if pivot_at_top:
        add_box(bm, -half_w, half_w, -half_d, half_d, -length, 0.0)
    else:
        add_box(bm, -half_w, half_w, -half_d, half_d, 0.0, length)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    for p in mesh.polygons:
        p.use_smooth = False

    attr = mesh.color_attributes.new(name="Col", type='FLOAT_COLOR', domain='POINT')
    for v in mesh.vertices:
        attr.data[v.index].color = (color[0], color[1], color[2], 1.0)
    mesh.color_attributes.active_color = attr
    mesh.materials.append(new_vertex_color_material("M_" + name, color))

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def set_parent_keep_world(child, parent, pivot_world):
    child.location = pivot_world
    child.parent = parent
    # parent.matrix_world can be stale immediately after a previous reparent/location
    # change until the dependency graph is re-evaluated - without this, each child's
    # inverse-parent correction is computed against the wrong matrix and the whole
    # chain drifts apart (this is exactly what produced a disconnected floating torso
    # on the first render).
    bpy.context.view_layer.update()
    child.matrix_parent_inverse = parent.matrix_world.inverted()
    bpy.context.view_layer.update()


def main():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    # Root sits at the feet (world Z=0) to match the existing anchor
    # convention: parented under Player at local Y=-1, i.e. capsule bottom.
    root = bpy.data.objects.new("PlayerBody", None)
    root.empty_display_size = 0.1
    bpy.context.collection.objects.link(root)
    root.location = (0.0, 0.0, 0.0)

    hip_h, torso_h, head_h = 0.85, 1.05, 1.55

    hips = build_part("Hips", 0.21, 0.13, 0.20, OUTFIT_COLOR, pivot_at_top=False)
    set_parent_keep_world(hips, root, (0, 0, hip_h))

    torso = build_part("Torso", 0.23, 0.14, 0.50, OUTFIT_COLOR, pivot_at_top=False)
    set_parent_keep_world(torso, hips, (0, 0, torso_h))

    head = build_part("Head", 0.15, 0.15, 0.30, SKIN_COLOR, pivot_at_top=False)
    set_parent_keep_world(head, torso, (0, 0, head_h))

    shoulder_h = 1.50
    arm_l = build_part("ArmL", 0.07, 0.07, 0.55, SKIN_COLOR, pivot_at_top=True)
    set_parent_keep_world(arm_l, torso, (0.32, 0, shoulder_h))
    arm_r = build_part("ArmR", 0.07, 0.07, 0.55, SKIN_COLOR, pivot_at_top=True)
    set_parent_keep_world(arm_r, torso, (-0.32, 0, shoulder_h))

    leg_l = build_part("LegL", 0.09, 0.09, 0.85, OUTFIT_COLOR, pivot_at_top=True)
    set_parent_keep_world(leg_l, root, (0.12, 0, hip_h))
    leg_r = build_part("LegR", 0.09, 0.09, 0.85, OUTFIT_COLOR, pivot_at_top=True)
    set_parent_keep_world(leg_r, root, (-0.12, 0, hip_h))

    all_objs = [root, hips, torso, head, arm_l, arm_r, leg_l, leg_r]
    for o in bpy.data.objects:
        o.select_set(o in all_objs)
    bpy.context.view_layer.objects.active = root

    os.makedirs(os.path.dirname(EXPORT_PATH), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=EXPORT_PATH,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        object_types={'EMPTY', 'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='OFF',
        colors_type='SRGB',
        add_leaf_bones=False,
        bake_anim=False,
    )
    print("[generate_player_character] Exported to {0}".format(EXPORT_PATH))

    os.makedirs(os.path.dirname(BLEND_SAVE_PATH), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_SAVE_PATH)
    print("[generate_player_character] Saved working file to {0}".format(BLEND_SAVE_PATH))


if __name__ == "__main__":
    main()
