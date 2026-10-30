"""Per-floor extraction: walls, openings, slab footprint, voids, room zones, labels."""
import math, random
from raster import *
from labels import room_labels
from shapely.geometry import Polygon, MultiPolygon, Point, LineString
from shapely.ops import unary_union

R_CLOSE = 0.16       # m, bridges double wall lines (<= ~0.32 m apart)
HOLE_FILL = 0.6      # m^2, fills hollow columns / wall pockets
LINTEL_MAX = 1.35    # m, max door width that receives a lintel


def line_kernel(length_px, ang_deg):
    k = np.zeros((length_px, length_px), np.uint8)
    c = (length_px - 1) / 2
    dx, dy = math.cos(math.radians(ang_deg)) * c, -math.sin(math.radians(ang_deg)) * c
    cv2.line(k, (int(round(c - dx)), int(round(c - dy))), (int(round(c + dx)), int(round(c + dy))), 1, 1)
    return k


def comp_shape(comp, g):
    cs, _ = cv2.findContours(comp, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    c = max(cs, key=cv2.contourArea)
    (cx, cy), (w, h), ang = cv2.minAreaRect(c)
    L, T = max(w, h) * g.res, min(w, h) * g.res
    return L, T


def flood_region(boundary, g, x, y, max_area_m2):
    """Flood fill from site point (x,y) inside free space of boundary mask."""
    px, py = g.to_px([[x, y]])[0]
    px, py = int(round(px)), int(round(py))
    H, W = boundary.shape
    free = boundary == 0
    # find nearest free pixel within 0.6 m
    if not (0 <= px < W and 0 <= py < H):
        return None
    if not free[py, px]:
        found = False
        r = int(0.6 / g.res)
        for rad in range(1, r):
            for dx in range(-rad, rad + 1):
                for dy in (-rad, rad):
                    for (qx, qy) in ((px + dx, py + dy), (px + dy, py + dx)):
                        if 0 <= qx < W and 0 <= qy < H and free[qy, qx]:
                            px, py = qx, qy; found = True; break
                    if found: break
                if found: break
            if found: break
        if not found:
            return None
    m = np.zeros((H + 2, W + 2), np.uint8)
    img = boundary.copy()
    cv2.floodFill(img, m, (px, py), 128, flags=4 | (128 << 8))
    reg = (img == 128).astype(np.uint8) * 255
    a = (reg > 0).sum() * g.res ** 2
    ys, xs = np.nonzero(reg)
    touches_edge = ys.min() == 0 or xs.min() == 0 or ys.max() == H - 1 or xs.max() == W - 1
    if a > max_area_m2 or touches_edge:
        return None
    return reg


def enclosure_filter(shell, cand, g, thr, step_m=0.2, max_m=16.0, nrays=24):
    """Keep candidate pixels from which >= thr of rays hit the shell within max_m."""
    f = max(1, int(round(step_m / g.res)))
    H, W = shell.shape
    Hc, Wc = H // f, W // f
    sc = shell[:Hc * f, :Wc * f].reshape(Hc, f, Wc, f).max(axis=(1, 3)) > 0
    cc = cand[:Hc * f, :Wc * f].reshape(Hc, f, Wc, f).max(axis=(1, 3)) > 0
    ys, xs = np.nonzero(cc)
    if len(ys) == 0:
        return np.zeros_like(cand)
    hits = np.zeros(len(ys), np.int32)
    nsteps = int(max_m / step_m)
    for k in range(nrays):
        ang = 2 * math.pi * k / nrays
        dx, dy = math.cos(ang), math.sin(ang)
        hit = np.zeros(len(ys), bool)
        for s_ in range(1, nsteps + 1):
            px = np.round(xs + dx * s_).astype(int); py = np.round(ys + dy * s_).astype(int)
            inside = (px >= 0) & (px < Wc) & (py >= 0) & (py < Hc)
            h = np.zeros(len(ys), bool)
            h[inside] = sc[py[inside], px[inside]]
            hit |= h
        hits += hit
    ok = hits >= thr * nrays
    keepc = np.zeros((Hc, Wc), np.uint8)
    keepc[ys[ok], xs[ok]] = 255
    keep = cv2.resize(keepc, (Wc * f, Hc * f), interpolation=cv2.INTER_NEAREST)
    out = np.zeros_like(cand)
    out[:Hc * f, :Wc * f] = keep
    out &= cand
    # smooth: drop specks
    out = cv2.morphologyEx(out, cv2.MORPH_OPEN, g.disk(0.3))
    return out


def extract_floor(page, aff, clip, opts=None):
    opts = opts or {}
    items = collect(page, aff, clip)
    g = Grid(clip.bounds, 0.04, 1.5)
    clipm = g.poly_mask(clip)

    walls = draw(g, items['wall']); draw(g, items['col'], walls)
    walls &= clipm
    K = g.disk(R_CLOSE)
    solid = cv2.morphologyEx(walls, cv2.MORPH_CLOSE, K)
    solid = fill_small_holes(solid, g, HOLE_FILL)
    solid = remove_small(solid, g, 0.03)

    # glazing drawn in the wall layer as 3 close parallel lines (Admin set): ~0.16-0.20 m strips
    glaz = g.blank()
    if opts.get('detect_glazing', True):
        from skimage.morphology import skeletonize
        dt = cv2.distanceTransform(solid, cv2.DIST_L2, 5)
        sk = skeletonize(solid > 0)
        th = 2 * dt * g.res
        seed = (sk & (th >= 0.145) & (th <= 0.205)).astype(np.uint8) * 255
        cand_g = cv2.dilate(seed, g.disk(0.12)) & solid
        # remove where a thick (>=0.22) wall core is nearby (wall ends/corners)
        thick = (sk & (th >= 0.22)).astype(np.uint8) * 255
        cand_g &= cv2.bitwise_not(cv2.dilate(thick, g.disk(0.2)))
        n_, lab_, st_, _ = cv2.connectedComponentsWithStats(cand_g, connectivity=8)
        for i in range(1, n_):
            comp = (lab_ == i).astype(np.uint8) * 255
            a = st_[i][4] * g.res ** 2
            if a < 0.08:
                continue
            L, T = comp_shape(comp, g)
            if L >= 0.8 and T <= 0.3 and a / max(L * T, 1e-6) > 0.55:
                glaz |= comp
        solid = solid & cv2.bitwise_not(glaz)

    thin = draw(g, items['thin']) & clipm
    stair_lines = draw(g, items['stair']) & clipm
    both = cv2.morphologyEx(cv2.bitwise_or(walls, thin), cv2.MORPH_CLOSE, K)
    both = fill_small_holes(both, g, HOLE_FILL)

    # --- windows / railings: thin-line infill inside wall gaps
    cand = cv2.bitwise_and(both, cv2.bitwise_not(solid))
    n, lab, st, _ = cv2.connectedComponentsWithStats(cand, connectivity=8)
    # freestanding columns: small compact solid components
    ns, slab_, sst, _ = cv2.connectedComponentsWithStats(solid, connectivity=8)
    freecol = np.zeros_like(solid)
    for i in range(1, ns):
        x, y, w, h, a = sst[i]
        am = a * g.res ** 2
        asp = max(w, h) / max(1, min(w, h))
        if am < 0.45 and asp < 1.7:
            freecol[slab_ == i] = 255
    col_d = cv2.dilate(freecol, g.disk(0.08))
    wall_d = cv2.dilate(cv2.bitwise_and(solid, cv2.bitwise_not(freecol)), g.disk(0.08))
    win = g.blank(); rail = g.blank()
    for i in range(1, n):
        a = st[i][4] * g.res ** 2
        if a < 0.05:
            continue
        comp = (lab == i).astype(np.uint8) * 255
        L, T = comp_shape(comp, g)
        fill = a / max(L * T, 1e-6)
        if fill < 0.6 or L < 0.45 or T > 0.5:
            continue
        cd = cv2.dilate(comp, g.disk(0.08))
        n_wall = cv2.countNonZero(cv2.bitwise_and(cd, wall_d))
        n_col = cv2.countNonZero(cv2.bitwise_and(cd, col_d))
        if n_wall == 0 and n_col == 0:
            continue
        if T >= 0.16 and n_col == 0:
            win |= comp
        else:
            rail |= comp

    win |= glaz
    # --- lintels over door-sized gaps
    lint = g.blank()
    Lk = int(round(LINTEL_MAX / g.res)) | 1
    for ang in range(0, 180, 15):
        lint |= cv2.morphologyEx(solid, cv2.MORPH_CLOSE, line_kernel(Lk, ang))
    lint &= cv2.bitwise_not(solid); lint &= cv2.bitwise_not(win); lint &= cv2.bitwise_not(rail)
    lint = cv2.morphologyEx(lint, cv2.MORPH_OPEN, g.disk(0.04))
    n, lab, st, _ = cv2.connectedComponentsWithStats(lint, connectivity=8)
    lint2 = g.blank()
    for i in range(1, n):
        a = st[i][4] * g.res ** 2
        if a < 0.04:
            continue
        comp = (lab == i).astype(np.uint8) * 255
        L, T = comp_shape(comp, g)
        fill = a / max(L * T, 1e-6)
        if fill > 0.7 and 0.6 <= L <= LINTEL_MAX + 0.1 and 0.08 <= T <= 0.5:
            lint2 |= comp
    lint = lint2

    # --- labels
    labels = room_labels(page, aff, clip)

    # --- footprint
    shell = solid | win | rail | lint | glaz
    fp_small = cv2.morphologyEx(shell, cv2.MORPH_CLOSE, g.disk(1.0))
    fp_small = fill_small_holes(fp_small, g, 1e9) & clipm
    fp_big = cv2.morphologyEx(shell, cv2.MORPH_CLOSE, g.disk(opts.get('fp_close', 6.5)))
    fp_big = fill_small_holes(fp_big, g, 1e9) & clipm
    cand = fp_big & cv2.bitwise_not(fp_small)
    ray_shell = shell | thin | stair_lines
    keep = enclosure_filter(ray_shell, cand, g, opts.get('encl_thr', 0.75))
    fp = fp_small | keep
    fp = fill_small_holes(fp, g, 30.0)
    fp = cv2.morphologyEx(fp, cv2.MORPH_CLOSE, g.disk(0.5))
    fp = cv2.morphologyEx(fp, cv2.MORPH_OPEN, g.disk(0.5))
    fp = remove_small(fp, g, 4.0)
    pocket_info = []
    # glazing drawn as several close thin lines on the envelope (Admin set facades)
    if opts.get('thin_glazing', True):
        thin_c = cv2.morphologyEx(thin, cv2.MORPH_CLOSE, g.disk(0.06))
        band = fp & cv2.bitwise_not(cv2.erode(fp, g.disk(0.8)))
        cand2 = thin_c & band & cv2.bitwise_not(cv2.dilate(solid, g.disk(0.02))) & cv2.bitwise_not(win)
        Lk2 = int(round(1.2 / g.res)) | 1
        for ang in range(0, 180, 15):
            op = cv2.morphologyEx(cand2, cv2.MORPH_OPEN, line_kernel(Lk2, ang))
            n2, lab2, st2, _ = cv2.connectedComponentsWithStats(op, connectivity=8)
            for i in range(1, n2):
                a2 = st2[i][4] * g.res ** 2
                if a2 < 0.04:
                    continue
                comp = (lab2 == i).astype(np.uint8) * 255
                L, T = comp_shape(comp, g)
                if L < 1.2 or T > 0.26 or a2 / max(L * T, 1e-6) < 0.6:
                    continue
                win |= comp
    for P in opts.get('fp_add', []):
        fp |= g.poly_mask(Polygon(P))
    for P in opts.get('fp_sub', []):
        fp &= cv2.bitwise_not(g.poly_mask(Polygon(P)))

    # boundary used for region flood fills: walls + all thin lines (+ door arcs)
    bnd = both | stair_lines | cv2.bitwise_not(fp)
    bnd = cv2.morphologyEx(bnd, cv2.MORPH_CLOSE, g.disk(0.06))

    voids = g.blank()
    void_info = []
    for L in labels:
        if L['kind'] != 'void':
            continue
        reg = flood_region(bnd, g, L['x'], L['y'], 2500)
        if reg is not None:
            voids |= reg
            void_info.append({'label': L['name'], 'ok': True})
        else:
            void_info.append({'label': L['name'], 'ok': False, 'x': L['x'], 'y': L['y']})
    voids = cv2.dilate(voids, g.disk(0.05)) & cv2.bitwise_not(solid)

    rooms = []
    for L in labels:
        if L['kind'] != 'room':
            continue
        exp = None
        if L.get('dims_m'):
            exp = L['dims_m'][0] * L['dims_m'][1]
        maxa = max(exp * 1.8, exp + 6) if exp else 400
        reg = flood_region(bnd, g, L['x'], L['y'], maxa)
        poly = None
        if reg is not None:
            reg = cv2.morphologyEx(reg, cv2.MORPH_OPEN, g.disk(0.05))
            poly = vectorize(reg, g, 0.05, 0.3)
            ps = as_list(poly)
            poly = max(ps, key=lambda p: p.area) if ps else None
            if poly is not None and exp and poly.area < 0.35 * exp:
                poly = None
        rooms.append(dict(L, poly=poly))

    res = {
        'grid': g, 'walls': vectorize(solid, g), 'windows': vectorize(win, g), 'rails': vectorize(rail, g),
        'lintels': vectorize(lint, g), 'footprint': vectorize(fp, g, 0.06, 1.0), 'voids': vectorize(voids, g, 0.06, 1.0),
        'labels': labels, 'rooms': rooms, 'void_info': void_info, 'pocket_info': pocket_info,
        'masks': {'solid': solid, 'win': win, 'rail': rail, 'lint': lint, 'fp': fp, 'voids': voids, 'thin': thin,
                  'stair': stair_lines},
    }
    return res


def preview(res, path):
    g = res['grid']; M = res['masks']
    vis = np.full((g.H, g.W, 3), 255, np.uint8)
    vis[M['fp'] > 0] = (235, 235, 225)
    rnd = random.Random(3)
    for r in res['rooms']:
        if r['poly'] is not None:
            col = (rnd.randint(150, 240), rnd.randint(150, 240), rnd.randint(150, 240))
            m = g.poly_mask(r['poly']); vis[m > 0] = col
    vis[M['voids'] > 0] = (255, 255, 160)
    vis[M['thin'] > 0] = (200, 200, 200)
    vis[M['stair'] > 0] = (0, 160, 0)
    vis[M['solid'] > 0] = (40, 40, 40)
    vis[M['win'] > 0] = (255, 150, 0)
    vis[M['rail'] > 0] = (0, 200, 0)
    vis[M['lint'] > 0] = (0, 0, 255)
    for L in res['labels']:
        P = g.to_px([[L['x'], L['y']]])[0].astype(int)
        txt = (L.get('name') or str(L.get('levels')))[:18]
        col = (0, 0, 200) if L['kind'] == 'void' else (120, 0, 0)
        if L['kind'] in ('room', 'void', 'level'):
            cv2.putText(vis, txt, (int(P[0]) - 20, int(P[1])), 0, 0.33, col, 1)
    cv2.imwrite(path, vis)
