import json
import os

base_dir = r"d:\PathFinder\ArchitecturalAssets"

with open(os.path.join(base_dir, "admin_blocks.json"), "r", encoding="utf-8") as f:
    admin_data = json.load(f)

with open(os.path.join(base_dir, "teaching_blocks.json"), "r", encoding="utf-8") as f:
    teaching_data = json.load(f)

print("=== ADMIN BLOCK ANALYSIS ===")
for p, blocks in admin_data.items():
    print(f"\n--- {p.upper()} ---")
    for b in blocks:
        t = b["text"].replace("\n", " ")
        if any(w in t.upper() for w in ["ENTRANCE", "LOBBY", "ROOM", "HALL", "STAGE", "TOILET", "LIFT", "STAIR", "CORRIDOR", "LVL", "FLOOR", "GALLERY", "OFFICER"]):
            print(f"  [{b['bbox'][0]:.1f}, {b['bbox'][1]:.1f}] -> {t}")

print("\n=== TEACHING BLOCK ANALYSIS ===")
for p, blocks in teaching_data.items():
    print(f"\n--- {p.upper()} ---")
    for b in blocks:
        t = b["text"].replace("\n", " ")
        if any(w in t.upper() for w in ["ENTRANCE", "LOBBY", "ROOM", "HALL", "STAGE", "TOILET", "LIFT", "STAIR", "CORRIDOR", "LVL", "FLOOR", "COURT", "OFFICE", "CLASS"]):
            print(f"  [{b['bbox'][0]:.1f}, {b['bbox'][1]:.1f}] -> {t}")
