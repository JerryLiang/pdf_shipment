namespace AmazonShipmentTool;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.SuspendLayout();

        // Top toolbar
        this.panelTop = new Panel();
        this.btnSelectPdf = new Button();
        this.btnSelectExcel = new Button();
        this.btnParse = new Button();
        this.btnExport = new Button();
        this.lblPdfPath = new Label();
        this.lblExcelPath = new Label();

        this.panelTop.Dock = DockStyle.Top;
        this.panelTop.Height = 50;
        this.panelTop.Padding = new Padding(8);

        this.btnSelectPdf.Text = "Select PDF";
        this.btnSelectPdf.Location = new Point(8, 12);
        this.btnSelectPdf.Size = new Size(100, 28);
        this.btnSelectPdf.Click += BtnSelectPdf_Click;

        this.lblPdfPath.Text = "No PDF selected";
        this.lblPdfPath.Location = new Point(116, 17);
        this.lblPdfPath.Size = new Size(200, 18);
        this.lblPdfPath.ForeColor = Color.Gray;

        this.btnSelectExcel.Text = "Select Excel";
        this.btnSelectExcel.Location = new Point(330, 12);
        this.btnSelectExcel.Size = new Size(100, 28);
        this.btnSelectExcel.Click += BtnSelectExcel_Click;

        this.lblExcelPath.Text = "No Excel selected";
        this.lblExcelPath.Location = new Point(438, 17);
        this.lblExcelPath.Size = new Size(200, 18);
        this.lblExcelPath.ForeColor = Color.Gray;

        this.btnParse.Text = "Parse";
        this.btnParse.Location = new Point(660, 12);
        this.btnParse.Size = new Size(80, 28);
        this.btnParse.Enabled = false;
        this.btnParse.Click += BtnParse_Click;

        this.btnExport.Text = "Export PDF";
        this.btnExport.Location = new Point(750, 12);
        this.btnExport.Size = new Size(100, 28);
        this.btnExport.Enabled = false;
        this.btnExport.Click += BtnExport_Click;

        this.panelTop.Controls.AddRange(new Control[] {
            btnSelectPdf, lblPdfPath, btnSelectExcel, lblExcelPath, btnParse, btnExport
        });

        // Left info panel
        this.panelLeft = new Panel();
        this.panelLeft.Dock = DockStyle.Left;
        this.panelLeft.Width = 220;
        this.panelLeft.Padding = new Padding(8);
        this.panelLeft.BackColor = Color.FromArgb(245, 245, 245);

        this.lblInfoTitle = new Label();
        this.lblInfoTitle.Text = "Appointment Info";
        this.lblInfoTitle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        this.lblInfoTitle.Location = new Point(8, 8);
        this.lblInfoTitle.Size = new Size(200, 22);

        this.lblAppointmentId = new Label();
        this.lblAppointmentId.Text = "Appointment ID: -";
        this.lblAppointmentId.Location = new Point(8, 36);
        this.lblAppointmentId.Size = new Size(200, 18);

        this.lblDestinationFC = new Label();
        this.lblDestinationFC.Text = "Destination FC: -";
        this.lblDestinationFC.Location = new Point(8, 56);
        this.lblDestinationFC.Size = new Size(200, 18);

        this.lblTrailerNumber = new Label();
        this.lblTrailerNumber.Text = "Trailer Number: -";
        this.lblTrailerNumber.Location = new Point(8, 76);
        this.lblTrailerNumber.Size = new Size(200, 18);

        this.lblOriginalRows = new Label();
        this.lblOriginalRows.Text = "Original Rows: -";
        this.lblOriginalRows.Location = new Point(8, 106);
        this.lblOriginalRows.Size = new Size(200, 18);

        this.lblNewRows = new Label();
        this.lblNewRows.Text = "New Rows: -";
        this.lblNewRows.Location = new Point(8, 126);
        this.lblNewRows.Size = new Size(200, 18);

        this.lblTotalRows = new Label();
        this.lblTotalRows.Text = "Total Rows: -";
        this.lblTotalRows.Location = new Point(8, 146);
        this.lblTotalRows.Size = new Size(200, 18);

        this.lblEstimatedPages = new Label();
        this.lblEstimatedPages.Text = "Est. Pages: -";
        this.lblEstimatedPages.Location = new Point(8, 166);
        this.lblEstimatedPages.Size = new Size(200, 18);

        this.lblStatus = new Label();
        this.lblStatus.Text = "Status: Ready";
        this.lblStatus.Location = new Point(8, 200);
        this.lblStatus.Size = new Size(200, 18);
        this.lblStatus.ForeColor = Color.Blue;

        this.panelLeft.Controls.AddRange(new Control[] {
            lblInfoTitle, lblAppointmentId, lblDestinationFC, lblTrailerNumber,
            lblOriginalRows, lblNewRows, lblTotalRows, lblEstimatedPages, lblStatus
        });

        // Bottom validation panel
        this.panelBottom = new Panel();
        this.panelBottom.Dock = DockStyle.Bottom;
        this.panelBottom.Height = 120;
        this.panelBottom.Padding = new Padding(8);

        this.lblValidationTitle = new Label();
        this.lblValidationTitle.Text = "Validation Results";
        this.lblValidationTitle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        this.lblValidationTitle.Location = new Point(8, 4);
        this.lblValidationTitle.Size = new Size(200, 18);

        this.txtValidation = new TextBox();
        this.txtValidation.Multiline = true;
        this.txtValidation.ReadOnly = true;
        this.txtValidation.ScrollBars = ScrollBars.Vertical;
        this.txtValidation.Location = new Point(8, 24);
        this.txtValidation.Size = new Size(950, 90);
        this.txtValidation.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        this.panelBottom.Controls.AddRange(new Control[] {
            lblValidationTitle, txtValidation
        });

        // Center DataGridView
        this.dgvShipment = new DataGridView();
        this.dgvShipment.Dock = DockStyle.Fill;
        this.dgvShipment.ReadOnly = true;
        this.dgvShipment.AllowUserToAddRows = false;
        this.dgvShipment.AllowUserToDeleteRows = false;
        this.dgvShipment.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.dgvShipment.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        this.dgvShipment.RowHeadersVisible = false;
        this.dgvShipment.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);

        // Main form
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(1000, 650);
        this.Controls.Add(this.dgvShipment);
        this.Controls.Add(this.panelLeft);
        this.Controls.Add(this.panelBottom);
        this.Controls.Add(this.panelTop);
        this.MinimumSize = new Size(800, 500);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = "Amazon Shipment PDF Tool";
        this.ResumeLayout(false);
    }

    private Panel panelTop;
    private Button btnSelectPdf;
    private Button btnSelectExcel;
    private Button btnParse;
    private Button btnExport;
    private Label lblPdfPath;
    private Label lblExcelPath;

    private Panel panelLeft;
    private Label lblInfoTitle;
    private Label lblAppointmentId;
    private Label lblDestinationFC;
    private Label lblTrailerNumber;
    private Label lblOriginalRows;
    private Label lblNewRows;
    private Label lblTotalRows;
    private Label lblEstimatedPages;
    private Label lblStatus;

    private Panel panelBottom;
    private Label lblValidationTitle;
    private TextBox txtValidation;

    private DataGridView dgvShipment;
}
