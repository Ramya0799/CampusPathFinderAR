import fitz
import os

base_dir = r"d:\PathFinder\ArchitecturalAssets"

def dump_full_page_text(pdf_name):
    pdf_path = os.path.join(base_dir, pdf_name)
    doc = fitz.open(pdf_path)
    print(f"\n=======================================================")
    print(f"DETAILED TEXT DUMP: {pdf_name}")
    print(f"=======================================================")
    for i, page in enumerate(doc):
        text = page.get_text("text")
        lines = [l.strip() for l in text.split("\n") if l.strip()]
        print(f"\n--- PAGE {i+1} (Total lines: {len(lines)}) ---")
        # Print title-like lines or key architectural terms
        key_lines = [l for l in lines if any(k in l.upper() for k in [
            "PLAN", "FLOOR", "LVL", "LEVEL", "BLOCK", "ENTRY", "ENTRANCE", "LOBBY", 
            "ROOM", "HALL", "LAB", "STAGE", "SEMINAR", "STAIR", "LIFT", "CORRIDOR",
            "OFFICE", "COURT", "TOILET", "CANTEEN", "FOOD", "GALLERY"
        ])]
        print(f"Key architectural items found ({len(key_lines)}):")
        for k in key_lines[:40]:
            print(f"  • {k}")

dump_full_page_text("ADMIN BLOCK FLOOR PLANS.pdf")
dump_full_page_text("TEACHING BLOCK FLOOR PLANS.pdf")
