import pdfplumber
import sys
sys.stdout.reconfigure(encoding='utf-8')

pdf = pdfplumber.open(r'D:\work\amazon_pdf_shipment\GYR3 5.4 440710003997.pdf')
page = pdf.pages[0]

words = page.extract_words(x_tolerance=3, y_tolerance=3)
print(f'Total words on page 1: {len(words)}')
print()

# Print all words
for w in words:
    print(f"  x0={w['x0']:7.1f} x1={w['x1']:7.1f} top={w['top']:7.1f} bottom={w['bottom']:7.1f} text='{w['text']}'")
