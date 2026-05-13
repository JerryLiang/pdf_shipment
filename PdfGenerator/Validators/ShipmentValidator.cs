using AmazonShipmentTool.Models;

namespace AmazonShipmentTool.Validators;

public class ShipmentValidator
{
    public ValidationResult Validate(List<ShipmentRow> originalRows, List<ShipmentRow> newRows)
    {
        var result = new ValidationResult();

        ValidateOriginalRows(originalRows, result);
        ValidateNewRows(newRows, result);
        CheckDuplicateArns(newRows, result);

        return result;
    }

    private static void ValidateOriginalRows(List<ShipmentRow> rows, ValidationResult result)
    {
        if (rows.Count == 0)
        {
            result.Warnings.Add("No shipment rows found in original PDF; treating source as header-only.");
        }
    }

    private static void ValidateNewRows(List<ShipmentRow> rows, ValidationResult result)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNum = i + 1;

            if (string.IsNullOrWhiteSpace(row.CarrierReferenceNumber))
            {
                result.Warnings.Add($"Excel row {rowNum}: PRO/Carrier Reference Number is empty.");
            }

            if (row.CartonCount <= 0)
            {
                result.Errors.Add($"Excel row {rowNum}: Carton Count must be greater than 0 (current: {row.CartonCount}).");
            }

            if (row.PalletCount < 0)
            {
                result.Errors.Add($"Excel row {rowNum}: Pallet Count cannot be negative (current: {row.PalletCount}).");
            }

            if (string.IsNullOrWhiteSpace(row.PoList))
            {
                result.Warnings.Add($"Excel row {rowNum}: PO List is empty.");
            }
        }
    }

    private static void CheckDuplicateArns(List<ShipmentRow> newRows, ValidationResult result)
    {
        var duplicateArns = newRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Arn))
            .GroupBy(r => r.Arn)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var arn in duplicateArns)
        {
            result.Errors.Add($"Duplicate ARN found in Excel data: {arn}");
        }
    }
}
