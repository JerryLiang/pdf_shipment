namespace AmazonShipmentTool.Models;

public sealed class PdfTableLayout
{
    public int PageIndex { get; set; }
    public double PageWidth { get; set; }
    public double PageHeight { get; set; }
    public double TableLeft { get; set; } = 19.5;
    public double TableRight { get; set; } = 575.8;
    public double HeaderTop { get; set; } = 454.0;
    public double HeaderHeight { get; set; } = 24.0;
    public double FirstDataRowTop { get; set; } = 479.0;
    public double LastDataRowTop { get; set; } = 479.0;
    public double RowHeight { get; set; } = 15.814;
    public double BottomMargin { get; set; } = 820.0;
    public bool HasShipmentTable { get; set; } = true;
    public bool HasShipmentInfoLabel { get; set; } = true;
    public bool HasOriginalDataRows { get; set; } = true;

    public double NextRowTop => LastDataRowTop + RowHeight;
    public double FooterCoverTop => Math.Max(LastDataRowTop, NextRowTop - 4.0);

    public double[] ColumnLefts { get; set; } =
    {
        19.5,   // row number
        43.7,   // ARN
        67.4,   // PRO / Carrier Reference Number
        155.8,  // BOL / Vendor Reference
        309.8,  // Vendor Name
        354.4,  // Pallet Count
        402.3,  // Carton Count
        453.5,  // Unit Count
        498.1   // PO List
    };

    public double[] ColumnRights { get; set; } =
    {
        43.7,
        67.4,
        155.8,
        309.8,
        354.4,
        402.3,
        453.5,
        498.1,
        575.8
    };

    public void NormalizeToA4Portrait()
    {
        PageWidth = 594.96;
        PageHeight = 841.92;
        TableRight = 575.8;
        BottomMargin = 820.0;
    }

    public void OffsetVertical(double offset)
    {
        if (Math.Abs(offset) < 0.01) return;

        HeaderTop += offset;
        FirstDataRowTop += offset;
        LastDataRowTop += offset;
    }

    public PdfTableLayout CloneForNewPortraitTablePage()
    {
        return new PdfTableLayout
        {
            PageIndex = PageIndex,
            PageWidth = 594.96,
            PageHeight = 841.92,
            TableLeft = 19.5,
            TableRight = 575.8,
            HeaderTop = 454.0,
            HeaderHeight = 24.0,
            FirstDataRowTop = 479.0,
            LastDataRowTop = 463.186,
            RowHeight = RowHeight,
            BottomMargin = 820.0,
            HasShipmentTable = false,
            HasShipmentInfoLabel = HasShipmentInfoLabel,
            HasOriginalDataRows = HasOriginalDataRows,
            ColumnLefts = (double[])ColumnLefts.Clone(),
            ColumnRights = (double[])ColumnRights.Clone()
        };
    }
}
