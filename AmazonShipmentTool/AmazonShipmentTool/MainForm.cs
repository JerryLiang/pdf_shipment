using AmazonShipmentTool.Models;
using AmazonShipmentTool.Parsers;
using AmazonShipmentTool.Services;
using AmazonShipmentTool.Validators;

namespace AmazonShipmentTool;

public partial class MainForm : Form
{
    private string? _pdfPath;
    private string? _excelPath;
    private AppointmentInfo? _appointmentInfo;
    private List<ShipmentRow>? _originalRows;
    private List<ShipmentRow>? _excelRows;
    private List<ShipmentRow>? _mergedRows;
    private ValidationResult? _validationResult;

    public MainForm()
    {
        InitializeComponent();
    }

    private void BtnSelectPdf_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select Amazon Appointment PDF"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _pdfPath = dialog.FileName;
            lblPdfPath.Text = Path.GetFileName(_pdfPath);
            lblPdfPath.ForeColor = Color.Black;
            UpdateParseButtonState();
        }
    }

    private void BtnSelectExcel_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx",
            Title = "Select Excel with new shipment data"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _excelPath = dialog.FileName;
            lblExcelPath.Text = Path.GetFileName(_excelPath);
            lblExcelPath.ForeColor = Color.Black;
            UpdateParseButtonState();
        }
    }

    private void UpdateParseButtonState()
    {
        btnParse.Enabled = _pdfPath != null && _excelPath != null;
    }

    private async void BtnParse_Click(object? sender, EventArgs e)
    {
        if (_pdfPath == null || _excelPath == null) return;

        btnParse.Enabled = false;
        btnExport.Enabled = false;
        lblStatus.Text = "Status: Parsing PDF...";
        lblStatus.ForeColor = Color.Blue;
        Application.DoEvents();

        try
        {
            var pdfParser = new PdfParser();
            pdfParser.Parse(_pdfPath);
            _appointmentInfo = pdfParser.AppointmentInfo;
            _originalRows = pdfParser.ShipmentRows;

            lblStatus.Text = "Status: Parsing Excel...";
            Application.DoEvents();

            var excelParser = new ExcelParser();
            _excelRows = excelParser.Parse(_excelPath);

            lblStatus.Text = "Status: Merging data...";
            Application.DoEvents();

            var merger = new DataMerger();
            _mergedRows = merger.Merge(_originalRows, _excelRows);

            var validator = new ShipmentValidator();
            _validationResult = validator.Validate(_originalRows, _excelRows);

            UpdateInfoPanel();
            UpdateDataGridView();
            UpdateValidationPanel();

            btnExport.Enabled = true;
            lblStatus.Text = $"Status: Parsed OK ({_originalRows.Count} original + {_excelRows.Count} new = {_mergedRows.Count} total)";
            lblStatus.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Status: Error - {ex.Message}";
            lblStatus.ForeColor = Color.Red;
            MessageBox.Show($"Error: {ex.Message}", "Parse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnParse.Enabled = true;
        }
    }

    private void UpdateInfoPanel()
    {
        if (_appointmentInfo == null) return;

        lblAppointmentId.Text = $"Appointment ID: {_appointmentInfo.AppointmentId}";
        lblDestinationFC.Text = $"Destination FC: {_appointmentInfo.DestinationFC}";
        lblTrailerNumber.Text = $"Trailer Number: {_appointmentInfo.TrailerNumber}";

        var originalCount = _originalRows?.Count ?? 0;
        var newCount = _excelRows?.Count ?? 0;
        var totalCount = _mergedRows?.Count ?? 0;

        lblOriginalRows.Text = $"Original Rows: {originalCount}";
        lblNewRows.Text = $"New Rows: {newCount}";
        lblTotalRows.Text = $"Total Rows: {totalCount}";

        var merger = new DataMerger();
        var pages = merger.EstimatePageCount(totalCount);
        lblEstimatedPages.Text = $"Est. Pages: {pages}";
    }

    private void UpdateDataGridView()
    {
        if (_mergedRows == null) return;

        dgvShipment.Columns.Clear();
        dgvShipment.Rows.Clear();

        dgvShipment.Columns.Add("Index", "#");
        dgvShipment.Columns.Add("Arn", "ARN");
        dgvShipment.Columns.Add("CarrierRef", "PRO/Carrier Reference");
        dgvShipment.Columns.Add("BolRef", "BOL/Vendor Reference");
        dgvShipment.Columns.Add("VendorName", "Vendor Name");
        dgvShipment.Columns.Add("PalletCount", "Pallet");
        dgvShipment.Columns.Add("CartonCount", "Carton");
        dgvShipment.Columns.Add("UnitCount", "Unit");
        dgvShipment.Columns.Add("PoList", "PO List");

        dgvShipment.Columns["Index"].Width = 45;
        dgvShipment.Columns["Arn"].Width = 80;
        dgvShipment.Columns["CarrierRef"].Width = 130;
        dgvShipment.Columns["BolRef"].Width = 150;
        dgvShipment.Columns["VendorName"].Width = 100;
        dgvShipment.Columns["PalletCount"].Width = 55;
        dgvShipment.Columns["CartonCount"].Width = 55;
        dgvShipment.Columns["UnitCount"].Width = 50;
        dgvShipment.Columns["PoList"].Width = 120;

        dgvShipment.Columns["PalletCount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        dgvShipment.Columns["CartonCount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        dgvShipment.Columns["UnitCount"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        dgvShipment.Columns["Index"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        foreach (var row in _mergedRows)
        {
            var idx = dgvShipment.Rows.Add(
                row.Index,
                row.Arn,
                row.CarrierReferenceNumber,
                row.BolOrVendorReference,
                row.VendorName,
                row.PalletCount,
                row.CartonCount,
                row.UnitCount?.ToString() ?? "",
                row.PoList
            );

            if (row.IsAddedFromExcel)
            {
                dgvShipment.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(255, 253, 231);
            }
        }
    }

    private void UpdateValidationPanel()
    {
        if (_validationResult == null) return;

        var lines = new List<string>();

        if (_validationResult.Errors.Count > 0)
        {
            lines.Add("ERRORS:");
            foreach (var error in _validationResult.Errors)
                lines.Add($"  [ERROR] {error}");
        }

        if (_validationResult.Warnings.Count > 0)
        {
            lines.Add("WARNINGS:");
            foreach (var warning in _validationResult.Warnings)
                lines.Add($"  [WARN] {warning}");
        }

        if (lines.Count == 0)
        {
            lines.Add("No issues found.");
        }

        txtValidation.Text = string.Join(Environment.NewLine, lines);
    }

    private async void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_originalRows == null || _excelRows == null || _appointmentInfo == null || _pdfPath == null) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Save exported PDF",
            FileName = GenerateOutputFileName()
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        btnExport.Enabled = false;
        lblStatus.Text = "Status: Generating PDF...";
        lblStatus.ForeColor = Color.Blue;
        Application.DoEvents();

        try
        {
            var appendService = new PdfAppendService();
            await Task.Run(() => appendService.AppendRowsToPdf(_pdfPath, _originalRows, _excelRows, dialog.FileName));

            lblStatus.Text = $"Status: PDF exported to {Path.GetFileName(dialog.FileName)}";
            lblStatus.ForeColor = Color.Green;

            var result = MessageBox.Show(
                $"PDF exported successfully!\n\n{dialog.FileName}\n\nOpen the file?",
                "Export Complete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Status: Export error - {ex.Message}";
            lblStatus.ForeColor = Color.Red;
            MessageBox.Show($"Export error: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnExport.Enabled = true;
        }
    }

    private string GenerateOutputFileName()
    {
        if (_pdfPath == null) return "output_updated.pdf";

        var dir = Path.GetDirectoryName(_pdfPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(_pdfPath);
        return Path.Combine(dir, $"{name}_appended.pdf");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
    }
}
