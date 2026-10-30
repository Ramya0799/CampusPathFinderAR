"""Stage 4: re-import every exported FBX into an empty Blender scene and check it."""
import bpy, os, sys, json, math
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'output')
sys.path.insert(0, os.path.join(os.path.dirname(bpy.app.binary_path or ''), ''))


def fbx_header(path):
    """Read GlobalSettings straight from the binary FBX with Blender's own parser."""
    from io_scene_fbx import parse_fbx
    root, version = parse_fbx.parse(path)
    out = {'fbxVersion': version}
    for child in root.elems:
        if child.id == b'GlobalSettings':
            for sub in child.elems:
                if sub.id == b'Properties70':
                    for p in sub.elems:
                        key = p.props[0].decode()
                        if key in ('UpAxis', 'UpAxisSign', 'FrontAxis', 'FrontAxisSign', 'CoordAxis', 'CoordAxisSign',
                                   'UnitScaleFactor', 'OriginalUnitScaleFactor'):
                            out[key] = p.props[-1]
    return out


def check(fname):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = os.path.join(OUT, fname)
    bpy.ops.import_scene.fbx(filepath=path, axis_forward='Z', axis_up='Y', use_custom_props=True)
    obs = list(bpy.context.scene.objects)
    meshes = [o for o in obs if o.type == 'MESH']
    empties = [o for o in obs if o.type == 'EMPTY']
    mats = sorted({s.material.name for o in meshes for s in o.material_slots if s.material})
    missing_mat = [o.name for o in meshes if not o.material_slots or not o.material_slots[0].material]
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    tris = 0; nverts = 0; bad_scale = []
    for o in meshes:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
        me = o.data
        tris += sum(len(p.vertices) - 2 for p in me.polygons); nverts += len(me.vertices)
        if any(abs(s - 1.0) > 1e-3 for s in o.matrix_world.to_scale()):
            bad_scale.append(o.name)
    markers = {}
    for n in ('PF_Origin', 'PF_Marker_PlanRight_10m', 'PF_Marker_PlanUp_10m', 'PF_Marker_Up_10m', 'PF_TrueNorth_10m'):
        ob = bpy.data.objects.get(n)
        if ob:
            markers[n] = [round(v, 3) for v in ob.matrix_world.translation]
    root = bpy.data.objects.get('PathFinderCampus')
    props_ok = sum(1 for o in obs if 'pfType' in o.keys())
    res = {
        'file': fname, 'sizeMB': round(os.path.getsize(path) / 1e6, 2), 'header': fbx_header(path),
        'objects': len(obs), 'meshes': len(meshes), 'empties': len(empties), 'triangles': tris, 'vertices': nverts,
        'materials': mats, 'meshesWithoutMaterial': missing_mat, 'objectsWithNonUnitScale': bad_scale[:10],
        'bboxMin_m': [round(v, 2) for v in lo], 'bboxMax_m': [round(v, 2) for v in hi],
        'bboxSize_m': [round(h - l, 2) for h, l in zip(hi, lo)], 'markers': markers,
        'rootPresent': root is not None, 'objectsWithPfProps': props_ok,
        'floors': sorted(o.name for o in empties if o.get('pfType') == 'floor'),
        'navAnchors': sum(1 for o in empties if o.get('pfType') == 'navAnchor'),
        'roomZones': sum(1 for o in meshes if o.get('pfType') == 'roomZone'),
    }
    # spot-check known dimensions: Teaching Block lift car 7'2" x 6'0" and classroom 42'3" x 31'6"
    return res


if __name__ == '__main__':
    report = [check(f) for f in ('PathFinder_Campus_3D.fbx', 'PathFinder_Admin_Block.fbx', 'PathFinder_Teaching_Block.fbx')]
    with open(os.path.join(OUT, 'fbx_validation.json'), 'w') as f:
        json.dump(report, f, indent=1, default=str)
    for r in report:
        print(json.dumps({k: v for k, v in r.items() if k not in ('floors',)}, default=str))
