"""Shape closed 3D shells from the approved front-view silhouette."""
import math

PIXELS_PER_UNIT = 146
image = bpy.data.images.load(str(project / "Unity/Assets/Resources/ChipiConcept.png"))
image.pack()
projection = bpy.data.materials.new("ChipiProjection")
projection.use_nodes = True
nodes = projection.node_tree.nodes
texture = nodes.new('ShaderNodeTexImage')
texture.image = image
emission = nodes.new('ShaderNodeEmission')
projection.node_tree.links.new(texture.outputs["Color"], emission.inputs["Color"])
projection.node_tree.links.new(emission.outputs[0], nodes.get("Material Output").inputs["Surface"])
shells = make_mat("Ceramic | soft blue", (.29, .52, .72), .45, .10)
armor = make_mat("Ceramic | warm white", (.58, .65, .72), .47, .10)
joints = make_mat("Joints | cool slate", (.13, .22, .29), .52)

def point(x, y, depth=0):
    return ((x - 254) / PIXELS_PER_UNIT, depth, (478 - y) / PIXELS_PER_UNIT + .35)

def smooth_outline(points):
    result = []
    for i, b in enumerate(points):
        a, c, d = points[i - 1], points[(i + 1) % len(points)], points[(i + 2) % len(points)]
        for step in range(5):
            t = step / 5
            result.append(tuple(.5 * ((2 * b[j]) + (-a[j] + c[j]) * t +
                (2*a[j] - 5*b[j] + 4*c[j] - d[j]) * t*t +
                (-a[j] + 3*b[j] - 3*c[j] + d[j]) * t*t*t) for j in range(2)))
    return result

def shell(name, outline, center, front, back, rear_material, seam=0):
    border = smooth_outline(outline)
    count = len(border)
    vertices, coordinates, faces, materials = [], [], [], []
    rings = [(math.sin(i * math.pi / 20), -front * math.cos(i * math.pi / 20))
             for i in range(1, 11)]
    rings += [(math.cos(i * math.pi / 16), back * math.sin(i * math.pi / 16))
              for i in range(1, 8)]
    for radius, depth in rings:
        for px, py in border:
            x = center[0] + (px - center[0]) * radius
            y = center[1] + (py - center[1]) * radius
            vertices.append(point(x, y, depth + seam))
            coordinates.append((x / 512, 1 - y / 512))
    for ring in range(len(rings) - 1):
        for i in range(count):
            following = (i + 1) % count
            faces.append((ring*count + i, (ring+1)*count + i,
                          (ring+1)*count + following, ring*count + following))
            materials.append(int(ring >= 9))
    for offset, depth, material in [(0, -front, 0), ((len(rings)-1)*count, back, 1)]:
        pole = len(vertices)
        vertices.append(point(*center, depth + seam))
        coordinates.append((center[0]/512, 1-center[1]/512))
        for i in range(count):
            face = (pole, offset+i, offset+(i+1)%count)
            faces.append(face if material == 0 else face[::-1])
            materials.append(material)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    uv = mesh.uv_layers.new(name='UVMap')
    for poly, material in zip(mesh.polygons, materials):
        poly.use_smooth = True
        poly.material_index = material
        for index in poly.loop_indices:
            uv.data[index].uv = coordinates[mesh.loops[index].vertex_index]
    mesh.materials.append(projection)
    mesh.materials.append(rear_material)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj

shell("SpeechMouth", [(258,42),(302,47),(341,64),(367,90),(383,129),(397,143),
    (404,160),(403,200),(398,215),(385,224),(375,246),(348,273),(315,286),
    (270,291),(215,288),(175,281),(147,265),(128,244),(119,229),(108,223),
    (103,202),(102,168),(107,149),(120,136),(130,106),(154,77),(184,60),(221,47)],
    (254,167), .53, .78, shells)
shell("Body | chest and belly", [(196,295),(217,289),(270,293),(304,289),(320,302),
    (325,329),(329,361),(330,388),(318,413),(290,429),(260,436),(234,433),
    (204,419),(185,397),(181,372),(185,334)], (255,363), .42, .38, armor)
shell("Arm | left", [(189,295),(199,307),(191,332),(180,342),(175,356),(169,382),
    (163,395),(168,403),(172,416),(165,420),(160,415),(154,416),(156,432),
    (149,435),(139,429),(136,418),(138,405),(130,397),(128,379),(132,359),
    (143,344),(152,329),(154,315),(168,301)], (158,368), .27, .21, joints, .035)
shell("Arm | right", [(319,294),(331,294),(345,302),(355,319),(357,336),(370,346),
    (378,362),(382,381),(378,395),(372,402),(374,413),(370,429),(363,435),
    (357,431),(359,417),(354,411),(347,417),(341,415),(342,405),(347,397),
    (340,388),(334,375),(331,357),(322,341),(316,321)], (350,368),
    .28, .21, joints, .035)
shell("Leg | left", [(194,401),(207,412),(231,424),(245,427),(243,445),(235,473),
    (221,476),(180,475),(174,471),(173,462),(181,446),(183,427)], (211,448),
    .32, .31, shells, .08)
shell("Leg | right", [(283,425),(306,419),(317,405),(325,414),(327,436),(331,453),
    (338,466),(335,473),(321,476),(279,474),(274,469),(274,452),(269,434)],
    (300,449), .34, .30, shells, .08)
