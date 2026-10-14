import bpy
import re
import os
import glob
import tempfile
import math
import mathutils

def clean_svg_content(svg_path):
    with open(svg_path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()

    # 1. Strip embedded base64 raster images and filter definitions
    content = re.sub(r'<image[^>]*>', '', content)
    content = re.sub(r'<filter.*?</filter>', '', content, flags=re.DOTALL)

    # 2. Extract CSS rules from <style> blocks
    style_blocks = re.findall(r'<style[^>]*>(.*?)</style>', content, flags=re.DOTALL)
    class_fills = {}
    for block in style_blocks:
        rules = re.findall(r'([^{]+)\{([^}]+)\}', block)
        for selectors, decls in rules:
            fill_m = re.search(r'fill:\s*([^;]+);', decls)
            if fill_m:
                val = fill_m.group(1).strip()
                for sel in selectors.split(','):
                    s = sel.strip()
                    if s.startswith('.'):
                        class_fills[s[1:]] = val

    # 3. For every XML tag with class="...", insert fill attribute if not present
    def repl_tag(match):
        tag_str = match.group(0)
        # Check if already has fill
        if 'fill=' in tag_str:
            return tag_str
        m_cls = re.search(r'class="([^"]+)"', tag_str)
        if not m_cls:
            m_cls = re.search(r"class='([^']+)'", tag_str)
        if m_cls:
            for c in m_cls.group(1).split():
                if c in class_fills:
                    color_val = class_fills[c]
                    if color_val != 'none':
                        # Insert fill before closing > or />
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

if __name__ == '__main__':
    tmp = clean_svg_content('Assets/obstacles/croc.svg')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_curve.svg(filepath=tmp)
    print('Imported croc objects:', len(bpy.context.scene.objects))
    for o in bpy.context.scene.objects[:8]:
        mat = o.data.materials[0] if o.data.materials else None
        col = 'none'
        if mat and mat.node_tree:
            for n in mat.node_tree.nodes:
                if 'Color' in n.inputs:
                    col = [round(c, 3) for c in n.inputs['Color'].default_value[:3]]
        print(f'  {o.name}: col={col}')
    os.remove(tmp)
