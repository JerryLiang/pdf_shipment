using System.Text.RegularExpressions;
using AmazonShipmentTool.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AmazonShipmentTool.Parsers;

public sealed class PdfTableLayoutAnalyzer
{
    private const double DefaultTableLeft = 19.5;
    private const double DefaultTableRight = 575.8;
    private const double DefaultFirstDataRowTop = 479.0;
    private const double DefaultRowHeight = 15.814;
    private const double DefaultBottomMargin = 820.0;

    public PdfTableLayout Analyze(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var pages = document.GetPages().ToList();
        if (pages.Count == 0)
            throw new InvalidOperationException("PDF does not contain any pages.");

        var dataLines = new List<DataLine>();
        var firstShipmentPageIndex = -1;
        var headerTop = 454.0;

        foreach (var page in pages)
        {
            var lines = GroupLettersIntoLines(page.Letters.ToList(), tolerance: 2.0);
            foreach (var line in lines)
            {
                var ordered = line.OrderBy(l => l.Location.X).ToList();
                if (ordered.Count == 0) continue;

                var text = string.Join("", ordered.Select(l => l.Value)).Trim();
                var x = ordered[0].Location.X;
                var yTop = ToTopY(page.Height, ordered);

                if (text.Contains("Shipment Information", StringComparison.OrdinalIgnoreCase))
                {
                    firstShipmentPageIndex = page.Number - 1;
                    headerTop = yTop + 21.0;
                }

                if (x < 40)
                {
                    var rowNumberText = string.Join("", ordered
                        .Where(l => l.Location.X < 50)
                        .OrderBy(l => l.Location.X)
                        .Select(l => l.Value));
                    var match = Regex.Match(rowNumberText, @"^\d+");
                    if (match.Success && int.TryParse(match.Value, out var rowNumber))
                    {
                        dataLines.Add(new DataLine(page.Number - 1, rowNumber, yTop));
                    }
                }
            }
        }

        var lastPage = pages[^1];
        var pageIndex = pages.Count - 1;
        var lastDataLine = dataLines
            .OrderBy(l => l.RowNumber)
            .ThenBy(l => l.PageIndex)
            .LastOrDefault();

        if (lastDataLine != null)
        {
            pageIndex = lastDataLine.PageIndex;
        }
        else if (firstShipmentPageIndex >= 0)
        {
            pageIndex = firstShipmentPageIndex;
        }

        var pageForLayout = pages[pageIndex];
        var pageDataLines = dataLines
            .Where(l => l.PageIndex == pageIndex)
            .OrderBy(l => l.Top)
            .ToList();

        var rowHeight = EstimateRowHeight(pageDataLines);
        var lastDataTop = lastDataLine?.Top ?? DefaultFirstDataRowTop;

        return new PdfTableLayout
        {
            PageIndex = pageIndex,
            PageWidth = pageForLayout.Width,
            PageHeight = pageForLayout.Height,
            TableLeft = DefaultTableLeft,
            TableRight = Math.Min(DefaultTableRight, pageForLayout.Width - 16.0),
            HeaderTop = headerTop,
            HeaderHeight = 24.0,
            FirstDataRowTop = pageDataLines.FirstOrDefault()?.Top ?? DefaultFirstDataRowTop,
            LastDataRowTop = lastDataTop,
            RowHeight = rowHeight,
            BottomMargin = Math.Min(DefaultBottomMargin, pageForLayout.Height - 20.0)
        };
    }

    private static double EstimateRowHeight(List<DataLine> pageDataLines)
    {
        var gaps = pageDataLines
            .Zip(pageDataLines.Skip(1), (a, b) => b.Top - a.Top)
            .Where(g => g > 8 && g < 25)
            .ToList();

        if (gaps.Count == 0)
            return DefaultRowHeight;

        return gaps
            .GroupBy(g => Math.Round(g, 1))
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Average();
    }

    private static double ToTopY(double pageHeight, List<Letter> line)
    {
        var maxBaselineY = line.Max(l => l.Location.Y);
        return pageHeight - maxBaselineY - 10.0;
    }

    private static List<List<Letter>> GroupLettersIntoLines(List<Letter> letters, double tolerance)
    {
        if (letters.Count == 0) return new List<List<Letter>>();

        var sorted = letters.OrderByDescending(l => l.Location.Y).ToList();
        var lines = new List<List<Letter>>();
        var currentLine = new List<Letter> { sorted[0] };
        var currentY = sorted[0].Location.Y;

        for (int i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].Location.Y - currentY) <= tolerance)
            {
                currentLine.Add(sorted[i]);
            }
            else
            {
                lines.Add(currentLine);
                currentLine = new List<Letter> { sorted[i] };
                currentY = sorted[i].Location.Y;
            }
        }

        lines.Add(currentLine);
        return lines;
    }

    private sealed record DataLine(int PageIndex, int RowNumber, double Top);
}
