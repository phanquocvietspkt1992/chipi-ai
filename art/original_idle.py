# Chipi Idle Prototype - Blender Python script
# Blender 3.6+ / 4.x
# Run: Blender > Scripting > New > paste this script > Run Script
# Creates an original, simple, editable chibi robot mascot and a looping Idle action.
# This is a primitive-based prototype model, not a finished production character.

import bpy
import math
from mathutils import Vector

# ------------------------------------------------------------
# Scene reset
# ------------------------------------------------------------
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for datablocks in (bpy.data.materials, bpy.data.actions):
    # Keep cleanup conservative; objects are the important part.
    pass

scene = bpy.context.scene
scene.frame_start = 1
scene.frame_end = 120
scene.render.fps = 30
scene.render.resolution_x = 1080
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 100

# ------------------------------------------------------------
# Materials
# ------------------------------------------------------------
def make_mat(name, color, roughness=0.42, metallic=0.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat

blue = make_mat("Chipi | Sky Blue", (0.20, 0.62, 0.88), 0.32, 0.08)
blue_dark = make_mat("Chipi | Blue Details", (0.08, 0.31, 0.52), 0.38)
ivory = make_mat("Chipi | Warm Ivory", (0.92, 0.94, 0.90), 0.48)
teal = make_mat("Chipi | Teal Eyes", (0.02, 0.48, 0.53), 0.22, 0.05)
black = make_mat("Chipi | Pupil", (0.012, 0.025, 0.035), 0.18)
pink = make_mat("Chipi | Cheeks", (1.0, 0.42, 0.48), 0.55)
gold = make_mat("Chipi | Antenna Star", (1.0, 0.72, 0.20), 0.3, 0.12)

# ------------------------------------------------------------
# Helpers
# ------------------------------------------------------------
def uv_sphere(name, loc, scale, mat, parent=None, segments=32, rings=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.shade_smooth()
    if mat:
        obj.data.materials.append(mat)
    if parent:
        obj.parent = parent
    return obj

def cylinder_between(name, p1, p2, radius, mat, vertices=24):
    direction = Vector(p2) - Vector(p1)
    midpoint = (Vector(p1) + Vector(p2)) / 2
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=direction.length, location=midpoint)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = direction.to_track_quat('Z', 'Y')
    bpy.ops.object.shade_smooth()
    if mat:
        obj.data.materials.append(mat)
    bevel = obj.modifiers.new("Soft rounded edges", 'BEVEL')
    bevel.width = radius * 0.35
    bevel.segments = 3
    obj.modifiers.new("Weighted corner normals", 'WEIGHTED_NORMAL')
    return obj

def set_parent_keep_transform(obj, parent):
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_world = world

# ------------------------------------------------------------
# Root and body
# Front of character faces -Y; camera looks toward +Y.
# ------------------------------------------------------------
root = bpy.data.objects.new("Chipi_Root", None)
bpy.context.collection.objects.link(root)
root.empty_display_type = 'CIRCLE'
root.empty_display_size = 0.35

body = uv_sphere("Body | rounded ivory torso", (0, 0, 1.18), (0.48, 0.38, 0.55), ivory)
set_parent_keep_transform(body, root)

# Belly/chest panel
chest = uv_sphere("Chest | blue panel", (0, -0.342, 1.22), (0.29, 0.055, 0.30), blue)
set_parent_keep_transform(chest, root)

# Letter C badge approximated with a small raised disk and a tiny highlight.
badge = uv_sphere("Chest | C badge", (0, -0.402, 1.23), (0.105, 0.025, 0.105), gold)
set_parent_keep_transform(badge, root)

# Head: large, slightly pear-like rounded silhouette
head = uv_sphere("Head | large rounded blue head", (0, 0, 2.12), (0.79, 0.62, 0.73), blue)
set_parent_keep_transform(head, root)

# Side accents: original fin-like details, not animal ears
left_fin = uv_sphere("Head | left side fin", (-0.73, 0.02, 2.18), (0.16, 0.22, 0.25), blue_dark)
right_fin = uv_sphere("Head | right side fin", (0.73, 0.02, 2.18), (0.16, 0.22, 0.25), blue_dark)
set_parent_keep_transform(left_fin, head)
set_parent_keep_transform(right_fin, head)

# Eyes and pupils on front (-Y)
eye_l = uv_sphere("Face | left eye white", (-0.29, -0.535, 2.20), (0.205, 0.105, 0.255), ivory)
eye_r = uv_sphere("Face | right eye white", (0.29, -0.535, 2.20), (0.205, 0.105, 0.255), ivory)
pupil_l = uv_sphere("Face | left teal iris", (-0.275, -0.628, 2.19), (0.105, 0.045, 0.145), teal)
pupil_r = uv_sphere("Face | right teal iris", (0.305, -0.628, 2.19), (0.105, 0.045, 0.145), teal)
dot_l = uv_sphere("Face | left pupil", (-0.265, -0.665, 2.18), (0.048, 0.025, 0.078), black)
dot_r = uv_sphere("Face | right pupil", (0.315, -0.665, 2.18), (0.048, 0.025, 0.078), black)
for obj in [eye_l, eye_r, pupil_l, pupil_r, dot_l, dot_r]:
    set_parent_keep_transform(obj, head)

# Eye highlights
hl_l = uv_sphere("Face | left eye highlight", (-0.30, -0.69, 2.255), (0.035, 0.018, 0.045), ivory)
hl_r = uv_sphere("Face | right eye highlight", (0.28, -0.69, 2.255), (0.035, 0.018, 0.045), ivory)
set_parent_keep_transform(hl_l, head)
set_parent_keep_transform(hl_r, head)

# Cheek panels
cheek_l = uv_sphere("Face | left cheek", (-0.53, -0.49, 1.98), (0.12, 0.035, 0.065), pink)
cheek_r = uv_sphere("Face | right cheek", (0.53, -0.49, 1.98), (0.12, 0.035, 0.065), pink)
set_parent_keep_transform(cheek_l, head)
set_parent_keep_transform(cheek_r, head)

# Tiny smile curve
curve = bpy.data.curves.new("Face | gentle smile curve", 'CURVE')
curve.dimensions = '3D'
curve.bevel_depth = 0.018
curve.bevel_resolution = 4
spline = curve.splines.new('BEZIER')
spline.bezier_points.add(2)
for bp, co in zip(spline.bezier_points, [(-0.12, -0.616, 1.99), (0, -0.635, 1.95), (0.12, -0.616, 1.99)]):
    bp.co = co
    bp.handle_left_type = 'AUTO'
    bp.handle_right_type = 'AUTO'
smile = bpy.data.objects.new("Face | smile", curve)
bpy.context.collection.objects.link(smile)
smile.data.materials.append(blue_dark)
set_parent_keep_transform(smile, head)

# Antenna stem and star-like rounded tip
antenna_stem = cylinder_between("Antenna | stem", (0, 0.0, 2.78), (0, 0.0, 3.02), 0.035, ivory)
set_parent_keep_transform(antenna_stem, head)
antenna_tip = uv_sphere("Antenna | glowing tip", (0, 0, 3.08), (0.105, 0.105, 0.105), gold)
set_parent_keep_transform(antenna_tip, head)

# Arms with rounded hands
arm_l = uv_sphere("Arm | left", (-0.53, 0, 1.40), (0.17, 0.18, 0.34), blue)
arm_r = uv_sphere("Arm | right", (0.53, 0, 1.40), (0.17, 0.18, 0.34), blue)
hand_l = uv_sphere("Hand | left", (-0.57, -0.01, 1.14), (0.16, 0.16, 0.15), ivory)
hand_r = uv_sphere("Hand | right", (0.57, -0.01, 1.14), (0.16, 0.16, 0.15), ivory)
for obj in [arm_l, arm_r, hand_l, hand_r]:
    set_parent_keep_transform(obj, root)

# Short legs and feet
leg_l = uv_sphere("Leg | left", (-0.22, 0, 0.68), (0.16, 0.17, 0.24), blue_dark)
leg_r = uv_sphere("Leg | right", (0.22, 0, 0.68), (0.16, 0.17, 0.24), blue_dark)
foot_l = uv_sphere("Foot | left", (-0.23, -0.08, 0.48), (0.22, 0.29, 0.13), ivory)
foot_r = uv_sphere("Foot | right", (0.23, -0.08, 0.48), (0.22, 0.29, 0.13), ivory)
for obj in [leg_l, leg_r, foot_l, foot_r]:
    set_parent_keep_transform(obj, root)

# ------------------------------------------------------------
# Simple Idle animation: gentle body bob, head tilt, arm sway,
# antenna tip bounce, and periodic eye blink.
# ------------------------------------------------------------
def key_loc(obj, frame, value):
    obj.location = value
    obj.keyframe_insert(data_path="location", frame=frame)

def key_rot(obj, frame, value):
    obj.rotation_euler = value
    obj.keyframe_insert(data_path="rotation_euler", frame=frame)

# Root/body subtle bob
base_z = root.location.z
for frame, z in [(1, 0.0), (31, 0.025), (61, 0.0), (91, -0.012), (121, 0.0)]:
    root.location.z = base_z + z
    root.keyframe_insert(data_path="location", frame=frame)

# Head micro-tilt
for frame, rot in [(1, (0, 0, -0.015)), (31, (0, 0, 0.025)), (61, (0, 0, 0.0)), (91, (0, 0, -0.025)), (121, (0, 0, -0.015))]:
    head.rotation_euler = rot
    head.keyframe_insert(data_path="rotation_euler", frame=frame)

# Arms sway slightly
for obj, sign in [(arm_l, -1), (arm_r, 1), (hand_l, -1), (hand_r, 1)]:
    for frame, angle in [(1, 0.0), (31, sign * 0.035), (61, 0.0), (91, -sign * 0.035), (121, 0.0)]:
        obj.rotation_euler[1] = angle
        obj.keyframe_insert(data_path="rotation_euler", frame=frame)

# Antenna tip gentle bounce
for frame, scale in [(1, (1,1,1)), (31, (1.05,1.05,0.94)), (61, (1,1,1)), (91, (0.96,0.96,1.05)), (121, (1,1,1))]:
    antenna_tip.scale = scale
    antenna_tip.keyframe_insert(data_path="scale", frame=frame)

# Blink by scaling eye whites and attached iris/pupil/highlights vertically.
eye_parts = [eye_l, eye_r, pupil_l, pupil_r, dot_l, dot_r, hl_l, hl_r]
for start in [42, 104]:
    for obj in eye_parts:
        original = obj.scale.copy()
        obj.scale = (original.x, original.y, original.z)
        obj.keyframe_insert(data_path="scale", frame=start-1)
        obj.scale.z = original.z * 0.08
        obj.keyframe_insert(data_path="scale", frame=start)
        obj.keyframe_insert(data_path="scale", frame=start+2)
        obj.scale = original
        obj.keyframe_insert(data_path="scale", frame=start+4)

# Make animation curves loop smoothly.
for obj in [root, head, arm_l, arm_r, hand_l, hand_r, antenna_tip] + eye_parts:
    if obj.animation_data and obj.animation_data.action:
        for fc in obj.animation_data.action.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = 'BEZIER'
            # Cycles modifier loops the idle action.
            fc.modifiers.new('CYCLES')

# Rename action(s) for clarity
for obj in [root, head, arm_l, arm_r, hand_l, hand_r, antenna_tip] + eye_parts:
    if obj.animation_data and obj.animation_data.action:
        obj.animation_data.action.name = "Chipi_Idle_Loop"

# ------------------------------------------------------------
# Stage, camera, and lighting for immediate preview
# ------------------------------------------------------------
floor_mat = make_mat("Stage | soft blue", (0.72, 0.84, 0.94), 0.8)
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0.30))
floor = bpy.context.object
floor.name = "Stage | floor"
floor.data.materials.append(floor_mat)

# Camera
bpy.ops.object.camera_add(location=(4.8, -8.5, 3.25))
camera = bpy.context.object
camera.name = "Camera | Chipi portrait"
target = Vector((0, 0, 1.72))
direction = target - camera.location
camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 4.2
scene.camera = camera

# Lighting
def area_light(name, loc, energy, size, target_point=(0,0,1.7)):
    bpy.ops.object.light_add(type='AREA', location=loc)
    light = bpy.context.object
    light.name = name
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (Vector(target_point) - light.location).to_track_quat('-Z', 'Y').to_euler()
    return light

area_light("Light | Key", (4, -5, 6), 500, 4)
area_light("Light | Fill", (-4, -3, 3.5), 280, 3)
area_light("Light | Rim", (0, 4, 5), 450, 3)

# World and render settings
scene.world.color = (0.55, 0.62, 0.72)
scene.render.engine = 'BLENDER_EEVEE_NEXT' if hasattr(bpy.types, "SceneEEVEE") else 'BLENDER_EEVEE'
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False

# Set a clean preview frame and select root.
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active = root

# Helpful viewport framing
for area in bpy.context.screen.areas:
    if area.type == 'VIEW_3D':
        area.spaces.active.region_3d.view_distance = 5.5
        area.spaces.active.region_3d.view_location = (0, 0, 1.6)

print("Chipi Idle Prototype created. Scrub frames 1-120 or play the timeline.")
