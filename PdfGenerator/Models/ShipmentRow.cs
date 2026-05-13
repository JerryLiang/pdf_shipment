namespace AmazonShipmentTool.Models;

public class ShipmentRow
{
    public int Index { get; set; }
    public string Arn { get; set; } = string.Empty;
    public string CarrierReferenceNumber { get; set; } = string.Empty;
    public string BolOrVendorReference { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public int PalletCount { get; set; }
    public int CartonCount { get; set; }
    public int? UnitCount { get; set; }
    public string PoList { get; set; } = string.Empty;
    public bool IsAddedFromExcel { get; set; }
}
