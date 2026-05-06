import pdfplumber
import sys
sys.stdout.reconfigure(encoding='utf-8')

pdf = pdfplumber.open(r'D:\work\amazon_pdf_shipment\GYR3 5.4 440710003997.pdf')

for page_idx, page in enumerate(pdf.pages):
    print(f'=== Page {page_idx+1} ===')
    words = page.extract_words(x_tolerance=3, y_tolerance=3)
    
    in_table = False
    for w in words:
        if 'Shipment Information' in w['text']:
            in_table = True
        if in_table:
            print(f"  x0={w['x0']:7.1f} x1={w['x1']:7.1f} top={w['top']:7.1f} text='{w['text']}'")
    print()
    if page_idx == 0:
        break  # just check page 1 first
