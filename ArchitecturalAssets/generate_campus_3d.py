import os
from mesh_builder import MeshBuilder, models_dir

# ── 1. GENERATE ADMINISTRATIVE BLOCK ──────────────────────────────────────────
admin = MeshBuilder("PathFinder_Admin_Block")

# Ground Floor (Elev: 7.16m)
# Floor slab: 28m x 36m x 0.25m
admin.add_box((0.0, 7.16, 0.0), (28.0, 0.25, 36.0))
# Exterior Walls: Height 3.6m, Thickness 0.25m
# North & South walls
admin.add_box((0.0, 7.16 + 1.8, 18.0), (28.0, 3.6, 0.25))
admin.add_box((0.0, 7.16 + 1.8, -18.0), (28.0, 3.6, 0.25))
# East & West walls
admin.add_box((14.0, 7.16 + 1.8, 0.0), (0.25, 3.6, 36.0))
admin.add_box((-14.0, 7.16 + 1.8, 0.0), (0.25, 3.6, 36.0))

# Interior Partitions Ground Floor (Offices & Reception)
# Chairman Office: 8.68m x 7.54m
admin.add_box((5.0, 7.16 + 1.8, 10.0), (10.0, 3.4, 0.20))
admin.add_box((0.0, 7.16 + 1.8, 14.0), (0.20, 3.4, 8.0))
# Trustee Office: 8.68m x 7.54m
admin.add_box((-5.0, 7.16 + 1.8, 10.0), (10.0, 3.4, 0.20))
# Reception & Waiting Lobby: 8.07m x 17.7m
admin.add_box((0.0, 7.16 + 1.8, -2.0), (16.0, 3.4, 0.20))
# Lift Core Ground: 2.18m x 1.83m x 15m (runs full height)
admin.add_box((10.0, 14.0, 0.0), (2.4, 15.0, 2.2))

# First Floor (Elev: 15.54m)
admin.add_box((0.0, 15.54, 0.0), (28.0, 0.25, 36.0))
# Board Room (26'6" x 54'9" -> 8.08m x 16.69m)
admin.add_box((0.0, 15.54 + 1.8, 8.0), (18.0, 3.4, 0.20))
admin.add_box((-9.0, 15.54 + 1.8, 0.0), (0.20, 3.4, 16.0))
admin.add_box((9.0, 15.54 + 1.8, 0.0), (0.20, 3.4, 16.0))

# Second Floor / Seminar Hall Auditorium (Elev: 19.65m)
admin.add_box((0.0, 19.65, 0.0), (28.0, 0.25, 36.0))
# Seminar Hall (28'6" x 74'0" -> 8.68m x 22.55m)
admin.add_box((0.0, 19.65 + 2.5, 0.0), (26.0, 5.0, 0.25))
# Seminar Stage (13'5" wide -> 4.09m)
admin.add_box((0.0, 19.65 + 0.6, 12.0), (12.0, 0.6, 4.2))
# Upper Gallery Seating
admin.add_box((0.0, 19.65 + 3.2, -12.0), (24.0, 0.3, 8.0))

# Roof slab
admin.add_box((0.0, 25.0, 0.0), (28.0, 0.3, 36.0))

# Write Admin Block Files
admin_obj = os.path.join(models_dir, "PathFinder_Admin_Block.obj")
admin_fbx = os.path.join(models_dir, "PathFinder_Admin_Block.fbx")
admin.write_obj(admin_obj, "AdminAccent")
admin.write_fbx_ascii(admin_fbx)
print(f"Exported Admin Block: {len(admin.vertices)} vertices, {len(admin.faces)} triangles.")

# ── 2. GENERATE TEACHING BLOCK ────────────────────────────────────────────────
teaching = MeshBuilder("PathFinder_Teaching_Block")

# Position offset: Teaching Block sits to the West/Northwest of Admin Block
# Center: (-50.0, Y, 0.0), Footprint: 64m x 42m
# Floors: LG3 (0.0m), LG2 (4.26m), LG1/FoodCourt (7.16m), Ground (11.73m), 1st (15.54m), 2nd (19.65m)

elevations = [
    ("LG3", 0.0),
    ("LG2", 4.26),
    ("LG1_FoodCourt", 7.16),
    ("Ground", 11.73),
    ("Floor1", 15.54),
    ("Floor2", 19.65)
]

for name, elev in elevations:
    # Floor slab with central courtyard cut-out (Courtyard: 18m x 18m)
    # North wing slab
    teaching.add_box((-50.0, elev, 14.0), (64.0, 0.25, 14.0))
    # South wing slab
    teaching.add_box((-50.0, elev, -14.0), (64.0, 0.25, 14.0))
    # East corridor slab
    teaching.add_box((-22.0, elev, 0.0), (8.0, 0.25, 14.0))
    # West corridor slab
    teaching.add_box((-78.0, elev, 0.0), (8.0, 0.25, 14.0))

    # Standard Classrooms along North & South perimeters (42'3" x 31'6" -> 12.88m x 9.60m)
    # Classrooms North Wing
    for cx in [-70.0, -56.0, -42.0, -28.0]:
        teaching.add_box((cx, elev + 1.7, 20.0), (12.5, 3.4, 0.20))
        teaching.add_box((cx + 6.25, elev + 1.7, 14.0), (0.20, 3.4, 12.0))
    
    # Classrooms South Wing
    for cx in [-70.0, -56.0, -42.0, -28.0]:
        teaching.add_box((cx, elev + 1.7, -20.0), (12.5, 3.4, 0.20))
        teaching.add_box((cx + 6.25, elev + 1.7, -14.0), (0.20, 3.4, 12.0))

    # 12'0" (3.66m) Corridors along Courtyard
    teaching.add_box((-50.0, elev + 1.7, 7.5), (48.0, 3.2, 0.15))
    teaching.add_box((-50.0, elev + 1.7, -7.5), (48.0, 3.2, 0.15))

# Vertical Lift Core (Dual Lifts: 4.4m x 2.2m x 24m)
teaching.add_box((-22.0, 11.0, 0.0), (4.5, 23.0, 2.4))
# Staircases East & West
teaching.add_box((-22.0, 11.0, 8.0), (5.0, 23.0, 3.0))
teaching.add_box((-78.0, 11.0, -8.0), (5.0, 23.0, 3.0))

# Food Court Counter Structures on LG1 (Elev: 7.16m)
teaching.add_box((-50.0, 7.16 + 1.5, 0.0), (16.0, 3.0, 6.0))

# Roof slab
teaching.add_box((-50.0, 23.5, 0.0), (66.0, 0.35, 44.0))

# Write Teaching Block Files
tb_obj = os.path.join(models_dir, "PathFinder_Teaching_Block.obj")
tb_fbx = os.path.join(models_dir, "PathFinder_Teaching_Block.fbx")
teaching.write_obj(tb_obj, "TeachingAccent")
teaching.write_fbx_ascii(tb_fbx)
print(f"Exported Teaching Block: {len(teaching.vertices)} vertices, {len(teaching.faces)} triangles.")

# ── 3. GENERATE COMPLETE CAMPUS MODEL (COMBINED WITH WALKWAY CONNECTORS) ───────
campus = MeshBuilder("PathFinder_Campus_3D")

# Merge Admin and Teaching geometry
v_offset = len(campus.vertices)
campus.vertices.extend(admin.vertices)
campus.faces.extend(admin.faces)

t_offset = len(campus.vertices)
for v in teaching.vertices:
    campus.vertices.append(v)
for f in teaching.faces:
    campus.faces.append((f[0] + t_offset, f[1] + t_offset, f[2] + t_offset))

# Add Physical Architectural Link / Walkway between Admin Block and Teaching Block
# Connection 1: At Food Court Level (+67.16 LVL -> Y = 7.16m)
# Walkway from Admin (-14.0m) to Teaching (-18.0m): span 10m x 4.5m
campus.add_box((-16.0, 7.16, 0.0), (10.0, 0.25, 4.5))
# Handrails
campus.add_box((-16.0, 7.16 + 0.6, 2.25), (10.0, 1.1, 0.15))
campus.add_box((-16.0, 7.16 + 0.6, -2.25), (10.0, 1.1, 0.15))

# Connection 2: Upper Ground Level (+71.73 LVL -> Y = 11.73m)
campus.add_box((-16.0, 11.73, 0.0), (10.0, 0.25, 5.0))
campus.add_box((-16.0, 11.73 + 0.6, 2.5), (10.0, 1.1, 0.15))
campus.add_box((-16.0, 11.73 + 0.6, -2.5), (10.0, 1.1, 0.15))

# Write Campus Combined Files
campus_obj = os.path.join(models_dir, "PathFinder_Campus_3D.obj")
campus_fbx = os.path.join(models_dir, "PathFinder_Campus_3D.fbx")
campus.write_obj(campus_obj, "CampusWall")
campus.write_fbx_ascii(campus_fbx)
print(f"Exported Complete Campus 3D: {len(campus.vertices)} vertices, {len(campus.faces)} triangles.")
