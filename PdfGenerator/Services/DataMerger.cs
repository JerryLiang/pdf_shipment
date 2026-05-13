using AmazonShipmentTool.Models;

namespace AmazonShipmentTool.Services;

public class DataMerger
{
    public List<ShipmentRow> Merge(List<ShipmentRow> originalRows, List<ShipmentRow> newRows)
    {
        var result = new List<ShipmentRow>();

        int maxIndex = originalRows.Count > 0 ? originalRows.Max(r => r.Index) : 0;

        foreach (var row in originalRows)
        {
            result.Add(new ShipmentRow
            {
                Index = row.Index,
                Arn = row.Arn,
                CarrierReferenceNumber = row.CarrierReferenceNumber,
                BolOrVendorReference = row.BolOrVendorReference,
                VendorName = row.VendorName,
                PalletCount = row.PalletCount,
                CartonCount = row.CartonCount,
                UnitCount = row.UnitCount,
                PoList = row.PoList,
                IsAddedFromExcel = false
            });
        }

        for (int i = 0; i < newRows.Count; i++)
        {
            var newRow = newRows[i];
            result.Add(new ShipmentRow
            {
                Index = maxIndex + i + 1,
                Arn = newRow.Arn,
                CarrierReferenceNumber = newRow.CarrierReferenceNumber,
                BolOrVendorReference = newRow.BolOrVendorReference,
                VendorName = newRow.VendorName,
                PalletCount = newRow.PalletCount,
                CartonCount = newRow.CartonCount,
                UnitCount = newRow.UnitCount,
                PoList = newRow.PoList,
                IsAddedFromExcel = true
            });
        }

        return result;
    }

    public int EstimatePageCount(int totalRows)
    {
        int firstPageRows = 22;
        int subsequentPageRows = 50;

        if (totalRows <= firstPageRows)
            return 1;

        int remaining = totalRows - firstPageRows;
        return 1 + (int)Math.Ceiling((double)remaining / subsequentPageRows);
    }
}
