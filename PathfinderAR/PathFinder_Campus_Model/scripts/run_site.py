"""Stage 1b: site layers (hatched areas) from Teaching sheet 01 -> data/site.json"""
from raster import *
from config import *

def hatch_polys(page, aff, pred, close_r, min_area, res=0.1):
    R = box(*SITE_EXTENT)
    g = Grid(R.bounds, res, 0.0)
    m = g.blank()
    for d in page.get_drawings():
        if d['rect'].width > 0.85 * page.rect.width: continue
        if not pred(d): continue
        for seg in iter_segments(d):
            Q = np.round(g.to_px(aff.apply(seg))).astype(np.int32)
            cv2.polylines(m, [Q], False, 255, 1)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, g.disk(close_r))
    m = fill_small_holes(m, g, 400)
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, g.disk(close_r * 0.8))
    return [p for p in as_list(vectorize(m, g, 0.25, min_area))]

def style(d):
    return round(d.get('width') or 0, 2), rgb_key(d.get('color'))

pg = fitz.open(PDF_TEACH)[0]; A = teach_affine(0)
existing = hatch_polys(pg, A, lambda d: style(d) == (0.26, (0.86, 0.86, 0.86)), 0.9, 100)
plaza = hatch_polys(pg, A, lambda d: style(d) == (0.26, (0.6, 0.6, 0.6)) or style(d) == (0.14, (0.6, 0.6, 0.6)), 0.6, 200)
lawn = hatch_polys(pg, A, lambda d: style(d) == (0.14, (0.55, 0.78, 0.22)), 0.4, 20)
for nm, L in [('existing', existing), ('plaza', plaza), ('lawn', lawn)]:
    print(nm, [(round(p.area), [round(v, 1) for v in p.bounds]) for p in L])
save_json(os.path.join(ROOT, 'data', 'site.json'), {
    'existing': [poly_to_json(p) for p in existing], 'plaza': [poly_to_json(p) for p in plaza],
    'lawn': [poly_to_json(p) for p in lawn]})
