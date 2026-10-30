"""Room / level / void labels from PDF text, grouped into multi-line clusters."""
import re, math
import numpy as np
from shapely.geometry import Point

DIM_RE = re.compile(r"(\d+)\s*'\s*(\d+(?:\.\d+)?)?\s*\"?\s*[xX]\s*(\d+)\s*'\s*(\d+(?:\.\d+)?)?")
FT_RE = re.compile(r"(\d+)\s*'\s*(\d+(?:\.\d+)?)?\s*\"?")
LVL_RE = re.compile(r'([+]?\d\d\.\d\d)\s*(?:LVL)?')

def ftin(f, i):
    return round((int(f) * 12 + float(i or 0)) * 0.0254, 3)

def parse_dims(t):
    m = DIM_RE.search(t.replace('’', "'").replace('”', '"'))
    if not m:
        return None
    a = ftin(m.group(1), m.group(2)); b = ftin(m.group(3), m.group(4))
    return [a, b]

def room_type(name):
    n = name.upper()
    table = [
        ('lift', ['LIFT LOBBY']), ('lobby', ['LOBBY', 'WAITING', 'RECEPTION', 'VERANDAH', 'INTERACTION']),
        ('lift', ['LIFT']),
        ('corridor', ['CORRIDOR', 'PASSAGE', 'PATHWAY']),
        ('toilet', ['TOILET', 'WASH', 'REST ROOM']),
        ('classroom', ['CLASS ROOM', 'CLASSROOM']),
        ('staff', ['STAFF']),
        ('food', ['FOOD', 'KITCHEN', 'FASTFOOD', 'PANTRY', 'DRINKING']),
        ('meeting', ['SEMINAR', 'BOARD', 'GROUP DISCUSSION', 'INTERVIEW', 'LOUNGE']),
        ('office', ['OFFICE', 'PRINCIPAL', 'CHAIRMAN', 'TRUSTEE', 'HEAD', 'ACCOUNTS', 'CASHIER', 'H.R', 'ASST',
                    'ADMISSION', 'PLACEMENT', 'WORK STATION', 'WORKSTATION', 'STRONG ROOM', 'COUNTER']),
        ('service', ['ELECTRICAL', 'DUCT', 'STORE', 'SERVICE', 'PANEL', 'COMMUNICATION']),
        ('open', ['COURTYARD', 'LANDSCAPE', 'OPEN AREA', 'FOUNTAIN', 'STAGE']),
        ('entrance', ['ENTRANCE', 'GATE']),
        ('stair', ['STEPS', 'STAIR']),
    ]
    for t, keys in table:
        if any(k in n for k in keys):
            return t
    return 'room'

SKIP = ['DRAWN BY', 'APPROVED', 'SCALE', 'DATE', 'SHEET', 'ARCHITECTURE', 'PROPOSED', 'AIET', 'Commercial', 'Floor, Kadri',
        'Telephone', 'mail', '2025-INS', 'ROAD', 'FOOTPATH', 'NH', 'MOODBIDRI', 'MANGALORE', 'HOUSE', 'EXISTING',
        'LAND RESERVED', 'FREEZONE', 'WIDINING', 'FLOOR LVL', 'FLOOR PLAN', 'GROUND 0', 'UPPER GALLERY LVL PLAN',
        'LOWER GROUND 0', '(ADMINISTRATIVE BLOCK)', '(TEACHING BLOCK)', 'sq ft', 'SEATER']

def text_lines(page, aff, clip):
    out = []
    for b in page.get_text('dict')['blocks']:
        for l in b.get('lines', []):
            t = ' '.join(s['text'] for s in l['spans']).strip()
            t = re.sub(r'\s+', ' ', t)
            if not t:
                continue
            x0, y0, x1, y1 = l['bbox']
            x, y = aff((x0 + x1) / 2, (y0 + y1) / 2)
            if clip is not None and not clip.contains(Point(x, y)):
                continue
            size = l['spans'][0]['size'] * aff.scale
            out.append({'t': t, 'x': x, 'y': y, 'dir': tuple(round(v, 2) for v in l['dir']), 'size': size})
    return out

def cluster(lines):
    """Group lines into labels: same dir & size, stacked within ~1.6 line heights."""
    n = len(lines); parent = list(range(n))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for i in range(n):
        for j in range(i + 1, n):
            a, b = lines[i], lines[j]
            if a['dir'] != b['dir'] or abs(a['size'] - b['size']) > 0.15 * a['size']:
                continue
            dx, dy = b['x'] - a['x'], b['y'] - a['y']
            ux, uy = a['dir'][0], -a['dir'][1]   # text baseline direction in site frame (y up)
            along = dx * ux + dy * uy
            perp = -dx * uy + dy * ux
            h = a['size']
            if abs(perp) <= 1.75 * h and abs(along) <= 3.5 * h:
                parent[find(i)] = find(j)
    groups = {}
    for i in range(n):
        groups.setdefault(find(i), []).append(lines[i])
    labels = []
    for g in groups.values():
        g.sort(key=lambda l: -(l['y'] * -l['dir'][1] + l['x'] * 0) if l['dir'][1] else -l['y'])
        names, dims, levels = [], None, []
        for l in g:
            t = l['t']
            d = parse_dims(t)
            lv = re.findall(r'[+]\d\d\.\d\d', t)
            if lv:
                levels += [float(v) for v in lv]; continue
            if d and dims is None:
                dims = d
                rest = DIM_RE.sub('', t).strip()
                if rest and re.search('[A-Z]{3,}', rest):
                    names.append(rest)
                continue
            names.append(t)
        name = ' '.join(names).strip()
        name = re.sub(r'\s+', ' ', name)
        x = float(np.mean([l['x'] for l in g])); y = float(np.mean([l['y'] for l in g]))
        labels.append({'name': name, 'dims_m': dims, 'levels': levels, 'x': round(x, 3), 'y': round(y, 3),
                       'dir': g[0]['dir'], 'size': g[0]['size'], 'raw': [l['t'] for l in g]})
    return labels

def room_labels(page, aff, clip):
    labs = cluster(text_lines(page, aff, clip))
    out = []
    for L in labs:
        nm = L['name']
        if any(s.lower() in nm.lower() for s in SKIP) and 'BELOW' not in nm.upper():
            continue
        if not nm or not re.search('[A-Za-z]{2,}', nm):
            if L['levels']:
                out.append(dict(L, kind='level'))
            continue
        if nm.upper() in ('UP', 'DN', 'N', 'S', 'E', 'W', 'NE', 'NW', 'SE', 'SW'):
            out.append(dict(L, kind='stairdir')); continue
        kind = 'void' if 'BELOW' in nm.upper() else 'room'
        out.append(dict(L, kind=kind, type=room_type(nm)))
    return out
