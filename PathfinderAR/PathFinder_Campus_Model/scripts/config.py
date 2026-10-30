"""Floor / block configuration for the PathFinder campus model.

All polygons are in SITE metres (see pf_common.py).  RL = reduced level in
metres as printed on the drawings.  'verified' flags record whether a value
was read directly from the drawings or assumed.
"""
from shapely.geometry import Polygon, box

# ---- clip regions ---------------------------------------------------------
TB_CLIP = Polygon([(-44.2, 12.9), (-19.6, 12.9), (-19.6, 14.0), (8, 14.0), (8, 70), (-31.3, 70),
                   (-35.5, 60), (-41.8, 45), (-44.2, 39.3)])
SPLIT_Y = 56.3  # Teaching LG01 (+67.92) / Food-court level (+67.16) split on sheet 03
TB_LG01_CLIP = TB_CLIP.intersection(box(-60, 0, 20, SPLIT_Y))
FC_CLIP = Polygon([(-40, SPLIT_Y), (8, SPLIT_Y), (8, 113.5), (-6.5, 113.5), (-24, 104.5), (-24.5, 96), (-40, 74)])
AB_CLIP = Polygon([(-29, 76.0), (6.2, 76.0), (6.2, 114.0), (-3.2, 114.0), (-21, 106.5), (-23.5, 104.0), (-29, 91)])

# south canopy strip that only exists at LG03 (entrance corridor)
TB_SOUTH_STRIP = [(-19.5, 12.0), (9.0, 12.0), (9.0, 20.35), (-19.5, 20.35)]
AB_WEST_SUB = [[(-31, 90.3), (-22.95, 90.3), (-22.95, 115), (-31, 115)], [(-31, 70), (-26.45, 70), (-26.45, 90.3), (-31, 90.3)]]
TB_NORTH_STEPS = [(-23.0, 65.95), (-3.0, 65.95), (-3.0, 72.0), (-23.0, 72.0)]

SLAB_T = 0.15

FLOORS = [
    # Teaching Block (sheets dated 17/03/2025, drawing 2025-INS-58-CS-IP-DRA-ZO-001 V3 R2)
    dict(id='TB_LG03', block='TeachingBlock', name='Lower Ground 03', rl=60.61, rl_verified=True,
         src=('teach', 0), clip=TB_CLIP, sheet='Teaching Block sheet 01 of 08'),
    dict(id='TB_LG02', block='TeachingBlock', name='Lower Ground 02', rl=64.26, rl_verified=True,
         src=('teach', 1), clip=TB_CLIP, sheet='Teaching Block sheet 02 of 08', fp_sub=[TB_SOUTH_STRIP]),
    dict(id='TB_LG01', block='TeachingBlock', name='Lower Ground 01', rl=67.92, rl_verified=True,
         src=('teach', 2), clip=TB_LG01_CLIP, sheet='Teaching Block sheet 03 of 08', fp_sub=[TB_SOUTH_STRIP]),
    dict(id='TB_GF', block='TeachingBlock', name='Ground Floor', rl=71.73, rl_verified=True,
         src=('teach', 3), clip=TB_CLIP, sheet='Teaching Block sheet 04 of 08', fp_sub=[TB_SOUTH_STRIP, TB_NORTH_STEPS]),
    dict(id='TB_1F', block='TeachingBlock', name='First Floor', rl=75.54, rl_verified=False,
         rl_note='Not printed on sheet 05; taken equal to Admin Block first floor +75.54 (+50\'6")',
         src=('teach', 4), clip=TB_CLIP, sheet='Teaching Block sheet 05 of 08', fp_sub=[TB_SOUTH_STRIP, TB_NORTH_STEPS]),
    dict(id='TB_2F', block='TeachingBlock', name='Second Floor', rl=79.35, rl_verified=False,
         rl_note='Not printed on sheet 06; assumed 3.81 m floor-to-floor (same as GF->1F)',
         src=('teach', 5), clip=TB_CLIP, sheet='Teaching Block sheet 06 of 08', fp_sub=[TB_SOUTH_STRIP, TB_NORTH_STEPS],
         roof_rl=83.16, roof_verified=False),

    # Administrative Block
    dict(id='AB_FC', block='AdministrativeBlock', name='Food Court Level', rl=67.16, rl_verified=True,
         src=('teach', 2), clip=FC_CLIP, sheet='Teaching Block sheet 03 of 08 (Mar-2025 revision)',
         roof_rl=70.51, roof_t=0.20, roof_name='Plaza_Deck',
         note='Only plan of this level available is the Mar-2025 site sheet; Dec-2025 Admin set shows a stair down to 67.16 (+23\'-0").'),
    dict(id='AB_GF', block='AdministrativeBlock', name='Ground Floor', rl=71.73, rl_verified=True, slab_t=1.42,
         src=('admin', 0), clip=AB_CLIP, fp_sub=[[(-30, 70), (8, 70), (8, 77.3), (-30, 77.3)]] + AB_WEST_SUB, sheet='Admin Block floor plans p1 (18/12/2025)'),
    dict(id='AB_FF', block='AdministrativeBlock', name='First Floor', rl=75.54, rl_verified=True,
         src=('admin', 1), clip=AB_CLIP, fp_sub=[[(-21.5, 70), (8, 70), (8, 81.55), (-21.5, 81.55)]] + AB_WEST_SUB, sheet='Admin Block floor plans p2 (18/12/2025)'),
    dict(id='AB_2F', block='AdministrativeBlock', name='Second Floor - Seminar Hall', rl=79.65, rl_verified=True,
         src=('admin', 2), clip=AB_CLIP, sheet='Admin Block floor plans p3/p4 (18/12/2025) - upper gallery plan', ignore_voids=True, fp_sub=AB_WEST_SUB,
         roof_rl=91.00, roof_verified=False,
         note='Hall floor plan at +79.65 not supplied (p4 duplicates p3); hall outline taken from the gallery-level plan.'),
]

# Seminar-hall upper gallery (raked seating), from Admin p3 spot levels
GALLERY = dict(
    front_corridor=dict(poly=[(-20.7, 94.55), (4.15, 94.55), (4.15, 95.65), (-20.7, 95.65)], rl=83.21),
    west_corridor=dict(poly=[(-22.9, 81.9), (-20.7, 81.9), (-20.7, 95.65), (-22.9, 95.65)], rl=83.22),
    main=dict(x0=-23.0, x1=4.15, y0=95.65, y1=103.75, rl0=83.21, rl1=86.39, rows=6),
    upper=dict(x0=-4.6, x1=4.15, y0=103.75, y1=111.6, rl0=86.39, rl1=87.67, rows=4),
    underside_rl=82.95,
)

# Stair cores digitised from the drawings (centre x,y, length, width, angle deg of long axis)
# Teaching stairs are auto-detected from the stair-tread layer; these are the Admin ones.
MANUAL_STAIRS = [
    dict(id='AB_MainStair', block='AdministrativeBlock', cx=-8.75, cy=105.65, L=4.3, W=3.7, ang=90, kind='dogleg',
         floors=['AB_FC', 'AB_GF', 'AB_FF', 'AB_2F'], verified=False),
    dict(id='AB_WestStair', block='AdministrativeBlock', cx=-24.1, cy=85.9, L=7.0, W=3.3, ang=90, kind='dogleg',
         floors=['AB_GF', 'AB_FF', 'AB_2F'], verified=False),
    # Teaching stairs: positions from the 0.51 pt stair-tread layer (auto-detected, see data/floor_TB_*.json)
    dict(id='TB_CentralStair', block='TeachingBlock', cx=-11.44, cy=48.44, L=8.65, W=2.50, ang=131.4, kind='straight',
         floors=['TB_LG03', 'TB_LG02', 'TB_LG01', 'TB_GF', 'TB_1F', 'TB_2F'], verified=True),
    dict(id='TB_WestStair', block='TeachingBlock', cx=-32.64, cy=38.90, L=4.72, W=3.40, ang=90, kind='dogleg',
         floors=['TB_LG03', 'TB_LG02', 'TB_LG01', 'TB_GF', 'TB_1F', 'TB_2F'], verified=True),
]

# External steps (polygon, low RL, high RL, rising direction unit vector)
STEPS = [
    dict(id='TB_NorthEntranceSteps', poly=[(-22.1, 65.95), (-3.7, 65.95), (-3.7, 68.5), (-22.1, 68.5)],
         rl_low=70.51, rl_high=71.73, rise_dir=(0, -1), n=7),
    dict(id='AB_EntranceSteps', poly=[(-10.3, 77.4), (-6.7, 77.4), (-6.7, 81.0), (-10.3, 81.0)],
         rl_low=70.51, rl_high=71.73, rise_dir=(0, 1), n=7),
    dict(id='AB_WestSteps', poly=[(-25.3, 77.4), (-21.8, 77.4), (-21.8, 81.0), (-25.3, 81.0)],
         rl_low=70.51, rl_high=71.73, rise_dir=(0, 1), n=7),
]

# Model origin in SITE metres: centre of Teaching Block north entrance (top of steps)
MODEL_ORIGIN_SITE = (-13.5, 65.95)
RL_DATUM = 60.00

# ---- site (provisional, digitised from the site sheets) --------------------
ROADS = [
    dict(id='Road_BetweenBlocks', width=6.5, pts=[(-33, 71.75), (8.2, 71.75), (46, 71.75)], label="23'0\" wide 2-lane asphalt road"),
    dict(id='Road_EastNorth', width=6.0, pts=[(8.2, 71.75), (8.2, 125)], label="23'0\" wide 2-lane asphalt road"),
    dict(id='Road_WestSouthLoop', width=7.0,
         pts=[(-33, 71.75), (-40, 66), (-46, 55), (-51.5, 41), (-55, 28), (-54, 17), (-48.5, 10.5), (-40, 8.6),
              (-25, 7.4), (-10, 6.6), (2, 7.2), (12, 9.0), (28, 12.8), (40, 16.8), (48, 21)],
         label="23'0\" / 16'0\" asphalt road"),
    dict(id='Road_LibraryEast', width=5.0, pts=[(31, 71.75), (33.5, 55), (33.5, 35), (38, 22)], label='16\'6" asphalt road'),
    dict(id='Road_NH169', width=10.0, pts=[(-75, -42.6), (50, -10.1)], label='NH-169 (Mangalore - Moodbidri)'),
]
EXISTING = [
    dict(id='Existing_Library', poly=[(12.3, 13.8), (28.5, 13.8), (28.5, 62.8), (12.3, 62.8)], height=11.0, note='height assumed'),
    dict(id='Existing_AdminBlock1', poly=[(13.8, 78.6), (23.9, 78.6), (26.6, 75.3), (29.3, 78.6), (36.3, 78.6),
                                          (36.3, 101.0), (42.8, 101.0), (42.8, 103.4), (36.3, 103.4), (36.3, 116.0),
                                          (13.9, 116.0), (13.9, 103.6), (13.8, 103.6)], height=12.0, note='height assumed'),
    dict(id='Existing_House', poly=[(-69.3, 47.5), (-60.3, 46.2), (-61.5, 38.2), (-69.3, 39.3)], height=6.0, note='height assumed'),
]
SPOT_LEVELS = [  # (x, y, RL) - printed levels plus assumed fill points (marked a)
    (-19.7, 3.9, 60.00), (-14.5, 15.2, 60.00), (42.8, 17.9, 61.00), (-8.0, 69.6, 70.51), (9.8, 69.7, 70.00),
    (-40.6, 61.1, 70.51), (-29.7, 87.3, 67.00), (-11.9, 111.4, 67.00), (-7.4, 111.2, 67.00),
    # assumed
    (-75, -45, 59.5), (50, -45, 60.0), (-75, 0, 60.0), (50, 0, 60.3), (5, 11, 60.3), (9.5, 25, 60.61), (9.5, 50, 60.61),
    (9.5, 62.5, 64.0), (20, 70, 70.0), (46, 72, 70.0), (46, 125, 70.0), (25, 125, 70.0), (8, 125, 69.0), (-20, 125, 67.0),
    (-40, 125, 67.5), (-75, 125, 68.0), (-75, 60, 66.0), (-60, 60, 69.0), (-52, 45, 66.0), (-55, 25, 61.5), (-48, 12, 60.4),
    (-46.5, 30, 60.8), (-46, 45, 64.5), (-30, 78, 67.0), (-24, 100, 67.0), (36, 45, 61.5), (45, 40, 61.2), (32, 70, 70.0),
    (-36, 70.5, 70.51), (2, 75, 70.51), (-28, 76, 70.51),
]
SITE_EXTENT = (-75, -45, 50, 125)

TRUE_NORTH_DEG = 175.0  # direction of true north in the plan frame, degrees CCW from plan +x (compass on sheets, +/-2 deg)
