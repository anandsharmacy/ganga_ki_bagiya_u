"""
Headless Blender animation pass.

    blender -b scene.blend -P animate_scene.py

Adds procedural animation to four existing objects, sets the frame range to
1-250 and saves "animated_scene.blend" next to the opened file:

  * Dolphin         - swims forward on Y and breaches on Z (additive sine F-curve modifiers)
  * Diya            - flickering flame: Noise modifier on its child light's energy
                      (falls back to an Emission material's strength)
  * Pollution_Pipe  - particle system emitting downward over the whole range
  * Ship            - sails along its local forward axis, with noise on pitch and roll

Missing objects are reported and skipped; one failing step never stops the others.
Uses the data API only (no context-dependent operators) apart from the final save.
"""

import math
import os
import sys
import traceback

import bpy
from mathutils import Vector

# -----------------------------------------------------------------------------
# Configuration - change object names and tuning here
# -----------------------------------------------------------------------------
DOLPHIN_NAME = "Dolphin"
DIYA_NAME = "Diya"
POLLUTION_PIPE_NAME = "Pollution_Pipe"
SHIP_NAME = "Ship"

FRAME_START = 1
FRAME_END = 250
OUTPUT_FILENAME = "animated_scene.blend"

DOLPHIN_SWIM_DISTANCE = 12.0    # metres travelled along +Y over the range
DOLPHIN_JUMP_HEIGHT = 1.5       # breach amplitude on Z
DOLPHIN_JUMP_PERIOD = 60        # frames per swim/breach cycle
DOLPHIN_SURGE = 0.4             # forward speed wobble on Y
DOLPHIN_PITCH_DEGREES = 25.0    # nose up/down following the arc

FLAME_BASE_ENERGY = 40.0        # watts; used if the light has none
FLAME_FLICKER_AMOUNT = 0.6      # noise strength as a fraction of the base energy (< 2 keeps it positive)
FLAME_FLICKER_SCALE = 3.0       # frames per noise feature (smaller = faster)
FLAME_EMISSION_STRENGTH = 5.0   # fallback: Emission strength base value

POLLUTION_PARTICLE_COUNT = 2000
POLLUTION_LIFETIME = 60         # frames each particle lives
POLLUTION_DOWN_SPEED = 3.0      # initial speed along the pipe's local -Z
POLLUTION_PARTICLE_SIZE = 0.05

SHIP_SAIL_DISTANCE = 15.0       # metres along the ship's local forward axis
SHIP_FORWARD_AXIS = (0.0, 1.0, 0.0)  # local forward; change to (1, 0, 0) etc. to match the model
SHIP_PITCH_DEGREES = 3.0        # noise strength on X rotation
SHIP_ROLL_DEGREES = 5.0         # noise strength on Y rotation


# -----------------------------------------------------------------------------
# Helpers
# -----------------------------------------------------------------------------
def log(message):
    print(f"[animate_scene] {message}")
    sys.stdout.flush()


def find_object(name):
    obj = bpy.data.objects.get(name)
    if obj is None:
        log(f"WARNING: object '{name}' not found in the scene, skipping.")
    return obj


def get_fcurve(datablock, data_path, index):
    """Return the F-curve for data_path[index] on datablock (Blender 4.4+ slotted actions, with legacy fallback)."""
    anim = datablock.animation_data
    if anim is None or anim.action is None:
        return None
    action = anim.action
    fcurves = None
    try:
        from bpy_extras import anim_utils
        if getattr(anim, "action_slot", None) is not None:
            channelbag = anim_utils.action_get_channelbag_for_slot(action, anim.action_slot)
            if channelbag is not None:
                fcurves = channelbag.fcurves
    except (ImportError, AttributeError):
        fcurves = None
    if fcurves is None:
        fcurves = getattr(action, "fcurves", None)  # Blender < 4.4
    if fcurves is None:
        return None
    return fcurves.find(data_path, index=index)


def key(datablock, data_path, index, frame, value):
    """Set datablock.<data_path>[index] (or the scalar if index < 0), keyframe it, return its F-curve."""
    if index >= 0:
        getattr(datablock, data_path)[index] = value  # simple vector props: location, rotation_euler
        datablock.keyframe_insert(data_path=data_path, index=index, frame=frame)
    else:
        owner_path, _, prop = data_path.rpartition(".")
        owner = datablock.path_resolve(owner_path) if owner_path else datablock
        setattr(owner, prop, value)
        datablock.keyframe_insert(data_path=data_path, frame=frame)
    return get_fcurve(datablock, data_path, max(index, 0))


def set_linear(fcurve):
    for point in fcurve.keyframe_points:
        point.interpolation = 'LINEAR'
    fcurve.extrapolation = 'LINEAR'


def add_sine(fcurve, amplitude, period_frames, phase=0.0):
    """Additive sine on top of the keyed value: amplitude * sin(2*pi*frame/period + phase)."""
    mod = fcurve.modifiers.new(type='FNGENERATOR')
    mod.function_type = 'SIN'
    mod.use_additive = True
    mod.amplitude = amplitude
    mod.phase_multiplier = 2.0 * math.pi / period_frames
    mod.phase_offset = phase
    mod.value_offset = 0.0
    return mod


def add_noise(fcurve, strength, scale, phase=1.0):
    """Additive smooth noise; different phases keep channels from moving in sync."""
    mod = fcurve.modifiers.new(type='NOISE')
    mod.blend_type = 'ADD'
    mod.strength = strength
    mod.scale = scale
    mod.phase = phase
    mod.depth = 1
    return mod


# -----------------------------------------------------------------------------
# Animators
# -----------------------------------------------------------------------------
def animate_dolphin(obj):
    start = obj.location.copy()
    # Forward swim on Y: linear keys plus a surge so it speeds up into each leap.
    key(obj, "location", 1, FRAME_START, start.y)
    y_curve = key(obj, "location", 1, FRAME_END, start.y + DOLPHIN_SWIM_DISTANCE)
    set_linear(y_curve)
    add_sine(y_curve, DOLPHIN_SURGE, DOLPHIN_JUMP_PERIOD, phase=math.pi / 2)

    # Breach on Z: half the cycle above the surface, half diving below it.
    z_curve = key(obj, "location", 2, FRAME_START, start.z)
    add_sine(z_curve, DOLPHIN_JUMP_HEIGHT, DOLPHIN_JUMP_PERIOD)

    # Pitch follows the arc (cosine leads the height by a quarter cycle): nose up rising, down falling.
    x_rot = key(obj, "rotation_euler", 0, FRAME_START, obj.rotation_euler.x)
    add_sine(x_rot, -math.radians(DOLPHIN_PITCH_DEGREES), DOLPHIN_JUMP_PERIOD, phase=math.pi / 2)
    log(f"Dolphin '{obj.name}': swim {DOLPHIN_SWIM_DISTANCE} m on Y, breach {DOLPHIN_JUMP_HEIGHT} m every {DOLPHIN_JUMP_PERIOD} frames.")


def find_flame_light(obj):
    for child in obj.children_recursive:
        if child.type == 'LIGHT':
            return child
    return obj if obj.type == 'LIGHT' else None


def find_emission_input(objects):
    for candidate in objects:
        for slot in getattr(candidate, "material_slots", []):
            material = slot.material
            if not material or not material.use_nodes:
                continue
            for node in material.node_tree.nodes:
                if node.type == 'EMISSION':
                    return material, node.inputs["Strength"]
                if node.type == 'BSDF_PRINCIPLED' and "Emission Strength" in node.inputs:
                    return material, node.inputs["Emission Strength"]
    return None, None


def animate_diya(obj):
    light = find_flame_light(obj)
    if light is not None:
        data = light.data
        base = data.energy if data.energy > 0 else FLAME_BASE_ENERGY
        curve = key(data, "energy", -1, FRAME_START, base)
        add_noise(curve, base * FLAME_FLICKER_AMOUNT, FLAME_FLICKER_SCALE, phase=7.3)
        log(f"Diya '{obj.name}': flicker on light '{light.name}' energy ({base:g} W, noise strength {base * FLAME_FLICKER_AMOUNT:g} W).")
        return

    material, socket = find_emission_input([obj] + list(obj.children_recursive))
    if socket is None:
        log(f"WARNING: Diya '{obj.name}' has no child light or emission material; no flicker added.")
        return
    base = socket.default_value if socket.default_value > 0 else FLAME_EMISSION_STRENGTH
    socket.default_value = base
    data_path = socket.path_from_id("default_value")
    material.node_tree.keyframe_insert(data_path=data_path, frame=FRAME_START)
    curve = get_fcurve(material.node_tree, data_path, 0)
    add_noise(curve, base * FLAME_FLICKER_AMOUNT, FLAME_FLICKER_SCALE, phase=7.3)
    log(f"Diya '{obj.name}': flicker on emission strength of material '{material.name}'.")


def animate_pollution(obj):
    if obj.type != 'MESH':
        log(f"WARNING: Pollution pipe '{obj.name}' is a {obj.type}, particles need a mesh; skipping.")
        return
    modifier = obj.modifiers.new(name="PollutionParticles", type='PARTICLE_SYSTEM')
    settings = modifier.particle_system.settings
    settings.name = "Pollution"
    settings.type = 'EMITTER'
    settings.count = POLLUTION_PARTICLE_COUNT
    settings.frame_start = FRAME_START
    settings.frame_end = FRAME_END
    settings.lifetime = POLLUTION_LIFETIME
    settings.lifetime_random = 0.3
    settings.emit_from = 'FACE'
    settings.physics_type = 'NEWTON'
    settings.normal_factor = 0.0
    settings.object_align_factor = (0.0, 0.0, -POLLUTION_DOWN_SPEED)  # out of the pipe, downward
    settings.factor_random = 0.4
    settings.particle_size = POLLUTION_PARTICLE_SIZE
    settings.size_random = 0.5
    settings.effector_weights.gravity = 1.0
    log(f"Pollution pipe '{obj.name}': {POLLUTION_PARTICLE_COUNT} particles, frames {FRAME_START}-{FRAME_END}, falling along local -Z.")


def animate_ship(obj):
    start = obj.location.copy()
    forward = (obj.matrix_world.to_3x3() @ Vector(SHIP_FORWARD_AXIS)).normalized()
    end = start + forward * SHIP_SAIL_DISTANCE
    for axis in range(3):
        key(obj, "location", axis, FRAME_START, start[axis])
        curve = key(obj, "location", axis, FRAME_END, end[axis])
        set_linear(curve)
    obj.location = start

    pitch = key(obj, "rotation_euler", 0, FRAME_START, obj.rotation_euler.x)
    add_noise(pitch, math.radians(SHIP_PITCH_DEGREES), scale=22.0, phase=3.1)
    roll = key(obj, "rotation_euler", 1, FRAME_START, obj.rotation_euler.y)
    add_noise(roll, math.radians(SHIP_ROLL_DEGREES), scale=30.0, phase=11.7)
    log(f"Ship '{obj.name}': sails {SHIP_SAIL_DISTANCE} m along local {SHIP_FORWARD_AXIS}, pitch/roll noise.")


# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
def output_path():
    folder = os.path.dirname(bpy.data.filepath) if bpy.data.filepath else os.getcwd()
    return os.path.join(folder, OUTPUT_FILENAME)


def main():
    scene = bpy.context.scene
    scene.frame_start = FRAME_START
    scene.frame_end = FRAME_END

    jobs = [
        (DOLPHIN_NAME, animate_dolphin),
        (DIYA_NAME, animate_diya),
        (POLLUTION_PIPE_NAME, animate_pollution),
        (SHIP_NAME, animate_ship),
    ]
    done = 0
    for name, animator in jobs:
        obj = find_object(name)
        if obj is None:
            continue
        try:
            animator(obj)
            done += 1
        except Exception:
            log(f"WARNING: animating '{name}' failed and was skipped:\n{traceback.format_exc()}")

    scene.frame_set(FRAME_START)
    path = output_path()
    try:
        bpy.ops.wm.save_as_mainfile(filepath=path)
    except Exception:
        log(f"ERROR: could not save '{path}':\n{traceback.format_exc()}")
        sys.exit(1)
    log(f"Animated {done}/{len(jobs)} objects, frames {FRAME_START}-{FRAME_END}. Saved: {path}")


if __name__ == "__main__":
    main()
