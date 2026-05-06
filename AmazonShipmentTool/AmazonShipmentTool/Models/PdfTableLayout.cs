namespace AmazonShipmentTool.Models;

public sealed class PdfTableLayout
{
    public int PageIndex { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double TableLeft { get; set; } = 16.75;
    public double TableRight { get; set; } = 579.0;
    public double HeaderTop { get; set; } = 454.0;
    public double HeaderHeight { get; set; } = 24.0;
    public double FirstDataRowTop { get; set; } = 479.0;
    public double LastDataRowTop { get; set; } = 479.0;
    public double RowHeight { get; set; } = 15.814;
    public double BottomMargin { get; set; } = 820.0;

    public double NextRowTop => LastDataRowTop + RowHeight;

    public double[] ColumnLefts { get; set; } =
    {
        18.0,   // row number
        49.0,   // ARN
        72.0,   // PRO / Carrier Reference Number
        161.0,  // BOL / Vendor Reference
        315.0,  // Vendor Name
        359.0,  // Pallet Count
        408.0,  // Carton Count
        458.0,  // Unit Count
        503.0   // PO List
    };

    public double[] ColumnRights { get; set; } =
    {
        49.0,
        72.0,
        161.0,
        315.0,
        359.0,
        408.0,
        458.0,
        503.0,
        579.0
    };
}
