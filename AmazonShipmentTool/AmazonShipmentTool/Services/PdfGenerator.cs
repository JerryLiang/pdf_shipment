using System.Diagnostics;
using System.Reflection;
using System.Text;
using AmazonShipmentTool.Models;
using Microsoft.Web.WebView2.WinForms;

namespace AmazonShipmentTool.Services;

public class PdfGenerator : IDisposable
{
    private WebView2? _webView;
    private bool _disposed;

    public async Task InitializeAsync()
    {
        _webView = new WebView2();
        var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync();
        await _webView.EnsureCoreWebView2Async(env);
    }

    public async Task GeneratePdfAsync(
        AppointmentInfo appointment,
        List<ShipmentRow> rows,
        string outputPath)
    {
        if (_webView == null)
            throw new InvalidOperationException("PdfGenerator not initialized. Call InitializeAsync first.");

        var html = GenerateHtml(appointment, rows);
        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"amazon_shipment_{Guid.NewGuid():N}.html");

        try
        {
            await File.WriteAllTextAsync(tempHtmlPath, html, Encoding.UTF8);

            var tcs = new TaskCompletionSource<bool>();
            _webView.CoreWebView2.NavigationCompleted += (s, e) => tcs.TrySetResult(e.IsSuccess);
            _webView.CoreWebView2.Navigate(new Uri(tempHtmlPath).AbsoluteUri);
            await tcs.Task;

            await Task.Delay(1000);

            var settings = _webView.CoreWebView2.Environment.CreatePrintSettings();
            settings.Orientation = Microsoft.Web.WebView2.Core.CoreWebView2PrintOrientation.Portrait;
            settings.ShouldPrintBackgrounds = true;
            settings.ScaleFactor = 1.0;

            var printResult = await _webView.CoreWebView2.PrintToPdfAsync(outputPath, settings);
        }
        finally
        {
            try { File.Delete(tempHtmlPath); } catch { }
        }
    }

    private string GenerateHtml(AppointmentInfo appointment, List<ShipmentRow> rows)
    {
        var template = GetEmbeddedTemplate();
        var tableRows = GenerateTableRows(rows);

        var estimatedPages = EstimatePages(rows.Count);

        var html = template
            .Replace("{{APPOINTMENT_ID}}", EscapeHtml(appointment.AppointmentId))
            .Replace("{{APPOINTMENT_REF_CODE}}", EscapeHtml(appointment.AppointmentReferenceCode))
            .Replace("{{DESTINATION_FC}}", EscapeHtml(appointment.DestinationFC))
            .Replace("{{CARRIER_SCAC}}", EscapeHtml(appointment.CarrierSCAC))
            .Replace("{{STATUS}}", EscapeHtml(appointment.Status))
            .Replace("{{TRAILER_NUMBER}}", EscapeHtml(appointment.TrailerNumber))
            .Replace("{{APPOINTMENT_TYPE}}", EscapeHtml(appointment.AppointmentType))
            .Replace("{{FREIGHT_TYPE}}", EscapeHtml(appointment.FreightType))
            .Replace("{{LOAD_TYPE}}", EscapeHtml(appointment.LoadType))
            .Replace("{{SCHEDULED_TIME}}", EscapeHtml(appointment.ScheduledTime))
            .Replace("{{CHECKED_IN_TIME}}", EscapeHtml(appointment.CheckedInTime))
            .Replace("{{COMPLETED_TIME}}", EscapeHtml(appointment.CompletedTime))
            .Replace("{{APPOINTMENT_CREATION_DATE}}", EscapeHtml(appointment.AppointmentCreationDate))
            .Replace("{{EARLIEST_ARRIVAL_TIME}}", EscapeHtml(appointment.EarliestArrivalTime))
            .Replace("{{ARRIVAL_TIME}}", EscapeHtml(appointment.ArrivalTime))
            .Replace("{{IS_FREIGHT_CLAMPABLE}}", EscapeHtml(appointment.IsFreightClampable))
            .Replace("{{TABLE_ROWS}}", tableRows)
            .Replace("{{TOTAL_ROWS}}", rows.Count.ToString());

        return html;
    }

    private string GenerateTableRows(List<ShipmentRow> rows)
    {
        var sb = new StringBuilder();

        foreach (var row in rows)
        {
            var rowClass = row.IsAddedFromExcel ? "added-row" : "";
            sb.AppendLine($@"<tr class=""{rowClass}"">
<td class=""col-index"">{row.Index}</td>
<td class=""col-arn"">{EscapeHtml(row.Arn)}</td>
<td class=""col-pro"">{EscapeHtml(row.CarrierReferenceNumber)}</td>
<td class=""col-bol"">{EscapeHtml(row.BolOrVendorReference)}</td>
<td class=""col-vendor"">{EscapeHtml(row.VendorName)}</td>
<td class=""col-pallet"">{row.PalletCount}</td>
<td class=""col-carton"">{row.CartonCount}</td>
<td class=""col-unit"">{(row.UnitCount.HasValue ? row.UnitCount.Value.ToString() : "")}</td>
<td class=""col-po"">{EscapeHtml(row.PoList)}</td>
</tr>");
        }

        return sb.ToString();
    }

    private string GetEmbeddedTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("AmazonShipmentTool.Templates.appointment.html");
        if (stream == null)
            throw new InvalidOperationException("Embedded template 'appointment.html' not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int EstimatePages(int totalRows)
    {
        int firstPageRows = 22;
        int subsequentPageRows = 50;

        if (totalRows <= firstPageRows)
            return 1;

        int remaining = totalRows - firstPageRows;
        return 1 + (int)Math.Ceiling((double)remaining / subsequentPageRows);
    }

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _webView?.Dispose();
            _webView = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
