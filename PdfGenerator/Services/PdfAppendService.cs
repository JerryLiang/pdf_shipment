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

        // PdfParser can miss rows in Amazon's embedded/Type3 landscape tables.
        // The layout analyzer reads visible row-number positions directly, so use
        // the greater of both sources to prevent appended rows from restarting at 1
        // or continuing from a too-small parsed subset.
        var parsedMaxIndex = originalRows.Count > 0 ? originalRows.Max(r => r.Index) : 0;
        var maxIndex = Math.Max(parsedMaxIndex, layout.MaxOriginalRowIndex);
        var numberedRows = rowsToAppend.Select((row, i) => CloneWithIndex(row, maxIndex + i + 1)).ToList();
        if (layout.IsLandscapeTable)
        {
            AppendRowsToLandscapePdf(document, layout, numberedRows, outputPath);
            return;
        }

        var page = document.Pages[Math.Min(layout.PageIndex, document.PageCount - 1)];
        var headerOnlyVisibleTable = layout.HasVisibleColumnHeader && !layout.HasOriginalDataRows;
        var currentY = headerOnlyVisibleTable ? layout.FirstDataRowTop : layout.NextRowTop;
        var currentPageBottomMargin = layout.BottomMargin;
        if (!layout.HasShipmentTable || headerOnlyVisibleTable)
        {
            var requiredStart = layout.HeaderTop + layout.HeaderHeight + layout.RowHeight;
            if (requiredStart > layout.BottomMargin || layout.PageHeight < 800.0)
            {
                layout.NormalizeToA4Portrait();
                ResizePageToLayout(page, layout);
                // Keep the expanded first page anchored at (0, 0). The source PDF's
                // MediaBox is already A4; only the visible CropBox was short. Using
                // a negative MediaBox/CropBox y-offset moves the original Amazon
                // header out of view in some viewers after appending rows.
                currentPageBottomMargin = layout.BottomMargin;
            }
            currentY = layout.FirstDataRowTop;
        }

        var normalFont = new XFont("Arial", 6.0, XFontStyleEx.Regular);
        var indexFont = new XFont("Arial", 7.4, XFontStyleEx.Regular);
        var headerFont = new XFont("Arial", 6.0, XFontStyleEx.Bold);
        var sectionFont = new XFont("Arial", 9.5, XFontStyleEx.Bold);
        var textBrush = new XSolidBrush(XColor.FromArgb(32, 55, 59));
        var rowSeparatorPen = new XPen(XColor.FromArgb(221, 221, 221), 0.45);
        // Amazon's portrait container side bars are very thin filled rectangles.
        // Draw them as source-matched fills instead of stroked lines so generated
        // continuation bars do not look offset or thicker at the old/new seam.
        var outerBorderBrush = new XSolidBrush(XColor.FromArgb(136, 140, 140));

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
                DrawShipmentTableHeader(gfx, layout, sectionFont, headerFont, textBrush, rowSeparatorPen, outerBorderBrush);
            }

            foreach (var row in numberedRows)
            {
                var rowHeight = MeasureRowHeight(gfx, layout, row, normalFont);
                if (currentY + rowHeight > currentPageBottomMargin)
                {
                    DrawPageBreakContinuation(gfx, layout, currentY, currentPageBottomMargin, outerBorderBrush, rowSeparatorPen, isFirstAppendPage);
                    gfx.Dispose();
                    page = AddContinuationPage(document, layout);
                    gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    currentY = 24.0;
                    currentPageBottomMargin = layout.BottomMargin;
                    isFirstAppendPage = false;
                    rowHeight = MeasureRowHeight(gfx, layout, row, normalFont);
                }

                DrawDataRow(gfx, layout, row, currentY, rowHeight, normalFont, indexFont, textBrush, rowSeparatorPen);
                currentY += rowHeight;
            }

            if (layout.HasShipmentTable)
                ScrubRightEditButtonArea(gfx, layout);
            DrawOuterContainerExtension(gfx, layout, currentY, outerBorderBrush, isFirstAppendPage);
        }
        finally
        {
            gfx.Dispose();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        RemoveBrowserFooterPageNumbers(document);
        document.Save(outputPath);
    }

    private static void AppendRowsToLandscapePdf(
        PdfDocument document,
        PdfTableLayout layout,
        IReadOnlyList<ShipmentRow> rows,
        string outputPath)
    {
        var page = document.Pages[Math.Min(layout.PageIndex, document.PageCount - 1)];
        ResizeLandscapePage(page);

        var indexFont = new XFont("Arial", 9.2, XFontStyleEx.Regular);
        var bodyFont = new XFont("Arial", 7.48, XFontStyleEx.Regular);
        var textBrush = new XSolidBrush(XColor.FromArgb(25, 46, 51));
        var whiteBrush = new XSolidBrush(XColors.White);
        var gridBrush = new XSolidBrush(XColor.FromArgb(213, 217, 217));
        var darkBrush = new XSolidBrush(XColor.FromArgb(136, 140, 140));

        var startY = layout.HasOriginalDataRows ? layout.NextRowTop : 56.52;
        var currentPageStartY = startY;
        var rowIndex = 0;

        while (rowIndex < rows.Count)
        {
            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                var capacity = Math.Max(1, (int)Math.Floor((LandscapeBottom - currentPageStartY) / LandscapeRowHeight));
                var rowsOnPage = Math.Min(capacity, rows.Count - rowIndex);

                // Header-only landscape PDFs can have hidden/stale body rows below a short CropBox.
                // Clear the body area before drawing the authoritative appended rows.
                gfx.DrawRectangle(whiteBrush, 0, currentPageStartY, LandscapePageWidth, LandscapeBottom - currentPageStartY + 1.0);
                DrawLandscapeGrid(gfx, currentPageStartY, rowsOnPage, rowsOnPage == capacity, gridBrush, darkBrush, whiteBrush);

                for (var i = 0; i < rowsOnPage; i++)
                {
                    DrawLandscapeRow(gfx, rows[rowIndex + i], currentPageStartY + i * LandscapeRowHeight, indexFont, bodyFont, textBrush);
                }

                rowIndex += rowsOnPage;
            }

            if (rowIndex >= rows.Count)
                break;

            page = document.AddPage();
            ResizeLandscapePage(page);
            currentPageStartY = LandscapeTop;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        RemoveBrowserFooterPageNumbers(document);
        document.Save(outputPath);
    }

    private const double LandscapePageWidth = 792.0;
    private const double LandscapePageHeight = 612.0;
    private const double LandscapeTop = 27.75;
    private const double LandscapeBottom = 584.153;
    private const double LandscapeRowHeight = 21.289;
    private static readonly double[] LandscapeColumnLefts = { 51.916, 84.138, 112.908, 221.657, 411.536, 466.773, 526.038, 588.756, 643.993 };
    private static readonly double[] LandscapeColumnRights = { 84.138, 112.908, 221.657, 411.536, 466.773, 526.038, 588.756, 643.993, 740.084 };
    // Match RowValues(): index, ARN, PRO/Carrier, BOL, Vendor, Pallet, Carton, Unit, PO.
    // A previous version omitted the ARN anchor, which shifted Excel PRO/FBA values into
    // the BOL column and made appended data appear in the wrong positions.
    private static readonly double[] LandscapeTextXs = { 58.82, 90.80, 119.86, 228.50, 418.47, 473.47, 532.75, 595.39, 651.00 };
    private const double LandscapeIndexHorizontalScale = 1.18;

    private static void ResizeLandscapePage(PdfPage page)
    {
        var rect = new PdfRectangle(new XRect(0, 0, LandscapePageWidth, LandscapePageHeight));
        page.Width = XUnit.FromPoint(LandscapePageWidth);
        page.Height = XUnit.FromPoint(LandscapePageHeight);
        page.MediaBox = rect;
        page.CropBox = rect;
        page.TrimBox = rect;
        page.BleedBox = rect;
        page.ArtBox = rect;
    }

    private static void DrawLandscapeGrid(XGraphics gfx, double startY, int rowCount, bool fullPage, XBrush gridBrush, XBrush darkBrush, XBrush whiteBrush)
    {
        var endY = fullPage ? LandscapeBottom : Math.Min(LandscapeBottom, startY + rowCount * LandscapeRowHeight);

        for (var row = 0; row < rowCount; row++)
        {
            var y = startY + row * LandscapeRowHeight;
            var y2 = Math.Min(LandscapeBottom, y + LandscapeRowHeight);
            for (var col = 0; col < LandscapeColumnLefts.Length; col++)
            {
                gfx.DrawRectangle(whiteBrush, LandscapeColumnLefts[col], y, LandscapeColumnRights[col] - LandscapeColumnLefts[col], y2 - y);
                gfx.DrawRectangle(gridBrush, LandscapeColumnLefts[col], y2 - 0.58, LandscapeColumnRights[col] - LandscapeColumnLefts[col], 0.58);
            }
        }

        // Original landscape PDFs have both a thin light table edge and an outside dark bar.
        // Match the rendered source weight; do not invent thicker bars or internal grid lines.
        gfx.DrawRectangle(darkBrush, 40.25, LandscapeTop, 0.70, endY - LandscapeTop);
        gfx.DrawRectangle(darkBrush, 751.00, LandscapeTop, 0.50, endY - LandscapeTop);
        gfx.DrawRectangle(gridBrush, 51.341, LandscapeTop, 0.58, endY - LandscapeTop);
        gfx.DrawRectangle(gridBrush, 740.084, LandscapeTop, 0.58, endY - LandscapeTop);

        if (!fullPage)
        {
            gfx.DrawRectangle(darkBrush, 40.25, endY - 0.50, 711.25, 0.50);
            gfx.DrawRectangle(gridBrush, 51.341, endY - 0.58, 689.318, 0.58);
        }
    }

    private static void DrawLandscapeRow(XGraphics gfx, ShipmentRow row, double top, XFont indexFont, XFont bodyFont, XBrush textBrush)
    {
        var values = RowValues(row).Select((value, i) => TruncateLandscapeValue(i, value)).ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i])) continue;
            if (i == 0)
                DrawLandscapeIndex(gfx, values[i], indexFont, textBrush, LandscapeTextXs[i], top + 9.55);
            else
                gfx.DrawString(values[i], bodyFont, textBrush, new XPoint(LandscapeTextXs[i], top + 9.55));
        }
    }

    private static void DrawLandscapeIndex(XGraphics gfx, string text, XFont font, XBrush brush, double x, double y)
    {
        // Amazon's original landscape row numbers are embedded Type3 glyphs: the
        // height matches a 9.2pt Arial-like font, but the digits are wider. Stretch
        // only the generated index text horizontally so the sequence column matches
        // the source without changing font height, row geometry, or other columns.
        var state = gfx.Save();
        gfx.TranslateTransform(x, y);
        gfx.ScaleTransform(LandscapeIndexHorizontalScale, 1.0);
        gfx.DrawString(text, font, brush, new XPoint(0, 0));
        gfx.Restore(state);
    }

    private static string TruncateLandscapeValue(int columnIndex, string value)
    {
        var limit = columnIndex switch
        {
            1 => 14,
            2 => 18,
            3 => 16,
            4 => 10,
            8 => 12,
            _ => int.MaxValue
        };
        return value.Length > limit ? value[..limit] : value;
    }

    private static PdfPage AddContinuationPage(PdfDocument document, PdfTableLayout layout)
    {
        var page = document.AddPage();
        ResizePageToLayout(page, layout);
        return page;
    }

    private static void RemoveBrowserFooterPageNumbers(PdfDocument document)
    {
        var whiteBrush = new XSolidBrush(XColors.White);
        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var width = page.Width.Point;
            var height = page.Height.Point;
            // Chrome/PDF browser footer page numbers such as "1/6" sit in the
            // bottom-right corner. Preserve the left footer URL by clearing only
            // the narrow right corner region.
            gfx.DrawRectangle(whiteBrush, Math.Max(0, width - 58.0), Math.Max(0, height - 28.0), 54.0, 18.0);
        }
    }

    private static double ResizePageToLayout(PdfPage page, PdfTableLayout layout, double? topAnchor = null)
    {
        var pageY = topAnchor.HasValue ? topAnchor.Value - layout.PageHeight : 0;
        var pageRect = new XRect(0, pageY, layout.PageWidth, layout.PageHeight);
        var pdfRect = new PdfRectangle(pageRect);
        page.Width = XUnit.FromPoint(layout.PageWidth);
        page.Height = XUnit.FromPoint(layout.PageHeight);
        page.MediaBox = pdfRect;
        page.CropBox = pdfRect;
        page.TrimBox = pdfRect;
        page.BleedBox = pdfRect;
        page.ArtBox = pdfRect;
        return pageY;
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
        XBrush outerBorderBrush)
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
            DrawOuterSideBar(gfx, OuterLeft, sectionY - 14.0, layout.FirstDataRowTop, outerBorderBrush);
            DrawOuterSideBar(gfx, OuterRight, sectionY - 14.0, layout.FirstDataRowTop, outerBorderBrush);
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
    private const double OuterBarWidth = 0.465;

    private static void DrawOuterSideBar(XGraphics gfx, double x, double y1, double y2, XBrush brush)
    {
        if (y2 <= y1) return;

        // Source PDFs encode these outside bars as thin filled rectangles:
        // left x=10.233..10.698 and right x=584.652..585.117.
        // Keeping that geometry avoids a visible step where generated rows begin.
        var left = Math.Abs(x - OuterRight) < 0.01 ? OuterRight - OuterBarWidth : x;
        gfx.DrawRectangle(brush, left, y1, OuterBarWidth, y2 - y1);
    }


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
        var coverTop = (!layout.HasOriginalDataRows && layout.HasVisibleColumnHeader)
            ? Math.Max(0, layout.HeaderTop + layout.HeaderHeight - 1.0)
            : layout.FooterCoverTop;
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

    private static void DrawOuterContainerExtension(XGraphics gfx, PdfTableLayout layout, double tableEndY, XBrush outerBorderBrush, bool isFirstAppendPage)
    {
        var startY = layout.HasShipmentTable
            ? (isFirstAppendPage
                // Header-only portrait sources already have a visible table header.
                // Match the source container by connecting the side bars from the
                // visible header/card top, not from the first generated data row.
                ? (!layout.HasOriginalDataRows && layout.HasVisibleColumnHeader ? layout.HeaderTop : layout.FooterCoverTop)
                : OuterTop)
            : Math.Max(OuterTop, layout.HeaderTop - 36.0);
        var endY = Math.Max(startY, tableEndY + 0.4);

        if (SuppressOuterContainerForEmptyTable(layout))
            return;

        DrawOuterSideBar(gfx, OuterLeft, startY, endY, outerBorderBrush);
        DrawOuterSideBar(gfx, OuterRight, startY, endY, outerBorderBrush);
        gfx.DrawRectangle(outerBorderBrush, OuterLeft, endY - OuterBarWidth, OuterRight - OuterLeft, OuterBarWidth);
    }

    private static void DrawPageBreakContinuation(
        XGraphics gfx,
        PdfTableLayout layout,
        double tableEndY,
        double pageBreakY,
        XBrush outerBorderBrush,
        XPen rowSeparatorPen,
        bool isFirstAppendPage)
    {
        var outerStartY = layout.HasShipmentTable
            ? (isFirstAppendPage
                ? (!layout.HasOriginalDataRows && layout.HasVisibleColumnHeader ? layout.HeaderTop : layout.FooterCoverTop)
                : OuterTop)
            : Math.Max(OuterTop, layout.HeaderTop - 36.0);

        // At a page break the Amazon PDF does not close the table/card with a
        // bottom border.  It only lets the side borders run to the page break,
        // then the next page continues with the same side-border style.
        if (!SuppressOuterContainerForEmptyTable(layout))
        {
            DrawOuterSideBar(gfx, OuterLeft, outerStartY, pageBreakY, outerBorderBrush);
            DrawOuterSideBar(gfx, OuterRight, outerStartY, pageBreakY, outerBorderBrush);
        }

        if (tableEndY < pageBreakY && ShouldDrawTableSideBorders(layout))
        {
            gfx.DrawLine(rowSeparatorPen, layout.TableLeft, tableEndY, layout.TableLeft, pageBreakY);
            gfx.DrawLine(rowSeparatorPen, layout.TableRight, tableEndY, layout.TableRight, pageBreakY);
        }
    }

    private static double MeasureRowHeight(XGraphics gfx, PdfTableLayout layout, ShipmentRow row, XFont font)
    {
        // If the source already has original portrait data rows, appended rows
        // must keep the original row cadence. Header-only sources still need the
        // old wrapping behavior so long Amazon reference strings do not spill into
        // neighboring columns on continuation pages.
        if (!layout.IsLandscapeTable && layout.HasOriginalDataRows)
            return layout.RowHeight;

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
            else if (!layout.IsLandscapeTable && layout.HasOriginalDataRows)
                DrawSingleLineFittedText(gfx, values[i], font, brush, rect);
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
        // Match the source table: keep horizontal row separators and draw the
        // outside table vertical borders when the source already had body rows or
        // a visible column header that should continue into generated rows.
        if (ShouldDrawTableSideBorders(layout))
        {
            gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y, layout.TableLeft, y + height);
            gfx.DrawLine(rowSeparatorPen, layout.TableRight, y, layout.TableRight, y + height);
        }
        gfx.DrawLine(rowSeparatorPen, layout.TableLeft, y + height, layout.TableRight, y + height);
    }

    private static bool ShouldDrawTableSideBorders(PdfTableLayout layout)
    {
        return layout.HasOriginalDataRows || layout.HasVisibleColumnHeader;
    }

    private static bool SuppressOuterContainerForEmptyTable(PdfTableLayout layout)
    {
        return !layout.HasOriginalDataRows && !layout.HasVisibleColumnHeader;
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

    private static void DrawSingleLineFittedText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var trimmed = text.Trim();
        var fittedFont = font;
        var width = gfx.MeasureString(trimmed, fittedFont).Width;
        if (width > rect.Width)
        {
            var fittedSize = Math.Max(3.0, font.Size * rect.Width / Math.Max(1.0, width));
            fittedFont = new XFont("Arial", fittedSize, XFontStyleEx.Regular);
        }

        var y = rect.Y + Math.Max(0, (rect.Height - fittedFont.Size) / 2.0) + fittedFont.Size - 1.0;
        var state = gfx.Save();
        gfx.IntersectClip(rect);
        gfx.DrawString(trimmed, fittedFont, brush, new XPoint(rect.X, y));
        gfx.Restore(state);
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
