using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

var pdfPath = @"D:\work\amazon_pdf_shipment\GYR3 5.4 440710003997.pdf";

Console.OutputEncoding = Encoding.UTF8;
using var document = PdfDocument.Open(pdfPath);

var page1 = document.GetPage(1);
Console.WriteLine($"Page 1: Width={page1.Width}, Height={page1.Height}");

var letters = page1.Letters.ToList();
Console.WriteLine($"Total letters on page 1: {letters.Count}");

var lines = letters
    .GroupBy(l => Math.Round(l.Location.Y, 0))
    .OrderByDescending(g => g.Key)
    .ToList();

Console.WriteLine($"Total lines (by Y): {lines.Count}");
Console.WriteLine();

bool inTable = false;
foreach (var line in lines)
{
    var text = string.Join("", line.OrderBy(l => l.Location.X).Select(l => l.Value)).Trim();
    
    if (text.Contains("Shipment Information"))
        inTable = true;
    
    if (inTable && !string.IsNullOrWhiteSpace(text))
    {
        var minX = line.Min(l => l.Location.X);
        var maxX = line.Max(l => l.Location.X);
        Console.WriteLine($"Y={line.Key,6:F0} X=[{minX,6:F1}-{maxX,6:F1}] Text=[{text}]");
    }
    
    if (inTable && text.Contains("5C81VLJV"))
        break;
}
