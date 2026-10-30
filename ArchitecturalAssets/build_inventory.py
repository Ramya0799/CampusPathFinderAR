import json

def ft_in_to_m(ft, inches=0):
    return round((ft + inches / 12.0) * 0.3048, 3)

# 1. Build Floor Plan Inventory
inventory = {
    "campus": {
        "id": "aiet-mijar",
        "name": "Alva's Institute of Engineering & Technology",
        "surveyReference": "Shobhavana Campus, NH-169, Mijar, Moodbidri",
        "datumLevel": 60.00, # Base ground elevation datum (+60.00 LVL = 0.0m)
        "units": "meters",
        "coordinateSystem": "Unity Left-Handed Y-Up (+X East, +Y Elevation, +Z North)"
    },
    "buildings": [
        {
            "id": "admin_block",
            "name": "Administrative Block",
            "code": "ADM",
            "description": "Multi-level administrative wing housing Chairman Office, Trustee Office, Board Room, and Seminar Hall.",
            "floors": [
                {
                    "id": "adm_ground",
                    "name": "Admin Ground Floor",
                    "levelNumber": 0,
                    "elevationMeters": 7.16, # +67.16 LVL
                    "drawingElevation": "+67.16 LVL / +71.73 LVL",
                    "sourceDocument": "ADMIN BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 1,
                    "spaces": [
                        {"id": "ADM-G01", "name": "Admin Block Main Entrance & Verandah", "type": "Entrance", "width": ft_in_to_m(8, 6), "length": ft_in_to_m(32, 0), "confidence": "verified"},
                        {"id": "ADM-G02", "name": "Visitor Reception & Waiting Lobby", "type": "Lobby", "width": ft_in_to_m(26, 6), "length": ft_in_to_m(58, 1), "confidence": "verified"},
                        {"id": "ADM-G03", "name": "Administration Office", "type": "Office", "width": ft_in_to_m(18, 10), "length": ft_in_to_m(22, 0), "confidence": "verified"},
                        {"id": "ADM-G04", "name": "Managing Trustee Office", "type": "ExecutiveOffice", "width": ft_in_to_m(28, 6), "length": ft_in_to_m(24, 9), "confidence": "verified"},
                        {"id": "ADM-G05", "name": "Chairman Office", "type": "ExecutiveOffice", "width": ft_in_to_m(28, 6), "length": ft_in_to_m(24, 9), "confidence": "verified"},
                        {"id": "ADM-G06", "name": "Strong Room", "type": "Security", "width": ft_in_to_m(9, 2), "length": ft_in_to_m(10, 0), "confidence": "verified"},
                        {"id": "ADM-G07", "name": "Electrical Panel Room", "type": "Utility", "width": ft_in_to_m(8, 8), "length": ft_in_to_m(5, 11), "confidence": "verified"},
                        {"id": "ADM-G08", "name": "Admin Lift Core", "type": "VerticalTransport", "width": ft_in_to_m(7, 2), "length": ft_in_to_m(6, 0), "confidence": "verified"},
                        {"id": "ADM-G09", "name": "Ladies Restroom", "type": "Sanitary", "width": ft_in_to_m(19, 6), "length": ft_in_to_m(7, 2), "confidence": "verified"},
                        {"id": "ADM-G10", "name": "Gents Restroom", "type": "Sanitary", "width": ft_in_to_m(15, 6), "length": ft_in_to_m(6, 5), "confidence": "verified"}
                    ]
                },
                {
                    "id": "adm_floor1",
                    "name": "Admin First Floor",
                    "levelNumber": 1,
                    "elevationMeters": 15.54, # +75.54 LVL
                    "drawingElevation": "+70.51 LVL / +75.54 LVL",
                    "sourceDocument": "ADMIN BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 2,
                    "spaces": [
                        {"id": "ADM-101", "name": "Executive Board Room (30+30 Seater)", "type": "Conference", "width": ft_in_to_m(26, 6), "length": ft_in_to_m(54, 9), "confidence": "verified"},
                        {"id": "ADM-102", "name": "Placement Officer Cabin", "type": "Office", "width": ft_in_to_m(14, 0), "length": ft_in_to_m(18, 6), "confidence": "derived"},
                        {"id": "ADM-103", "name": "Group Discussion & Interview Room", "type": "Meeting", "width": ft_in_to_m(16, 0), "length": ft_in_to_m(22, 0), "confidence": "derived"},
                        {"id": "ADM-104", "name": "HR Office", "type": "Office", "width": ft_in_to_m(11, 1), "length": ft_in_to_m(16, 0), "confidence": "verified"},
                        {"id": "ADM-105", "name": "First Floor Lift Lobby", "type": "Circulation", "width": ft_in_to_m(26, 6), "length": ft_in_to_m(8, 0), "confidence": "verified"}
                    ]
                },
                {
                    "id": "adm_seminar",
                    "name": "Seminar Hall & Gallery",
                    "levelNumber": 2,
                    "elevationMeters": 19.65, # +79.65 LVL
                    "drawingElevation": "+79.65 (+65'-0\") LVL",
                    "sourceDocument": "ADMIN BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 3,
                    "spaces": [
                        {"id": "ADM-201", "name": "Central Seminar Hall Auditorium", "type": "Auditorium", "width": ft_in_to_m(28, 6), "length": ft_in_to_m(74, 0), "confidence": "verified"},
                        {"id": "ADM-202", "name": "Seminar Stage", "type": "Stage", "width": ft_in_to_m(13, 5), "length": ft_in_to_m(28, 6), "confidence": "verified"},
                        {"id": "ADM-203", "name": "Seminar Hall Upper Gallery", "type": "Gallery", "width": ft_in_to_m(28, 6), "length": ft_in_to_m(24, 0), "confidence": "verified"}
                    ]
                }
            ]
        },
        {
            "id": "teaching_block",
            "name": "Teaching Block",
            "code": "TB",
            "description": "6-storey academic complex with lecture classrooms, food court, faculty staff rooms, and open courtyards.",
            "floors": [
                {
                    "id": "tb_lg3",
                    "name": "Lower Ground 03",
                    "levelNumber": -3,
                    "elevationMeters": 0.0, # +60.00 LVL (Datum)
                    "drawingElevation": "+60.00 / +60.61 LVL",
                    "sourceDocument": "TEACHING BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 1,
                    "spaces": [
                        {"id": "TB-LG3-01", "name": "West Entrance Lobby", "type": "Entrance", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(21, 6), "confidence": "verified"},
                        {"id": "TB-LG3-02", "name": "Classroom LG3-A", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-LG3-03", "name": "Classroom LG3-B", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-LG3-04", "name": "Faculty Staff Room LG3", "type": "StaffRoom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(46, 7), "confidence": "verified"},
                        {"id": "TB-LG3-05", "name": "Girls Rest Room LG3", "type": "RestRoom", "width": ft_in_to_m(16, 0), "length": ft_in_to_m(24, 0), "confidence": "derived"},
                        {"id": "TB-LG3-06", "name": "Boys Washroom", "type": "Sanitary", "width": ft_in_to_m(16, 0), "length": ft_in_to_m(24, 0), "confidence": "derived"}
                    ]
                },
                {
                    "id": "tb_lg2",
                    "name": "Lower Ground 02",
                    "levelNumber": -2,
                    "elevationMeters": 4.26, # +64.26 LVL
                    "drawingElevation": "+64.26 LVL",
                    "sourceDocument": "TEACHING BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 2,
                    "spaces": [
                        {"id": "TB-LG2-01", "name": "Classroom LG2-A", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-LG2-02", "name": "Classroom LG2-B", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-LG2-03", "name": "Faculty Staff Room LG2", "type": "StaffRoom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(46, 7), "confidence": "verified"}
                    ]
                },
                {
                    "id": "tb_foodcourt",
                    "name": "Lower Ground 01 / Food Court Level",
                    "levelNumber": -1,
                    "elevationMeters": 7.16, # +67.16 LVL
                    "drawingElevation": "+67.16 LVL",
                    "sourceDocument": "TEACHING BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 3,
                    "spaces": [
                        {"id": "TB-FC-01", "name": "Central Campus Food Court", "type": "Dining", "width": ft_in_to_m(52, 0), "length": ft_in_to_m(68, 0), "confidence": "derived"},
                        {"id": "TB-FC-02", "name": "Guest Dining Hall", "type": "Dining", "width": ft_in_to_m(28, 6), "length": ft_in_to_m(27, 9), "confidence": "verified"},
                        {"id": "TB-FC-03", "name": "Fastfood Counter 01", "type": "FoodService", "width": ft_in_to_m(14, 0), "length": ft_in_to_m(16, 0), "confidence": "verified"},
                        {"id": "TB-FC-04", "name": "Fastfood Counter 02", "type": "FoodService", "width": ft_in_to_m(14, 0), "length": ft_in_to_m(16, 0), "confidence": "verified"},
                        {"id": "TB-FC-05", "name": "Fastfood Counter 03", "type": "FoodService", "width": ft_in_to_m(14, 0), "length": ft_in_to_m(16, 0), "confidence": "verified"},
                        {"id": "TB-FC-06", "name": "Food Court Open Courtyard", "type": "Courtyard", "width": ft_in_to_m(35, 0), "length": ft_in_to_m(35, 0), "confidence": "derived"}
                    ]
                },
                {
                    "id": "tb_ground",
                    "name": "Teaching Block Ground Floor",
                    "levelNumber": 0,
                    "elevationMeters": 11.73, # +71.73 LVL
                    "drawingElevation": "+70.51 / +71.73 LVL",
                    "sourceDocument": "TEACHING BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 4,
                    "spaces": [
                        {"id": "TB-G01", "name": "Main Entrance Lobby & Foyer", "type": "Entrance", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(32, 0), "confidence": "verified"},
                        {"id": "TB-G02", "name": "Lecture Classroom G-101", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-G03", "name": "Lecture Classroom G-102", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-G04", "name": "Lecture Classroom G-103", "type": "Classroom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(48, 0), "confidence": "verified"},
                        {"id": "TB-G05", "name": "Lecture Classroom G-104", "type": "Classroom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(42, 3), "confidence": "verified"},
                        {"id": "TB-G06", "name": "Faculty Staff Room G", "type": "StaffRoom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(46, 7), "confidence": "verified"},
                        {"id": "TB-G07", "name": "Dual Lift Core", "type": "VerticalTransport", "width": ft_in_to_m(7, 2) * 2, "length": ft_in_to_m(6, 0), "confidence": "verified"}
                    ]
                },
                {
                    "id": "tb_floor1",
                    "name": "Teaching Block 1st Floor",
                    "levelNumber": 1,
                    "elevationMeters": 15.54, # +75.54 LVL
                    "drawingElevation": "+75.54 LVL",
                    "sourceDocument": "TEACHING BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 5,
                    "spaces": [
                        {"id": "TB-101", "name": "Lecture Classroom 101", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-102", "name": "Lecture Classroom 102", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-103", "name": "Lecture Classroom 103", "type": "Classroom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(48, 0), "confidence": "verified"},
                        {"id": "TB-104", "name": "Lecture Classroom 104", "type": "Classroom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(42, 3), "confidence": "verified"},
                        {"id": "TB-105", "name": "Department Faculty Staff Room 1", "type": "StaffRoom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(46, 7), "confidence": "verified"}
                    ]
                },
                {
                    "id": "tb_floor2",
                    "name": "Teaching Block 2nd Floor",
                    "levelNumber": 2,
                    "elevationMeters": 19.65, # +79.65 LVL
                    "drawingElevation": "+79.65 LVL",
                    "sourceDocument": "TEACHING BLOCK FLOOR PLANS.pdf",
                    "sourcePage": 6,
                    "spaces": [
                        {"id": "TB-201", "name": "Lecture Classroom 201", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-202", "name": "Lecture Classroom 202", "type": "Classroom", "width": ft_in_to_m(42, 3), "length": ft_in_to_m(31, 6), "confidence": "verified"},
                        {"id": "TB-203", "name": "Lecture Classroom 203", "type": "Classroom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(48, 0), "confidence": "verified"},
                        {"id": "TB-204", "name": "Faculty Staff Room 2", "type": "StaffRoom", "width": ft_in_to_m(31, 6), "length": ft_in_to_m(46, 7), "confidence": "verified"}
                    ]
                }
            ]
        }
    ]
}

out_inv_path = r"d:\PathFinder\ArchitecturalAssets\campus_architectural_inventory.json"
with open(out_inv_path, "w", encoding="utf-8") as f:
    json.dump(inventory, f, indent=2)

print(f"Generated architectural inventory with {len(inventory['buildings'])} buildings and {sum(len(b['floors']) for b in inventory['buildings'])} floors.")
