"""Stage 3 (Blender): build the campus from data/scene.json and export FBX files.

Runs either with the `bpy` Python module (`python3 blender_build.py`) or inside Blender:
    blender --background --python blender_build.py
Only bpy + the standard library are used here, so a stock Blender install is enough.
"""
import bpy, json, os, sys, math

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'data')
OUT = os.path.join(ROOT, 'output')
os.makedirs(OUT, exist_ok=True)

FBX_KW = dict(object_types={'MESH', 'EMPTY'}, use_custom_props=True, apply_unit_scale=True,
              apply_scale_options='FBX_SCALE_ALL', axis_forward='Z', axis_up='Y', bake_space_transform=True,
              mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False, path_mode='AUTO',
              embed_textures=False, global_scale=1.0, use_triangles=False)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    sc.unit_settings.length_unit = 'METERS'


def make_materials(mats):
    out = {}
    for name, (r, g, b, a) in mats.items():
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (r, g, b, 1.0)
        bsdf.inputs['Roughness'].default_value = 0.15 if a < 1 else 0.8
        if a < 1.0:
            bsdf.inputs['Alpha'].default_value = a
            try:
                m.surface_render_method = 'BLENDED'
            except Exception:
                try:
                    m.blend_method = 'BLEND'
                except Exception:
                    pass
        m.diffuse_color = (r, g, b, a)
        out[name] = m
    return out


def set_props(obj, props):
    for k, v in (props or {}).items():
        if isinstance(v, (list, tuple, dict)):
            v = json.dumps(v)
        if v is None:
            v = ''
        obj[k] = v


def build(scene):
    mats = make_materials(scene['materials'])
    objs = {}
    coll = bpy.context.scene.collection
    for o in scene['objects']:
        if o['type'] == 'EMPTY':
            ob = bpy.data.objects.new(o['name'], None)
            ob.empty_display_type = 'PLAIN_AXES'
            ob.empty_display_size = 0.5
            ob.location = o['loc']
        else:
            me = bpy.data.meshes.new(o['name'])
            me.from_pydata([tuple(v) for v in o['verts']], [], [tuple(f) for f in o['faces']])
            me.validate(clean_customdata=False)
            me.update()
            me.materials.append(mats[o['material']])
            ob = bpy.data.objects.new(o['name'], me)
        set_props(ob, o.get('props'))
        coll.objects.link(ob)
        objs[o['name']] = (ob, o)
    for name, (ob, o) in objs.items():
        if o.get('parent'):
            ob.parent = objs[o['parent']][0]
    return {n: v[0] for n, v in objs.items()}


def subtree(root):
    out = [root]
    for c in root.children:
        out += subtree(c)
    return out


def export(path, roots, extra=()):
    bpy.ops.object.select_all(action='DESELECT')
    sel = []
    for r in roots:
        sel += subtree(r)
    sel += list(extra)
    for ob in sel:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = sel[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, **FBX_KW)
    return len(sel)


def main():
    reset()
    with open(os.path.join(DATA, 'scene.json')) as f:
        scene = json.load(f)
    O = build(scene)
    root = O['PathFinderCampus']
    blend = os.path.join(OUT, 'PathFinder_Campus_3D.blend')
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    n = export(os.path.join(OUT, 'PathFinder_Campus_3D.fbx'), [root])
    print('exported campus objects', n)
    # per-building exports keep the same coordinates; they include the root + reference markers so
    # every file shares one origin and can be overlaid in Unity.
    for blk, fname in (('AdministrativeBlock', 'PathFinder_Admin_Block.fbx'), ('TeachingBlock', 'PathFinder_Teaching_Block.fbx')):
        navs = [ob for ob in O['NavigationAnchors'].children if ob.get('buildingId') == blk]
        bpy.ops.object.select_all(action='DESELECT')
        sel = [root, O['NavigationAnchors'], O['ReferenceMarkers']] + subtree(O[blk]) + navs + list(O['ReferenceMarkers'].children)
        for ob in sel:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, fname), use_selection=True, **FBX_KW)
        print('exported', fname, len(sel))


if __name__ == '__main__':
    main()
