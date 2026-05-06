using AmazonShipmentTool.Parsers;
using AmazonShipmentTool.Services;
using AmazonShipmentTool.Validators;
using System.Text;
Console.OutputEncoding = Encoding.UTF8;

var pdfPath = @"D:\work\amazon_pdf_shipment\GYR3 5.4 440710003997.pdf";
var excelPath = @"D:\work\amazon_pdf_shipment\440710003997.xlsx";

Console.WriteLine("=== PDF Parsing ===");
var pdfParser = new PdfParser();
pdfParser.Parse(pdfPath);

Console.WriteLine($"Appt ID: [{pdfParser.AppointmentInfo.AppointmentId}]");
Console.WriteLine($"RefCode: [{pdfParser.AppointmentInfo.AppointmentReferenceCode}]");
Console.WriteLine($"FC: [{pdfParser.AppointmentInfo.DestinationFC}]");
Console.WriteLine($"SCAC: [{pdfParser.AppointmentInfo.CarrierSCAC}]");
Console.WriteLine($"Status: [{pdfParser.AppointmentInfo.Status}]");
Console.WriteLine($"Trailer: [{pdfParser.AppointmentInfo.TrailerNumber}]");
Console.WriteLine($"Type: [{pdfParser.AppointmentInfo.AppointmentType}]");
Console.WriteLine($"Freight: [{pdfParser.AppointmentInfo.FreightType}]");
Console.WriteLine($"Load: [{pdfParser.AppointmentInfo.LoadType}]");
Console.WriteLine($"Scheduled: [{pdfParser.AppointmentInfo.ScheduledTime}]");
Console.WriteLine($"CheckedIn: [{pdfParser.AppointmentInfo.CheckedInTime}]");
Console.WriteLine($"Completed: [{pdfParser.AppointmentInfo.CompletedTime}]");
Console.WriteLine($"Clampable: [{pdfParser.AppointmentInfo.IsFreightClampable}]");
Console.WriteLine($"CreationDate: [{pdfParser.AppointmentInfo.AppointmentCreationDate}]");

Console.WriteLine($"\nShipment Rows: {pdfParser.ShipmentRows.Count}");

Console.WriteLine("\n=== First 3 rows ===");
for (int i = 0; i < Math.Min(3, pdfParser.ShipmentRows.Count); i++)
{
    var r = pdfParser.ShipmentRows[i];
    Console.WriteLine($"  #{r.Index}: ARN=[{r.Arn}] PRO=[{r.CarrierReferenceNumber}] BOL=[{r.BolOrVendorReference}] Vendor=[{r.VendorName}] P={r.PalletCount} C={r.CartonCount} U={r.UnitCount} PO=[{r.PoList}]");
}

Console.WriteLine("\n=== Last 3 rows ===");
for (int i = Math.Max(0, pdfParser.ShipmentRows.Count - 3); i < pdfParser.ShipmentRows.Count; i++)
{
    var r = pdfParser.ShipmentRows[i];
    Console.WriteLine($"  #{r.Index}: ARN=[{r.Arn}] PRO=[{r.CarrierReferenceNumber}] BOL=[{r.BolOrVendorReference}] Vendor=[{r.VendorName}] P={r.PalletCount} C={r.CartonCount} U={r.UnitCount} PO=[{r.PoList}]");
}

Console.WriteLine("\n=== Verification ===");
Console.WriteLine($"[CRIT 2] Appt ID == 440710003997: {pdfParser.AppointmentInfo.AppointmentId == "440710003997"}");
Console.WriteLine($"[CRIT 3] FC == GYR3: {pdfParser.AppointmentInfo.DestinationFC == "GYR3"}");
Console.WriteLine($"[CRIT 4] Rows == 113: {pdfParser.ShipmentRows.Count == 113} (actual: {pdfParser.ShipmentRows.Count})");

Console.WriteLine("\n=== Excel Parsing ===");
try
{
    var excelParser = new ExcelParser();
    var excelRows = excelParser.Parse(excelPath);
    Console.WriteLine($"[CRIT 5] Excel rows loaded: {excelRows.Count}");
    for (int i = 0; i < Math.Min(3, excelRows.Count); i++)
    {
        var r = excelRows[i];
        Console.WriteLine($"  Excel {i + 1}: ARN=[{r.Arn}] PRO=[{r.CarrierReferenceNumber}] BOL=[{r.BolOrVendorReference}] Vendor=[{r.VendorName}] P={r.PalletCount} C={r.CartonCount} U={r.UnitCount} PO=[{r.PoList}]");
    }

    Console.WriteLine("\n=== Merge ===");
    var merger = new DataMerger();
    var merged = merger.Merge(pdfParser.ShipmentRows, excelRows);
    Console.WriteLine($"[CRIT 6-8] Total merged: {merged.Count}, Original: {pdfParser.ShipmentRows.Count}, New: {excelRows.Count}");
    Console.WriteLine($"  First new row index: {merged[pdfParser.ShipmentRows.Count]?.Index}");
    Console.WriteLine($"  [CRIT 7] Original unchanged: {merged.Take(pdfParser.ShipmentRows.Count).All(r => !r.IsAddedFromExcel)}");
    Console.WriteLine($"  [CRIT 8] New rows from 114: {merged[pdfParser.ShipmentRows.Count]?.Index == 114}");
    Console.WriteLine($"  [CRIT 8] Sequential numbering: {Enumerable.Range(0, merged.Count).All(i => merged[i].Index == i + 1)}");

    Console.WriteLine("\n=== Validation ===");
    var validator = new ShipmentValidator();
    var validation = validator.Validate(pdfParser.ShipmentRows, excelRows);
    Console.WriteLine($"[CRIT 12] Errors: {validation.Errors.Count}, Warnings: {validation.Warnings.Count}");
    foreach (var err in validation.Errors.Take(5))
        Console.WriteLine($"  ERROR: {err}");
    foreach (var warn in validation.Warnings.Take(5))
        Console.WriteLine($"  WARN: {warn}");

    Console.WriteLine("\n=== Page Estimation ===");
    int pages = merger.EstimatePageCount(merged.Count);
    Console.WriteLine($"[CRIT 11] Estimated pages: {pages}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
