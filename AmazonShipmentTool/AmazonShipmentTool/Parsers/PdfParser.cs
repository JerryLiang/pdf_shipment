using System.Text;
using System.Text.RegularExpressions;
using AmazonShipmentTool.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AmazonShipmentTool.Parsers;

public class PdfParser
{
    private static readonly double[] ColBoundaries = { 50, 72, 161, 315, 359, 408, 458, 503 };
    private static readonly string[] ColNames = { "Index", "ARN", "PRO", "BOL", "VendorName", "PalletCount", "CartonCount", "UnitCount", "POList" };

    public AppointmentInfo AppointmentInfo { get; private set; } = new();
    public List<ShipmentRow> ShipmentRows { get; private set; } = new();

    public void Parse(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);

        var allText = new StringBuilder();
        var allLettersByPage = new List<List<Letter>>();

        foreach (var page in document.GetPages())
        {
            var letters = page.Letters.ToList();
            allLettersByPage.Add(letters);

            var lines = GroupLettersIntoLines(letters, tolerance: 2.0)
                .Select(orderedLetters => BuildLineTextWithSpaces(orderedLetters))
                .Where(t => !string.IsNullOrWhiteSpace(t));
            foreach (var w in lines)
                allText.AppendLine(w);
        }

        ParseAppointmentInfo(allText.ToString());
        ParseShipmentTable(allLettersByPage);
    }

    private void ParseAppointmentInfo(string text)
    {
        AppointmentInfo.AppointmentId = ExtractField(text, @"Appointment ID:\s*(\d+)")
            ?? ExtractField(text, @"Appointment ID\s+(\d+)") ?? string.Empty;

        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.Contains("GYR3-") && line.Contains("HJLY"))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    AppointmentInfo.AppointmentReferenceCode = parts[0];
                    AppointmentInfo.DestinationFC = parts[1];
                    AppointmentInfo.CarrierSCAC = parts[2];
                    AppointmentInfo.Status = parts[3];
                }
                if (parts.Length >= 7)
                {
                    AppointmentInfo.EarliestArrivalTime = string.Join(" ", parts.Skip(4).Take(2));
                }
                if (parts.Length >= 9)
                {
                    AppointmentInfo.ArrivalTime = string.Join(" ", parts.Skip(6));
                }
            }

            if (line.Contains("Live Load") && line.Contains("Truck"))
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                AppointmentInfo.AppointmentType = parts.Length > 0 ? parts[0] + " " + parts[1] : "";
                AppointmentInfo.FreightType = parts.Length > 2 ? parts[2] + " " + parts[3] : "";

                var loadParts = new List<string>();
                for (int j = 4; j < parts.Length; j++)
                {
                    if (Regex.IsMatch(parts[j], @"^\d{2}/\d{2}/\d{4}$") || Regex.IsMatch(parts[j], @"^\d{2}:\d{2}$") || parts[j] == "MST" || parts[j] == "EST" || parts[j] == "PST" || parts[j] == "CST")
                        break;
                    loadParts.Add(parts[j]);
                }
                AppointmentInfo.LoadType = string.Join(" ", loadParts);
            }

            if (line.StartsWith("No ") && line.Length > 3)
            {
                AppointmentInfo.IsFreightClampable = "No";
                var remainder = line.Substring(3).Trim();
                if (!string.IsNullOrEmpty(remainder))
                {
                    var firstToken = remainder.Split(' ')[0];
                    if (int.TryParse(firstToken, out _))
                        AppointmentInfo.TrailerNumber = firstToken;
                }
                else if (i + 1 < lines.Count)
                {
                    var nextLine = lines[i + 1];
                    if (int.TryParse(nextLine.Split(' ')[0], out _))
                        AppointmentInfo.TrailerNumber = nextLine.Split(' ')[0];
                }
            }

            if (Regex.IsMatch(line, @"\bScheduled\b") && !line.Contains("Scheduled Time") && !line.Contains("Earliest"))
            {
                var dateMatch = Regex.Match(line, @"(\d{2}/\d{2}/\d{4})");
                if (dateMatch.Success)
                {
                    var datePart = dateMatch.Groups[1].Value;
                    var timePart = ExtractTimeFromCurrentOrNextLine(lines, i);
                    AppointmentInfo.ScheduledTime = string.IsNullOrEmpty(timePart) ? datePart : $"{datePart} {timePart}";
                }
            }

            if (line.Contains("Checked In") || line.Contains("CheckedIn"))
            {
                var dateMatch = Regex.Match(line, @"(\d{2}/\d{2}/\d{4})");
                if (dateMatch.Success)
                {
                    var datePart = dateMatch.Groups[1].Value;
                    var timePart = ExtractTimeFromCurrentOrNextLine(lines, i);
                    AppointmentInfo.CheckedInTime = string.IsNullOrEmpty(timePart) ? datePart : $"{datePart} {timePart}";
                }
            }

            if (line.Contains("Completed") && !line.Contains("Checked In"))
            {
                var dateMatch = Regex.Match(line, @"(\d{2}/\d{2}/\d{4})");
                if (dateMatch.Success)
                {
                    var datePart = dateMatch.Groups[1].Value;
                    var timePart = ExtractTimeFromNextLinesOnly(lines, i);
                    AppointmentInfo.CompletedTime = string.IsNullOrEmpty(timePart) ? datePart : $"{datePart} {timePart}";
                }
            }

            if (line.Contains("Appointment creation date") || line.Contains("Appointment creation"))
            {
                var m = Regex.Match(line, @"(\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}\s+\w+)");
                if (m.Success)
                {
                    AppointmentInfo.AppointmentCreationDate = m.Groups[1].Value;
                }
                else
                {
                    var datePart = ExtractTimeFromNextLines(lines, i);
                    if (!string.IsNullOrEmpty(datePart))
                        AppointmentInfo.AppointmentCreationDate = datePart;
                }
            }
        }

        if (string.IsNullOrEmpty(AppointmentInfo.TrailerNumber))
        {
            AppointmentInfo.TrailerNumber = ExtractField(text, @"Trailer Number\s+(\S+)") ?? "";
        }
    }

    private static string ExtractTimeFromCurrentOrNextLine(List<string> lines, int currentIdx)
    {
        var timePattern = @"(\d{2}:\d{2}\s+\w+)";
        var m = Regex.Match(lines[currentIdx], timePattern);
        if (m.Success) return m.Groups[1].Value;

        for (int j = currentIdx + 1; j < Math.Min(currentIdx + 3, lines.Count); j++)
        {
            m = Regex.Match(lines[j], timePattern);
            if (m.Success) return m.Groups[1].Value;
        }
        return string.Empty;
    }

    private static string ExtractTimeFromNextLines(List<string> lines, int currentIdx)
    {
        var pattern = @"(\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}\s+\w+)";
        for (int j = currentIdx + 1; j < Math.Min(currentIdx + 3, lines.Count); j++)
        {
            var m = Regex.Match(lines[j], pattern);
            if (m.Success) return m.Groups[1].Value;
        }
        return string.Empty;
    }

    private static string ExtractTimeFromNextLinesOnly(List<string> lines, int currentIdx)
    {
        var timePattern = @"(\d{2}:\d{2}\s+\w+)";
        for (int j = currentIdx + 1; j < Math.Min(currentIdx + 3, lines.Count); j++)
        {
            var m = Regex.Match(lines[j], timePattern);
            if (m.Success) return m.Groups[1].Value;
        }
        return string.Empty;
    }

    private void ParseShipmentTable(List<List<Letter>> allLettersByPage)
    {
        var tableLinesByPage = new List<List<TableLine>>();
        bool foundHeader = false;
        bool headerRowSkipped = false;

        foreach (var letters in allLettersByPage)
        {
            var pageLines = new List<TableLine>();
            var lines = GroupLettersIntoLines(letters, tolerance: 2.0);

            foreach (var line in lines)
            {
                var orderedLetters = line.OrderBy(l => l.Location.X).ToList();
                if (orderedLetters.Count == 0) continue;

                var lineText = string.Join("", orderedLetters.Select(l => l.Value)).Trim();

                if (!foundHeader)
                {
                    if (lineText.Contains("Shipment Information"))
                        foundHeader = true;
                    continue;
                }

                if (!headerRowSkipped)
                {
                    if (lineText.Contains("PRO/Carrier") || lineText.Contains("Pallet Count") ||
                        lineText.Contains("BOL/Vendor") || lineText.Contains("ARN") ||
                        lineText.Contains("Vendor") || lineText.Contains("PO List") ||
                        lineText.Contains("separator") || lineText.Contains("Number List") ||
                        lineText.Contains("Carton Count") || lineText.Contains("Unit Count"))
                        continue;
                    headerRowSkipped = true;
                }

                pageLines.Add(new TableLine(orderedLetters, orderedLetters[0].Location.X, orderedLetters.Max(l => l.Location.Y), lineText));
            }

            tableLinesByPage.Add(pageLines);
        }

        var dataRows = new List<Dictionary<string, string>>();

        foreach (var pageLines in tableLinesByPage)
        {
            var rowStarts = pageLines
                .Where(IsRowStart)
                .OrderByDescending(l => l.Y)
                .ToList();

            var typicalRowGap = EstimateTypicalRowGap(rowStarts);

            for (int i = 0; i < rowStarts.Count; i++)
            {
                var rowLine = rowStarts[i];
                var rowData = ExtractColumnsFromLetters(rowLine.Letters);

                var previousGap = i == 0 ? typicalRowGap : rowStarts[i - 1].Y - rowLine.Y;
                var nextGap = i == rowStarts.Count - 1 ? typicalRowGap : rowLine.Y - rowStarts[i + 1].Y;
                var upperBoundary = rowLine.Y + previousGap / 2.0;
                var lowerBoundary = rowLine.Y - nextGap / 2.0;

                var continuationLines = pageLines
                    .Where(line => !ReferenceEquals(line, rowLine) && !IsRowStart(line))
                    .Where(line => line.Y < upperBoundary && line.Y > lowerBoundary)
                    .OrderByDescending(line => line.Y)
                    .ToList();

                foreach (var continuationLine in continuationLines)
                    AppendContinuationToRow(rowData, continuationLine);

                dataRows.Add(rowData);
            }
        }

        ShipmentRows = BuildShipmentRows(dataRows);
    }

    private static bool IsRowStart(TableLine line)
    {
        return line.StartX < 40 &&
               !string.IsNullOrEmpty(line.Text) &&
               char.IsDigit(line.Text[0]);
    }

    private static double EstimateTypicalRowGap(IReadOnlyList<TableLine> rowStarts)
    {
        var gaps = rowStarts
            .Zip(rowStarts.Skip(1), (current, next) => current.Y - next.Y)
            .Where(gap => gap > 8 && gap < 40)
            .OrderBy(gap => gap)
            .ToList();

        if (gaps.Count == 0)
            return 16.0;

        return gaps[gaps.Count / 2];
    }

    private static void AppendContinuationToRow(Dictionary<string, string> rowData, TableLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text)) return;

        var targetColumn = line.StartX switch
        {
            >= 72 and < 161 => "PRO",
            >= 161 and < 315 => "BOL",
            >= 315 and < 359 => "VendorName",
            >= 503 => "POList",
            _ => null
        };

        if (targetColumn == null) return;

        if (!string.IsNullOrWhiteSpace(rowData[targetColumn]))
            rowData[targetColumn] += " " + line.Text;
        else
            rowData[targetColumn] = line.Text;
    }

    private List<List<Letter>> GroupLettersIntoLines(List<Letter> letters, double tolerance)
    {
        if (letters.Count == 0) return new List<List<Letter>>();

        var sorted = letters.OrderBy(l => l.Location.Y).ToList();
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
                if (currentLine.Count > 0)
                    lines.Add(currentLine);
                currentLine = new List<Letter> { sorted[i] };
                currentY = sorted[i].Location.Y;
            }
        }

        if (currentLine.Count > 0)
            lines.Add(currentLine);

        lines.Reverse();
        return lines;
    }

    private string BuildLineTextWithSpaces(List<Letter> line)
    {
        var ordered = line.OrderBy(l => l.Location.X).ToList();
        if (ordered.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.Append(ordered[0].Value);

        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var curr = ordered[i];
            var gap = curr.Location.X - (prev.Location.X + prev.Width);

            if (gap > 1.5)
                sb.Append(' ');

            sb.Append(curr.Value);
        }

        return sb.ToString().Trim();
    }

    private Dictionary<string, string> ExtractColumnsFromLetters(List<Letter> letters)
    {
        var columns = new Dictionary<string, string>();
        foreach (var colName in ColNames)
            columns[colName] = string.Empty;

        var colLetters = new Dictionary<string, List<char>>();
        foreach (var colName in ColNames)
            colLetters[colName] = new List<char>();

        foreach (var letter in letters)
        {
            var x = letter.Location.X;
            var colIdx = GetColumnIndex(x);
            var colName = ColNames[colIdx];

            if (letter.Value.Length > 0)
                colLetters[colName].Add(letter.Value[0]);
        }

        foreach (var kvp in colLetters)
        {
            var val = new string(kvp.Value.ToArray()).Trim();
            if (kvp.Key == "POList" && val.StartsWith(","))
                val = val.TrimStart(',').Trim();
            if (kvp.Key == "POList" && val.StartsWith(" "))
                val = val.TrimStart();
            columns[kvp.Key] = val;
        }

        return columns;
    }

    private int GetColumnIndex(double x)
    {
        for (int i = 0; i < ColBoundaries.Length; i++)
        {
            if (x < ColBoundaries[i])
                return i;
        }
        return ColBoundaries.Length;
    }

    private List<ShipmentRow> BuildShipmentRows(List<Dictionary<string, string>> dataRows)
    {
        var rows = new List<ShipmentRow>();

        foreach (var rowData in dataRows)
        {
            var row = new ShipmentRow
            {
                IsAddedFromExcel = false
            };

            if (int.TryParse(rowData["Index"], out var idx))
                row.Index = idx;

            row.Arn = rowData["ARN"];
            row.CarrierReferenceNumber = rowData["PRO"];
            row.BolOrVendorReference = rowData["BOL"];
            row.VendorName = rowData["VendorName"];

            if (int.TryParse(rowData["PalletCount"], out var pc))
                row.PalletCount = pc;

            if (int.TryParse(rowData["CartonCount"], out var cc))
                row.CartonCount = cc;

            if (int.TryParse(rowData["UnitCount"], out var uc))
                row.UnitCount = uc;

            row.PoList = rowData["POList"];

            rows.Add(row);
        }

        return rows;
    }

    private sealed record TableLine(List<Letter> Letters, double StartX, double Y, string Text);

    private static string? ExtractField(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
