using AmazonShipmentTool.Models;
using AmazonShipmentTool.Parsers;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AmazonShipmentTool.Services;

public sealed class PdfAppendService
{
    public void AppendRowsToPdf(
        string originalPdfPath,
        IReadOnlyList<ShipmentRow> originalRows,
        IReadOnlyList<ShipmentRow> rowsToAppend,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(originalPdfPath))
            throw new ArgumentException("Original PDF path is required.", nameof(originalPdfPath));
        if (!File.Exists(originalPdfPath))
            throw new FileNotFoundException("Original PDF not found.", originalPdfPath);
        if (rowsToAppend.Count == 0)
            throw new InvalidOperationException("No Excel rows to append.");

        var analyzer = new PdfTableLayoutAnalyzer();
        var layout = analyzer.Analyze(originalPdfPath);
        PdfFontResolver.EnsureRegistered();

        using var document = PdfReader.Open(originalPdfPath, PdfDocumentOpenMode.Modify);
        if (document.PageCount == 0)
            throw new InvalidOperationException("PDF does not contain any pages.");

        var maxIndex = originalRows.Count > 0 ? originalRows.Max(r => r.Index) : 0;
        var numberedRows = rowsToAppend.Select((row, i) => CloneWithIndex(row, maxIndex + i + 1)).ToList();

        var page = document.Pages[Math.Min(layout.PageIndex, document.PageCount - 1)];
        var currentY = layout.NextRowTop;

        var normalFont = new XFont("Arial", 6.0, XFontStyleEx.Regular);
        var textBrush = new XSolidBrush(XColor.FromArgb(32, 55, 59));
        var gridPen = new XPen(XColor.FromArgb(221, 221, 221), 0.45);
        var borderPen = new XPen(XColor.FromArgb(230, 230, 230), 0.7);

        XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var segmentStartY = currentY;
        try
        {
            foreach (var row in numberedRows)
            {
                if (currentY + layout.RowHeight > layout.BottomMargin)
                {
                    DrawAppendSegmentBorder(gfx, layout, segmentStartY, currentY, borderPen);
                    gfx.Dispose();
                    page = AddContinuationPage(document, layout);
                    gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    currentY = 24.0;
                    segmentStartY = currentY;
                }

                DrawDataRow(gfx, layout, row, currentY, normalFont, textBrush, gridPen);
                currentY += layout.RowHeight;
            }

            DrawAppendSegmentBorder(gfx, layout, segmentStartY, currentY, borderPen);
        }
        finally
        {
            gfx.Dispose();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        document.Save(outputPath);
    }

    private static PdfPage AddContinuationPage(PdfDocument document, PdfTableLayout layout)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(layout.PageWidth);
        page.Height = XUnit.FromPoint(layout.PageHeight);
        return page;
    }

    private static void DrawAppendSegmentBorder(XGraphics gfx, PdfTableLayout layout, double startY, double endY, XPen borderPen)
    {
        if (endY <= startY) return;

        gfx.DrawLine(borderPen, layout.TableLeft, startY, layout.TableLeft, endY);
        gfx.DrawLine(borderPen, layout.TableRight, startY, layout.TableRight, endY);
        gfx.DrawLine(borderPen, layout.TableLeft, endY, layout.TableRight, endY);
    }

    private static void DrawDataRow(
        XGraphics gfx,
        PdfTableLayout layout,
        ShipmentRow row,
        double y,
        XFont font,
        XBrush brush,
        XPen gridPen)
    {
        var values = new[]
        {
            row.Index.ToString(),
            row.Arn,
            row.CarrierReferenceNumber,
            row.BolOrVendorReference,
            row.VendorName,
            row.PalletCount.ToString(),
            row.CartonCount.ToString(),
            row.UnitCount?.ToString() ?? string.Empty,
            row.PoList
        };

        gfx.DrawLine(gridPen, layout.TableLeft, y + layout.RowHeight, layout.TableRight, y + layout.RowHeight);

        for (int i = 0; i < values.Length; i++)
        {
            var rect = CellRect(layout, i, y, layout.RowHeight, i == 0 ? 0 : 3, 0);
            var center = i == 0 || i >= 5;
            DrawSingleLineText(gfx, values[i], font, brush, rect, center);
        }
    }

    private static XRect CellRect(PdfTableLayout layout, int columnIndex, double y, double height, double padX, double padY)
    {
        var left = layout.ColumnLefts[columnIndex] + padX;
        var right = layout.ColumnRights[columnIndex] - padX;
        return new XRect(left, y + padY, Math.Max(1, right - left), Math.Max(1, height - padY * 2));
    }

    private static void DrawSingleLineText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, bool center)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var format = new XStringFormat
        {
            Alignment = center ? XStringAlignment.Center : XStringAlignment.Near,
            LineAlignment = XLineAlignment.Center
        };

        var fitted = FitText(gfx, text.Trim(), font, rect.Width);
        gfx.DrawString(fitted, font, brush, rect, format);
    }

    private static void DrawWrappedText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, bool center)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var lines = text.Replace("\r", "").Split('\n');
        var lineHeight = font.Size + 1.5;
        var totalHeight = lines.Length * lineHeight;
        var startY = rect.Y + Math.Max(0, (rect.Height - totalHeight) / 2.0) + font.Size;

        foreach (var line in lines)
        {
            var format = new XStringFormat
            {
                Alignment = center ? XStringAlignment.Center : XStringAlignment.Near,
                LineAlignment = XLineAlignment.Near
            };
            var lineRect = new XRect(rect.X, startY - font.Size, rect.Width, lineHeight);
            gfx.DrawString(FitText(gfx, line.Trim(), font, rect.Width), font, brush, lineRect, format);
            startY += lineHeight;
        }
    }

    private static string FitText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || gfx.MeasureString(text, font).Width <= maxWidth)
            return text;

        const string ellipsis = "…";
        var candidate = text;
        while (candidate.Length > 1 && gfx.MeasureString(candidate + ellipsis, font).Width > maxWidth)
        {
            candidate = candidate[..^1];
        }

        return candidate + ellipsis;
    }

    private static ShipmentRow CloneWithIndex(ShipmentRow row, int index)
    {
        return new ShipmentRow
        {
            Index = index,
            Arn = row.Arn,
            CarrierReferenceNumber = row.CarrierReferenceNumber,
            BolOrVendorReference = row.BolOrVendorReference,
            VendorName = row.VendorName,
            PalletCount = row.PalletCount,
            CartonCount = row.CartonCount,
            UnitCount = row.UnitCount,
            PoList = row.PoList,
            IsAddedFromExcel = true
        };
    }
}
