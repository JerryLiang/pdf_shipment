using AmazonShipmentTool.Models;
using ClosedXML.Excel;

namespace AmazonShipmentTool.Parsers;

public class ExcelParser
{
    private static readonly Dictionary<string, string> HeaderMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ARN"] = "Arn",
        ["PRO"] = "CarrierReferenceNumber",
        ["BOL"] = "BolOrVendorReference",
        ["BOL List (use , as separator)"] = "BolOrVendorReference",
        ["Vendor Name"] = "VendorName",
        ["Pallet Count"] = "PalletCount",
        ["Carton Count"] = "CartonCount",
        ["Unit Count"] = "UnitCount",
        ["PO List"] = "PoList",
        ["PO List (use , as separator) *"] = "PoList"
    };

    public List<ShipmentRow> Parse(string excelPath)
    {
        var rows = new List<ShipmentRow>();

        using var workbook = new XLWorkbook(excelPath);
        var worksheet = workbook.Worksheets.First();

        var headerRow = worksheet.Row(1);
        var columnMap = BuildColumnMap(headerRow);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (int rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var row = ReadRow(worksheet.Row(rowNum), columnMap);
            if (row != null)
                rows.Add(row);
        }

        return rows;
    }

    private Dictionary<int, string> BuildColumnMap(IXLRow headerRow)
    {
        var map = new Dictionary<int, string>();
        var cells = headerRow.CellsUsed();

        foreach (var cell in cells)
        {
            var headerText = cell.GetString().Trim();
            if (string.IsNullOrEmpty(headerText)) continue;

            foreach (var mapping in HeaderMapping)
            {
                if (headerText.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase) ||
                    mapping.Key.Contains(headerText, StringComparison.OrdinalIgnoreCase))
                {
                    map[cell.Address.ColumnNumber] = mapping.Value;
                    break;
                }
            }
        }

        return map;
    }

    private ShipmentRow? ReadRow(IXLRow row, Dictionary<int, string> columnMap)
    {
        var shipmentRow = new ShipmentRow { IsAddedFromExcel = true };
        bool hasData = false;

        foreach (var mapping in columnMap)
        {
            var cell = row.Cell(mapping.Key);
            var value = cell.GetString().Trim();

            if (string.IsNullOrEmpty(value)) continue;
            hasData = true;

            switch (mapping.Value)
            {
                case "Arn":
                    shipmentRow.Arn = value;
                    break;
                case "CarrierReferenceNumber":
                    shipmentRow.CarrierReferenceNumber = value;
                    break;
                case "BolOrVendorReference":
                    shipmentRow.BolOrVendorReference = value;
                    break;
                case "VendorName":
                    shipmentRow.VendorName = value;
                    break;
                case "PalletCount":
                    if (int.TryParse(value, out var pc))
                        shipmentRow.PalletCount = pc;
                    break;
                case "CartonCount":
                    if (int.TryParse(value, out var cc))
                        shipmentRow.CartonCount = cc;
                    break;
                case "UnitCount":
                    if (int.TryParse(value, out var uc))
                        shipmentRow.UnitCount = uc;
                    break;
                case "PoList":
                    shipmentRow.PoList = value;
                    break;
            }
        }

        return hasData ? shipmentRow : null;
    }
}
