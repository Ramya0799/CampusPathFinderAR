"""Stage 1: extract every floor + site layers from the PDFs into data/*.json (SITE metres)."""
import sys, time, json
from extract import *
from config import *
from shapely.geometry import mapping

DATA = os.path.join(ROOT, 'data')
PREV = os.path.join(ROOT, 'analysis', 'floors')
os.makedirs(DATA, exist_ok=True); os.makedirs(PREV, exist_ok=True)


def page_aff(src):
    kind, idx = src
    if kind == 'teach':
        return fitz.open(PDF_TEACH)[idx], teach_affine(idx)
    return fitz.open(PDF_ADMIN)[idx], admin_affine(idx)


def stair_cores(stair_mask, g):
    """Stair-tread layer -> oriented rectangles (teaching sheets draw treads at 0.51pt)."""
    m = cv2.morphologyEx(stair_mask, cv2.MORPH_CLOSE, g.disk(0.35))
    n, lab, st, _ = cv2.connectedComponentsWithStats(m, connectivity=8)
    out = []
    for i in range(1, n):
        if st[i][4] * g.res ** 2 < 5.0:
            continue
        comp = (lab == i).astype(np.uint8) * 255
        cs, _ = cv2.findContours(comp, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        (cx, cy), (w, h), ang = cv2.minAreaRect(max(cs, key=cv2.contourArea))
        c = g.to_m([[cx, cy]])[0]
        Lr, Wr = max(w, h) * g.res, min(w, h) * g.res
        # long-axis angle in site frame (y up)
        a = ang if w >= h else ang + 90
        a = (-a) % 180
        dens = cv2.countNonZero(stair_mask & comp) / max(1, cv2.countNonZero(comp))
        if Wr < 1.5 or Lr > 14 or dens < 0.04:
            continue
        out.append({'cx': round(float(c[0]), 3), 'cy': round(float(c[1]), 3), 'L': round(Lr, 3), 'W': round(Wr, 3),
                    'ang': round(a, 1)})
    return out


def run(ids=None):
    for F in FLOORS:
        if ids and F['id'] not in ids:
            continue
        t = time.time()
        page, aff = page_aff(F['src'])
        opts = {'fp_sub': F.get('fp_sub', []), 'fp_add': F.get('fp_add', [])}
        res = extract_floor(page, aff, F['clip'], opts)
        preview(res, os.path.join(PREV, f"{F['id']}.png"))
        g = res['grid']
        out = {
            'id': F['id'],
            'walls': [poly_to_json(p) for p in as_list(res['walls'])],
            'windows': [poly_to_json(p) for p in as_list(res['windows'])],
            'rails': [poly_to_json(p) for p in as_list(res['rails'])],
            'lintels': [poly_to_json(p) for p in as_list(res['lintels'])],
            'footprint': [poly_to_json(p) for p in as_list(res['footprint'])],
            'voids': [poly_to_json(p) for p in as_list(res['voids'])],
            'void_info': res['void_info'],
            'stairs_auto': stair_cores(res['masks']['stair'], g) if F['src'][0] == 'teach' else [],
            'labels': [{k: v for k, v in L.items() if k != 'poly'} for L in res['labels']],
            'rooms': [dict({k: v for k, v in r.items() if k != 'poly'},
                           poly=poly_to_json(r['poly']) if r['poly'] is not None else None) for r in res['rooms']],
        }
        save_json(os.path.join(DATA, f"floor_{F['id']}.json"), out)
        fp = sum(Polygon(p['outer'], p['holes']).area for p in out['footprint'])
        print(f"{F['id']:8s} walls={len(out['walls'])} win={len(out['windows'])} rails={len(out['rails'])} "
              f"lint={len(out['lintels'])} fp={fp:.0f}m2 voids={[v['ok'] for v in out['void_info']]} "
              f"rooms={sum(r['poly'] is not None for r in out['rooms'])}/{len(out['rooms'])} "
              f"stairs={len(out['stairs_auto'])} {time.time()-t:.1f}s")


if __name__ == '__main__':
    run(sys.argv[1:] or None)
