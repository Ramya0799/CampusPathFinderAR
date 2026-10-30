"""Rasterise PDF vector layers into SITE-metre masks and vectorise them back."""
import math
import numpy as np
import cv2
from shapely.geometry import Polygon, MultiPolygon, Point, box
from shapely.ops import unary_union
from shapely import affinity
from pf_common import *


class Grid:
    def __init__(self, bounds, res=0.04, margin=2.0):
        x0, y0, x1, y1 = bounds
        self.x0, self.y1 = x0 - margin, y1 + margin
        self.res = res
        self.W = int(math.ceil((x1 - x0 + 2 * margin) / res))
        self.H = int(math.ceil((y1 - y0 + 2 * margin) / res))

    def to_px(self, P):
        P = np.asarray(P, float)
        return np.c_[(P[:, 0] - self.x0) / self.res, (self.y1 - P[:, 1]) / self.res]

    def to_m(self, Q):
        Q = np.asarray(Q, float)
        return np.c_[Q[:, 0] * self.res + self.x0, self.y1 - Q[:, 1] * self.res]

    def blank(self):
        return np.zeros((self.H, self.W), np.uint8)

    def disk(self, r_m):
        r = max(1, int(round(r_m / self.res)))
        return cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))

    def poly_mask(self, geom):
        m = self.blank()
        geoms = getattr(geom, 'geoms', [geom])
        for g in geoms:
            if g.is_empty:
                continue
            cv2.fillPoly(m, [np.round(self.to_px(np.array(g.exterior.coords))).astype(np.int32)], 255)
            for h in g.interiors:
                cv2.fillPoly(m, [np.round(self.to_px(np.array(h.coords))).astype(np.int32)], 0)
        return m


def classify(d, page_w, page_h):
    """Return 'wall', 'col', 'thin', or None for a PyMuPDF drawing dict."""
    r = d['rect']
    if r.width > 0.85 * page_w or r.height > 0.85 * page_h:
        return None  # page frame
    w = d.get('width') or 0
    c = rgb_key(d.get('color')); f = rgb_key(d.get('fill'))
    dark = c is not None and max(c) <= 0.15
    if d['type'] in ('f', 'fs') and f is not None and max(f) <= 0.55 and r.width < 30 and r.height < 30:
        return 'col'
    if d['type'] in ('s', 'fs') and dark and 0.6 <= w <= 0.9:
        return 'wall'
    if d['type'] in ('s', 'fs') and dark and 0.45 <= w < 0.6:
        return 'stair'
    if d['type'] in ('s', 'fs') and dark and w < 0.45:
        return 'thin'
    return None


def collect(page, aff, clip_poly, kinds=('wall', 'col', 'thin', 'stair')):
    """Return {kind: [ndarray Nx2 site-metre polylines]} clipped to clip_poly bbox+contains test."""
    out = {k: [] for k in kinds}
    pw, ph = page.rect.width, page.rect.height
    from shapely.prepared import prep
    pc = prep(clip_poly)
    for d in page.get_drawings():
        k = classify(d, pw, ph)
        if k not in out:
            continue
        for seg in iter_segments(d):
            P = aff.apply(seg)
            # keep if midpoint of segment inside clip
            mid = P.mean(0)
            if pc.contains(Point(mid[0], mid[1])):
                out[k].append((P, d['type'] in ('f', 'fs')))
    return out


def draw(grid, items, mask=None, filled_ok=True, thickness=1):
    m = grid.blank() if mask is None else mask
    for P, filled in items:
        Q = np.round(grid.to_px(P)).astype(np.int32)
        if filled and filled_ok and len(Q) >= 3:
            cv2.fillPoly(m, [Q], 255)
        else:
            cv2.polylines(m, [Q], False, 255, thickness)
    return m


def fill_small_holes(mask, grid, max_area_m2):
    inv = cv2.bitwise_not(mask)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(inv, connectivity=4)
    out = mask.copy()
    amax = max_area_m2 / grid.res ** 2
    for i in range(1, n):
        x, y, w, h, a = stats[i]
        if a <= amax and x > 0 and y > 0 and x + w < mask.shape[1] and y + h < mask.shape[0]:
            out[lab == i] = 255
    return out


def remove_small(mask, grid, min_area_m2):
    n, lab, stats, _ = cv2.connectedComponentsWithStats(mask, connectivity=8)
    out = np.zeros_like(mask)
    amin = min_area_m2 / grid.res ** 2
    for i in range(1, n):
        if stats[i][4] >= amin:
            out[lab == i] = 255
    return out


def vectorize(mask, grid, simplify_m=0.03, min_area_m2=0.02):
    """mask -> shapely MultiPolygon in site metres (holes preserved)."""
    cs, hier = cv2.findContours(mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
    polys = []
    if hier is None:
        return MultiPolygon()
    hier = hier[0]
    for i, c in enumerate(cs):
        if hier[i][3] != -1:
            continue  # hole
        if len(c) < 3:
            continue
        # pixel-edge correction: contours run through pixel centres
        ext = grid.to_m(c[:, 0, :] + 0.0)
        holes = []
        j = hier[i][2]
        while j != -1:
            if len(cs[j]) >= 3:
                holes.append(grid.to_m(cs[j][:, 0, :] + 0.0))
            j = hier[j][0]
        p = Polygon(ext, holes).buffer(grid.res * 0.5, join_style=2)  # grow by half pixel
        p = p.simplify(simplify_m, preserve_topology=True)
        if p.is_empty:
            continue
        for g in getattr(p, 'geoms', [p]):
            if g.area >= min_area_m2:
                polys.append(g)
    return unary_union(polys) if polys else MultiPolygon()


def as_list(geom):
    if geom is None or geom.is_empty:
        return []
    return [g for g in getattr(geom, 'geoms', [geom]) if g.geom_type == 'Polygon' and not g.is_empty]


def poly_to_json(p):
    return {'outer': [[round(x, 3), round(y, 3)] for x, y in np.array(p.exterior.coords)[:-1]],
            'holes': [[[round(x, 3), round(y, 3)] for x, y in np.array(h.coords)[:-1]] for h in p.interiors]}
