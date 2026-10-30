import fitz
import os
import json

base_dir = r"d:\PathFinder\ArchitecturalAssets"

def analyze_blocks(pdf_file, output_json):
    doc = fitz.open(os.path.join(base_dir, pdf_file))
    result = {}
    for page_idx, page in enumerate(doc):
        blocks = page.get_text("blocks")
        # each block is (x0, y0, x1, y1, text, block_no, block_type)
        page_items = []
        for b in blocks:
            text = b[4].strip()
            if text:
                page_items.append({
                    "bbox": [round(b[0], 2), round(b[1], 2), round(b[2], 2), round(b[3], 2)],
                    "text": text
                })
        result[f"page_{page_idx + 1}"] = page_items
    
    with open(os.path.join(base_dir, output_json), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
    print(f"Saved {output_json} with {len(result)} pages.")

analyze_blocks("ADMIN BLOCK FLOOR PLANS.pdf", "admin_blocks.json")
analyze_blocks("TEACHING BLOCK FLOOR PLANS.pdf", "teaching_blocks.json")
