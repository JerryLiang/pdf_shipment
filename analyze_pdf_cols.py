import pdfplumber
import sys
sys.stdout.reconfigure(encoding='utf-8')

pdf = pdfplumber.open(r'D:\work\amazon_pdf_shipment\GYR3 5.4 440710003997.pdf')
page = pdf.pages[0]

words = page.extract_words(x_tolerance=3, y_tolerance=3)
print("=== Header + First 3 data rows ===")
for w in words:
    if w['top'] >= 455 and w['top'] <= 500:
        print(f"  x0={w['x0']:7.1f} x1={w['x1']:7.1f} top={w['top']:7.1f} text='{w['text']}'")

print()
print("=== Second page first 3 rows ===")
if len(pdf.pages) > 1:
    page2 = pdf.pages[1]
    words2 = page2.extract_words(x_tolerance=3, y_tolerance=3)
    # Find first data rows (those with digits at x~25)
    count = 0
    for w in words2:
        if w['top'] < 150 and w['x0'] < 50:
            print(f"  x0={w['x0']:7.1f} x1={w['x1']:7.1f} top={w['top']:7.1f} text='{w['text']}'")
            count += 1
            if count >= 20:
                break

    # Print all words on page 2 first few lines
    print()
    print("=== Page 2 all words top < 80 ===")
    for w in words2:
        if w['top'] < 80:
            print(f"  x0={w['x0']:7.1f} x1={w['x1']:7.1f} top={w['top']:7.1f} text='{w['text']}'")
