import bpy
import os

# Batch re-export all collections/objects from GangaKiBagiya.blend
def export_all():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    blend_script = os.path.join(script_dir, "build_all_assets.py")
    if os.path.exists(blend_script):
        # Delegate to the comprehensive build pipeline to maintain exact asset fidelity
        import subprocess
        cmd = ["blender", "-b", "--python", blend_script]
        subprocess.run(cmd, check=True)
    else:
        print("Build script not found:", blend_script)

if __name__ == '__main__':
    export_all()
