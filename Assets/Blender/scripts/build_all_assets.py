import bpy
import os
import re
import math
import tempfile
import mathutils
import bmesh

# -----------------------------------------------------------------------------
# SVG Cleaning & Color Inlining
# -----------------------------------------------------------------------------
def clean_svg_content(svg_path):
    with open(svg_path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()

    # Strip embedded base64 raster images and filter definitions
    content = re.sub(r'<image[^>]*>', '', content)
    content = re.sub(r'<filter.*?</filter>', '', content, flags=re.DOTALL)

    # Extract CSS rules from <style> blocks
    style_blocks = re.findall(r'<style[^>]*>(.*?)</style>', content, flags=re.DOTALL)
    class_fills = {}
    for block in style_blocks:
        rules = re.findall(r'([^{]+)\{([^}]+)\}', block)
        for selectors, decls in rules:
            fill_m = re.search(r'fill:\s*([^;]+);', decls)
            if fill_m:
                val = fill_m.group(1).strip()
                # Gradient fills are blend-mode overlays the importer can't render; drop them.
                if val.startswith('url('):
                    val = 'none'
                for sel in selectors.split(','):
                    s = sel.strip()
                    if s.startswith('.'):
                        class_fills[s[1:]] = val

    # For every XML tag with class="...", insert fill attribute if not present
    def repl_tag(match):
        tag_str = match.group(0)
        if 'fill=' in tag_str:
            return tag_str
        m_cls = re.search(r'class="([^"]+)"', tag_str)
        if not m_cls:
            m_cls = re.search(r"class='([^']+)'", tag_str)
        if m_cls:
            for c in m_cls.group(1).split():
                if c in class_fills:
                    # Inline 'none' too, otherwise stroke-only paths fall back to SVG's default black fill.
                    color_val = class_fills[c]
                    if tag_str.endswith('/>'):
                        return tag_str[:-2] + f' fill="{color_val}"/>'
                    elif tag_str.endswith('>'):
                        return tag_str[:-1] + f' fill="{color_val}">'
        return tag_str

    content = re.sub(r'<[a-zA-Z0-9_\-]+[^>]*>', repl_tag, content)

    temp_file = tempfile.NamedTemporaryFile('w', suffix='.svg', delete=False, encoding='utf-8')
    temp_file.write(content)
    temp_file.close()
    return temp_file.name

# -----------------------------------------------------------------------------
# Material Shader Setup (PBR Principled BSDF)
# -----------------------------------------------------------------------------
def convert_materials_to_pbr(materials, roughness=0.65, metallic=0.0, emission_col=None, emission_strength=0.0):
    for mat in materials:
        if not mat or not mat.node_tree:
            continue
        tree = mat.node_tree
        diffuse_node = None
        output_node = None
        color = (0.8, 0.8, 0.8, 1.0)
        for n in tree.nodes:
            if n.type == 'BSDF_DIFFUSE':
                diffuse_node = n
                if 'Color' in n.inputs:
                    color = list(n.inputs['Color'].default_value)
            elif n.type == 'OUTPUT_MATERIAL':
                output_node = n

        # Clear and rebuild with Principled BSDF
        tree.nodes.clear()
        out = tree.nodes.new(type='ShaderNodeOutputMaterial')
        pbr = tree.nodes.new(type='ShaderNodeBsdfPrincipled')
        pbr.inputs['Base Color'].default_value = color
        pbr.inputs['Roughness'].default_value = roughness
        pbr.inputs['Metallic'].default_value = metallic

        if emission_col and emission_strength > 0:
            if 'Emission Color' in pbr.inputs:
                pbr.inputs['Emission Color'].default_value = emission_col
                pbr.inputs['Emission Strength'].default_value = emission_strength

        tree.links.new(pbr.outputs['BSDF'], out.inputs['Surface'])

# -----------------------------------------------------------------------------
# Mesh Normalization & Solidification
# -----------------------------------------------------------------------------
def process_curves_to_mesh(obj_list, thickness=0.04):
    # SVG layers import coplanar and z-fight; stack them in document order (later = nearer the
    # viewer) with a gap of 0.2% of the tallest layer, i.e. ~0.002 units once normalized to height 1.
    height = max(o.dimensions.y for o in obj_list)
    for i, o in enumerate(obj_list):
        o.location.z += i * height * 0.002
    for o in obj_list:
        o.select_set(True)
    bpy.context.view_layer.objects.active = obj_list[0]
    bpy.ops.object.convert(target='MESH')
    if len(obj_list) > 1:
        bpy.ops.object.join()
    mesh_obj = bpy.context.active_object

    # Stroke-only paths leave loose edges/verts that Unity drops on import but that inflate the
    # bounds used for normalization; remove them first.
    bm = bmesh.new()
    bm.from_mesh(mesh_obj.data)
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context='EDGES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_edges], context='VERTS')
    bm.to_mesh(mesh_obj.data)
    bm.free()

    # Add Solidify modifier
    mod = mesh_obj.modifiers.new(name='Solidify', type='SOLIDIFY')
    mod.thickness = thickness
    mod.offset = 0.0 # center depth
    bpy.ops.object.modifier_apply(modifier='Solidify')

    return mesh_obj

def normalize_mesh(mesh_obj, target_height=1.0, is_river=False, max_depth=0.05):
    # Calculate bounding box
    mesh = mesh_obj.data
    if len(mesh.vertices) == 0:
        return
    min_x = min(v.co.x for v in mesh.vertices)
    max_x = max(v.co.x for v in mesh.vertices)
    min_y = min(v.co.y for v in mesh.vertices)
    max_y = max(v.co.y for v in mesh.vertices)
    min_z = min(v.co.z for v in mesh.vertices)
    max_z = max(v.co.z for v in mesh.vertices)

    cx = (min_x + max_x) / 2.0
    cy = (min_y + max_y) / 2.0
    cz = (min_z + max_z) / 2.0

    width = max_x - min_x
    height = max_y - min_y

    # Translate vertices to center
    for v in mesh.vertices:
        v.co.x -= cx
        v.co.y -= cy
        v.co.z -= cz

    # Scale vertices
    if is_river:
        # For river, normalize width to 1.0 and height to 1.0 so Unity stretches it cleanly
        scale_x = 1.0 / max(0.001, width)
        scale_y = 1.0 / max(0.001, height)
        for v in mesh.vertices:
            v.co.x *= scale_x
            v.co.y *= scale_y
    else:
        scale = target_height / max(0.001, height)
        for v in mesh.vertices:
            v.co.x *= scale
            v.co.y *= scale
            v.co.z *= scale

    # Layer stacking leaves sheets up to ~0.8 deep; squash Z (keeps layer order) so mesh
    # thickness never competes with the game's depth bands.
    depth = max(v.co.z for v in mesh.vertices) - min(v.co.z for v in mesh.vertices)
    if max_depth and depth > max_depth:
        for v in mesh.vertices:
            v.co.z *= max_depth / depth

    mesh.update()

# -----------------------------------------------------------------------------
# Action / Animation Creation Helpers
# -----------------------------------------------------------------------------
def create_action(target_obj, action_name):
    if not target_obj.animation_data:
        target_obj.animation_data_create()
    act = bpy.data.actions.new(name=action_name)
    target_obj.animation_data.action = act
    return act

def push_action_to_nla(target_obj, action, track_name=None):
    if not track_name:
        track_name = action.name + '_track'
    track = target_obj.animation_data.nla_tracks.new()
    track.name = track_name
    track.strips.new(name=action.name, start=1, action=action)

# -----------------------------------------------------------------------------
# Asset Animation Generators
# -----------------------------------------------------------------------------
def animate_sun(sun_obj):
    # One 120-frame loop: a full turn plus two scale pulses (60-frame period divides the loop).
    create_action(sun_obj, 'sun_idle')
    for frame, rot, sc in ((1, 0, 1.0), (30, 0.5, 1.08), (60, 1.0, 1.0), (90, 1.5, 1.08), (120, 2.0, 1.0)):
        sun_obj.rotation_euler = (0, 0, math.pi * rot)
        sun_obj.scale = (sc, sc, 1.0)
        sun_obj.keyframe_insert(data_path='rotation_euler', frame=frame)
        sun_obj.keyframe_insert(data_path='scale', frame=frame)

def animate_cloud(cloud_obj, drift_amount=0.25, duration=240):
    act = create_action(cloud_obj, 'cloud_drift')
    cloud_obj.location = (0, 0, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=1)

    cloud_obj.location = (drift_amount, 0.02, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=duration // 4)

    cloud_obj.location = (0, 0, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=duration // 2)

    cloud_obj.location = (-drift_amount, -0.02, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=(duration * 3) // 4)

    cloud_obj.location = (0, 0, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=duration)
    push_action_to_nla(cloud_obj, act)

def animate_cloud_back(cloud_obj, drift_amount=0.15, duration=360):
    act = create_action(cloud_obj, 'cloud_back_drift')
    cloud_obj.location = (0, 0, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=1)

    cloud_obj.location = (drift_amount, 0.01, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=duration // 4)

    cloud_obj.location = (0, 0, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=duration // 2)

    cloud_obj.location = (-drift_amount, -0.01, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=(duration * 3) // 4)

    cloud_obj.location = (0, 0, 0)
    cloud_obj.keyframe_insert(data_path='location', frame=duration)
    push_action_to_nla(cloud_obj, act)

def animate_river(river_obj, name, duration=30, amp=0.04):
    act = create_action(river_obj, name)
    # Undulation displacement and swell
    river_obj.location = (0, 0, 0)
    river_obj.scale = (1.0, 1.0, 1.0)
    river_obj.keyframe_insert(data_path='location', frame=1)
    river_obj.keyframe_insert(data_path='scale', frame=1)

    river_obj.location = (amp, amp * 0.75, 0)
    river_obj.scale = (1.02, 1.04, 1.0)
    river_obj.keyframe_insert(data_path='location', frame=duration // 4)
    river_obj.keyframe_insert(data_path='scale', frame=duration // 4)

    river_obj.location = (0, 0, 0)
    river_obj.scale = (1.0, 1.0, 1.0)
    river_obj.keyframe_insert(data_path='location', frame=duration // 2)
    river_obj.keyframe_insert(data_path='scale', frame=duration // 2)

    river_obj.location = (-amp, -amp * 0.75, 0)
    river_obj.scale = (0.98, 0.96, 1.0)
    river_obj.keyframe_insert(data_path='location', frame=(duration * 3) // 4)
    river_obj.keyframe_insert(data_path='scale', frame=(duration * 3) // 4)

    river_obj.location = (0, 0, 0)
    river_obj.scale = (1.0, 1.0, 1.0)
    river_obj.keyframe_insert(data_path='location', frame=duration)
    river_obj.keyframe_insert(data_path='scale', frame=duration)
    push_action_to_nla(river_obj, act)

def animate_boat(boat_obj):
    # Action 1: boat_idle (60 frames, loop, 2s)
    act_idle = create_action(boat_obj, 'boat_idle')
    boat_obj.rotation_euler = (0, 0, 0)
    boat_obj.location = (0, 0, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=1)
    boat_obj.keyframe_insert(data_path='location', frame=1)

    boat_obj.rotation_euler = (math.radians(3), 0, math.radians(7))
    boat_obj.location = (0, 0.025, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=15)
    boat_obj.keyframe_insert(data_path='location', frame=15)

    boat_obj.rotation_euler = (0, 0, 0)
    boat_obj.location = (0, 0, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=30)
    boat_obj.keyframe_insert(data_path='location', frame=30)

    boat_obj.rotation_euler = (-math.radians(3), 0, -math.radians(7))
    boat_obj.location = (0, -0.025, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=45)
    boat_obj.keyframe_insert(data_path='location', frame=45)

    boat_obj.rotation_euler = (0, 0, 0)
    boat_obj.location = (0, 0, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=60)
    boat_obj.keyframe_insert(data_path='location', frame=60)
    push_action_to_nla(boat_obj, act_idle)

    # Action 2: boat_sail_flutter (45 frames, loop, 1.5s)
    act_flutter = create_action(boat_obj, 'boat_sail_flutter')
    boat_obj.rotation_euler = (0, 0, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=1)
    boat_obj.rotation_euler = (0, math.radians(5), math.radians(3))
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=12)
    boat_obj.rotation_euler = (0, 0, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=23)
    boat_obj.rotation_euler = (0, -math.radians(5), -math.radians(3))
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=34)
    boat_obj.rotation_euler = (0, 0, 0)
    boat_obj.keyframe_insert(data_path='rotation_euler', frame=45)
    push_action_to_nla(boat_obj, act_flutter)

def animate_croc(croc_obj):
    # Action 1: croc_idle (90 frames, loop, 3s)
    act_idle = create_action(croc_obj, 'croc_idle')
    croc_obj.rotation_euler = (0, 0, 0)
    croc_obj.location = (0, 0, 0)
    croc_obj.scale = (1.0, 1.0, 1.0)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=1)
    croc_obj.keyframe_insert(data_path='location', frame=1)
    croc_obj.keyframe_insert(data_path='scale', frame=1)

    croc_obj.rotation_euler = (0, 0, math.radians(4))
    croc_obj.location = (0.015, 0.015, 0)
    croc_obj.scale = (1.02, 1.03, 1.0)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=23)
    croc_obj.keyframe_insert(data_path='location', frame=23)
    croc_obj.keyframe_insert(data_path='scale', frame=23)

    croc_obj.rotation_euler = (0, 0, 0)
    croc_obj.location = (0, 0, 0)
    croc_obj.scale = (1.0, 1.0, 1.0)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=45)
    croc_obj.keyframe_insert(data_path='location', frame=45)
    croc_obj.keyframe_insert(data_path='scale', frame=45)

    croc_obj.rotation_euler = (0, 0, -math.radians(4))
    croc_obj.location = (-0.015, -0.01, 0)
    croc_obj.scale = (0.98, 0.98, 1.0)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=68)
    croc_obj.keyframe_insert(data_path='location', frame=68)
    croc_obj.keyframe_insert(data_path='scale', frame=68)

    croc_obj.rotation_euler = (0, 0, 0)
    croc_obj.location = (0, 0, 0)
    croc_obj.scale = (1.0, 1.0, 1.0)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=90)
    croc_obj.keyframe_insert(data_path='location', frame=90)
    croc_obj.keyframe_insert(data_path='scale', frame=90)
    push_action_to_nla(croc_obj, act_idle)

    # Action 2: croc_snap (20 frames, oneshot, 0.67s)
    act_snap = create_action(croc_obj, 'croc_snap')
    croc_obj.location = (0, 0, 0)
    croc_obj.rotation_euler = (0, 0, 0)
    croc_obj.keyframe_insert(data_path='location', frame=1)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    # Lurch forward and open
    croc_obj.location = (0.05, 0.02, 0)
    croc_obj.rotation_euler = (0, 0, math.radians(12))
    croc_obj.keyframe_insert(data_path='location', frame=6)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=6)

    # Snap closed hard
    croc_obj.location = (0.08, -0.01, 0)
    croc_obj.rotation_euler = (0, 0, -math.radians(4))
    croc_obj.keyframe_insert(data_path='location', frame=10)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=10)

    # Return
    croc_obj.location = (0, 0, 0)
    croc_obj.rotation_euler = (0, 0, 0)
    croc_obj.keyframe_insert(data_path='location', frame=20)
    croc_obj.keyframe_insert(data_path='rotation_euler', frame=20)
    push_action_to_nla(croc_obj, act_snap)

def animate_diya(diya_obj):
    # Action 1: diya_idle (30 frames, loop, 1s - flame flicker)
    act_idle = create_action(diya_obj, 'diya_idle')
    diya_obj.scale = (1.0, 1.0, 1.0)
    diya_obj.rotation_euler = (0, 0, 0)
    diya_obj.keyframe_insert(data_path='scale', frame=1)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    diya_obj.scale = (1.08, 1.15, 1.0)
    diya_obj.rotation_euler = (0, 0, math.radians(4))
    diya_obj.keyframe_insert(data_path='scale', frame=8)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=8)

    diya_obj.scale = (0.94, 0.92, 1.0)
    diya_obj.rotation_euler = (0, 0, -math.radians(3))
    diya_obj.keyframe_insert(data_path='scale', frame=18)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=18)

    diya_obj.scale = (1.0, 1.0, 1.0)
    diya_obj.rotation_euler = (0, 0, 0)
    diya_obj.keyframe_insert(data_path='scale', frame=30)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=30)
    push_action_to_nla(diya_obj, act_idle)

    # Action 2: diya_float (90 frames, loop, 3s - whole float on water)
    act_float = create_action(diya_obj, 'diya_float')
    diya_obj.location = (0, 0, 0)
    diya_obj.rotation_euler = (0, 0, 0)
    diya_obj.keyframe_insert(data_path='location', frame=1)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    diya_obj.location = (0, 0.02, 0)
    diya_obj.rotation_euler = (0, 0, math.radians(3))
    diya_obj.keyframe_insert(data_path='location', frame=23)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=23)

    diya_obj.location = (0, 0, 0)
    diya_obj.rotation_euler = (0, 0, 0)
    diya_obj.keyframe_insert(data_path='location', frame=45)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=45)

    diya_obj.location = (0, -0.02, 0)
    diya_obj.rotation_euler = (0, 0, -math.radians(3))
    diya_obj.keyframe_insert(data_path='location', frame=68)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=68)

    diya_obj.location = (0, 0, 0)
    diya_obj.rotation_euler = (0, 0, 0)
    diya_obj.keyframe_insert(data_path='location', frame=90)
    diya_obj.keyframe_insert(data_path='rotation_euler', frame=90)
    push_action_to_nla(diya_obj, act_float)

def animate_dolphine(dolphine_obj):
    # Action 1: dolphine_leap (60 frames, loop, 2s)
    act_leap = create_action(dolphine_obj, 'dolphine_leap')
    dolphine_obj.location = (0, -0.08, 0)
    dolphine_obj.rotation_euler = (0, 0, -math.radians(20))
    dolphine_obj.keyframe_insert(data_path='location', frame=1)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    dolphine_obj.location = (0, 0.04, 0)
    dolphine_obj.rotation_euler = (0, 0, math.radians(25))
    dolphine_obj.keyframe_insert(data_path='location', frame=18)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=18)

    dolphine_obj.location = (0, 0.09, 0)
    dolphine_obj.rotation_euler = (0, 0, 0)
    dolphine_obj.keyframe_insert(data_path='location', frame=30)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=30)

    dolphine_obj.location = (0, 0.02, 0)
    dolphine_obj.rotation_euler = (0, 0, -math.radians(35))
    dolphine_obj.keyframe_insert(data_path='location', frame=45)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=45)

    dolphine_obj.location = (0, -0.08, 0)
    dolphine_obj.rotation_euler = (0, 0, -math.radians(20))
    dolphine_obj.keyframe_insert(data_path='location', frame=60)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=60)
    push_action_to_nla(dolphine_obj, act_leap)

    # Action 2: dolphine_swim (36 frames, loop, 1.2s)
    act_swim = create_action(dolphine_obj, 'dolphine_swim')
    dolphine_obj.rotation_euler = (0, 0, 0)
    dolphine_obj.location = (0, 0, 0)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=1)
    dolphine_obj.keyframe_insert(data_path='location', frame=1)

    dolphine_obj.rotation_euler = (0, math.radians(12), math.radians(5))
    dolphine_obj.location = (0, 0.015, 0)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=9)
    dolphine_obj.keyframe_insert(data_path='location', frame=9)

    dolphine_obj.rotation_euler = (0, 0, 0)
    dolphine_obj.location = (0, 0, 0)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=18)
    dolphine_obj.keyframe_insert(data_path='location', frame=18)

    dolphine_obj.rotation_euler = (0, -math.radians(12), -math.radians(5))
    dolphine_obj.location = (0, -0.015, 0)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=27)
    dolphine_obj.keyframe_insert(data_path='location', frame=27)

    dolphine_obj.rotation_euler = (0, 0, 0)
    dolphine_obj.location = (0, 0, 0)
    dolphine_obj.keyframe_insert(data_path='rotation_euler', frame=36)
    dolphine_obj.keyframe_insert(data_path='location', frame=36)
    push_action_to_nla(dolphine_obj, act_swim)

def animate_fish(fish_obj, name, duration=24, rot_z=14):
    act = create_action(fish_obj, name)
    fish_obj.rotation_euler = (0, 0, 0)
    fish_obj.location = (0, 0, 0)
    fish_obj.keyframe_insert(data_path='rotation_euler', frame=1)
    fish_obj.keyframe_insert(data_path='location', frame=1)

    fish_obj.rotation_euler = (math.radians(8), 0, math.radians(rot_z))
    fish_obj.location = (0, 0.018, 0)
    fish_obj.keyframe_insert(data_path='rotation_euler', frame=duration // 4)
    fish_obj.keyframe_insert(data_path='location', frame=duration // 4)

    fish_obj.rotation_euler = (0, 0, 0)
    fish_obj.location = (0, 0, 0)
    fish_obj.keyframe_insert(data_path='rotation_euler', frame=duration // 2)
    fish_obj.keyframe_insert(data_path='location', frame=duration // 2)

    fish_obj.rotation_euler = (-math.radians(8), 0, -math.radians(rot_z))
    fish_obj.location = (0, -0.018, 0)
    fish_obj.keyframe_insert(data_path='rotation_euler', frame=(duration * 3) // 4)
    fish_obj.keyframe_insert(data_path='location', frame=(duration * 3) // 4)

    fish_obj.rotation_euler = (0, 0, 0)
    fish_obj.location = (0, 0, 0)
    fish_obj.keyframe_insert(data_path='rotation_euler', frame=duration)
    fish_obj.keyframe_insert(data_path='location', frame=duration)
    push_action_to_nla(fish_obj, act)

def animate_flower(flower_obj):
    # Action 1: flower_sway (90 frames, loop, 3s)
    act_sway = create_action(flower_obj, 'flower_sway')
    flower_obj.rotation_euler = (0, 0, 0)
    flower_obj.location = (0, 0, 0)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=1)
    flower_obj.keyframe_insert(data_path='location', frame=1)

    flower_obj.rotation_euler = (0, 0, math.radians(5))
    flower_obj.location = (0, 0.015, 0)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=23)
    flower_obj.keyframe_insert(data_path='location', frame=23)

    flower_obj.rotation_euler = (0, 0, 0)
    flower_obj.location = (0, 0, 0)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=45)
    flower_obj.keyframe_insert(data_path='location', frame=45)

    flower_obj.rotation_euler = (0, 0, -math.radians(5))
    flower_obj.location = (0, -0.015, 0)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=68)
    flower_obj.keyframe_insert(data_path='location', frame=68)

    flower_obj.rotation_euler = (0, 0, 0)
    flower_obj.location = (0, 0, 0)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=90)
    flower_obj.keyframe_insert(data_path='location', frame=90)
    push_action_to_nla(flower_obj, act_sway)

    # Action 2: flower_bloom (60 frames, oneshot, 2s)
    act_bloom = create_action(flower_obj, 'flower_bloom')
    flower_obj.scale = (0.55, 0.55, 0.8)
    flower_obj.rotation_euler = (0, 0, -math.radians(15))
    flower_obj.keyframe_insert(data_path='scale', frame=1)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    flower_obj.scale = (1.1, 1.1, 1.05)
    flower_obj.rotation_euler = (0, 0, math.radians(5))
    flower_obj.keyframe_insert(data_path='scale', frame=45)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=45)

    flower_obj.scale = (1.0, 1.0, 1.0)
    flower_obj.rotation_euler = (0, 0, 0)
    flower_obj.keyframe_insert(data_path='scale', frame=60)
    flower_obj.keyframe_insert(data_path='rotation_euler', frame=60)
    push_action_to_nla(flower_obj, act_bloom)

def animate_plastic(plastic_obj):
    # One 75-frame loop that stays in the screen plane: bob plus a +-15 degree Z sway.
    create_action(plastic_obj, 'plastic_drift')
    for frame, y, rz in ((1, 0, 0), (20, 0.025, 15), (40, 0, 0), (60, -0.025, -15), (75, 0, 0)):
        plastic_obj.location = (0, y, 0)
        plastic_obj.rotation_euler = (0, 0, math.radians(rz))
        plastic_obj.keyframe_insert(data_path='location', frame=frame)
        plastic_obj.keyframe_insert(data_path='rotation_euler', frame=frame)

def animate_pollution(pollution_obj):
    # Action 1: pollution_flow (60 frames, loop, 2s)
    act_flow = create_action(pollution_obj, 'pollution_flow')
    pollution_obj.scale = (1.0, 1.0, 1.0)
    pollution_obj.rotation_euler = (0, 0, 0)
    pollution_obj.keyframe_insert(data_path='scale', frame=1)
    pollution_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    pollution_obj.scale = (1.05, 1.15, 1.0)
    pollution_obj.rotation_euler = (0, 0, math.radians(4))
    pollution_obj.keyframe_insert(data_path='scale', frame=20)
    pollution_obj.keyframe_insert(data_path='rotation_euler', frame=20)

    pollution_obj.scale = (0.95, 0.9, 1.0)
    pollution_obj.rotation_euler = (0, 0, -math.radians(3))
    pollution_obj.keyframe_insert(data_path='scale', frame=40)
    pollution_obj.keyframe_insert(data_path='rotation_euler', frame=40)

    pollution_obj.scale = (1.0, 1.0, 1.0)
    pollution_obj.rotation_euler = (0, 0, 0)
    pollution_obj.keyframe_insert(data_path='scale', frame=60)
    pollution_obj.keyframe_insert(data_path='rotation_euler', frame=60)
    push_action_to_nla(pollution_obj, act_flow)

    # Action 2: pollution_drip (45 frames, loop, 1.5s)
    act_drip = create_action(pollution_obj, 'pollution_drip')
    pollution_obj.location = (0, 0, 0)
    pollution_obj.keyframe_insert(data_path='location', frame=1)
    pollution_obj.location = (0, -0.03, 0)
    pollution_obj.keyframe_insert(data_path='location', frame=25)
    pollution_obj.location = (0, 0, 0)
    pollution_obj.keyframe_insert(data_path='location', frame=45)
    push_action_to_nla(pollution_obj, act_drip)

def animate_dirt(dirt_obj):
    # Action 1: dirt_float (120 frames, loop, 4s)
    act_float = create_action(dirt_obj, 'dirt_float')
    dirt_obj.location = (0, 0, 0)
    dirt_obj.rotation_euler = (0, 0, 0)
    dirt_obj.keyframe_insert(data_path='location', frame=1)
    dirt_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    dirt_obj.location = (0, 0.01, 0)
    dirt_obj.rotation_euler = (0, 0, math.radians(1.5))
    dirt_obj.keyframe_insert(data_path='location', frame=30)
    dirt_obj.keyframe_insert(data_path='rotation_euler', frame=30)

    dirt_obj.location = (0, 0, 0)
    dirt_obj.rotation_euler = (0, 0, 0)
    dirt_obj.keyframe_insert(data_path='location', frame=60)
    dirt_obj.keyframe_insert(data_path='rotation_euler', frame=60)

    dirt_obj.location = (0, -0.01, 0)
    dirt_obj.rotation_euler = (0, 0, -math.radians(1.5))
    dirt_obj.keyframe_insert(data_path='location', frame=90)
    dirt_obj.keyframe_insert(data_path='rotation_euler', frame=90)

    dirt_obj.location = (0, 0, 0)
    dirt_obj.rotation_euler = (0, 0, 0)
    dirt_obj.keyframe_insert(data_path='location', frame=120)
    dirt_obj.keyframe_insert(data_path='rotation_euler', frame=120)
    push_action_to_nla(dirt_obj, act_float)

def animate_matka(matka_obj):
    # Action 1: matka_idle (60 frames, loop, 2s)
    act_idle = create_action(matka_obj, 'matka_idle')
    matka_obj.location = (0, 0, 0)
    matka_obj.rotation_euler = (0, 0, 0)
    matka_obj.keyframe_insert(data_path='location', frame=1)
    matka_obj.keyframe_insert(data_path='rotation_euler', frame=1)

    matka_obj.location = (0, 0.02, 0)
    matka_obj.rotation_euler = (0, 0, math.radians(4))
    matka_obj.keyframe_insert(data_path='location', frame=15)
    matka_obj.keyframe_insert(data_path='rotation_euler', frame=15)

    matka_obj.location = (0, 0, 0)
    matka_obj.rotation_euler = (0, 0, 0)
    matka_obj.keyframe_insert(data_path='location', frame=30)
    matka_obj.keyframe_insert(data_path='rotation_euler', frame=30)

    matka_obj.location = (0, -0.02, 0)
    matka_obj.rotation_euler = (0, 0, -math.radians(4))
    matka_obj.keyframe_insert(data_path='location', frame=45)
    matka_obj.keyframe_insert(data_path='rotation_euler', frame=45)

    matka_obj.location = (0, 0, 0)
    matka_obj.rotation_euler = (0, 0, 0)
    matka_obj.keyframe_insert(data_path='location', frame=60)
    matka_obj.keyframe_insert(data_path='rotation_euler', frame=60)
    push_action_to_nla(matka_obj, act_idle)

    # Action 2: matka_collect (30 frames, oneshot, 1s - bounce & water intake dip)
    act_collect = create_action(matka_obj, 'matka_collect')
    matka_obj.scale = (1.0, 1.0, 1.0)
    matka_obj.location = (0, 0, 0)
    matka_obj.keyframe_insert(data_path='scale', frame=1)
    matka_obj.keyframe_insert(data_path='location', frame=1)

    # Squash dip
    matka_obj.scale = (1.2, 0.8, 1.2)
    matka_obj.location = (0, -0.04, 0)
    matka_obj.keyframe_insert(data_path='scale', frame=8)
    matka_obj.keyframe_insert(data_path='location', frame=8)

    # Stretch pop
    matka_obj.scale = (0.85, 1.25, 0.85)
    matka_obj.location = (0, 0.05, 0)
    matka_obj.keyframe_insert(data_path='scale', frame=16)
    matka_obj.keyframe_insert(data_path='location', frame=16)

    # Settle
    matka_obj.scale = (1.0, 1.0, 1.0)
    matka_obj.location = (0, 0, 0)
    matka_obj.keyframe_insert(data_path='scale', frame=30)
    matka_obj.keyframe_insert(data_path='location', frame=30)
    push_action_to_nla(matka_obj, act_collect)

# -----------------------------------------------------------------------------
# Matka 3D Procedural Model Creator
# -----------------------------------------------------------------------------
def srgb_to_linear(hex_rgb):
    # Blender colour fields are linear; palette values are authored as sRGB hex.
    c = [int(hex_rgb[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple((x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4) for x in c) + (1.0,)

def _matka_part(op, hex_rgb, name, **kw):
    op(**kw)
    obj = bpy.context.active_object
    obj.name = name
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = srgb_to_linear(hex_rgb)
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.75
    obj.data.materials.append(mat)
    return obj

def create_matka_model():
    # Clay pot standing on Y (the sheet frame's up) facing +Z; palette sampled from the reference art.
    up = (math.radians(90), 0, 0)
    body = _matka_part(bpy.ops.mesh.primitive_uv_sphere_add, '#C5602D', 'Matka', radius=0.45, rotation=up)
    body.scale = (1.0, 1.0, 0.9)
    parts = [body,
        _matka_part(bpy.ops.mesh.primitive_cylinder_add, '#C5602D', 'Neck', radius=0.24, depth=0.22, location=(0, 0.45, 0), rotation=up),
        _matka_part(bpy.ops.mesh.primitive_torus_add, '#A34921', 'Rim', major_radius=0.27, minor_radius=0.06, location=(0, 0.58, 0), rotation=up),
        _matka_part(bpy.ops.mesh.primitive_uv_sphere_add, '#ECDCBA', 'Belly', radius=0.28, location=(0, -0.02, 0.37), rotation=up)]
    parts[3].scale = (0.8, 1.0, 0.25)
    for i in range(5):
        x = (i - 2) * 0.13
        dot = _matka_part(bpy.ops.mesh.primitive_uv_sphere_add, '#F6E8CC', 'Dot', radius=0.03,
                          location=(x, 0.3 - 0.03 * (2 - abs(i - 2)) * 2, 0.43), rotation=up)
        parts.append(dot)
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.join()
    matka = bpy.context.active_object
    matka.name = 'Matka'
    normalize_mesh(matka, target_height=1.0, max_depth=None)
    animate_matka(matka)
    return matka

# -----------------------------------------------------------------------------
# Asset Catalog / Manifest
# -----------------------------------------------------------------------------
ASSET_CATALOG = [
    # Background
    {
        'id': 'sun',
        'svg': 'Assets/background/sun.svg',
        'out_fbx': 'Assets/Models/background/sun.fbx',
        'alt_fbx': 'Assets/Models/background/sun.fbx',
        'category': 'Background',
        'anim': animate_sun,
        'depth': 0.03,
        'emission': (1.0, 0.7, 0.2, 1.0),
        'emission_strength': 0.5
    },
    {
        'id': 'cloud_back_2',
        'svg': 'Assets/background/clod back 2.svg',
        'out_fbx': 'Assets/Models/background/cloud_back_2.fbx',
        'alt_fbx': 'Assets/Models/background/clod back 2.fbx',
        'category': 'Background',
        'anim': lambda obj: animate_cloud_back(obj, drift_amount=0.15),
        'depth': 0.03
    },
    {
        'id': 'cloud_back_1',
        'svg': 'Assets/background/cloud back 1.svg',
        'out_fbx': 'Assets/Models/background/cloud_back_1.fbx',
        'alt_fbx': 'Assets/Models/background/cloud back 1.fbx',
        'category': 'Background',
        'anim': lambda obj: animate_cloud_back(obj, drift_amount=0.18),
        'depth': 0.03
    },
    {
        'id': 'cloud_1',
        'svg': 'Assets/background/cloud 1.svg',
        'out_fbx': 'Assets/Models/background/cloud_1.fbx',
        'alt_fbx': 'Assets/Models/background/cloud 1.fbx',
        'category': 'Background',
        'anim': lambda obj: animate_cloud(obj, drift_amount=0.25),
        'depth': 0.04
    },
    {
        'id': 'cloud_2',
        'svg': 'Assets/background/cloud 2.svg',
        'out_fbx': 'Assets/Models/background/cloud_2.fbx',
        'alt_fbx': 'Assets/Models/background/cloud 2.fbx',
        'category': 'Background',
        'anim': lambda obj: animate_cloud(obj, drift_amount=0.22),
        'depth': 0.04
    },
    {
        'id': 'cloud_3',
        'svg': 'Assets/background/cloud 3.svg',
        'out_fbx': 'Assets/Models/background/cloud_3.fbx',
        'alt_fbx': 'Assets/Models/background/cloud 3.fbx',
        'category': 'Background',
        'anim': lambda obj: animate_cloud(obj, drift_amount=0.28),
        'depth': 0.04
    },

    # Water
    {
        'id': 'fast_river',
        'svg': 'Assets/water/fast river.svg',
        'out_fbx': 'Assets/Models/water/fast_river.fbx',
        'alt_fbx': 'Assets/Models/water/fast river.fbx',
        'category': 'Water',
        'anim': lambda obj: animate_river(obj, 'river_fast_flow', duration=30, amp=0.045),
        'depth': 0.02,
        'is_river': True
    },
    {
        'id': 'medium_river',
        'svg': 'Assets/water/medium river.svg',
        'out_fbx': 'Assets/Models/water/medium_river.fbx',
        'alt_fbx': 'Assets/Models/water/medium river.fbx',
        'category': 'Water',
        'anim': lambda obj: animate_river(obj, 'river_medium_flow', duration=45, amp=0.035),
        'depth': 0.02,
        'is_river': True
    },
    {
        'id': 'slow_river',
        'svg': 'Assets/water/slow river.svg',
        'out_fbx': 'Assets/Models/water/slow_river.fbx',
        'alt_fbx': 'Assets/Models/water/slow river.fbx',
        'category': 'Water',
        'anim': lambda obj: animate_river(obj, 'river_slow_flow', duration=90, amp=0.025),
        'depth': 0.02,
        'is_river': True
    },

    # Obstacles - Hazards
    {
        'id': 'boat',
        'drop_actions': ['boat_sail_flutter'],
        'svg': 'Assets/obstacles/boat.svg',
        'out_fbx': 'Assets/Models/obstacles/boat.fbx',
        'alt_fbx': 'Assets/Models/obstacles/boat.fbx',
        'category': 'Obstacles_Hazards',
        'anim': animate_boat,
        'depth': 0.04
    },
    {
        'id': 'diya',
        'drop_actions': ['diya_idle'],
        'svg': 'Assets/obstacles/diya.svg',
        'out_fbx': 'Assets/Models/obstacles/diya.fbx',
        'alt_fbx': 'Assets/Models/obstacles/diya.fbx',
        'category': 'Obstacles_Hazards',
        'anim': animate_diya,
        'depth': 0.04,
        'emission': (1.0, 0.6, 0.1, 1.0),
        'emission_strength': 1.0
    },
    {
        'id': 'dolphine',
        'drop_actions': ['dolphine_swim'],
        'svg': 'Assets/obstacles/dolphine.svg',
        'out_fbx': 'Assets/Models/obstacles/dolphine.fbx',
        'alt_fbx': 'Assets/Models/obstacles/dolphine.fbx',
        'category': 'Obstacles_Hazards',
        'anim': animate_dolphine,
        'depth': 0.04
    },
    {
        'id': 'plastic',
        'svg': 'Assets/obstacles/plastic.svg',
        'out_fbx': 'Assets/Models/obstacles/plastic.fbx',
        'alt_fbx': 'Assets/Models/obstacles/plastic.fbx',
        'category': 'Obstacles_Hazards',
        'anim': animate_plastic,
        'depth': 0.03
    },
    {
        'id': 'pollution',
        'drop_actions': ['pollution_drip'],
        'svg': 'Assets/obstacles/pollution.svg',
        'out_fbx': 'Assets/Models/obstacles/pollution.fbx',
        'alt_fbx': 'Assets/Models/obstacles/pollution.fbx',
        'category': 'Obstacles_Hazards',
        'anim': animate_pollution,
        'depth': 0.04
    },

    # Obstacles - Scenery
    {
        'id': 'croc',
        'svg': 'Assets/obstacles/croc.svg',
        'out_fbx': 'Assets/Models/obstacles/croc.fbx',
        'alt_fbx': 'Assets/Models/obstacles/croc.fbx',
        'category': 'Obstacles_Scenery',
        'anim': animate_croc,
        'depth': 0.04
    },
    {
        'id': 'dirt',
        'svg': 'Assets/obstacles/dirt.svg',
        'out_fbx': 'Assets/Models/obstacles/dirt.fbx',
        'alt_fbx': 'Assets/Models/obstacles/dirt.fbx',
        'category': 'Obstacles_Scenery',
        'anim': animate_dirt,
        'depth': 0.02
    },
    {
        'id': 'fish_1',
        'svg': 'Assets/obstacles/fish 1.svg',
        'out_fbx': 'Assets/Models/obstacles/fish_1.fbx',
        'alt_fbx': 'Assets/Models/obstacles/fish 1.fbx',
        'category': 'Obstacles_Scenery',
        'anim': lambda obj: animate_fish(obj, 'fish1_swim', duration=24, rot_z=16),
        'depth': 0.035
    },
    {
        'id': 'fish_2',
        'svg': 'Assets/obstacles/fish 2.svg',
        'out_fbx': 'Assets/Models/obstacles/fish_2.fbx',
        'alt_fbx': 'Assets/Models/obstacles/fish 2.fbx',
        'category': 'Obstacles_Scenery',
        'anim': lambda obj: animate_fish(obj, 'fish2_swim', duration=30, rot_z=14),
        'depth': 0.035
    },
    {
        'id': 'flower',
        'svg': 'Assets/obstacles/flower.svg',
        'out_fbx': 'Assets/Models/obstacles/flower.fbx',
        'alt_fbx': 'Assets/Models/obstacles/flower.fbx',
        'category': 'Obstacles_Scenery',
        'anim': animate_flower,
        'depth': 0.03
    }
]

# -----------------------------------------------------------------------------
# Main Execution Pipeline
# -----------------------------------------------------------------------------
def export_upright_fbx(filepath):
    # Assets are authored as flat sheets in Blender's XY plane. Stand them up under one
    # parent so they land in Unity's XY plane facing a camera at -Z, un-mirrored; children
    # keep their local XY-frame animations.
    # Detach NLA tracks (they blend every action into every baked take) and rest the pose;
    # fake users keep the actions alive for the exporter's per-action bake.
    for o in bpy.context.scene.objects:
        if o.animation_data:
            for track in list(o.animation_data.nla_tracks):
                o.animation_data.nla_tracks.remove(track)
            o.animation_data.action = None
        o.location, o.rotation_euler, o.scale = (0, 0, 0), (0, 0, 0), (1, 1, 1)
    for act in bpy.data.actions:
        act.use_fake_user = True
    upright = bpy.data.objects.new('Upright', None)
    bpy.context.scene.collection.objects.link(upright)
    upright.rotation_euler = (math.radians(90), 0, math.radians(180))
    for o in bpy.context.scene.objects:
        if o.parent is None and o is not upright:
            o.parent = upright
    bpy.ops.export_scene.fbx(
        filepath=filepath,
        axis_forward='-Z',
        axis_up='Y',
        bake_anim=True,
        bake_anim_use_all_actions=True,
        # NLA-strip takes duplicate the 'object|action' takes but also bake the root, whose
        # constant curves would override the position/scale the game sets on instances.
        bake_anim_use_nla_strips=False,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        use_selection=False,
        apply_scale_options='FBX_SCALE_ALL'
    )

def build_single_asset(item):
    print(f"\n==================================================")
    print(f"Building Asset: {item['id']} ({item['svg']})")
    print(f"==================================================")

    # 1. Clean SVG
    tmp_svg = clean_svg_content(item['svg'])

    # 2. Reset blender scene
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # 3. Import SVG
    bpy.ops.import_curve.svg(filepath=tmp_svg)
    imported_objs = list(bpy.context.scene.objects)
    print(f"Imported {len(imported_objs)} curves from SVG.")
    if not imported_objs:
        print(f"ERROR: No objects imported for {item['id']}")
        os.remove(tmp_svg)
        return False

    # 4. Solidify & join into mesh
    thickness = item.get('depth', 0.04)
    mesh_obj = process_curves_to_mesh(imported_objs, thickness=thickness)
    mesh_obj.name = item['id']

    # 5. Convert materials to PBR
    emission_col = item.get('emission', None)
    emission_strength = item.get('emission_strength', 0.0)
    convert_materials_to_pbr(
        mesh_obj.data.materials,
        roughness=0.65,
        metallic=0.0,
        emission_col=emission_col,
        emission_strength=emission_strength
    )

    # 6. Normalize geometry & center
    is_river = item.get('is_river', False)
    normalize_mesh(mesh_obj, target_height=1.0, is_river=is_river)

    # 7. Apply Animation
    anim_fn = item.get('anim', None)
    if anim_fn:
        anim_fn(mesh_obj)

    for name in item.get('drop_actions', []):
        bpy.data.actions.remove(bpy.data.actions[name])

    # 8. Export FBX
    out_dir = os.path.dirname(item['out_fbx'])
    os.makedirs(out_dir, exist_ok=True)
    export_upright_fbx(item['out_fbx'])
    print(f"SUCCESS: Exported {item['out_fbx']}")

    # If alt_fbx exists and is different, write a copy
    alt_fbx = item.get('alt_fbx', None)
    if alt_fbx and alt_fbx != item['out_fbx']:
        import shutil
        shutil.copy2(item['out_fbx'], alt_fbx)
        print(f"SUCCESS: Copied to {alt_fbx}")

    os.remove(tmp_svg)
    return True

def build_collector_asset():
    print(f"\n==================================================")
    print(f"Building Asset: Matka (Collector Pot)")
    print(f"==================================================")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    matka_obj = create_matka_model()
    out_fbx = 'Assets/Models/water/matka.fbx'
    alt_fbx = 'Assets/Models/obstacles/matka.fbx'
    os.makedirs(os.path.dirname(out_fbx), exist_ok=True)
    os.makedirs(os.path.dirname(alt_fbx), exist_ok=True)
    export_upright_fbx(out_fbx)
    import shutil
    shutil.copy2(out_fbx, alt_fbx)
    print(f"SUCCESS: Exported {out_fbx} and {alt_fbx}")

def assemble_master_blend():
    print(f"\n==================================================")
    print(f"Assembling Master Blender File: Assets/Blender/GangaKiBagiya.blend")
    print(f"==================================================")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    # Create collection hierarchy
    collections = {
        'Background': bpy.data.collections.new('Background'),
        'Water': bpy.data.collections.new('Water'),
        'Obstacles_Hazards': bpy.data.collections.new('Obstacles_Hazards'),
        'Obstacles_Scenery': bpy.data.collections.new('Obstacles_Scenery'),
        'Collector': bpy.data.collections.new('Collector')
    }
    for col in collections.values():
        scene.collection.children.link(col)

    # Import every FBX into its collection
    for item in ASSET_CATALOG:
        cat = item['category']
        col = collections.get(cat, scene.collection)
        fbx_path = item['out_fbx']
        if os.path.exists(fbx_path):
            bpy.ops.import_scene.fbx(filepath=fbx_path)
            imported = [o for o in bpy.context.selected_objects]
            for o in imported:
                # Link to proper collection and unlink from default
                for parent_col in o.users_collection:
                    parent_col.objects.unlink(o)
                col.objects.link(o)

    # Import Matka
    matka_path = 'Assets/Models/water/matka.fbx'
    if os.path.exists(matka_path):
        bpy.ops.import_scene.fbx(filepath=matka_path)
        for o in bpy.context.selected_objects:
            for parent_col in o.users_collection:
                parent_col.objects.unlink(o)
            collections['Collector'].objects.link(o)

    # Save master blend file
    blend_path = 'Assets/Blender/GangaKiBagiya.blend'
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print(f"Master file saved: {blend_path}")

if __name__ == '__main__':
    success_count = 0
    for item in ASSET_CATALOG:
        if build_single_asset(item):
            success_count += 1
    build_collector_asset()
    assemble_master_blend()
    print(f"\nCOMPLETED: {success_count}/{len(ASSET_CATALOG)} SVG assets converted and animated!")
