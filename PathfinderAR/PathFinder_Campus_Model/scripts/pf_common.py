"""Shared helpers for the PathFinder floor-plan -> 3D pipeline.

Coordinate frames
-----------------
PDF page   : points, origin top-left, y down (as PyMuPDF reports).
SITE frame : metres, x = page-right, y = page-up, derived from the Teaching
             Block site sheets.  Scale is calibrated from the metric 17.50 m /
             22.50 m dimension line printed on every Teaching Block sheet
             (230.363 pt = 22.50 m, 179.205 pt = 17.50 m  ->  10.239 pt/m).
             The SITE origin is the knee point of that dimension line on each
             sheet, so all six sheets register by a pure translation.
MODEL frame: SITE shifted so (0,0) is the Teaching Block main entrance
             (see MODEL_ORIGIN_SITE), z = RL - 60.00 m.
"""
import json, math, os
import numpy as np
import pymupdf as fitz

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PDF_ADMIN = os.path.join(ROOT, 'src_pdfs', 'admin.pdf')
PDF_TEACH = os.path.join(ROOT, 'src_pdfs', 'teaching.pdf')

PT_PER_M = 10.239          # teaching site sheets
RL_DATUM = 60.00           # model z = RL - RL_DATUM

# knee point of the green 17.50/22.50 m dimension line, per teaching sheet
TEACH_ANCHORS = {
    0: (718.28, 1278.12),
    1: (711.165, 1252.24),
    2: (713.555, 1252.24),
    3: (713.645, 1252.24),
    4: (842.04, 1252.24),
    5: (713.035, 1252.24),
}


class Affine:
    """2D affine page-pt -> site-m.  site = M @ [x, y, 1]."""
    def __init__(self, M):
        self.M = np.asarray(M, float)

    def __call__(self, x, y):
        M = self.M
        return (M[0, 0] * x + M[0, 1] * y + M[0, 2],
                M[1, 0] * x + M[1, 1] * y + M[1, 2])

    def apply(self, pts):
        pts = np.asarray(pts, float)
        return pts @ self.M[:, :2].T + self.M[:, 2]

    def then_site_of_teach(self, sheet):
        """compose: (this maps admin->teach-sheet page) then teach-sheet->site"""
        T = teach_affine(sheet).M
        A = np.vstack([self.M, [0, 0, 1]])
        T3 = np.vstack([T, [0, 0, 1]])
        return Affine((T3 @ A)[:2])

    @property
    def scale(self):
        return math.sqrt(abs(np.linalg.det(self.M[:, :2])))


def teach_affine(sheet):
    ax, ay = TEACH_ANCHORS[sheet]
    s = 1.0 / PT_PER_M
    return Affine([[s, 0, -ax * s], [0, -s, ay * s]])


def column_centres(page, size, tol, clip=None):
    out = []
    for d in page.get_drawings():
        if d['type'] in ('f', 'fs'):
            r = d['rect']
            if clip is not None and not clip.contains(r):
                continue
            if abs(r.width - size) < tol and abs(r.height - size) < tol:
                out.append(((r.x0 + r.x1) / 2, (r.y0 + r.y1) / 2))
    return np.array(out)


def fit_similarity_by_points(src, dst, scale_guess, tol):
    """Find scale+translation (no rotation) mapping src->dst point sets."""
    best = None
    for a in src:
        for t in dst:
            tr = t - a * scale_guess
            P = src * scale_guess + tr
            d = np.min(np.linalg.norm(P[:, None] - dst[None], axis=2), axis=1)
            n = int((d < tol).sum())
            if best is None or n > best[0]:
                best = (n, tr)
    tr = best[1]
    P = src * scale_guess + tr
    D = np.linalg.norm(P[:, None] - dst[None], axis=2)
    idx = np.argmin(D, axis=1)
    m = D[np.arange(len(src)), idx] < tol
    X, Y = src[m], dst[idx[m]]
    # least squares uniform scale + translation
    Xc, Yc = X - X.mean(0), Y - Y.mean(0)
    s = (Xc * Yc).sum() / (Xc * Xc).sum()
    t = Y.mean(0) - s * X.mean(0)
    res = X * s + t - Y
    return s, t, int(m.sum()), float(np.sqrt((res ** 2).sum(1).mean()))


_admin_cache = {}


def admin_affine(page_index):
    """admin PDF page -> site metres (via Teaching sheet 4 column grid)."""
    if page_index in _admin_cache:
        return _admin_cache[page_index]
    adoc = fitz.open(PDF_ADMIN)
    tdoc = fitz.open(PDF_TEACH)
    T = column_centres(tdoc[3], 4.68, 0.1, fitz.Rect(400, 0, 800, 470))
    A1 = column_centres(adoc[0], 15.5, 0.3)
    s1, t1, n1, e1 = fit_similarity_by_points(A1, T, 0.35313, 3.0)
    info = {'p1_to_sheet4': (s1, t1.tolist(), n1, e1)}
    if page_index in (0, 2, 3):
        s, t = s1, t1
    elif page_index == 1:
        A2 = column_centres(adoc[1], 15.1, 0.3)
        s21, t21, n2, e2 = fit_similarity_by_points(A2, A1, 1.0257, 4.0)
        info['p2_to_p1'] = (s21, t21.tolist(), n2, e2)
        s, t = s1 * s21, s1 * t21 + t1
    M = [[s, 0, t[0]], [0, s, t[1]]]
    aff = Affine(M).then_site_of_teach(3)
    aff.info = info
    _admin_cache[page_index] = aff
    return aff


def rgb_key(c):
    return tuple(round(v, 2) for v in c) if c else None


def iter_segments(drawing, n_curve=8):
    """Yield polyline point lists (page pts) for every path item."""
    for it in drawing['items']:
        k = it[0]
        if k == 'l':
            yield [(it[1].x, it[1].y), (it[2].x, it[2].y)]
        elif k == 're':
            r = it[1]
            yield [(r.x0, r.y0), (r.x1, r.y0), (r.x1, r.y1), (r.x0, r.y1), (r.x0, r.y0)]
        elif k == 'qu':
            q = it[1]
            yield [(q.ul.x, q.ul.y), (q.ur.x, q.ur.y), (q.lr.x, q.lr.y), (q.ll.x, q.ll.y), (q.ul.x, q.ul.y)]
        elif k == 'c':
            p0, p1, p2, p3 = it[1], it[2], it[3], it[4]
            pts = []
            for i in range(n_curve + 1):
                t = i / n_curve
                a = (1 - t) ** 3; b = 3 * (1 - t) ** 2 * t; c = 3 * (1 - t) * t * t; d = t ** 3
                pts.append((a * p0.x + b * p1.x + c * p2.x + d * p3.x,
                            a * p0.y + b * p1.y + c * p2.y + d * p3.y))
            yield pts


def save_json(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w') as f:
        json.dump(obj, f, indent=1)


def load_json(path):
    with open(path) as f:
        return json.load(f)
