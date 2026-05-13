using AmazonShipmentTool.Models;
using AmazonShipmentTool.Parsers;
using AmazonShipmentTool.Services;
using AmazonShipmentTool.Validators;
using System.Text;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;

Console.OutputEncoding = Encoding.UTF8;

// 设置字体解析器
PdfFontResolver.EnsureRegistered();

Console.WriteLine("=== PDF 重新生成 ===");

// 文件路径
var pdfPath = @"/home/ubuntu/.hermes/profiles/dev/cache/documents/外面拿回来的PDF_appended.pdf";
var excelPath = @"/home/ubuntu/.hermes/profiles/dev/cache/documents/doc_b269e002f18e_750969833.xlsx";
var outputPath = @"/home/ubuntu/.hermes/profiles/dev/cache/documents/外面拿回来的PDF_重新生成修复版.pdf";

try
{
    Console.WriteLine("1. 解析原始PDF...");
    var pdfParser = new PdfParser();
    pdfParser.Parse(pdfPath);
    
    Console.WriteLine($"   Appointment ID: {pdfParser.AppointmentInfo.AppointmentId}");
    Console.WriteLine($"   原始数据行数: {pdfParser.ShipmentRows.Count}");

    Console.WriteLine("2. 解析Excel数据...");
    var excelParser = new ExcelParser();
    var excelRows = excelParser.Parse(excelPath);
    Console.WriteLine($"   Excel数据行数: {excelRows.Count}");

    Console.WriteLine("3. 验证数据...");
    var validator = new ShipmentValidator();
    var validation = validator.Validate(pdfParser.ShipmentRows, excelRows);
    
    Console.WriteLine($"   错误数: {validation.Errors.Count}");
    Console.WriteLine($"   警告数: {validation.Warnings.Count}");
    
    if (validation.Errors.Count > 0)
    {
        Console.WriteLine("   错误列表:");
        foreach (var err in validation.Errors)
        {
            Console.WriteLine($"     - {err}");
        }
    }

    Console.WriteLine("4. 合并数据...");
    var merger = new DataMerger();
    var mergedRows = merger.Merge(pdfParser.ShipmentRows, excelRows);
    Console.WriteLine($"   合并后总行数: {mergedRows.Count}");

    Console.WriteLine("5. 生成新PDF...");
    var appendService = new PdfAppendService();
    appendService.AppendRowsToPdf(
        originalPdfPath: pdfPath,
        originalRows: pdfParser.ShipmentRows,
        rowsToAppend: excelRows,
        outputPath: outputPath
    );

    Console.WriteLine($"6. 完成! 新PDF已生成: {outputPath}");

    // 验证生成结果
    Console.WriteLine("7. 验证生成结果...");
    var outputDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.ReadOnly);
    Console.WriteLine($"   输出PDF页数: {outputDoc.PageCount}");
    
    // 尝试解析生成的新PDF
    var newParser = new PdfParser();
    newParser.Parse(outputPath);
    Console.WriteLine($"   新PDF数据行数: {newParser.ShipmentRows.Count}");

    Console.WriteLine("✅ PDF重新生成完成!");

}
catch (Exception ex)
{
    Console.WriteLine($"❌ 错误: {ex.Message}");
    Console.WriteLine($"详细: {ex.StackTrace}");
}