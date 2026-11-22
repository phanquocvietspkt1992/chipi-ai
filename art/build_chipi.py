"""Build the approved-reference Chipi rig and preview with Blender 5."""

from pathlib import Path

project = Path(__file__).resolve().parent.parent
source = (project / "art/original_idle.py").read_text()
exec(source[: source.index("# Belly/chest panel")])
bpy.data.objects.remove(body, do_unlink=True)
exec((project / "art/reference_mesh.py").read_text())

pieces = {
    obj.name: (obj, obj.matrix_world.copy())
    for obj in scene.objects
    if obj.type == "MESH"
}
armature = bpy.data.armatures.new("ChipiSkeleton")
rig = bpy.data.objects.new("ChipiRig", armature)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")


def bone(name, start, end, parent=None):
    result = armature.edit_bones.new(name)
    result.head, result.tail = start, end
    if parent:
        result.parent = armature.edit_bones[parent]


bone("Body", (0, 0, 0.35), (0, 0, 1.65))
bone("Head", (0, 0, 1.62), (0, 0, 2.7), "Body")
for side, sign in [("L", -1), ("R", 1)]:
    bone("Arm" + side, (0.49 * sign, 0, 1.68), (0.57 * sign, 0, 1.1), "Body")
    bone("Leg" + side, (0.22 * sign, 0, 0.86), (0.22 * sign, 0, 0.39), "Body")
bpy.ops.object.mode_set(mode="OBJECT")


def attach(obj, world, name):
    obj.parent = rig
    obj.parent_type = "BONE"
    obj.parent_bone = name
    obj.matrix_world = world


for name, (obj, world) in pieces.items():
    part = name.split(" | ")[0]
    target = "Body"
    if part == "SpeechMouth":
        target = "Head"
    if part in ("Arm", "Hand", "Leg", "Foot"):
        target = ("Arm" if part in ("Arm", "Hand") else "Leg") + (
            "L" if "left" in name else "R"
        )
    attach(obj, world, target)

marker = bpy.data.objects.new("FaceDirection", None)
scene.collection.objects.link(marker)
marker.location = (0, -1, 2)
bpy.context.view_layer.update()
attach(marker, marker.matrix_world.copy(), "Head")
bpy.data.objects.remove(root, do_unlink=True)

motions = {
    "Idle": [(1, 0, -0.03, 0, 0), (46, 0.033, 0.04, 0, 0), (91, 0, -0.03, 0, 0)],
    "Greeting": [
        (1, 0, 0, 0, 0),
        (12, 0.03, 0.08, 1.15, 0),
        (22, 0.04, 0.09, 1.65, 0),
        (32, 0.03, 0.06, 1.25, 0),
        (42, 0.03, 0.04, 1.6, 0),
        (56, 0, 0, 0, 0),
    ],
    "Listening": [(1, 0, 0.12, 0, 0), (38, 0.021, 0.18, 0, 0), (76, 0, 0.12, 0, 0)],
    "Talking": [
        (1, 0, -0.04, -0.18, 0.12),
        (15, 0.025, 0.07, -0.29, 0.21),
        (31, 0, -0.04, -0.18, 0.12),
    ],
    "Happy": [
        (1, 0, 0, 0, 0),
        (13, 0.16, -0.10, -1.02, 1.02),
        (25, 0, 0.08, -0.22, 0.22),
        (37, 0.11, 0, -0.71, 0.71),
        (52, 0, 0, 0, 0),
    ],
}
data = rig.animation_data_create()
for name, frames in motions.items():
    data.action = bpy.data.actions.new(name)
    for frame, height, tilt, left, right in frames:
        body = rig.pose.bones["Body"]
        body.location.y = height
        body.keyframe_insert("location", frame=frame)
        for joint, angle in [("Head", tilt), ("ArmL", left), ("ArmR", right)]:
            pivot = rig.pose.bones[joint]
            pivot.rotation_mode = "XYZ"
            pivot.rotation_euler.z = angle
            pivot.keyframe_insert("rotation_euler", frame=frame)
    action = data.action
    data.action = None
    track = data.nla_tracks.new()
    track.name = name
    track.strips.new(name, 1, action)
    track.mute = True
for track in data.nla_tracks:
    track.mute = False
scene.frame_set(1)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(
    filepath=str(project / "Unity/Assets/Resources/Chipi.fbx"),
    use_selection=True,
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=True,
)

for track in data.nla_tracks:
    track.mute = track.name != "Idle"
exec(source[source.index("floor_mat =") : source.index('area_light("Light | Rim"')])
area_light("Light | Rim", (0, 4, 5), 450, 3)
camera.location = (0, -9, 1.85)
camera.rotation_euler = (
    (Vector((0, 0, 1.85)) - camera.location).to_track_quat("-Z", "Y").to_euler()
)
scene.view_settings.view_transform = "Standard"
scene.frame_set(1)
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 12
scene.render.resolution_percentage = 50
scene.render.filepath = str(project / "build/Chipi-model.png")
bpy.ops.wm.save_as_mainfile(filepath=str(project / "art/Chipi.blend"))
bpy.ops.render.render(write_still=True)
