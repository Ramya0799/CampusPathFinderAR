"""Stage 2: turn extracted floor data into a triangulated scene description (data/scene.json),
plus the room inventory and navigation anchors.  Pure Python (shapely, earcut, scipy) - no Blender."""
import math, re, json, os
import numpy as np
import mapbox_earcut as earcut
from shapely.geometry import Polygon, MultiPolygon, Point, LineString, box
from shapely.geometry.polygon import orient
from shapely.ops import unary_union
from shapely import affinity
import shapely
from scipy.interpolate import LinearNDInterpolator
from pf_common import ROOT, load_json, save_json, RL_DATUM
from config import *

DATA = os.path.join(ROOT, 'data')
OX, OY = MODEL_ORIGIN_SITE

MATERIALS = {
    'PF_Wall': (0.87, 0.85, 0.80, 1.0), 'PF_Glass': (0.55, 0.75, 0.92, 0.35), 'PF_Slab': (0.66, 0.66, 0.64, 1.0),
    'PF_Roof': (0.52, 0.53, 0.56, 1.0), 'PF_Railing': (0.30, 0.31, 0.34, 1.0), 'PF_Stair': (0.74, 0.71, 0.66, 1.0),
    'PF_Steps': (0.74, 0.71, 0.66, 1.0), 'PF_Gallery': (0.55, 0.36, 0.25, 1.0), 'PF_Stage': (0.45, 0.30, 0.22, 1.0),
    'PF_Terrain': (0.42, 0.55, 0.32, 1.0), 'PF_Road': (0.17, 0.17, 0.19, 1.0), 'PF_Plaza': (0.80, 0.74, 0.64, 1.0),
    'PF_Lawn': (0.33, 0.64, 0.29, 1.0), 'PF_Existing': (0.78, 0.75, 0.72, 1.0), 'PF_Marker': (0.90, 0.20, 0.20, 1.0),
    'PF_Zone_classroom': (0.98, 0.84, 0.52, 1.0), 'PF_Zone_toilet': (0.58, 0.80, 0.96, 1.0),
    'PF_Zone_lobby': (0.96, 0.74, 0.74, 1.0), 'PF_Zone_office': (0.72, 0.90, 0.68, 1.0),
    'PF_Zone_staff': (0.84, 0.74, 0.96, 1.0), 'PF_Zone_food': (1.00, 0.68, 0.38, 1.0),
    'PF_Zone_meeting': (0.68, 0.70, 1.00, 1.0), 'PF_Zone_service': (0.60, 0.60, 0.60, 1.0),
    'PF_Zone_lift': (0.40, 0.42, 0.48, 1.0), 'PF_Zone_open': (0.60, 0.86, 0.50, 1.0),
    'PF_Zone_entrance': (0.96, 0.58, 0.58, 1.0), 'PF_Zone_room': (0.90, 0.90, 0.80, 1.0),
    'PF_Zone_stair': (0.74, 0.71, 0.66, 1.0),
}
NO_ZONE_TYPES = {'corridor'}
SILL, HEAD = 0.90, 2.10


def mx(x, y):
    return (round(x - OX, 4), round(y - OY, 4))


def mz(rl):
    return round(rl - RL_DATUM, 4)


def unity_expected(p):
    """Expected Unity position (x, y, z) for a model point (X, Y, Z) with the export settings used."""
    return [p[0], p[2], p[1]]


def geom_from_json(lst):
    ps = [Polygon(p['outer'], p['holes']).buffer(0) for p in lst if len(p['outer']) >= 3]
    return unary_union(ps) if ps else MultiPolygon()


def polys(g):
    if g is None or g.is_empty:
        return []
    if g.geom_type == 'Polygon':
        return [g]
    out = []
    for gg in getattr(g, 'geoms', []):
        out += polys(gg)
    return out


class Mesh:
    def __init__(self):
        self.v = []; self.f = []

    def add_prism(self, geom, z0, z1, min_area=0.01):
        if z1 - z0 < 0.005:
            return
        for p in polys(geom):
            if p.area < min_area:
                continue
            p = orient(p.simplify(0.005, preserve_topology=True), 1.0)
            if p.is_empty or p.geom_type != 'Polygon':
                continue
            rings = [np.array(p.exterior.coords)[:-1]] + [np.array(h.coords)[:-1] for h in p.interiors]
            rings = [r for r in rings if len(r) >= 3]
            if not rings:
                continue
            flat = np.vstack(rings)
            ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
            tris = earcut.triangulate_float64(flat, ends).reshape(-1, 3)
            n = len(flat); base = len(self.v)
            for (x, y) in flat:
                self.v.append((*mx(x, y), mz(z0)))
            for (x, y) in flat:
                self.v.append((*mx(x, y), mz(z1)))
            for a, b, c in tris:
                pa, pb, pc = flat[a], flat[b], flat[c]
                cross = (pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0])
                if cross < 0:
                    b, c = c, b
                self.f.append((base + n + a, base + n + b, base + n + c))   # top (CCW from above)
                self.f.append((base + a, base + c, base + b))               # bottom
            off = 0
            for r in rings:
                m = len(r)
                for i in range(m):
                    a = base + off + i; b = base + off + (i + 1) % m
                    self.f.append((a, b, b + n, a + n))
                off += m

    def add_surface(self, geom, zfun, dz=0.0):
        """Top-only triangulated surface with per-vertex height zfun(x, y) (site coords, RL)."""
        for p in polys(geom):
            if p.area < 1e-4:
                continue
            p = orient(p, 1.0)
            rings = [np.array(p.exterior.coords)[:-1]] + [np.array(h.coords)[:-1] for h in p.interiors]
            rings = [r for r in rings if len(r) >= 3]
            flat = np.vstack(rings)
            ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
            tris = earcut.triangulate_float64(flat, ends).reshape(-1, 3)
            base = len(self.v)
            for (x, y) in flat:
                self.v.append((*mx(x, y), mz(zfun(x, y) + dz)))
            for a, b, c in tris:
                pa, pb, pc = flat[a], flat[b], flat[c]
                if (pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0]) < 0:
                    b, c = c, b
                self.f.append((base + a, base + b, base + c))

    def add_raw(self, verts, faces):
        base = len(self.v)
        self.v += verts
        self.f += [tuple(base + i for i in f) for f in faces]

    def empty(self):
        return not self.f


OBJECTS = []


def emit_mesh(name, parent, material, mesh, props=None):
    if mesh.empty():
        return None
    OBJECTS.append({'type': 'MESH', 'name': name, 'parent': parent, 'material': material,
                    'verts': [[round(c, 4) for c in v] for v in mesh.v], 'faces': [list(map(int, f)) for f in mesh.f],
                    'props': props or {}})
    return name


def emit_empty(name, parent, loc=(0, 0, 0), props=None):
    OBJECTS.append({'type': 'EMPTY', 'name': name, 'parent': parent, 'loc': [round(c, 4) for c in loc], 'props': props or {}})
    return name


def safe(s, n=40):
    s = re.sub(r'[^A-Za-z0-9]+', '_', s).strip('_')
    return s[:n] or 'Unnamed'


def rect(cx, cy, L, W, ang_deg):
    r = box(-L / 2, -W / 2, L / 2, W / 2)
    r = affinity.rotate(r, ang_deg, origin=(0, 0))
    return affinity.translate(r, cx, cy)


# --------------------------------------------------------------------------------------------
def main():
    floors = {}
    for F in FLOORS:
        d = load_json(os.path.join(DATA, f"floor_{F['id']}.json"))
        F = dict(F)
        F['fp'] = geom_from_json(d['footprint'])
        F['voids'] = MultiPolygon() if F.get('ignore_voids') else geom_from_json(d['voids'])
        F['walls'] = geom_from_json(d['walls'])
        F['windows'] = geom_from_json(d['windows'])
        F['rails'] = geom_from_json(d['rails'])
        F['lintels'] = geom_from_json(d['lintels'])
        F['data'] = d
        F['slab_t'] = F.get('slab_t', SLAB_T)
        floors[F['id']] = F
    order = [F['id'] for F in FLOORS]
    site = load_json(os.path.join(DATA, 'site.json'))

    # default top for each floor: next floor in same block, else explicit roof
    for fid in order:
        F = floors[fid]
        same = sorted([G for G in floors.values() if G['block'] == F['block'] and G['rl'] > F['rl'] + 1.0], key=lambda G: G['rl'])
        if 'roof_rl' in F:
            F['roof_t'] = F.get('roof_t', 0.20)
            F['default_top'] = F['roof_rl'] - F['roof_t']
            F['roof_top'] = F['roof_rl']
        elif same:
            F['roof_t'] = same[0]['slab_t'] if same[0]['slab_t'] < 0.5 else SLAB_T
            F['roof_top'] = same[0]['rl']
            F['default_top'] = same[0]['rl'] - F['roof_t']
        else:
            F['roof_t'] = 0.2; F['roof_top'] = F['rl'] + 3.8; F['default_top'] = F['roof_top'] - 0.2
        # a floor can be 'covered' by slabs up to ~two storeys above (double-height spaces);
        # anything higher is treated as open and gets its own roof at default_top
        F['covers'] = [(G['fp'], G['rl'] - G['slab_t'], G['id']) for G in
                       sorted([G for G in floors.values() if F['rl'] + 1.0 < G['rl'] <= F['rl'] + 8.5],
                              key=lambda G: G['rl'])]

    def split_cover(F, geom):
        out = []
        rem = geom
        for cfp, top, gid in F['covers']:
            if rem.is_empty:
                break
            inter = rem.intersection(cfp)
            if not inter.is_empty and inter.area > 1e-4:
                out.append((inter, top))
                rem = rem.difference(cfp)
        if not rem.is_empty and rem.area > 1e-4:
            out.append((rem, F['default_top']))
        return out

    # ---- stairs & lifts --------------------------------------------------------------------
    stair_open = {fid: [] for fid in order}
    stair_meshes = []  # (block, floor_id, name, Mesh, props)
    nav = []
    for S in MANUAL_STAIRS:
        R = rect(S['cx'], S['cy'], S['L'], S['W'], S['ang'])
        u = np.array([math.cos(math.radians(S['ang'])), math.sin(math.radians(S['ang']))])
        v = np.array([-u[1], u[0]])
        c = np.array([S['cx'], S['cy']])
        fl = S['floors']
        for i in range(len(fl) - 1):
            Fa, Fb = floors[fl[i]], floors[fl[i + 1]]
            z0, z1 = Fa['rl'], Fb['rl']
            stair_open[Fb['id']].append(R.buffer(-0.02))
            m = Mesh()
            nr = max(2, int(round((z1 - z0) / 0.165)))
            def step_box(s0, s1, t0, t1, top):
                q = Polygon([tuple(c + u * s0 + v * t0), tuple(c + u * s1 + v * t0), tuple(c + u * s1 + v * t1), tuple(c + u * s0 + v * t1)])
                m.add_prism(q, top - 0.45, top)
            L, W = S['L'], S['W']
            if S['kind'] == 'straight':
                land = 1.4
                run = (L - land) / 2
                n1 = nr // 2; n2 = nr - n1
                zm = z0 + (z1 - z0) * n1 / nr
                for k in range(n1):
                    s0 = -L / 2 + run * k / n1; s1 = -L / 2 + run * (k + 1) / n1
                    step_box(s0, s1, -W / 2, W / 2, z0 + (z1 - z0) * (k + 1) / nr)
                step_box(-L / 2 + run, -L / 2 + run + land, -W / 2, W / 2, zm)
                for k in range(n2):
                    s0 = -L / 2 + run + land + run * k / n2; s1 = s0 + run / n2
                    step_box(s0, s1, -W / 2, W / 2, zm + (z1 - zm) * (k + 1) / n2 if k < n2 - 1 else z1)
                bottom = c - u * (L / 2 + 0.6); top = c + u * (L / 2 + 0.6)
            else:  # dog-leg: flight 1 in -v half, landing at +u end, flight 2 in +v half back to -u end
                land = min(1.5, L * 0.3)
                run = L - land
                n1 = nr // 2; n2 = nr - n1
                zm = z0 + (z1 - z0) * n1 / nr
                for k in range(n1):
                    s0 = -L / 2 + run * k / n1; s1 = -L / 2 + run * (k + 1) / n1
                    step_box(s0, s1, -W / 2, -0.05, z0 + (z1 - z0) * (k + 1) / nr)
                step_box(L / 2 - land, L / 2, -W / 2, W / 2, zm)
                for k in range(n2):
                    s1 = L / 2 - land - run * k / n2; s0 = L / 2 - land - run * (k + 1) / n2
                    step_box(s0, s1, 0.05, W / 2, zm + (z1 - zm) * (k + 1) / n2)
                bottom = c - u * (L / 2 + 0.6) - v * W / 4; top = c - u * (L / 2 + 0.6) + v * W / 4
            props = {'pfType': 'stair', 'stairId': S['id'], 'fromFloor': Fa['id'], 'toFloor': Fb['id'],
                     'fromRL': z0, 'toRL': z1, 'geometry': 'schematic flights inside traced stair core',
                     'positionVerified': bool(S['verified'])}
            stair_meshes.append((S['block'], Fa['id'], f"{Fa['id']}_{S['id']}_to_{Fb['id']}", m, props))
            nav.append(dict(name=f"NAV_{Fa['id']}_{S['id']}_Up", floor=Fa['id'], block=S['block'], kind='stair',
                            site=bottom.tolist(), rl=z0, props={'stairId': S['id'], 'connectsTo': f"NAV_{Fb['id']}_{S['id']}_Down"}))
            nav.append(dict(name=f"NAV_{Fb['id']}_{S['id']}_Down", floor=Fb['id'], block=S['block'], kind='stair',
                            site=(top if S['kind'] == 'straight' else top).tolist(), rl=z1,
                            props={'stairId': S['id'], 'connectsTo': f"NAV_{Fa['id']}_{S['id']}_Up"}))

    # lifts: union of 'LIFT' labels with dimensions per block
    lifts = {}
    for fid in order:
        F = floors[fid]
        for L in F['data']['labels']:
            if L['kind'] == 'room' and L['name'].strip().upper() == 'LIFT' and L.get('dims_m'):
                ang = math.degrees(math.atan2(-L['dir'][1], L['dir'][0]))
                key = F['block']
                lst = lifts.setdefault(key, [])
                if not any(math.hypot(L['x'] - q['x'], L['y'] - q['y']) < 1.2 for q in lst):
                    lst.append({'x': L['x'], 'y': L['y'], 'a': L['dims_m'][0], 'b': L['dims_m'][1], 'ang': ang, 'src': fid})
    for blk, lst in lifts.items():
        bfloors = [fid for fid in order if floors[fid]['block'] == blk]
        lowest = min(bfloors, key=lambda f: floors[f]['rl'])
        for k, q in enumerate(lst):
            R = rect(q['x'], q['y'], q['a'], q['b'], q['ang'])
            for fid in bfloors:
                F = floors[fid]
                if not F['fp'].buffer(0.5).contains(Point(q['x'], q['y'])):
                    continue
                if fid != lowest:
                    stair_open[fid].append(R.buffer(-0.02))
                nav.append(dict(name=f"NAV_{fid}_Lift_{k + 1}", floor=fid, block=blk, kind='lift', site=[q['x'], q['y']],
                                rl=F['rl'], props={'liftId': f"{blk}_Lift_{k + 1}", 'carSize_m': [q['a'], q['b']]}))

    # ---- hierarchy -------------------------------------------------------------------------
    emit_empty('PathFinderCampus', None, props={'pfType': 'root', 'units': 'metres', 'rlDatum': RL_DATUM,
                                                 'originSite': list(MODEL_ORIGIN_SITE),
                                                 'origin': 'Teaching Block north entrance (top of steps), RL 60.00 = z 0',
                                                 'trueNorthDegFromPlusX': TRUE_NORTH_DEG})
    for blk in ('AdministrativeBlock', 'TeachingBlock'):
        emit_empty(blk, 'PathFinderCampus', props={'pfType': 'building', 'buildingId': blk})
    emit_empty('Site', 'PathFinderCampus', props={'pfType': 'site', 'accuracy': 'provisional'})
    emit_empty('NavigationAnchors', 'PathFinderCampus', props={'pfType': 'navroot'})
    emit_empty('ReferenceMarkers', 'PathFinderCampus', props={'pfType': 'markers'})

    inventory = []
    stats = {}
    FOUND = {}
    for fid in order:
        F = floors[fid]
        blk = F['block']
        fe = emit_empty(fid, blk, props={'pfType': 'floor', 'floorId': fid, 'floorName': F['name'], 'buildingId': blk,
                                         'rl': F['rl'], 'rlVerified': bool(F.get('rl_verified')), 'modelZ': mz(F['rl']),
                                         'source': F['sheet']})
        # slab
        slab = F['fp'].difference(F['voids'])
        for o in stair_open[fid]:
            slab = slab.difference(o)
        m = Mesh(); m.add_prism(slab, F['rl'] - F['slab_t'], F['rl'])
        emit_mesh(f'{fid}_Slab', fe, 'PF_Slab', m, {'pfType': 'slab', 'floorId': fid})
        FOUND[fid] = F
        # walls / openings
        mw = Mesh(); mg = Mesh(); mr = Mesh()
        for part, top in split_cover(F, F['walls']):
            mw.add_prism(part, F['rl'], top)
        for part, top in split_cover(F, F['windows']):
            mw.add_prism(part, F['rl'], F['rl'] + SILL)
            mg.add_prism(part, F['rl'] + SILL, min(F['rl'] + HEAD, top))
            if top > F['rl'] + HEAD + 0.05:
                mw.add_prism(part, F['rl'] + HEAD, top)
        for part, top in split_cover(F, F['lintels']):
            if top > F['rl'] + HEAD + 0.05:
                mw.add_prism(part, F['rl'] + HEAD, top)
        mr.add_prism(F['rails'], F['rl'], F['rl'] + 1.0)
        emit_mesh(f'{fid}_Walls', fe, 'PF_Wall', mw, {'pfType': 'walls', 'floorId': fid})
        emit_mesh(f'{fid}_Glazing', fe, 'PF_Glass', mg, {'pfType': 'glazing', 'floorId': fid})
        emit_mesh(f'{fid}_Railings', fe, 'PF_Railing', mr, {'pfType': 'railing', 'floorId': fid})
        # roof over parts not covered by any higher floor
        cov = unary_union([c[0] for c in F['covers']]) if F['covers'] else MultiPolygon()
        roof = F['fp'].difference(F['voids']).difference(cov)
        roof = unary_union([p for p in polys(roof) if p.area > 2.0])
        if not roof.is_empty:
            m = Mesh(); m.add_prism(roof, F['roof_top'] - F['roof_t'], F['roof_top'])
            nm = F.get('roof_name', f'{fid}_Roof')
            emit_mesh(f'{fid}_{nm}' if nm == 'Plaza_Deck' else nm, fe, 'PF_Plaza' if nm == 'Plaza_Deck' else 'PF_Roof', m,
                      {'pfType': 'roof', 'floorId': fid, 'rl': F['roof_top'], 'rlVerified': bool(F.get('roof_verified', True))})
            if 'roof_rl' in F and nm != 'Plaza_Deck':
                ring = unary_union([p.difference(p.buffer(-0.23)) for p in polys(roof) if p.area > 20])
                m = Mesh(); m.add_prism(ring, F['roof_top'], F['roof_top'] + 1.0)
                emit_mesh(f'{fid}_Parapet', fe, 'PF_Wall', m, {'pfType': 'parapet', 'floorId': fid})
        # stairs starting on this floor
        for (sblk, sfid, sname, sm, sprops) in stair_meshes:
            if sfid == fid:
                emit_mesh(sname, fe, 'PF_Stair', sm, sprops)
        # rooms
        free = slab.difference(F['walls'])
        accepted = []
        rooms = [r for r in F['data']['rooms']]
        cand = []
        for r in rooms:
            p = Polygon(r['poly']['outer'], r['poly']['holes']).buffer(0) if r.get('poly') else None
            cand.append(p)
        # flood regions shared by several labels are leaks -> drop both
        for i, p in enumerate(cand):
            if not isinstance(p, Polygon):
                continue
            for j in range(i + 1, len(cand)):
                q = cand[j]
                if isinstance(q, (Polygon, str)) and not isinstance(q, str):
                    inter = p.intersection(q).area
                    if inter > 0.6 * min(p.area, q.area):
                        cand[i] = 'leak'; cand[j] = 'leak'
                        break
        zn = 0
        for i, r in enumerate(rooms):
            p = cand[i]
            conf = None
            if isinstance(p, Polygon) and p.is_valid and not p.is_empty:
                zone = p.intersection(free).buffer(-0.04)
                conf = 'traced (flood fill of drawn walls)'
            else:
                zone = None
                if r.get('dims_m'):
                    ang = math.degrees(math.atan2(-r['dir'][1], r['dir'][0]))
                    R = rect(r['x'], r['y'], r['dims_m'][0], r['dims_m'][1], ang)
                    z2 = R.intersection(free)
                    if z2.area >= 0.5 * R.area:
                        zone = z2.buffer(-0.04); conf = 'approximate (labelled size centred on label)'
            zname = None
            if zone is not None and not zone.is_empty and r['type'] not in NO_ZONE_TYPES:
                zone = unary_union([q for q in polys(zone) if q.area > 0.3])
                if not zone.is_empty:
                    zn += 1
                    m = Mesh(); m.add_prism(zone, F['rl'] + 0.005, F['rl'] + 0.02)
                    zname = f"{fid}_Zone_{zn:02d}_{safe(r['name'])}"
                    emit_mesh(zname, fe, f"PF_Zone_{r['type']}", m,
                              {'pfType': 'roomZone', 'roomName': r['name'], 'roomType': r['type'], 'floorId': fid,
                               'buildingId': blk, 'areaM2': round(zone.area, 2), 'confidence': conf})
            rid = f"{fid}_R{i + 1:02d}"
            nav.append(dict(name=f"NAV_{fid}_{r['type']}_{i + 1:02d}_{safe(r['name'], 30)}", floor=fid, block=blk,
                            kind='room', site=[r['x'], r['y']], rl=F['rl'],
                            props={'roomId': rid, 'roomName': r['name'], 'roomType': r['type'], 'zone': zname or ''}))
            pos = mx(r['x'], r['y'])
            inventory.append({
                'buildingId': blk, 'floorId': fid, 'floorName': F['name'], 'roomId': rid, 'roomName': r['name'],
                'roomType': r['type'], 'sourceDocument': F['sheet'], 'sourcePage': F['src'][1] + 1,
                'rawLabel': r['raw'],
                'dimensions': {'label_m': r['dims_m'], 'zoneAreaM2': round(zone.area, 2) if zname else None},
                'position': {'site_m': [r['x'], r['y']], 'model_m': [pos[0], pos[1], mz(F['rl'])],
                             'unity_expected_m': unity_expected([pos[0], pos[1], mz(F['rl'])])},
                'elevation': {'rl_m': F['rl'], 'rlVerified': bool(F.get('rl_verified'))},
                'entrances': [], 'connectedRooms': [],
                'accessibilityNotes': ('Lift serves this floor' if any(n['floor'] == fid and n['kind'] == 'lift' for n in nav)
                                       else 'No lift recorded on this floor'),
                'measurementConfidence': {'dimensions': 'verified (printed on drawing)' if r['dims_m'] else 'not labelled',
                                          'geometry': conf or 'label position only', 'position': 'label anchor (+/-0.5 m)'},
                'zoneObject': zname,
            })
        stats[fid] = {'zones': zn, 'labels': len(rooms)}

    # ---- seminar hall gallery & stage ---------------------------------------------------------
    F2 = floors['AB_2F']
    fe = 'AB_2F'
    free2 = F2['fp'].difference(F2['walls'])
    for o in stair_open['AB_2F']:
        free2 = free2.difference(o.buffer(0.3))
    G = GALLERY
    mg = Mesh()
    for key in ('front_corridor', 'west_corridor'):
        mg.add_prism(Polygon(G[key]['poly']).intersection(free2), G['underside_rl'], G[key]['rl'])
    for key in ('main', 'upper'):
        T = G[key]
        dy = (T['y1'] - T['y0']) / T['rows']
        for k in range(T['rows']):
            tier = box(T['x0'], T['y0'] + k * dy, T['x1'], T['y0'] + (k + 1) * dy).intersection(free2)
            top = T['rl0'] + (k + 1) * (T['rl1'] - T['rl0']) / T['rows']
            mg.add_prism(tier, G['underside_rl'], top)
    emit_mesh('AB_2F_SeminarHall_UpperGallery', fe, 'PF_Gallery', mg,
              {'pfType': 'gallery', 'note': 'raked seating tiers from spot levels 83.21-87.67 (approximate rows)', 'verified': False})
    m = Mesh(); m.add_prism(box(-21.0, 78.3, 4.1, 82.4).intersection(free2), F2['rl'], F2['rl'] + 0.75)
    emit_mesh('AB_2F_SeminarStage', fe, 'PF_Stage', m, {'pfType': 'stage', 'note': "13'-5\" deep stage; height assumed 0.75 m", 'verified': False})
    nav.append(dict(name='NAV_AB_2F_Gallery_FrontCorridor', floor='AB_2F', block='AdministrativeBlock', kind='room',
                    site=[-8.0, 95.1], rl=83.21, props={'roomName': 'Upper gallery 4\'0" corridor', 'roomType': 'corridor'}))

    # ---- external steps -----------------------------------------------------------------------
    for S in STEPS:
        P = Polygon(S['poly'])
        d = np.array(S['rise_dir'], float)
        xs = np.array(P.exterior.coords)[:, :2] @ d
        s_lo, s_hi = xs.min(), xs.max()
        m = Mesh()
        for k in range(S['n']):
            a = s_lo + (s_hi - s_lo) * k / S['n']
            band = P.intersection(Polygon([tuple(p) for p in _band(d, a, s_hi + 0.01)]))
            m.add_prism(band, S['rl_low'] - 0.3, S['rl_low'] + (S['rl_high'] - S['rl_low']) * (k + 1) / S['n'])
        blk = 'AdministrativeBlock' if S['id'].startswith('AB') else 'TeachingBlock'
        emit_mesh(S['id'], blk, 'PF_Steps', m, {'pfType': 'steps', 'rlLow': S['rl_low'], 'rlHigh': S['rl_high']})
        c = np.array(P.centroid.coords[0])
        nav.append(dict(name=f"NAV_{S['id']}_Bottom", floor='site', block=blk, kind='entrance',
                        site=(c - d * (s_hi - s_lo) / 2 - d * 0.6).tolist(), rl=S['rl_low'], props={'connectsTo': f"NAV_{S['id']}_Top"}))
        nav.append(dict(name=f"NAV_{S['id']}_Top", floor='entry', block=blk, kind='entrance',
                        site=(c + d * (s_hi - s_lo) / 2 + d * 0.6).tolist(), rl=S['rl_high'], props={'connectsTo': f"NAV_{S['id']}_Bottom"}))

    # ---- site ---------------------------------------------------------------------------------
    pts = np.array([[x, y] for x, y, _ in SPOT_LEVELS]); zs = np.array([z for _, _, z in SPOT_LEVELS])
    lin = LinearNDInterpolator(pts, zs, fill_value=float(np.median(zs)))
    # smooth the faceted linear surface with a Gaussian on a 1 m grid
    from scipy.ndimage import gaussian_filter
    from scipy.interpolate import RegularGridInterpolator
    x0_, y0_, x1_, y1_ = SITE_EXTENT
    tgx = np.arange(x0_ - 10, x1_ + 10.01, 1.0); tgy = np.arange(y0_ - 10, y1_ + 10.01, 1.0)
    TGX, TGY = np.meshgrid(tgx, tgy)
    TGZ = gaussian_filter(lin(TGX, TGY), sigma=4.0, mode='nearest')
    rgi = RegularGridInterpolator((tgy, tgx), TGZ, bounds_error=False, fill_value=None)
    def terr(X, Y):
        X = np.asarray(X, float); Y = np.asarray(Y, float)
        return rgi(np.c_[Y.ravel(), X.ravel()]).reshape(X.shape)
    def tz(x, y):
        return float(terr(np.array([x]), np.array([y]))[0])
    existing = [Polygon(p['outer'], p['holes']) for p in site['existing']]
    lib = [p for p in existing if p.bounds[0] > 10 and p.bounds[1] > 10 and p.bounds[3] < 65]
    adm1 = [p for p in existing if p.bounds[0] > 10 and p.bounds[1] > 70]
    plaza = unary_union([Polygon(p['outer'], p['holes']) for p in site['plaza']])
    lawn = unary_union([Polygon(p['outer'], p['holes']) for p in site['lawn']])
    all_fp = unary_union([F['fp'] for F in floors.values()])
    grade_fp = unary_union([F['fp'] for F in floors.values() if F['rl'] <= 71.8])  # floors that meet the ground
    house = Polygon(EXISTING[2]['poly'])
    steps_u = unary_union([Polygon(S['poly']) for S in STEPS])
    holes = unary_union([grade_fp.buffer(0.05), plaza, house, steps_u] + lib + adm1)

    x0, y0, x1, y1 = SITE_EXTENT
    step = 1.0
    gx = np.arange(x0, x1 + 0.01, step); gy = np.arange(y0, y1 + 0.01, step)
    GX, GY = np.meshgrid(gx, gy)
    GZ = terr(GX, GY)
    nyv, nxv = GX.shape
    verts = [(*mx(GX[j, i], GY[j, i]), mz(GZ[j, i])) for j in range(nyv) for i in range(nxv)]
    cells = shapely.box(GX[:-1, :-1].ravel(), GY[:-1, :-1].ravel(), GX[1:, 1:].ravel(), GY[1:, 1:].ravel())
    shapely.prepare(holes)
    hit = shapely.intersects(holes, cells).reshape(GX[:-1, :-1].shape)
    faces = []
    edge_cells = []
    for j in range(nyv - 1):
        for i in range(nxv - 1):
            if hit[j, i]:
                edge_cells.append(cells[j * (nxv - 1) + i])
                continue
            a = j * nxv + i; b = a + 1; c = a + nxv + 1; d = a + nxv
            faces.append((a, b, c, d))
    m = Mesh(); m.add_raw(verts, faces)
    rest = shapely.union_all(edge_cells).difference(holes)
    m.add_surface(rest, tz)
    emit_mesh('Site_Terrain', 'Site', 'PF_Terrain', m, {'pfType': 'terrain', 'accuracy': 'provisional - interpolated from printed spot levels',
                                                        'spotLevels': len(SPOT_LEVELS)})
    # foundations: parts of a floor that are not above any lower floor are filled down below grade
    for fid, F in FOUND.items():
        if fid not in ('TB_LG03', 'AB_FC', 'AB_GF'):
            continue  # only grade-bearing floors get fill; upper overhangs stay open
        below = [G['fp'] for G in floors.values() if G['rl'] < F['rl'] - 1.0]
        base = F['fp'].difference(unary_union(below)) if below else F['fp']
        base = unary_union([p for p in polys(base) if p.area > 1.0])
        if base.is_empty:
            continue
        zmin = min(tz(x, y) for p in polys(base) for x, y in p.exterior.coords)
        bottom = min(zmin - 1.0, F['rl'] - F['slab_t'] - 0.05)
        if F['rl'] - F['slab_t'] - bottom < 0.1:
            continue
        m = Mesh(); m.add_prism(base, bottom, F['rl'] - F['slab_t'])
        emit_mesh(f'{fid}_Foundation', fid, 'PF_Slab', m, {'pfType': 'foundation', 'floorId': fid,
                                                           'note': 'below-grade fill under parts with no floor beneath (schematic)'})

    # roads
    deck = unary_union([plaza, floors['AB_FC']['fp']]).buffer(1.0)
    shapely.prepare(deck)
    mroad = Mesh()
    for R in ROADS:
        line = LineString(R['pts'])
        n = max(2, int(line.length / 1.0))
        P = np.array([line.interpolate(t, normalized=True).coords[0] for t in np.linspace(0, 1, n + 1)])
        T = np.gradient(P, axis=0); T /= np.linalg.norm(T, axis=1)[:, None]
        N = np.c_[-T[:, 1], T[:, 0]]
        L_ = P + N * R['width'] / 2; Rr = P - N * R['width'] / 2
        vv = []; ff = []
        for k in range(len(P)):
            # flat cross-section sitting on the highest terrain point across the carriageway
            samples = [P[k] + N[k] * R['width'] * f for f in (-0.5, -0.25, 0.0, 0.25, 0.5)]
            zq = max(tz(q[0], q[1]) for q in samples)
            if shapely.contains_xy(deck, P[k][0], P[k][1]):
                zq = 70.51
            for q in (L_[k], Rr[k]):
                vv.append((*mx(q[0], q[1]), mz(zq + 0.06)))
        for k in range(len(P) - 1):
            a = 2 * k
            ff.append((a, a + 2, a + 3, a + 1))
        mroad.add_raw(vv, ff)
    emit_mesh('Site_Roads', 'Site', 'PF_Road', mroad, {'pfType': 'roads', 'accuracy': 'approximate centrelines digitised from site sheets',
                                                       'roads': ', '.join(r['id'] for r in ROADS)})
    # plaza paving outside the food-court deck
    pav = plaza.difference(all_fp.buffer(0.02)).difference(steps_u)
    m = Mesh()
    for p in polys(pav):
        if p.area < 0.5:
            continue
        zmin = min(tz(x, y) for x, y in p.exterior.coords)
        m.add_prism(p, min(70.31, zmin - 0.5), 70.51)   # raised plaza: solid down to grade
    emit_mesh('Site_PlazaPaving', 'Site', 'PF_Plaza', m, {'pfType': 'paving', 'rl': 70.51})
    m = Mesh(); m.add_prism(lawn.intersection(floors['TB_LG03']['fp']), 60.61, 60.64)
    emit_mesh('TB_LG03_CourtyardLawn', 'TB_LG03', 'PF_Lawn', m, {'pfType': 'lawn'})
    # existing buildings (massing)
    for nm, plist, h in (('Existing_Library', lib, 11.0), ('Existing_AdminBlock1', adm1, 12.0), ('Existing_House', [house], 6.0)):
        m = Mesh()
        for p in plist:
            zsv = [tz(x, y) for x, y in p.exterior.coords]
            m.add_prism(p, min(zsv) - 0.5, min(zsv) + h)
        emit_mesh(nm, 'Site', 'PF_Existing', m, {'pfType': 'existingBuilding', 'heightAssumed_m': h,
                                                 'note': 'massing only - outline from site sheet hatch, height assumed'})

    # ---- markers --------------------------------------------------------------------------------
    emit_empty('PF_Origin', 'ReferenceMarkers', (0, 0, 0), {'note': 'site point %s; RL 60.00' % (MODEL_ORIGIN_SITE,)})
    emit_empty('PF_Marker_PlanRight_10m', 'ReferenceMarkers', (10, 0, 0), {'note': 'plan +x (page right); expect Unity +X'})
    emit_empty('PF_Marker_PlanUp_10m', 'ReferenceMarkers', (0, 10, 0), {'note': 'plan +y (page up); expect Unity +Z'})
    emit_empty('PF_Marker_Up_10m', 'ReferenceMarkers', (0, 0, 10), {'note': 'height; expect Unity +Y'})
    na = math.radians(TRUE_NORTH_DEG)
    emit_empty('PF_TrueNorth_10m', 'ReferenceMarkers', (10 * math.cos(na), 10 * math.sin(na), 0),
               {'note': 'true north from drawing compass (+/-2 deg)', 'trueNorthDegFromPlusX': TRUE_NORTH_DEG})
    # flat north arrow on the plaza west of the food court
    ax, ay = -45.0, 100.0
    ctr = np.array([ax, ay]); u = np.array([math.cos(na), math.sin(na)]); v = np.array([-u[1], u[0]])
    arrow = Polygon([tuple(ctr + u * 5), tuple(ctr - u * 3 + v * 2.5), tuple(ctr - u * 1.5), tuple(ctr - u * 3 - v * 2.5)])
    m = Mesh(); m.add_prism(arrow, tz(ax, ay) + 0.05, tz(ax, ay) + 0.35)
    emit_mesh('PF_NorthArrow', 'ReferenceMarkers', 'PF_Marker', m, {'pfType': 'marker', 'note': 'points to true north'})
    for k, (x, y, z) in enumerate(SPOT_LEVELS[:9]):
        pass

    # ---- navigation anchors ---------------------------------------------------------------------
    for a in nav:
        p = mx(*a['site'])
        z = mz(a['rl'])
        props = dict(a['props']); props.update({'pfType': 'navAnchor', 'kind': a['kind'], 'floorId': a['floor'],
                                                'buildingId': a['block'], 'rl': a['rl']})
        emit_empty(a['name'][:60], 'NavigationAnchors', (p[0], p[1], z), props)
        a['model'] = [p[0], p[1], z]; a['unity_expected'] = unity_expected([p[0], p[1], z])

    # uniqueness
    seen = {}
    for o in OBJECTS:
        n = o['name']
        if n in seen:
            seen[n] += 1; o['name'] = f"{n}_{seen[n]}"
        else:
            seen[n] = 0
    scene = {'materials': MATERIALS, 'objects': OBJECTS,
             'blocks': {'AdministrativeBlock': [f for f in order if floors[f]['block'] == 'AdministrativeBlock'],
                        'TeachingBlock': [f for f in order if floors[f]['block'] == 'TeachingBlock']}}
    save_json(os.path.join(DATA, 'scene.json'), scene)
    save_json(os.path.join(DATA, 'room_inventory.json'), {
        'coordinateSystem': {'site': 'metres, x = drawing right, y = drawing up, scale from printed 17.50/22.50 m dimension',
                             'model': 'site shifted by origin %s; z = RL - %.2f' % (MODEL_ORIGIN_SITE, RL_DATUM),
                             'unity_expected': 'Unity (x, y, z) = model (x, z, y) with the shipped FBX export settings',
                             'trueNorthDegFromPlusX': TRUE_NORTH_DEG},
        'floors': [{k: (v if not hasattr(v, 'geom_type') else None) for k, v in FL.items()
                    if k in ('id', 'block', 'name', 'rl', 'rl_verified', 'rl_note', 'sheet', 'note', 'roof_rl', 'roof_verified')}
                   for FL in FLOORS],
        'rooms': inventory})
    save_json(os.path.join(DATA, 'nav_anchors.json'), nav)
    print('objects', len(OBJECTS), 'meshes', sum(o['type'] == 'MESH' for o in OBJECTS), 'nav', len(nav),
          'rooms', len(inventory))
    print(stats)


def _band(d, a, b):
    """Large polygon covering s in [a, b] along unit direction d."""
    d = np.asarray(d, float); n = np.array([-d[1], d[0]])
    big = 500
    return [d * a + n * big, d * b + n * big, d * b - n * big, d * a - n * big]


if __name__ == '__main__':
    main()
