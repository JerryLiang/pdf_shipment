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
        var indexFont = new XFont("Arial", 7.4, XFontStyleEx.Regular);
        var textBrush = new XSolidBrush(XColor.FromArgb(32, 55, 59));
        var rowSeparatorPen = new XPen(XColor.FromArgb(221, 221, 221), 0.45);
        var outerBorderPen = new XPen(XColor.FromArgb(136, 140, 140), 0.47);

        XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var isFirstAppendPage = true;
        try
        {
            RemoveEditAnnotations(page);
            CoverOriginalEditAndOuterBottom(gfx);

            foreach (var row in numberedRows)
            {
                var rowHeight = MeasureRowHeight(gfx, layout, row, normalFont);
                if (currentY + rowHeight > layout.BottomMargin)
                {
                    DrawOuterContainerExtension(gfx, currentY, outerBorderPen, isFirstAppendPage);
                    gfx.Dispose();
                    page = AddContinuationPage(document, layout);
                    gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    currentY = 24.0;
                    isFirstAppendPage = false;
                    rowHeight = MeasureRowHeight(gfx, layout, row, normalFont);
                }

                DrawDataRow(gfx, layout, row, currentY, rowHeight, normalFont, indexFont, textBrush, rowSeparatorPen);
                currentY += rowHeight;
            }

            DrawOuterContainerExtension(gfx, currentY, outerBorderPen, isFirstAppendPage);
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

    private const double OuterLeft = 10.23;
    private const double OuterRight = 585.12;
    private const double OuterTop = 14.25;
    private const double OriginalOuterBottom = 722.62;

    private static void RemoveEditAnnotations(PdfPage page)
    {
        if (!page.HasAnnotations) return;

        for (var i = page.Annotations.Count - 1; i >= 0; i--)
        {
            var annotation = page.Annotations[i];
            var rect = annotation.Rectangle;
            if (rect.X1 >= 550 || rect.X2 >= 550)
                page.Annotations.Remove(annotation);
        }
    }

    private static void CoverOriginalEditAndOuterBottom(XGraphics gfx)
    {
        var whiteBrush = new XSolidBrush(XColors.White);

        // The source Amazon PDF places the card bottom border and the yellow Edit link
        // immediately below the last original row.  Cover them before drawing appended
        // rows, otherwise the old card border cuts through the new table data.
        gfx.DrawRectangle(whiteBrush, OuterLeft - 2.0, OriginalOuterBottom - 1.2, OuterRight - OuterLeft + 4.0, 24.0);
    }

    private static void DrawOuterContainerExtension(XGraphics gfx, double tableEndY, XPen outerBorderPen, bool isFirstAppendPage)
    {
        var startY = isFirstAppendPage ? OriginalOuterBottom - 0.2 : OuterTop;
        var endY = Math.Max(startY, tableEndY + 0.4);

        gfx.DrawLine(outerBorderPen, OuterLeft, startY, OuterLeft, endY);
        gfx.DrawLine(outerBorderPen, OuterRight, startY, OuterRight, endY);
        gfx.DrawLine(outerBorderPen, OuterLeft, endY, OuterRight, endY);
    }

    private static double MeasureRowHeight(XGraphics gfx, PdfTableLayout layout, ShipmentRow row, XFont font)
    {
        var values = RowValues(row);
        var maxLines = 1;
        for (int i = 1; i < values.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i])) continue;
            var rect = CellRect(layout, i, 0, layout.RowHeight, i == 0 ? 0 : 5.4, 0);
            maxLines = Math.Max(maxLines, WrapText(gfx, values[i].Trim(), font, rect.Width).Count);
        }

        if (maxLines <= 1) return layout.RowHeight;

        var lineHeight = font.Size + 2.4;
        return Math.Max(layout.RowHeight, 6.0 + maxLines * lineHeight);
    }

    private static void DrawDataRow(
        XGraphics gfx,
        PdfTableLayout layout,
        ShipmentRow row,
        double y,
        double rowHeight,
        XFont normalFont,
        XFont indexFont,
        XBrush brush,
        XPen rowSeparatorPen)
    {
        var values = RowValues(row);

        DrawRowSeparator(gfx, layout, y, rowHeight, rowSeparatorPen);

        for (int i = 0; i < values.Length; i++)
        {
            var rect = CellRect(layout, i, y, rowHeight, i == 0 ? 5.6 : 5.4, 0);
            var font = i == 0 ? indexFont : normalFont;
            if (i == 0)
                DrawSingleLineText(gfx, values[i], font, brush, rect);
            else
                DrawCellText(gfx, values[i], font, brush, rect);
        }
    }

    private static string[] RowValues(ShipmentRow row)
    {
        return new[]
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
    }

    private static void DrawRowSeparator(XGraphics gfx, PdfTableLayout layout, double y, double height, XPen rowSeparatorPen)
    {
        // Match the source table: keep the table's outside vertical borders and
        // horizontal row separators, but do not draw internal column grid lines.
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y, layout.TableLeft, y + height);
        gfx.DrawLine(rowSeparatorPen, layout.TableRight, y, layout.TableRight, y + height);
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y + height, layout.TableRight, y + height);
    }

    private static XRect CellRect(PdfTableLayout layout, int columnIndex, double y, double height, double padX, double padY)
    {
        var left = layout.ColumnLefts[columnIndex] + padX;
        var right = layout.ColumnRights[columnIndex] - padX;
        return new XRect(left, y + padY, Math.Max(1, right - left), Math.Max(1, height - padY * 2));
    }

    private static void DrawSingleLineText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var y = rect.Y + Math.Max(0, (rect.Height - font.Size) / 2.0) + font.Size - 1.0;
        gfx.DrawString(text.Trim(), font, brush, new XPoint(rect.X, y));
    }

    private static void DrawCellText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var lines = WrapText(gfx, text.Trim(), font, rect.Width);
        var lineHeight = font.Size + 2.4;
        var totalHeight = lines.Count * lineHeight;
        var startY = rect.Y + Math.Max(0, (rect.Height - totalHeight) / 2.0) + font.Size;

        foreach (var line in lines)
        {
            gfx.DrawString(line, font, brush, new XPoint(rect.X, startY));
            startY += lineHeight;
        }
    }

    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var effectiveMaxWidth = Math.Max(1, maxWidth * 0.86);
        var normalized = text.Replace("\r", "").Replace("\n", " ").Trim();
        foreach (var paragraph in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            AddWrappedToken(gfx, paragraph, font, effectiveMaxWidth, result);
        }

        if (result.Count == 0)
            result.Add(string.Empty);

        return result;
    }

    private static void AddWrappedToken(XGraphics gfx, string token, XFont font, double maxWidth, List<string> lines)
    {
        var parts = token.Split(new[] { ',', '-' }, StringSplitOptions.None);
        var separators = token.Where(c => c == ',' || c == '-').ToList();

        if (parts.Length > 1)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var suffix = i < separators.Count ? separators[i].ToString() : string.Empty;
                AddWrappedPiece(gfx, part + suffix, font, maxWidth, lines);
            }
            return;
        }

        AddWrappedPiece(gfx, token, font, maxWidth, lines);
    }

    private static void AddWrappedPiece(XGraphics gfx, string piece, XFont font, double maxWidth, List<string> lines)
    {
        if (string.IsNullOrEmpty(piece)) return;

        var charLimit = Math.Max(1, (int)Math.Floor(maxWidth / Math.Max(1.0, font.Size * 0.58)));

        if (lines.Count == 0)
        {
            if (piece.Length <= charLimit)
            {
                lines.Add(piece);
                return;
            }
        }
        else
        {
            var candidate = lines[^1] + piece;
            if (candidate.Length <= charLimit && gfx.MeasureString(candidate, font).Width <= maxWidth)
            {
                lines[^1] = candidate;
                return;
            }
        }

        if (piece.Length <= charLimit && gfx.MeasureString(piece, font).Width <= maxWidth)
        {
            lines.Add(piece);
            return;
        }

        var remaining = piece;
        while (remaining.Length > 0)
        {
            var take = Math.Min(remaining.Length, charLimit);
            while (take > 1 && gfx.MeasureString(remaining[..take], font).Width > maxWidth)
                take--;

            lines.Add(remaining[..take]);
            remaining = remaining[take..];
        }
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
