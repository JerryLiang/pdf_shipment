using AmazonShipmentTool.Models;

namespace AmazonShipmentTool.Validators;

public class ShipmentValidator
{
    public ValidationResult Validate(List<ShipmentRow> originalRows, List<ShipmentRow> newRows)
    {
        var result = new ValidationResult();

        ValidateOriginalRows(originalRows, result);
        ValidateNewRows(newRows, result);
        CheckDuplicateArns(originalRows, newRows, result);

        return result;
    }

    private void ValidateOriginalRows(List<ShipmentRow> rows, ValidationResult result)
    {
        if (rows.Count == 0)
        {
            result.Errors.Add("No shipment rows found in original PDF.");
        }
    }

    private void ValidateNewRows(List<ShipmentRow> rows, ValidationResult result)
    {
        for (int i = 0; i < rows.Count; i++)
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

            if (string.IsNullOrWhiteSpace(row.UnitCount?.ToString()) || row.UnitCount == null)
            {
                result.Warnings.Add($"Excel row {rowNum}: Unit Count is empty.");
            }
        }
    }

    private void CheckDuplicateArns(List<ShipmentRow> originalRows, List<ShipmentRow> newRows, ValidationResult result)
    {
        var arnsInExcel = newRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Arn))
            .Select(r => r.Arn)
            .ToList();

        var duplicateInExcel = arnsInExcel
            .GroupBy(a => a)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var arn in duplicateInExcel)
        {
            result.Errors.Add($"Duplicate ARN found in Excel data: {arn}");
        }
    }
}
