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
        // The parser can occasionally see hidden/cropped row-number text in browser PDFs.
        // The authoritative signal for an empty source table is the parsed original row list.
        layout.HasOriginalDataRows = originalRows.Count > 0;
        PdfFontResolver.EnsureRegistered();

        using var document = PdfReader.Open(originalPdfPath, PdfDocumentOpenMode.Modify);
        if (document.PageCount == 0)
            throw new InvalidOperationException("PDF does not contain any pages.");

        var maxIndex = originalRows.Count > 0 ? originalRows.Max(r => r.Index) : 0;
        var numberedRows = rowsToAppend.Select((row, i) => CloneWithIndex(row, maxIndex + i + 1)).ToList();

        var page = document.Pages[Math.Min(layout.PageIndex, document.PageCount - 1)];
        var currentY = layout.NextRowTop;
        if (!layout.HasShipmentTable)
        {
            var requiredStart = layout.HeaderTop + layout.HeaderHeight + layout.RowHeight;
            if (requiredStart > layout.BottomMargin || layout.PageHeight < 800.0)
            {
                var contentYOffset = CalculatePageExpansionContentYOffset(page, 841.92);
                layout.NormalizeToA4Portrait();
                layout.OffsetVertical(contentYOffset);
                ResizePageToLayout(page, layout);
            }
            currentY = layout.FirstDataRowTop;
        }

        var normalFont = new XFont("Arial", 6.0, XFontStyleEx.Regular);
        var indexFont = new XFont("Arial", 7.4, XFontStyleEx.Regular);
        var headerFont = new XFont("Arial", 6.0, XFontStyleEx.Bold);
        var sectionFont = new XFont("Arial", 9.5, XFontStyleEx.Bold);
        var textBrush = new XSolidBrush(XColor.FromArgb(32, 55, 59));
        var rowSeparatorPen = new XPen(XColor.FromArgb(221, 221, 221), 0.45);
        var outerBorderPen = new XPen(XColor.FromArgb(136, 140, 140), 0.47);

        XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var isFirstAppendPage = true;
        try
        {
            if (layout.HasShipmentTable)
            {
                RemoveEditAnnotations(page);
                CoverOriginalEditAndOuterBottom(gfx, layout);
                DrawAppendStartSeparator(gfx, layout, currentY, rowSeparatorPen);
            }
            else
            {
                CoverNoTableShipmentArea(gfx, layout);
                DrawShipmentTableHeader(gfx, layout, sectionFont, headerFont, textBrush, rowSeparatorPen, outerBorderPen);
            }

            foreach (var row in numberedRows)
            {
                var rowHeight = MeasureRowHeight(gfx, layout, row, normalFont);
                if (currentY + rowHeight > layout.BottomMargin)
                {
                    DrawPageBreakContinuation(gfx, layout, currentY, outerBorderPen, rowSeparatorPen, isFirstAppendPage);
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

            if (layout.HasShipmentTable)
                ScrubRightEditButtonArea(gfx, layout);
            DrawOuterContainerExtension(gfx, layout, currentY, outerBorderPen, isFirstAppendPage);
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
        ResizePageToLayout(page, layout);
        return page;
    }

    private static void ResizePageToLayout(PdfPage page, PdfTableLayout layout)
    {
        var pageRect = new XRect(0, 0, layout.PageWidth, layout.PageHeight);
        var pdfRect = new PdfRectangle(pageRect);
        page.Width = XUnit.FromPoint(layout.PageWidth);
        page.Height = XUnit.FromPoint(layout.PageHeight);
        page.MediaBox = pdfRect;
        page.CropBox = pdfRect;
        page.TrimBox = pdfRect;
        page.BleedBox = pdfRect;
        page.ArtBox = pdfRect;
    }

    private static double CalculatePageExpansionContentYOffset(PdfPage page, double targetPageHeight)
    {
        // PDFsharp keeps the original page contents in their source MediaBox
        // coordinates. If a browser PDF is a cropped Letter page and we expand
        // it to A4, the original visible content appears lower by the new-height
        // minus old-MediaBox-height delta, plus any crop-box top offset. Shift
        // overlay coordinates by the same amount so the new table stays below the
        // existing `Shipment info` / `Shipment Information` label.
        var mediaHeight = page.MediaBox.Height;
        var cropTopOffset = mediaHeight - Math.Max(page.CropBox.Y1, page.CropBox.Y2);
        return Math.Max(0, targetPageHeight - mediaHeight + Math.Max(0, cropTopOffset));
    }

    private static PdfPage AddShipmentTablePage(PdfDocument document, PdfTableLayout layout)
    {
        var page = document.AddPage();
        ResizePageToLayout(page, layout);
        return page;
    }

    private static void CoverNoTableShipmentArea(XGraphics gfx, PdfTableLayout layout)
    {
        // Browser-generated PDFs may have the remaining shipment table content hidden
        // below a clipped CropBox. Once we expand the page to A4, that stale hidden
        // content becomes visible. Clear the area below the existing `Shipment info`
        // label before drawing the normalized reference-style table.
        var coverTop = layout.HasShipmentInfoLabel
            ? Math.Max(0, layout.HeaderTop - 16.0)
            : Math.Max(0, layout.HeaderTop - 28.0);
        var whiteBrush = new XSolidBrush(XColors.White);
        gfx.DrawRectangle(whiteBrush, 0, coverTop, layout.PageWidth, Math.Max(0, layout.PageHeight - coverTop));
    }

    private static void DrawShipmentTableHeader(
        XGraphics gfx,
        PdfTableLayout layout,
        XFont sectionFont,
        XFont headerFont,
        XBrush textBrush,
        XPen rowSeparatorPen,
        XPen outerBorderPen)
    {
        var headerY = layout.HeaderTop;
        var sectionY = Math.Max(20.0, headerY - 22.0);
        if (!layout.HasShipmentInfoLabel)
            gfx.DrawString("Shipment info", sectionFont, textBrush, new XPoint(layout.TableLeft, sectionY));

        var headerFill = new XSolidBrush(XColor.FromArgb(250, 250, 250));
        gfx.DrawRectangle(headerFill, layout.TableLeft, headerY, layout.TableRight - layout.TableLeft, layout.HeaderHeight);
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, headerY, layout.TableRight, headerY);
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, headerY + layout.HeaderHeight, layout.TableRight, headerY + layout.HeaderHeight);
        if (ShouldDrawTableSideBorders(layout))
        {
            gfx.DrawLine(rowSeparatorPen, layout.TableLeft, headerY, layout.TableLeft, headerY + layout.HeaderHeight);
            gfx.DrawLine(rowSeparatorPen, layout.TableRight, headerY, layout.TableRight, headerY + layout.HeaderHeight);
        }

        for (var i = 1; i < layout.ColumnLefts.Length; i++)
            gfx.DrawLine(rowSeparatorPen, layout.ColumnLefts[i], headerY, layout.ColumnLefts[i], headerY + layout.HeaderHeight);

        var labels = new[]
        {
            string.Empty,
            "ARN",
            "PRO/Carrier\nReference Number",
            "BOL/Vendor\nReference Number",
            "Vendor\nName",
            "Pallet\nCount",
            "Carton\nCount",
            "Unit\nCount",
            "PO List"
        };

        for (var i = 1; i < labels.Length; i++)
        {
            var rect = CellRect(layout, i, headerY, layout.HeaderHeight, 3.0, 2.5);
            DrawHeaderText(gfx, labels[i], headerFont, textBrush, rect);
        }

        // The Amazon page uses a larger card around the shipment table. Start the
        // card at the shipment section label only when we are extending a source
        // table that already had data rows. Empty-table outputs should not add
        // the two outermost vertical lines.
        if (layout.HasOriginalDataRows)
        {
            gfx.DrawLine(outerBorderPen, OuterLeft, sectionY - 14.0, OuterLeft, layout.FirstDataRowTop);
            gfx.DrawLine(outerBorderPen, OuterRight, sectionY - 14.0, OuterRight, layout.FirstDataRowTop);
        }
    }

    private static void DrawHeaderText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
    {
        var lines = text.Split('\n');
        var lineHeight = font.Size + 1.2;
        var totalHeight = lines.Length * lineHeight;
        var y = rect.Y + Math.Max(0, (rect.Height - totalHeight) / 2.0) + font.Size;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, brush, new XPoint(rect.X, y));
            y += lineHeight;
        }
    }

    private const double OuterLeft = 10.23;
    private const double OuterRight = 585.12;
    private const double OuterTop = 14.25;

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

    private static void CoverOriginalEditAndOuterBottom(XGraphics gfx, PdfTableLayout layout)
    {
        var whiteBrush = new XSolidBrush(XColors.White);

        // The source Amazon PDF places the card bottom border and the yellow Edit link
        // immediately below the last original row. Cover them before drawing appended
        // rows, otherwise the old card border cuts through the new table data.
        // Different PDFs can end at very different Y positions, so do not use a
        // fixed template coordinate here; derive it from the detected last row.
        var coverTop = layout.FooterCoverTop;
        var coverHeight = Math.Max(0, layout.PageHeight - coverTop);
        gfx.DrawRectangle(whiteBrush, OuterLeft - 2.0, coverTop, OuterRight - OuterLeft + 4.0, coverHeight);
    }

    private static void DrawAppendStartSeparator(XGraphics gfx, PdfTableLayout layout, double y, XPen rowSeparatorPen)
    {
        // Covering the stale footer can also cover the original bottom separator of
        // the last source row. Redraw the seam line so the first appended row is
        // visually connected to the original table.
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y, layout.TableRight, y);
    }

    private static void ScrubRightEditButtonArea(XGraphics gfx, PdfTableLayout layout)
    {
        // Link annotations are removed separately, but the visible Edit button/text is
        // ordinary page content. Some viewers make the right-side button artifact more
        // obvious, so explicitly clear only the unused right gutter and redraw borders.
        var whiteBrush = new XSolidBrush(XColors.White);
        var x = Math.Min(548.0, layout.PageWidth - 1.0);
        var y = layout.FooterCoverTop;
        var width = Math.Max(1.0, layout.PageWidth - x);
        var height = Math.Max(0, layout.PageHeight - y);
        gfx.DrawRectangle(whiteBrush, x, y, width, height);
    }

    private static void DrawOuterContainerExtension(XGraphics gfx, PdfTableLayout layout, double tableEndY, XPen outerBorderPen, bool isFirstAppendPage)
    {
        var startY = layout.HasShipmentTable
            ? (isFirstAppendPage ? layout.FooterCoverTop : OuterTop)
            : Math.Max(OuterTop, layout.HeaderTop - 36.0);
        var endY = Math.Max(startY, tableEndY + 0.4);

        if (SuppressOuterContainerForEmptyTable(layout))
        {
            gfx.DrawLine(outerBorderPen, layout.TableLeft, endY, layout.TableRight, endY);
            return;
        }

        gfx.DrawLine(outerBorderPen, OuterLeft, startY, OuterLeft, endY);
        gfx.DrawLine(outerBorderPen, OuterRight, startY, OuterRight, endY);
        gfx.DrawLine(outerBorderPen, OuterLeft, endY, OuterRight, endY);
    }

    private static void DrawPageBreakContinuation(
        XGraphics gfx,
        PdfTableLayout layout,
        double tableEndY,
        XPen outerBorderPen,
        XPen rowSeparatorPen,
        bool isFirstAppendPage)
    {
        var outerStartY = layout.HasShipmentTable
            ? (isFirstAppendPage ? layout.FooterCoverTop : OuterTop)
            : Math.Max(OuterTop, layout.HeaderTop - 36.0);
        var pageBreakY = layout.BottomMargin;

        // At a page break the Amazon PDF does not close the table/card with a
        // bottom border.  It only lets the side borders run to the page break,
        // then the next page continues with the same side-border style.
        if (!SuppressOuterContainerForEmptyTable(layout))
        {
            gfx.DrawLine(outerBorderPen, OuterLeft, outerStartY, OuterLeft, pageBreakY);
            gfx.DrawLine(outerBorderPen, OuterRight, outerStartY, OuterRight, pageBreakY);
        }

        if (tableEndY < pageBreakY && ShouldDrawTableSideBorders(layout))
        {
            gfx.DrawLine(rowSeparatorPen, layout.TableLeft, tableEndY, layout.TableLeft, pageBreakY);
            gfx.DrawLine(rowSeparatorPen, layout.TableRight, tableEndY, layout.TableRight, pageBreakY);
        }
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
        // Match the source table: keep horizontal row separators and only draw
        // table outside vertical borders when the source body already had them.
        if (ShouldDrawTableSideBorders(layout))
        {
            gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y, layout.TableLeft, y + height);
            gfx.DrawLine(rowSeparatorPen, layout.TableRight, y, layout.TableRight, y + height);
        }
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y + height, layout.TableRight, y + height);
    }

    private static bool ShouldDrawTableSideBorders(PdfTableLayout layout)
    {
        // Empty source tables should not introduce the two outermost vertical
        // lines around the generated data/header body.
        return layout.HasOriginalDataRows;
    }

    private static bool SuppressOuterContainerForEmptyTable(PdfTableLayout layout)
    {
        return !layout.HasOriginalDataRows;
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
