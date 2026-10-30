"""Render preview images of the built campus (.blend from blender_build.py)."""
import bpy, os, sys, math
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'output')
ENGINE = os.environ.get('PF_ENGINE', 'BLENDER_WORKBENCH')


def setup():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, 'PathFinder_Campus_3D.blend'))
    sc = bpy.context.scene
    sc.render.engine = ENGINE
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
    sc.render.film_transparent = True
    if ENGINE == 'BLENDER_WORKBENCH':
        sh = sc.display.shading
        sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'
        sh.show_shadows = True; sh.show_cavity = True; sh.cavity_type = 'BOTH'
        sh.show_object_outline = False
        sc.display.shadow_shift = 0.1
        try:
            sh.background_type = 'VIEWPORT'
        except Exception:
            pass
        sc.world = sc.world or bpy.data.worlds.new('W')
        sc.world.color = (0.93, 0.95, 0.98)
    else:
        sc.cycles.samples = 24
    return sc


def camera(sc, loc, target, ortho=None, lens=35):
    cam = bpy.data.objects.get('PF_PreviewCam')
    if cam is None:
        cd = bpy.data.cameras.new('PF_PreviewCam')
        cam = bpy.data.objects.new('PF_PreviewCam', cd)
        sc.collection.objects.link(cam)
    cam.location = Vector(loc)
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    if ortho:
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = ortho
    else:
        cam.data.type = 'PERSP'; cam.data.lens = lens
    cam.data.clip_end = 2000
    sc.camera = cam


def show_only(pred):
    for ob in bpy.context.scene.objects:
        if ob.type == 'MESH':
            ob.hide_render = not pred(ob)


def render(sc, name):
    sc.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
    # composite on a light sky background
    img = bpy.data.images.load(sc.render.filepath)
    px = list(img.pixels)
    W, H = img.size
    out = []
    for i in range(0, len(px), 4):
        r, g, b, a = px[i:i + 4]
        row = (i // 4) // W
        t = row / H
        bg = (0.80 + 0.14 * t, 0.86 + 0.10 * t, 0.93 + 0.05 * t)
        out += [r * a + bg[0] * (1 - a), g * a + bg[1] * (1 - a), b * a + bg[2] * (1 - a), 1.0]
    img.pixels = out
    img.save()
    print('rendered', name)


def floor_of(ob):
    p = ob.parent
    while p is not None:
        if p.get('pfType') == 'floor':
            return p.name
        p = p.parent
    return None


if __name__ == '__main__':
    sc = setup()
    which = sys.argv[1:] or ['aerial', 'plan', 'tb_cut', 'ab_cut', 'ab_hall']
    TB_UP = {'TB_1F', 'TB_2F'}
    if 'aerial' in which:
        show_only(lambda ob: True)
        camera(sc, (-85, -95, 80), (-5, 12, 6), lens=30)
        render(sc, 'preview_aerial_SW.png')
    if 'aerialSE' in which:
        show_only(lambda ob: True)
        camera(sc, (95, -95, 85), (-5, 15, 8), lens=30)
        render(sc, 'preview_aerial_SE.png')
    if 'aerial2' in which:
        show_only(lambda ob: True)
        camera(sc, (-110, 80, 70), (-5, 10, 6), lens=32)
        render(sc, 'preview_aerial_NW.png')
    if 'plan' in which:
        show_only(lambda ob: True)
        camera(sc, (5, -2, 300), (5, -1.99, 0), ortho=176)
        render(sc, 'preview_plan_top.png')
    if 'tb_cut' in which:
        # Teaching Block cut at Ground Floor: hide floors above GF and all roofs
        def keep(ob):
            f = floor_of(ob)
            if f in TB_UP or f in ('AB_FF', 'AB_2F'):
                return False
            if f == 'TB_GF' and ob.get('pfType') in ('roof', 'parapet'):
                return False
            return True
        show_only(keep)
        camera(sc, (40, -75, 75), (-14, -18, 5), lens=32)
        render(sc, 'preview_teaching_GF_cutaway.png')
    if 'ab_cut' in which:
        def keep(ob):
            f = floor_of(ob)
            if f in ('AB_2F',) or f in TB_UP:
                return False
            return True
        show_only(keep)
        camera(sc, (40, -5, 60), (-10, 30, 10), lens=30)
        render(sc, 'preview_admin_FF_cutaway.png')
    if 'ab_hall' in which:
        def keep(ob):
            f = floor_of(ob)
            if f == 'AB_2F' and ob.get('pfType') in ('roof', 'parapet'):
                return False
            return f not in TB_UP
        show_only(keep)
        camera(sc, (35, 5, 70), (-10, 30, 20), lens=30)
        render(sc, 'preview_admin_seminar_hall.png')
