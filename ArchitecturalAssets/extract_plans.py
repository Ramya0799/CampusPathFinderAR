import fitz # PyMuPDF
import os
import json

base_dir = r"d:\PathFinder\ArchitecturalAssets"
admin_pdf = os.path.join(base_dir, "ADMIN BLOCK FLOOR PLANS.pdf")
teaching_pdf = os.path.join(base_dir, "TEACHING BLOCK FLOOR PLANS.pdf")

out_images_dir = os.path.join(base_dir, "extracted_pages")
os.makedirs(out_images_dir, exist_ok=True)

def inspect_pdf(pdf_path, name):
    print(f"\n==========================================")
    print(f"INSPECTING: {name} ({pdf_path})")
    print(f"==========================================")
    doc = fitz.open(pdf_path)
    print(f"Total pages: {len(doc)}")
    
    summary = []
    
    for i, page in enumerate(doc):
        text = page.get_text("text").strip()
        lines = [line.strip() for line in text.split("\n") if line.strip()]
        
        # Render page to high-res PNG for computer vision and manual visual verification
        pix = page.get_pixmap(dpi=150)
        img_name = f"{name}_page_{i+1}.png"
        img_path = os.path.join(out_images_dir, img_name)
        pix.save(img_path)
        
        page_info = {
            "page_number": i + 1,
            "rect": [page.rect.x0, page.rect.y0, page.rect.x1, page.rect.y1],
            "rendered_image": img_path,
            "text_sample": lines[:30],
            "all_text": text
        }
        summary.append(page_info)
        print(f"Page {i+1}: size={page.rect.width:.1f}x{page.rect.height:.1f}, text lines={len(lines)}, image saved to {img_name}")
        if lines:
            print("  First few text elements:", lines[:10])
        else:
            print("  (No direct vector text on this page - pure raster/scanned drawing)")
            
    with open(os.path.join(base_dir, f"{name}_summary.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

inspect_pdf(admin_pdf, "ADMIN_BLOCK")
inspect_pdf(teaching_pdf, "TEACHING_BLOCK")
