using System.Text;
using AmazonShipmentTool.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace AmazonShipmentTool.Services;

public class PdfAppender : IDisposable
{
    private const float RowHeight = 15.81f;
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float TableLeft = 24f;
    private const float TableRight = 556f;
    private const float TopStartY = 817f;
    private const float BottomLimitY = 35f;

    private static readonly float[] VerticalLines = { 24f, 50f, 72f, 161f, 315f, 359f, 408f, 458f, 503f, 556f };

    private static readonly string[] HeaderTexts = {
        "#",
        "ARN",
        "PRO/Carrier Reference Number",
        "BOL/Vendor or Seller Reference\nNumber List (use , as separator)",
        "Vendor Name",
        "Pallet\nCount",
        "Carton\nCount",
        "Unit\nCount",
        "PO List (use , as separator) *"
    };

    private static readonly BaseColor Black = new BaseColor(0, 0, 0);
    private static readonly BaseColor HeaderBg = new BaseColor(232, 232, 232);

    private BaseFont? _dataFont;
    private BaseFont? _indexFont;
    private BaseFont? _headerFont;

    public async Task AppendRowsAsync(string originalPdfPath, List<ShipmentRow> newRows, string outputPath)
    {
        await Task.Run(() => AppendRows(originalPdfPath, newRows, outputPath));
    }

    private void AppendRows(string originalPdfPath, List<ShipmentRow> newRows, string outputPath)
    {
        var (lastPageNum, lastRowY, lastRowIndex) = AnalyzeLastRow(originalPdfPath);

        _dataFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
        _indexFont = BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
        _headerFont = BaseFont.CreateFont(BaseFont.HELVETICA_BOLD, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);

        var originalBytes = File.ReadAllBytes(originalPdfPath);
        using var reader = new PdfReader(originalBytes);
        using var ms = new MemoryStream();
        var stamper = new PdfStamper(reader, ms);
        stamper.Writer.CloseStream = false;

        float currentY = lastRowY - RowHeight;
        int currentPage = lastPageNum;
        int currentIndex = lastRowIndex + 1;

        foreach (var row in newRows)
        {
            if (currentY < BottomLimitY)
            {
                currentPage++;
                stamper.InsertPage(currentPage, new iTextSharp.text.Rectangle(PageWidth, PageHeight));
                currentY = TopStartY;
                DrawHeader(stamper.GetOverContent(currentPage), currentY);
                currentY -= RowHeight;
            }

            var cb = stamper.GetOverContent(currentPage);
            DrawRowBorders(cb, currentY);
            DrawRowText(cb, row, currentIndex, currentY);

            currentY -= RowHeight;
            currentIndex++;
        }

        stamper.Close();
        reader.Close();

        File.WriteAllBytes(outputPath, ms.ToArray());
    }

    private (int pageNum, float lastRowY, int lastRowIndex) AnalyzeLastRow(string pdfPath)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var lastPageNum = doc.NumberOfPages;
        var page = doc.GetPage(lastPageNum);

        var letters = page.Letters.ToList();
        var lineGroups = GroupLettersIntoLines(letters);

        var dataRows = new List<(float Y, string Text)>();

        foreach (var line in lineGroups)
        {
            var ordered = line.OrderBy(l => l.Location.X).ToList();
            if (ordered.Count == 0) continue;

            var firstX = ordered[0].Location.X;
            var firstChar = ordered[0].Value;

            if (firstX < 40 && firstChar.Length > 0 && char.IsDigit(firstChar[0]))
            {
                var text = string.Join("", ordered.Select(l => l.Value));
                dataRows.Add(((float)ordered[0].Location.Y, text));
            }
        }

        if (dataRows.Count == 0)
            return (lastPageNum, TopStartY, 0);

        var lastRow = dataRows.OrderBy(r => r.Y).First();
        var indexStr = lastRow.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0";
        int.TryParse(indexStr, out var index);

        return (lastPageNum, lastRow.Y, index);
    }

    private List<List<UglyToad.PdfPig.Content.Letter>> GroupLettersIntoLines(List<UglyToad.PdfPig.Content.Letter> letters)
    {
        if (letters.Count == 0) return new List<List<UglyToad.PdfPig.Content.Letter>>();

        var sorted = letters.OrderBy(l => l.Location.Y).ToList();
        var lines = new List<List<UglyToad.PdfPig.Content.Letter>>();
        var currentLine = new List<UglyToad.PdfPig.Content.Letter> { sorted[0] };
        var currentY = sorted[0].Location.Y;

        for (int i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].Location.Y - currentY) <= 2.0)
            {
                currentLine.Add(sorted[i]);
            }
            else
            {
                if (currentLine.Count > 0)
                    lines.Add(currentLine);
                currentLine = new List<UglyToad.PdfPig.Content.Letter> { sorted[i] };
                currentY = sorted[i].Location.Y;
            }
        }

        if (currentLine.Count > 0)
            lines.Add(currentLine);

        return lines;
    }

    private void DrawRowBorders(PdfContentByte cb, float y)
    {
        cb.SetLineWidth(0.5f);
        cb.SetColorStroke(Black);

        cb.MoveTo(TableLeft, y + RowHeight);
        cb.LineTo(TableRight, y + RowHeight);
        cb.Stroke();

        foreach (var x in VerticalLines)
        {
            cb.MoveTo(x, y);
            cb.LineTo(x, y + RowHeight);
            cb.Stroke();
        }
    }

    private void DrawRowText(PdfContentByte cb, ShipmentRow row, int index, float y)
    {
        float textY = y + 4;

        cb.BeginText();
        cb.SetColorFill(Black);

        cb.SetFontAndSize(_indexFont!, 11);
        cb.ShowTextAligned(PdfContentByte.ALIGN_LEFT, index.ToString(), 26, textY, 0);

        cb.SetFontAndSize(_dataFont!, 9);

        if (!string.IsNullOrEmpty(row.Arn))
            cb.ShowTextAligned(PdfContentByte.ALIGN_LEFT, TruncateText(row.Arn, 18), 52, textY, 0);

        if (!string.IsNullOrEmpty(row.CarrierReferenceNumber))
            cb.ShowTextAligned(PdfContentByte.ALIGN_LEFT, TruncateText(row.CarrierReferenceNumber, 28), 74, textY, 0);

        if (!string.IsNullOrEmpty(row.BolOrVendorReference))
            cb.ShowTextAligned(PdfContentByte.ALIGN_LEFT, TruncateText(row.BolOrVendorReference, 50), 163, textY, 0);

        if (!string.IsNullOrEmpty(row.VendorName))
            cb.ShowTextAligned(PdfContentByte.ALIGN_LEFT, TruncateText(row.VendorName, 14), 317, textY, 0);

        if (row.PalletCount > 0)
            cb.ShowTextAligned(PdfContentByte.ALIGN_CENTER, row.PalletCount.ToString(), (359f + 408f) / 2, textY, 0);

        if (row.CartonCount > 0)
            cb.ShowTextAligned(PdfContentByte.ALIGN_CENTER, row.CartonCount.ToString(), (408f + 458f) / 2, textY, 0);

        if (row.UnitCount.HasValue && row.UnitCount.Value > 0)
            cb.ShowTextAligned(PdfContentByte.ALIGN_CENTER, row.UnitCount.Value.ToString(), (458f + 503f) / 2, textY, 0);

        if (!string.IsNullOrEmpty(row.PoList))
            cb.ShowTextAligned(PdfContentByte.ALIGN_LEFT, TruncateText(row.PoList, 28), 505, textY, 0);

        cb.EndText();
    }

    private void DrawHeader(PdfContentByte cb, float y)
    {
        cb.SetColorFill(HeaderBg);
        cb.SetColorStroke(Black);
        cb.Rectangle(TableLeft, y, TableRight - TableLeft, RowHeight * 2);
        cb.FillStroke();

        DrawRowBorders(cb, y);

        cb.SetLineWidth(0.5f);
        cb.MoveTo(TableLeft, y);
        cb.LineTo(TableRight, y);
        cb.Stroke();

        cb.BeginText();
        cb.SetFontAndSize(_headerFont!, 7);
        cb.SetColorFill(Black);

        for (int i = 0; i < HeaderTexts.Length; i++)
        {
            float left = VerticalLines[i] + 2;
            float right = VerticalLines[i + 1] - 2;
            float centerX = (left + right) / 2;

            var lines = HeaderTexts[i].Split('\n');
            for (int j = 0; j < lines.Length; j++)
            {
                float lineY = y + RowHeight * 2 - 8 - j * 8;
                cb.ShowTextAligned(PdfContentByte.ALIGN_CENTER, lines[j], centerX, lineY, 0);
            }
        }

        cb.EndText();
    }

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLen ? text : text[..maxLen];
    }

    public void Dispose()
    {
    }
}
