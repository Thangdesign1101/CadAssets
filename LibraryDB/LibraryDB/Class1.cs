using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

// NAMESPACE CHUẨN CHO ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(DB_ArchitectTools.CadFontTranslatorPluginV3))]

namespace DB_ArchitectTools
{
    public class CadFontTranslatorPluginV3
    {
        private static MainToolFormV3 currentForm = null;

        [CommandMethod("DF", CommandFlags.UsePickSet)]
        public void ShowFontManagerUI()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptSelectionResult psr = ed.SelectImplied();
            ObjectId[] selectedIds = (psr.Status == PromptStatus.OK && psr.Value != null)
                ? psr.Value.GetObjectIds()
                : new ObjectId[0];

            if (currentForm != null && !currentForm.IsDisposed)
            {
                currentForm.Focus();
                currentForm.ReloadSelectionFromCommand(selectedIds);
                return;
            }

            currentForm = new MainToolFormV3(selectedIds);
            currentForm.FormClosed += (s, e) => { currentForm = null; };

            // Mở dạng Modeless để vừa mở bảng vừa thao tác trực tiếp trên CAD
            Application.ShowModelessDialog(currentForm);
        }
    }

    public class TextItemModel
    {
        public ObjectId EntityId { get; set; }
        public ObjectId ParentStyleId { get; set; }
        public string EntityType { get; set; }
        public string CurrentStyleName { get; set; }
        public string OriginalText { get; set; }
        public bool IsUpperCaseFont { get; set; }
        public double TopY { get; set; }
        public double LeftX { get; set; }
        public double ApproxHeight { get; set; }
    }

    public class StyleComboItem
    {
        public string Name { get; set; }
        public ObjectId StyleId { get; set; }
        public override string ToString()
        {
            return Name;
        }
    }

    public class LanguageOptionItem
    {
        public string DisplayName { get; set; }
        public string LangCode { get; set; }
        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class MainToolFormV3 : Form
    {
        [DllImport("gdi32.dll", EntryPoint = "AddFontResourceW", SetLastError = true)]
        public static extern int AddFontResource([In][MarshalAs(UnmanagedType.LPWStr)] string lpFileName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TabControl tabMain;
        private TabPage tabSelection;
        private TabPage tabCloud;

        private DataGridView dgvTexts;
        private ComboBox cboTargetStyle;
        private ComboBox cboTargetLang;
        private CheckBox chkFixEncoding;
        private CheckBox chkSelectAll;

        private Button btnReselect;
        private Button btnLoadAllDrawing;
        private Label lblLang;
        private Button btnTranslateTable;
        private Button btnApply;

        // Khu vực Tìm kiếm, Zoom, Thống kê & Thay thế
        private Panel pnlSearch;
        private Label lblFind;
        private TextBox txtSearch;
        private Label lblReplace;
        private TextBox txtReplace;
        private Label lblReplaceStat;
        private CheckBox chkMatchCase;
        private Button btnSearchNow;
        private Button btnZoomPrev;
        private Button btnZoomNext;
        private Button btnReplaceLocal;
        private Button btnReplaceAll;
        private List<int> matchedRowIndices = new List<int>();
        private int currentMatchCursor = -1;

        // Tab Cloud Sync
        private DataGridView dgvAssets;
        private TextBox txtCloudUrl;
        private Button btnSyncCloud;
        private Label lblStatus;

        private string baseCloudUrl = "https://raw.githubusercontent.com/Thangdesign1101/CadAssets/main/";
        private readonly string cadFontDir = Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "Fonts");
        private readonly string cadSupportDir = Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "Support");
        private readonly string winFontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        private static readonly HttpClient httpClient = new HttpClient();
        private List<TextItemModel> currentTextItems = new List<TextItemModel>();

        // Lưu bản dịch tạm trong phiên để khi chọn thêm đối tượng trên CAD không mất chữ đã dịch
        private Dictionary<ObjectId, string> sessionTranslations = new Dictionary<ObjectId, string>();
        private Document subscribedDoc = null;
        private bool isInternalSelectionUpdate = false;

        // Quản lý co giãn cột theo tỷ lệ (như Excel/Word)
        private double[] dgvTextsColRatios = new double[] { 0.05, 0.07, 0.13, 0.31, 0.31, 0.065, 0.065 };
        private double[] dgvAssetsColRatios = new double[] { 0.12, 0.31, 0.31, 0.26 };
        private bool isScalingColumns = false;
        private float currentGridZoom = 1.0f;
        private int BaseRowHeight => Math.Max(26, (int)Math.Round(28 * currentGridZoom));

        // Quản lý thao tác Double-click hoặc kéo tại đáy ô "..." để bung full chiều cao dòng
        private const int CELL_BOTTOM_EDGE_MARGIN = 8;
        private bool isDraggingRowDivider = false;
        private DataGridView activeDragGrid = null;
        private int dragRowIndex = -1;
        private int dragStartScreenY = 0;
        private int dragStartRowHeight = 0;
        private bool suppressNextCellClickAction = false;

        // Kích thước khung nhỏ nhất cố định để không bao giờ bị mất bất kỳ nút hay thông tin nào
        private const int MIN_FORM_WIDTH = 920;
        private const int MIN_FORM_HEIGHT = 500;

        public MainToolFormV3(ObjectId[] initialSelection)
        {
            InitializeComponent();
            UpdateResponsiveLayout();
            ScaleGridColumnsProportionally(dgvTexts, dgvTextsColRatios);
            ScaleGridColumnsProportionally(dgvAssets, dgvAssetsColRatios);
            LoadDrawingTextStyles();
            LoadSelectedTextEntities(initialSelection);
            ScanDrawingAssets();
            HookCadSelectionEvents();
        }

        // Bấm nút ESC để đóng bảng nhanh
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (dgvTexts != null && dgvTexts.IsCurrentCellInEditMode)
                {
                    dgvTexts.CancelEdit();
                    return true;
                }
                this.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnhookCadSelectionEvents();
            base.OnFormClosed(e);
        }

        // =========================================================================
        // ĐỒNG BỘ CHỌN / BỎ CHỌN ĐỐI TƯỢNG TRỰC TIẾP TRÊN MÀN HÌNH CAD
        // =========================================================================
        private void HookCadSelectionEvents()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                subscribedDoc = doc;
                subscribedDoc.ImpliedSelectionChanged += OnCadImpliedSelectionChanged;
            }
            Application.DocumentManager.DocumentActivated += OnCadDocumentActivated;
        }

        private void UnhookCadSelectionEvents()
        {
            if (subscribedDoc != null)
            {
                try { subscribedDoc.ImpliedSelectionChanged -= OnCadImpliedSelectionChanged; } catch { }
                subscribedDoc = null;
            }
            try { Application.DocumentManager.DocumentActivated -= OnCadDocumentActivated; } catch { }
        }

        private void OnCadDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            if (subscribedDoc != null)
            {
                try { subscribedDoc.ImpliedSelectionChanged -= OnCadImpliedSelectionChanged; } catch { }
            }
            subscribedDoc = e.Document;
            if (subscribedDoc != null)
            {
                subscribedDoc.ImpliedSelectionChanged += OnCadImpliedSelectionChanged;
                LoadDrawingTextStyles();
                SyncWithCurrentCadSelection();
            }
        }

        private void OnCadImpliedSelectionChanged(object sender, EventArgs e)
        {
            if (isInternalSelectionUpdate || this.IsDisposed) return;
            SyncWithCurrentCadSelection();
        }

        private void SyncWithCurrentCadSelection()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptSelectionResult psr = ed.SelectImplied();
            ObjectId[] selectedIds = (psr.Status == PromptStatus.OK && psr.Value != null)
                ? psr.Value.GetObjectIds()
                : new ObjectId[0];

            LoadSelectedTextEntities(selectedIds);
        }

        public void ReloadSelectionFromCommand(ObjectId[] newSelection)
        {
            LoadDrawingTextStyles();
            LoadSelectedTextEntities(newSelection);
        }

        // =========================================================================
        // KHỞI TẠO GIAO DIỆN (KHÓA MINIMUM SIZE & THU PHÓNG TỶ LỆ NHƯ EXCEL/WORD)
        // =========================================================================
        private void InitializeComponent()
        {
            this.Text = "DB-Architect - Đổi Style, Dịch & Tìm Kiếm (ESC để thoát)";
            this.Size = new Size(1040, 640);
            // Đặt kích thước khung nhỏ nhất để đảm bảo không bao giờ mất nút hay thông tin
            this.MinimumSize = new Size(MIN_FORM_WIDTH, MIN_FORM_HEIGHT);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.KeyPreview = true;

            tabMain = new TabControl();
            tabMain.Location = new Point(10, 10);
            tabMain.Size = new Size(1000, 540);
            tabMain.Font = new System.Drawing.Font("Segoe UI", 9.2f, FontStyle.Regular);

            // =========================================================================
            // TAB 1: ĐỔI STYLE, DỊCH & TÌM KIẾM
            // =========================================================================
            tabSelection = new TabPage("1. Đổi Style & Dịch");

            // --- Hàng 1: Chọn vùng, Quét toàn bộ, Style đích ---
            btnReselect = new Button();
            btnReselect.Text = "Chọn vùng";
            btnReselect.Location = new Point(10, 10);
            btnReselect.Size = new Size(110, 30);
            btnReselect.BackColor = Color.FromArgb(240, 240, 240);
            btnReselect.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);
            btnReselect.Click += BtnReselect_Click;

            btnLoadAllDrawing = new Button();
            btnLoadAllDrawing.Text = "Quét toàn bộ";
            btnLoadAllDrawing.Location = new Point(126, 10);
            btnLoadAllDrawing.Size = new Size(115, 30);
            btnLoadAllDrawing.BackColor = Color.FromArgb(230, 242, 255);
            btnLoadAllDrawing.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);
            btnLoadAllDrawing.Click += BtnLoadAllDrawing_Click;

            chkSelectAll = new CheckBox();
            chkSelectAll.Text = "Tất cả";
            chkSelectAll.Location = new Point(252, 15);
            chkSelectAll.AutoSize = true;
            chkSelectAll.Checked = true;
            chkSelectAll.Font = new System.Drawing.Font("Segoe UI", 9);
            chkSelectAll.CheckedChanged += ChkSelectAll_CheckedChanged;

            Label lblStyle = new Label();
            lblStyle.Text = "Style đích:";
            lblStyle.Location = new Point(330, 15);
            lblStyle.AutoSize = true;
            lblStyle.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);

            cboTargetStyle = new ComboBox();
            cboTargetStyle.Location = new Point(402, 11);
            cboTargetStyle.Width = 210;
            cboTargetStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            cboTargetStyle.Font = new System.Drawing.Font("Segoe UI", 9.2f);

            chkFixEncoding = new CheckBox();
            chkFixEncoding.Text = "Khử lỗi mã";
            chkFixEncoding.Location = new Point(628, 15);
            chkFixEncoding.AutoSize = true;
            chkFixEncoding.Checked = true;
            chkFixEncoding.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);
            chkFixEncoding.ForeColor = Color.DarkGreen;

            // --- Hàng 2: Tìm kiếm, Zoom, Biến thống kê & Thay thế ---
            pnlSearch = new Panel();
            pnlSearch.Location = new Point(10, 46);
            pnlSearch.Size = new Size(965, 40);
            pnlSearch.BackColor = Color.FromArgb(242, 246, 252);
            pnlSearch.BorderStyle = BorderStyle.FixedSingle;

            lblFind = new Label();
            lblFind.Text = "Tìm:";
            lblFind.Location = new Point(6, 10);
            lblFind.AutoSize = true;
            lblFind.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(42, 7);
            txtSearch.Width = 120;
            txtSearch.Font = new System.Drawing.Font("Segoe UI", 9.2f);
            txtSearch.TextChanged += (s, e) => UpdateReplaceStatistics(false);
            txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) PerformSearchAndZoom(); };

            btnSearchNow = new Button();
            btnSearchNow.Text = "Tìm";
            btnSearchNow.Location = new Point(166, 5);
            btnSearchNow.Size = new Size(48, 28);
            btnSearchNow.BackColor = Color.FromArgb(0, 120, 215);
            btnSearchNow.ForeColor = Color.White;
            btnSearchNow.Font = new System.Drawing.Font("Segoe UI", 8.8f, FontStyle.Bold);
            btnSearchNow.Click += (s, e) => PerformSearchAndZoom();

            btnZoomPrev = new Button();
            btnZoomPrev.Text = "◀";
            btnZoomPrev.Location = new Point(216, 5);
            btnZoomPrev.Size = new Size(32, 28);
            btnZoomPrev.Click += (s, e) => NavigateSearchMatch(-1);

            btnZoomNext = new Button();
            btnZoomNext.Text = "▶";
            btnZoomNext.Location = new Point(250, 5);
            btnZoomNext.Size = new Size(32, 28);
            btnZoomNext.Click += (s, e) => NavigateSearchMatch(1);

            lblReplace = new Label();
            lblReplace.Text = "Thay:";
            lblReplace.Location = new Point(288, 10);
            lblReplace.AutoSize = true;
            lblReplace.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);

            txtReplace = new TextBox();
            txtReplace.Location = new Point(330, 7);
            txtReplace.Width = 120;
            txtReplace.Font = new System.Drawing.Font("Segoe UI", 9.2f);

            lblReplaceStat = new Label();
            lblReplaceStat.Text = "(0 đối tượng)";
            lblReplaceStat.Location = new Point(455, 10);
            lblReplaceStat.AutoSize = true;
            lblReplaceStat.ForeColor = Color.FromArgb(192, 57, 43);
            lblReplaceStat.Font = new System.Drawing.Font("Segoe UI", 8.8f, FontStyle.Bold);

            chkMatchCase = new CheckBox();
            chkMatchCase.Text = "Hoa/thường";
            chkMatchCase.Location = new Point(575, 10);
            chkMatchCase.AutoSize = true;
            chkMatchCase.Font = new System.Drawing.Font("Segoe UI", 8.5f);
            chkMatchCase.CheckedChanged += (s, e) => UpdateReplaceStatistics(false);

            btnReplaceLocal = new Button();
            btnReplaceLocal.Text = "Thay cục bộ";
            btnReplaceLocal.Location = new Point(668, 5);
            btnReplaceLocal.Size = new Size(108, 28);
            btnReplaceLocal.BackColor = Color.FromArgb(255, 140, 0);
            btnReplaceLocal.ForeColor = Color.White;
            btnReplaceLocal.Font = new System.Drawing.Font("Segoe UI", 8.8f, FontStyle.Bold);
            btnReplaceLocal.Click += BtnReplaceLocal_Click;

            btnReplaceAll = new Button();
            btnReplaceAll.Text = "Thay toàn bộ";
            btnReplaceAll.Location = new Point(780, 5);
            btnReplaceAll.Size = new Size(112, 28);
            btnReplaceAll.BackColor = Color.FromArgb(192, 57, 43);
            btnReplaceAll.ForeColor = Color.White;
            btnReplaceAll.Font = new System.Drawing.Font("Segoe UI", 8.8f, FontStyle.Bold);
            btnReplaceAll.Click += BtnReplaceAll_Click;

            pnlSearch.Controls.AddRange(new Control[] {
                lblFind, txtSearch, btnSearchNow, btnZoomPrev, btnZoomNext,
                lblReplace, txtReplace, lblReplaceStat, chkMatchCase, btnReplaceLocal, btnReplaceAll
            });

            // --- Bảng dữ liệu Tab 1 ---
            dgvTexts = new DataGridView();
            dgvTexts.Location = new Point(10, 92);
            dgvTexts.Size = new Size(965, 345);
            dgvTexts.AllowUserToAddRows = false;
            dgvTexts.AllowUserToResizeColumns = true;
            dgvTexts.AllowUserToResizeRows = true;
            dgvTexts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dgvTexts.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvTexts.RowTemplate.Height = 28;
            dgvTexts.ScrollBars = ScrollBars.Both;
            dgvTexts.ShowCellToolTips = true;
            dgvTexts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvTexts.BackgroundColor = Color.White;
            dgvTexts.RowHeadersVisible = false;
            dgvTexts.Font = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Regular);
            dgvTexts.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvTexts.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvTexts.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;

            DataGridViewCheckBoxColumn colCheck = new DataGridViewCheckBoxColumn();
            colCheck.Name = "Checked";
            colCheck.HeaderText = "Chọn";
            colCheck.Width = 48;
            colCheck.MinimumWidth = 32;
            colCheck.Resizable = DataGridViewTriState.True;

            DataGridViewTextBoxColumn colType = new DataGridViewTextBoxColumn();
            colType.Name = "EntityType";
            colType.HeaderText = "Loại";
            colType.Width = 65;
            colType.MinimumWidth = 36;
            colType.ReadOnly = true;
            colType.Resizable = DataGridViewTriState.True;

            DataGridViewTextBoxColumn colCurStyle = new DataGridViewTextBoxColumn();
            colCurStyle.Name = "CurrentStyle";
            colCurStyle.HeaderText = "Style";
            colCurStyle.Width = 125;
            colCurStyle.MinimumWidth = 45;
            colCurStyle.ReadOnly = true;
            colCurStyle.Resizable = DataGridViewTriState.True;

            DataGridViewTextBoxColumn colOrigText = new DataGridViewTextBoxColumn();
            colOrigText.Name = "OriginalText";
            colOrigText.HeaderText = "Nội dung gốc";
            colOrigText.Width = 295;
            colOrigText.MinimumWidth = 60;
            colOrigText.ReadOnly = false;
            colOrigText.Resizable = DataGridViewTriState.True;

            DataGridViewTextBoxColumn colTransText = new DataGridViewTextBoxColumn();
            colTransText.Name = "TranslatedText";
            colTransText.HeaderText = "Bản dịch";
            colTransText.Width = 295;
            colTransText.MinimumWidth = 60;
            colTransText.ReadOnly = false;
            colTransText.Resizable = DataGridViewTriState.True;

            DataGridViewButtonColumn colZoomBtn = new DataGridViewButtonColumn();
            colZoomBtn.Name = "ZoomBtn";
            colZoomBtn.HeaderText = "Xem";
            colZoomBtn.Text = "Zoom";
            colZoomBtn.UseColumnTextForButtonValue = true;
            colZoomBtn.Width = 62;
            colZoomBtn.MinimumWidth = 40;
            colZoomBtn.Resizable = DataGridViewTriState.True;

            DataGridViewButtonColumn colCopyBtn = new DataGridViewButtonColumn();
            colCopyBtn.Name = "CopyBtn";
            colCopyBtn.HeaderText = "Chép";
            colCopyBtn.Text = "Copy";
            colCopyBtn.UseColumnTextForButtonValue = true;
            colCopyBtn.Width = 62;
            colCopyBtn.MinimumWidth = 40;
            colCopyBtn.Resizable = DataGridViewTriState.True;

            dgvTexts.Columns.AddRange(new DataGridViewColumn[] {
                colCheck, colType, colCurStyle, colOrigText, colTransText, colZoomBtn, colCopyBtn
            });

            AttachExcelLikeGridBehaviors(dgvTexts, dgvTextsColRatios);
            dgvTexts.CellContentClick += DgvTexts_CellContentClick;
            dgvTexts.CellDoubleClick += DgvTexts_CellDoubleClick;
            dgvTexts.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < currentTextItems.Count)
                {
                    if (dgvTexts.Columns[e.ColumnIndex].Name == "TranslatedText")
                    {
                        string val = Convert.ToString(dgvTexts.Rows[e.RowIndex].Cells["TranslatedText"].Value ?? "");
                        sessionTranslations[currentTextItems[e.RowIndex].EntityId] = val;
                    }
                    UpdateReplaceStatistics(false);
                }
            };

            // --- Hàng dưới cùng: Đích (VI/EN), nút Dịch & nút Áp dụng ---
            lblLang = new Label();
            lblLang.Text = "Đích:";
            lblLang.Location = new Point(10, 456);
            lblLang.AutoSize = true;
            lblLang.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);

            cboTargetLang = new ComboBox();
            cboTargetLang.Location = new Point(52, 452);
            cboTargetLang.Width = 150;
            cboTargetLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cboTargetLang.Items.Add(new LanguageOptionItem { DisplayName = "Tiếng Việt (VI)", LangCode = "vi" });
            cboTargetLang.Items.Add(new LanguageOptionItem { DisplayName = "Tiếng Anh (EN)", LangCode = "en" });
            cboTargetLang.SelectedIndex = 0;

            btnTranslateTable = new Button();
            btnTranslateTable.Text = "Dịch";
            btnTranslateTable.Location = new Point(215, 444);
            btnTranslateTable.Size = new Size(370, 44);
            btnTranslateTable.BackColor = Color.FromArgb(209, 52, 56);
            btnTranslateTable.ForeColor = Color.White;
            btnTranslateTable.Font = new System.Drawing.Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnTranslateTable.Click += BtnTranslateTable_Click;

            btnApply = new Button();
            btnApply.Text = "Áp dụng";
            btnApply.Location = new Point(600, 444);
            btnApply.Size = new Size(375, 44);
            btnApply.BackColor = Color.FromArgb(16, 137, 62);
            btnApply.ForeColor = Color.White;
            btnApply.Font = new System.Drawing.Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnApply.Click += BtnApply_Click;

            tabSelection.Controls.AddRange(new Control[] {
                btnReselect, btnLoadAllDrawing, chkSelectAll, lblStyle, cboTargetStyle,
                chkFixEncoding, pnlSearch, dgvTexts, lblLang, cboTargetLang, btnTranslateTable, btnApply
            });

            // =========================================================================
            // TAB 2: ĐỒNG BỘ FONT & HATCH THIẾU
            // =========================================================================
            tabCloud = new TabPage("2. Đồng Bộ Font & Hatch");

            Label lblUrl = new Label();
            lblUrl.Text = "GitHub URL:";
            lblUrl.Location = new Point(15, 18);
            lblUrl.AutoSize = true;
            lblUrl.Font = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);

            txtCloudUrl = new TextBox();
            txtCloudUrl.Text = baseCloudUrl;
            txtCloudUrl.Location = new Point(105, 15);
            txtCloudUrl.Width = 865;
            txtCloudUrl.Font = new System.Drawing.Font("Segoe UI", 9);

            dgvAssets = new DataGridView();
            dgvAssets.Location = new Point(15, 50);
            dgvAssets.Size = new Size(955, 385);
            dgvAssets.AllowUserToAddRows = false;
            dgvAssets.AllowUserToResizeColumns = true;
            dgvAssets.AllowUserToResizeRows = true;
            dgvAssets.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            dgvAssets.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dgvAssets.RowTemplate.Height = 28;
            dgvAssets.ScrollBars = ScrollBars.Both;
            dgvAssets.ShowCellToolTips = true;
            dgvAssets.ReadOnly = true;
            dgvAssets.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvAssets.BackgroundColor = Color.White;
            dgvAssets.RowHeadersVisible = false;
            dgvAssets.Font = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Regular);
            dgvAssets.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvAssets.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvAssets.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopLeft;

            int c1 = dgvAssets.Columns.Add("AssetType", "Loại");
            dgvAssets.Columns[c1].Width = 110;
            dgvAssets.Columns[c1].MinimumWidth = 40;

            int c2 = dgvAssets.Columns.Add("Name", "Tên Style / Hatch");
            dgvAssets.Columns[c2].Width = 290;
            dgvAssets.Columns[c2].MinimumWidth = 60;

            int c3 = dgvAssets.Columns.Add("FileName", "File");
            dgvAssets.Columns[c3].Width = 290;
            dgvAssets.Columns[c3].MinimumWidth = 60;

            int c4 = dgvAssets.Columns.Add("Status", "Trạng thái");
            dgvAssets.Columns[c4].Width = 240;
            dgvAssets.Columns[c4].MinimumWidth = 60;

            AttachExcelLikeGridBehaviors(dgvAssets, dgvAssetsColRatios);

            btnSyncCloud = new Button();
            btnSyncCloud.Text = "Đồng bộ ngay";
            btnSyncCloud.Location = new Point(15, 444);
            btnSyncCloud.Size = new Size(955, 44);
            btnSyncCloud.BackColor = Color.FromArgb(0, 120, 215);
            btnSyncCloud.ForeColor = Color.White;
            btnSyncCloud.Font = new System.Drawing.Font("Segoe UI", 10f, FontStyle.Bold);
            btnSyncCloud.Click += BtnSyncCloud_Click;

            tabCloud.Controls.AddRange(new Control[] { lblUrl, txtCloudUrl, dgvAssets, btnSyncCloud });

            tabMain.TabPages.Add(tabSelection);
            tabMain.TabPages.Add(tabCloud);

            lblStatus = new Label();
            lblStatus.Text = "Mẹo: Double-click vào cạnh dưới (đáy) của ô có dấu '...' để bung toàn bộ nội dung như Excel.";
            lblStatus.Location = new Point(12, 558);
            lblStatus.AutoSize = true;
            lblStatus.ForeColor = Color.DarkBlue;
            lblStatus.Font = new System.Drawing.Font("Segoe UI", 9.2f, FontStyle.Italic);

            this.Controls.Add(tabMain);
            this.Controls.Add(lblStatus);

            this.Resize += (s, e) => UpdateResponsiveLayout();
            tabMain.SelectedIndexChanged += (s, e) => UpdateResponsiveLayout();
        }

        // =========================================================================
        // CƠ CHẾ THU PHÓNG BẢNG THEO TỶ LỆ & DOUBLE-CLICK ĐÁY Ô "..." NHƯ EXCEL
        // =========================================================================
        private void AttachExcelLikeGridBehaviors(DataGridView dgv, double[] colRatios)
        {
            dgv.CellPainting += Dgv_CellPainting_Ellipsis;
            dgv.CellFormatting += Dgv_CellFormatting_Tooltip;

            // Khi người dùng tự kéo độ rộng cột bằng chuột -> Lưu lại tỷ lệ mới của các cột
            dgv.ColumnWidthChanged += (s, e) =>
            {
                if (isScalingColumns) return;
                SaveCurrentColumnRatios(dgv, colRatios);
            };

            // Khi bảng thay đổi kích thước theo cửa sổ -> Tự động co giãn các cột theo tỷ lệ
            dgv.Resize += (s, e) =>
            {
                ScaleGridColumnsProportionally(dgv, colRatios);
            };

            // Hỗ trợ Ctrl + Lăn chuột để thu phóng tỷ lệ chữ và dòng giống Excel / Word
            dgv.MouseWheel += Dgv_MouseWheel_Zoom;

            // Hỗ trợ đưa chuột vào đáy ô "..." để hiện mũi tên ↕, Double-click để bung hết chữ, hoặc kéo để chỉnh cao độ dòng
            dgv.CellMouseMove += Dgv_CellMouseMove_BottomEdge;
            dgv.CellMouseDown += Dgv_CellMouseDown_BottomEdge;
            dgv.MouseMove += Dgv_MouseMove_DragRowHeight;
            dgv.MouseUp += Dgv_MouseUp_DragRowHeight;
        }

        private void SaveCurrentColumnRatios(DataGridView dgv, double[] ratios)
        {
            if (dgv == null || dgv.Columns.Count != ratios.Length) return;
            double totalW = 0;
            for (int i = 0; i < dgv.Columns.Count; i++)
                totalW += dgv.Columns[i].Width;

            if (totalW <= 10) return;
            for (int i = 0; i < dgv.Columns.Count; i++)
                ratios[i] = dgv.Columns[i].Width / totalW;
        }

        private void ScaleGridColumnsProportionally(DataGridView dgv, double[] ratios)
        {
            if (dgv == null || dgv.Columns.Count == 0 || dgv.Columns.Count != ratios.Length) return;
            if (dgv.ClientSize.Width <= 50) return;

            isScalingColumns = true;
            try
            {
                int vScrollWidth = dgv.Controls.OfType<VScrollBar>().FirstOrDefault(v => v.Visible)?.Width ?? 0;
                int availWidth = Math.Max(dgv.ClientSize.Width - vScrollWidth - 2, 100);

                int allocated = 0;
                for (int i = 0; i < dgv.Columns.Count; i++)
                {
                    int w = (i == dgv.Columns.Count - 1)
                        ? Math.Max(dgv.Columns[i].MinimumWidth, availWidth - allocated)
                        : Math.Max(dgv.Columns[i].MinimumWidth, (int)Math.Round(availWidth * ratios[i]));

                    dgv.Columns[i].Width = w;
                    allocated += w;
                }
            }
            finally
            {
                isScalingColumns = false;
            }
        }

        private void Dgv_MouseWheel_Zoom(object sender, MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) != Keys.Control) return;

            DataGridView dgv = sender as DataGridView;
            if (dgv == null) return;

            float step = e.Delta > 0 ? 0.1f : -0.1f;
            float newZoom = Math.Max(0.8f, Math.Min(1.8f, currentGridZoom + step));
            if (Math.Abs(newZoom - currentGridZoom) < 0.01f) return;

            float ratio = newZoom / currentGridZoom;
            currentGridZoom = newZoom;

            System.Drawing.Font newFont = new System.Drawing.Font("Segoe UI", 9f * currentGridZoom, FontStyle.Regular);
            dgvTexts.Font = newFont;
            dgvAssets.Font = newFont;

            int newHeaderHeight = Math.Max(26, (int)Math.Round(28 * currentGridZoom));
            dgvTexts.ColumnHeadersHeight = newHeaderHeight;
            dgvAssets.ColumnHeadersHeight = newHeaderHeight;

            foreach (DataGridViewRow r in dgvTexts.Rows)
                r.Height = Math.Max(BaseRowHeight, (int)Math.Round(r.Height * ratio));

            foreach (DataGridViewRow r in dgvAssets.Rows)
                r.Height = Math.Max(BaseRowHeight, (int)Math.Round(r.Height * ratio));

            lblStatus.Text = "Tỷ lệ thu phóng bảng hiện tại: " + Math.Round(currentGridZoom * 100) + "% (Ctrl + Lăn chuột để thu phóng như Excel/Word).";
        }

        private void Dgv_CellMouseMove_BottomEdge(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (isDraggingRowDivider) return;
            DataGridView dgv = sender as DataGridView;
            if (dgv == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                if (dgv != null) dgv.Cursor = Cursors.Default;
                return;
            }

            int rowHeight = dgv.Rows[e.RowIndex].Height;
            if (e.Y >= rowHeight - CELL_BOTTOM_EDGE_MARGIN)
            {
                dgv.Cursor = Cursors.SizeNS;
            }
            else
            {
                dgv.Cursor = Cursors.Default;
            }
        }

        private void Dgv_CellMouseDown_BottomEdge(object sender, DataGridViewCellMouseEventArgs e)
        {
            suppressNextCellClickAction = false;
            DataGridView dgv = sender as DataGridView;
            if (dgv == null || e.RowIndex < 0 || e.ColumnIndex < 0 || e.Button != MouseButtons.Left) return;

            int rowHeight = dgv.Rows[e.RowIndex].Height;
            if (e.Y >= rowHeight - CELL_BOTTOM_EDGE_MARGIN)
            {
                suppressNextCellClickAction = true;

                // Nếu người dùng Double-click vào đáy ô "..." -> Bung toàn bộ nội dung ô đó như Excel và đẩy các ô dưới xuống
                if (e.Clicks == 2)
                {
                    isDraggingRowDivider = false;
                    activeDragGrid = null;
                    ToggleOrAutoFitRowHeight(dgv, e.RowIndex);
                }
                else if (e.Clicks == 1)
                {
                    isDraggingRowDivider = true;
                    activeDragGrid = dgv;
                    dragRowIndex = e.RowIndex;
                    dragStartScreenY = Cursor.Position.Y;
                    dragStartRowHeight = rowHeight;
                }
            }
        }

        private void Dgv_MouseMove_DragRowHeight(object sender, MouseEventArgs e)
        {
            if (!isDraggingRowDivider || activeDragGrid == null || dragRowIndex < 0 || dragRowIndex >= activeDragGrid.Rows.Count)
                return;

            int deltaY = Cursor.Position.Y - dragStartScreenY;
            int newHeight = Math.Max(BaseRowHeight, dragStartRowHeight + deltaY);
            activeDragGrid.Rows[dragRowIndex].Height = newHeight;
        }

        private void Dgv_MouseUp_DragRowHeight(object sender, MouseEventArgs e)
        {
            if (isDraggingRowDivider)
            {
                isDraggingRowDivider = false;
                if (activeDragGrid != null) activeDragGrid.Cursor = Cursors.Default;
                activeDragGrid = null;
                dragRowIndex = -1;
            }
        }

        /// <summary>
        /// Tính toán chiều cao đầy đủ của toàn bộ chữ trong dòng khi Double-click ở đáy ô "...":
        /// - Nếu ô đang bị thu gọn (hiển thị '...'), mở rộng chiều cao dòng để hiện 100% nội dung và tự đẩy các dòng dưới xuống.
        /// - Nếu ô đã mở rộng hết cỡ, Double-click lần nữa ở đáy ô sẽ thu gọn về 1 dòng chuẩn.
        /// </summary>
        private void ToggleOrAutoFitRowHeight(DataGridView dgv, int rowIndex)
        {
            if (dgv == null || rowIndex < 0 || rowIndex >= dgv.Rows.Count) return;

            DataGridViewRow row = dgv.Rows[rowIndex];
            int neededHeight = CalculateFullRowTextHeight(dgv, rowIndex);

            if (row.Height < neededHeight - 2)
            {
                row.Height = neededHeight;
                dgv.InvalidateRow(rowIndex);
                lblStatus.Text = "Đã mở rộng toàn bộ nội dung dòng " + (rowIndex + 1) + " (Double-click đáy ô lần nữa để thu gọn).";
            }
            else
            {
                row.Height = BaseRowHeight;
                dgv.InvalidateRow(rowIndex);
                lblStatus.Text = "Đã thu gọn dòng " + (rowIndex + 1) + ".";
            }
        }

        private int CalculateFullRowTextHeight(DataGridView dgv, int rowIndex)
        {
            int maxCellHeight = BaseRowHeight;
            System.Drawing.Font font = dgv.Font;
            TextFormatFlags measureFlags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

            using (Graphics g = dgv.CreateGraphics())
            {
                for (int c = 0; c < dgv.Columns.Count; c++)
                {
                    if (!(dgv.Columns[c] is DataGridViewTextBoxColumn)) continue;

                    string val = Convert.ToString(dgv.Rows[rowIndex].Cells[c].Value ?? "");
                    if (string.IsNullOrEmpty(val)) continue;

                    int textAreaWidth = Math.Max(dgv.Columns[c].Width - 10, 20);
                    Size measured = TextRenderer.MeasureText(g, val, font, new Size(textAreaWidth, int.MaxValue), measureFlags);
                    int requiredH = measured.Height + 12;
                    if (requiredH > maxCellHeight)
                        maxCellHeight = requiredH;
                }
            }

            return maxCellHeight;
        }

        /// <summary>
        /// Quản lý bố cục co giãn toàn khung:
        /// - Khung nhỏ nhất đã được khóa bằng MinimumSize nên không bao giờ mất thông tin.
        /// - Khi kéo to/nhỏ cửa sổ, cả thanh tìm kiếm, bảng chi tiết và các nút dưới đáy đều co giãn theo tỷ lệ.
        /// </summary>
        private void UpdateResponsiveLayout()
        {
            if (tabMain == null || tabSelection == null || tabCloud == null) return;

            int formW = Math.Max(this.ClientSize.Width, MIN_FORM_WIDTH - 16);
            int formH = Math.Max(this.ClientSize.Height, MIN_FORM_HEIGHT - 38);

            tabMain.Location = new Point(8, 8);
            tabMain.Size = new Size(formW - 16, formH - 38);

            if (lblStatus != null)
            {
                lblStatus.Location = new Point(10, tabMain.Bottom + 5);
            }

            // Bố cục Tab 1
            int tab1W = Math.Max(tabSelection.ClientSize.Width, MIN_FORM_WIDTH - 40);
            int tab1H = Math.Max(tabSelection.ClientSize.Height, MIN_FORM_HEIGHT - 80);
            int bottomBarY = tab1H - 50;

            cboTargetStyle.Width = Math.Max(180, Math.Min(320, tab1W - 720));
            chkFixEncoding.Left = cboTargetStyle.Right + 14;

            pnlSearch.Location = new Point(10, 46);
            pnlSearch.Size = new Size(tab1W - 20, 40);
            LayoutSearchPanelControls(pnlSearch.ClientSize.Width);

            dgvTexts.Location = new Point(10, 92);
            dgvTexts.Size = new Size(tab1W - 20, Math.Max(bottomBarY - 98, 150));

            lblLang.Location = new Point(10, bottomBarY + 12);
            cboTargetLang.Location = new Point(52, bottomBarY + 8);

            int btnStartX = 215;
            int totalBtnWidth = Math.Max(tab1W - 10 - btnStartX, 300);
            int halfBtnW = (totalBtnWidth - 14) / 2;

            btnTranslateTable.Location = new Point(btnStartX, bottomBarY);
            btnTranslateTable.Size = new Size(halfBtnW, 44);

            btnApply.Location = new Point(btnStartX + halfBtnW + 14, bottomBarY);
            btnApply.Size = new Size(totalBtnWidth - halfBtnW - 14, 44);

            // Bố cục Tab 2
            int tab2W = Math.Max(tabCloud.ClientSize.Width, MIN_FORM_WIDTH - 40);
            int tab2H = Math.Max(tabCloud.ClientSize.Height, MIN_FORM_HEIGHT - 80);
            int bottom2Y = tab2H - 50;

            txtCloudUrl.Width = Math.Max(tab2W - 120, 300);
            dgvAssets.Location = new Point(15, 50);
            dgvAssets.Size = new Size(tab2W - 30, Math.Max(bottom2Y - 58, 150));

            btnSyncCloud.Location = new Point(15, bottom2Y);
            btnSyncCloud.Size = new Size(tab2W - 30, 44);
        }

        private void LayoutSearchPanelControls(int panelWidth)
        {
            if (pnlSearch == null) return;

            // Neo cụm nút bên phải (Thay toàn bộ, Thay cục bộ, Hoa/thường) luôn cố định không bao giờ bị che
            btnReplaceAll.Location = new Point(panelWidth - btnReplaceAll.Width - 6, 5);
            btnReplaceLocal.Location = new Point(btnReplaceAll.Left - btnReplaceLocal.Width - 5, 5);
            chkMatchCase.Location = new Point(btnReplaceLocal.Left - chkMatchCase.Width - 8, 10);

            int statWidth = Math.Max(lblReplaceStat.PreferredWidth, 95);
            int availForInputs = Math.Max(chkMatchCase.Left - 245 - statWidth, 180);
            int eachBoxWidth = Math.Max(availForInputs / 2, 90);

            lblFind.Location = new Point(6, 10);
            txtSearch.Location = new Point(40, 7);
            txtSearch.Width = eachBoxWidth;

            btnSearchNow.Location = new Point(txtSearch.Right + 4, 5);
            btnZoomPrev.Location = new Point(btnSearchNow.Right + 3, 5);
            btnZoomNext.Location = new Point(btnZoomPrev.Right + 2, 5);

            lblReplace.Location = new Point(btnZoomNext.Right + 6, 10);
            txtReplace.Location = new Point(lblReplace.Right + 2, 7);
            txtReplace.Width = eachBoxWidth;

            lblReplaceStat.Location = new Point(txtReplace.Right + 5, 10);
        }

        /// <summary>
        /// Vẽ nội dung các cột văn bản trong bảng:
        /// - Khi ô nhỏ hơn chữ sẽ hiện dấu '...' (EndEllipsis).
        /// - Khi người dùng Double-click vào đáy ô, dòng bung cao ra và hiển thị đầy đủ tất cả các dòng chữ.
        /// </summary>
        private void Dgv_CellPainting_Ellipsis(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            DataGridView dgv = sender as DataGridView;
            if (dgv == null) return;

            if (e.RowIndex == -1)
            {
                e.PaintBackground(e.ClipBounds, false);
                string headerText = Convert.ToString(e.FormattedValue ?? "");
                if (!string.IsNullOrEmpty(headerText))
                {
                    Rectangle hRect = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 2, Math.Max(e.CellBounds.Width - 8, 2), Math.Max(e.CellBounds.Height - 4, 2));
                    TextFormatFlags hFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                    TextRenderer.DrawText(e.Graphics, headerText, e.CellStyle.Font, hRect, e.CellStyle.ForeColor, hFlags);
                }
                e.Handled = true;
                return;
            }

            if (dgv.Columns[e.ColumnIndex] is DataGridViewTextBoxColumn)
            {
                e.PaintBackground(e.ClipBounds, true);
                string text = Convert.ToString(e.FormattedValue ?? "");
                if (!string.IsNullOrEmpty(text))
                {
                    Rectangle rect = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 4, Math.Max(e.CellBounds.Width - 8, 2), Math.Max(e.CellBounds.Height - 6, 2));
                    Color foreColor = (e.State & DataGridViewElementStates.Selected) != 0
                        ? e.CellStyle.SelectionForeColor
                        : e.CellStyle.ForeColor;

                    System.Drawing.Font cellFont = dgv.Font;
                    TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    string displayStr;

                    if (rect.Height < cellFont.Height * 1.75)
                    {
                        flags |= TextFormatFlags.SingleLine;
                        displayStr = text.Replace("\r\n", " ").Replace("\n", " ");
                    }
                    else
                    {
                        flags |= TextFormatFlags.WordBreak;
                        displayStr = text;
                    }

                    TextRenderer.DrawText(e.Graphics, displayStr, cellFont, rect, foreColor, flags);
                }
                e.Handled = true;
            }
        }

        private void Dgv_CellFormatting_Tooltip(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridView dgv = sender as DataGridView;
            if (dgv != null && dgv.Columns[e.ColumnIndex] is DataGridViewTextBoxColumn)
            {
                DataGridViewCell cell = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex];
                cell.ToolTipText = Convert.ToString(e.Value ?? "");
            }
        }

        // =========================================================================
        // NẠP TEXT STYLE & CẬP NHẬT DANH SÁCH ĐỐI TƯỢNG THEO VÙNG CHỌN TRÊN CAD
        // =========================================================================
        private void LoadDrawingTextStyles()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;

            string prevSelected = cboTargetStyle.SelectedItem != null ? cboTargetStyle.SelectedItem.ToString() : "DB-Segoe UI";
            cboTargetStyle.Items.Clear();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);

                if (!tst.Has("DB-Segoe UI") && !tst.Has("DB-SegoeUI"))
                {
                    TextStyleTableRecord newStyle = new TextStyleTableRecord();
                    newStyle.Name = "DB-Segoe UI";
                    newStyle.FileName = "segoeui.ttf";
                    tst.Add(newStyle);
                    tr.AddNewlyCreatedDBObject(newStyle, true);
                }

                List<StyleComboItem> styleList = new List<StyleComboItem>();
                foreach (ObjectId id in tst)
                {
                    TextStyleTableRecord tstr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (string.IsNullOrEmpty(tstr.Name) || tstr.Name.Contains("|") || tstr.IsShapeFile || tstr.Name.Contains("$"))
                        continue;

                    styleList.Add(new StyleComboItem { Name = tstr.Name, StyleId = id });
                }

                styleList = styleList.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

                int defaultIdx = 0;
                for (int i = 0; i < styleList.Count; i++)
                {
                    cboTargetStyle.Items.Add(styleList[i]);
                    if (styleList[i].Name.Equals(prevSelected, StringComparison.OrdinalIgnoreCase) ||
                        styleList[i].Name.Equals("DB-Segoe UI", StringComparison.OrdinalIgnoreCase))
                    {
                        defaultIdx = i;
                    }
                }

                if (cboTargetStyle.Items.Count > 0)
                    cboTargetStyle.SelectedIndex = defaultIdx;

                tr.Commit();
            }
        }

        private void LoadSelectedTextEntities(ObjectId[] selectedIds)
        {
            for (int i = 0; i < dgvTexts.Rows.Count && i < currentTextItems.Count; i++)
            {
                string existingTrans = Convert.ToString(dgvTexts.Rows[i].Cells["TranslatedText"].Value ?? "");
                if (!string.IsNullOrEmpty(existingTrans))
                {
                    sessionTranslations[currentTextItems[i].EntityId] = existingTrans;
                }
            }

            dgvTexts.Rows.Clear();
            currentTextItems.Clear();
            matchedRowIndices.Clear();
            currentMatchCursor = -1;

            if (selectedIds == null || selectedIds.Length == 0)
            {
                UpdateReplaceStatistics(false);
                lblStatus.Text = "Đã bỏ chọn (0 đối tượng). Chọn chữ trên CAD để tự động nạp vào bảng.";
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            List<TextItemModel> tempList = new List<TextItemModel>();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                HashSet<ObjectId> upperCaseStyles = new HashSet<ObjectId>();
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in tst)
                {
                    TextStyleTableRecord tstr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    string fName = Path.GetFileNameWithoutExtension(tstr.FileName ?? "").ToUpperInvariant();
                    if ((fName.StartsWith(".VN") || fName.StartsWith("VN")) && fName.EndsWith("H"))
                        upperCaseStyles.Add(id);
                }

                foreach (ObjectId id in selectedIds)
                {
                    if (id.IsNull || id.IsErased) continue;
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    DBText txt = ent as DBText;
                    if (txt != null && !string.IsNullOrEmpty(txt.TextString.Trim()))
                    {
                        tempList.Add(CreateTextModel(tr, txt, txt.ObjectId, txt.TextStyleId, "TEXT", txt.TextString, upperCaseStyles.Contains(txt.TextStyleId), txt.Height, txt.Position));
                        continue;
                    }

                    MText mtxt = ent as MText;
                    if (mtxt != null && !string.IsNullOrEmpty(mtxt.Contents.Trim()))
                    {
                        string cleanMText = SmartVietConverter.StripMTextFormatting(mtxt.Contents);
                        if (!string.IsNullOrEmpty(cleanMText.Trim()))
                        {
                            tempList.Add(CreateTextModel(tr, mtxt, mtxt.ObjectId, mtxt.TextStyleId, "MTEXT", cleanMText, upperCaseStyles.Contains(mtxt.TextStyleId), mtxt.TextHeight, mtxt.Location));
                        }
                        continue;
                    }

                    BlockReference blkRef = ent as BlockReference;
                    if (blkRef != null && blkRef.AttributeCollection.Count > 0)
                    {
                        foreach (ObjectId attId in blkRef.AttributeCollection)
                        {
                            AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (att != null && !string.IsNullOrEmpty(att.TextString.Trim()))
                            {
                                tempList.Add(CreateTextModel(tr, att, att.ObjectId, att.TextStyleId, "ATTRIB", att.TextString, upperCaseStyles.Contains(att.TextStyleId), att.Height, att.Position));
                            }
                        }
                    }
                }
                tr.Commit();
            }

            // Mặc định luôn sắp xếp từ Trên-Trái của Box
            tempList = SortEntitiesTopLeftOfBox(tempList);

            foreach (TextItemModel item in tempList)
            {
                currentTextItems.Add(item);
                string savedTrans;
                if (!sessionTranslations.TryGetValue(item.EntityId, out savedTrans))
                    savedTrans = "";

                int rowIdx = dgvTexts.Rows.Add(chkSelectAll.Checked, item.EntityType, item.CurrentStyleName, item.OriginalText, savedTrans);
                dgvTexts.Rows[rowIdx].Height = BaseRowHeight;
                if (!string.IsNullOrEmpty(savedTrans))
                {
                    dgvTexts.Rows[rowIdx].Cells["TranslatedText"].Style.BackColor = Color.FromArgb(235, 248, 255);
                }
            }

            ScaleGridColumnsProportionally(dgvTexts, dgvTextsColRatios);
            UpdateReplaceStatistics(false);
            lblStatus.Text = "Đang hiển thị " + currentTextItems.Count + " đối tượng (Double-click đáy ô '...' để bung hết nội dung).";
        }

        private TextItemModel CreateTextModel(Transaction tr, Entity ent, ObjectId entId, ObjectId styleId, string typeName, string rawText, bool isUpper, double fallbackHeight, Point3d fallbackPt)
        {
            string styleName = "Standard";
            if (!styleId.IsNull)
            {
                TextStyleTableRecord tstr = tr.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord;
                if (tstr != null) styleName = tstr.Name;
            }

            double topY = fallbackPt.Y;
            double leftX = fallbackPt.X;
            double height = fallbackHeight > 0 ? fallbackHeight : 2.5;

            try
            {
                Extents3d ext = ent.GeometricExtents;
                leftX = ext.MinPoint.X;
                topY = ext.MaxPoint.Y;
                double boxH = ext.MaxPoint.Y - ext.MinPoint.Y;
                if (boxH > 0) height = boxH;
            }
            catch { }

            return new TextItemModel
            {
                EntityId = entId,
                ParentStyleId = styleId,
                EntityType = typeName,
                CurrentStyleName = styleName,
                OriginalText = SmartVietConverter.Convert(rawText, isUpper),
                IsUpperCaseFont = isUpper,
                TopY = topY,
                LeftX = leftX,
                ApproxHeight = height
            };
        }

        private List<TextItemModel> SortEntitiesTopLeftOfBox(List<TextItemModel> items)
        {
            if (items.Count <= 1) return items;

            double avgHeight = items.Average(x => x.ApproxHeight > 0 ? x.ApproxHeight : 2.5);
            double rowTolerance = Math.Max(avgHeight * 0.65, 1.0);

            List<TextItemModel> sortedByY = items.OrderByDescending(x => x.TopY).ToList();
            List<TextItemModel> finalSorted = new List<TextItemModel>();

            int i = 0;
            while (i < sortedByY.Count)
            {
                double currentRowY = sortedByY[i].TopY;
                List<TextItemModel> sameRow = new List<TextItemModel>();

                while (i < sortedByY.Count && Math.Abs(sortedByY[i].TopY - currentRowY) <= rowTolerance)
                {
                    sameRow.Add(sortedByY[i]);
                    i++;
                }

                sameRow = sameRow.OrderBy(x => x.LeftX).ToList();
                finalSorted.AddRange(sameRow);
            }

            return finalSorted;
        }

        private void BtnReselect_Click(object sender, EventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            this.WindowState = FormWindowState.Minimized;
            isInternalSelectionUpdate = true;
            try
            {
                SetForegroundWindow(Application.MainWindow.Handle);
                using (DocumentLock docLock = doc.LockDocument())
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\nQuét chọn các đối tượng chữ: ";
                    TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "TEXT,MTEXT,INSERT") };
                    PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));

                    if (psr.Status == PromptStatus.OK && psr.Value != null)
                    {
                        ObjectId[] ids = psr.Value.GetObjectIds();
                        ed.SetImpliedSelection(ids);
                        LoadSelectedTextEntities(ids);
                    }
                }
            }
            finally
            {
                isInternalSelectionUpdate = false;
                this.WindowState = FormWindowState.Normal;
                this.Activate();
            }
        }

        private void BtnLoadAllDrawing_Click(object sender, EventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            isInternalSelectionUpdate = true;
            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                {
                    TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "TEXT,MTEXT,INSERT") };
                    PromptSelectionResult psr = ed.SelectAll(new SelectionFilter(filter));

                    if (psr.Status == PromptStatus.OK && psr.Value != null)
                    {
                        ObjectId[] ids = psr.Value.GetObjectIds();
                        LoadSelectedTextEntities(ids);
                    }
                    else
                    {
                        lblStatus.Text = "Không tìm thấy chữ nào trong bản vẽ.";
                    }
                }
            }
            finally
            {
                isInternalSelectionUpdate = false;
            }
        }

        private void ChkSelectAll_CheckedChanged(object sender, EventArgs e)
        {
            bool state = chkSelectAll.Checked;
            foreach (DataGridViewRow row in dgvTexts.Rows)
            {
                row.Cells["Checked"].Value = state;
            }
            UpdateReplaceStatistics(false);
        }

        // =========================================================================
        // CHỨC NĂNG DỊCH (VI / EN) & ÁP DỤNG (GIỮ NGUYÊN CĂN LỀ + KHUNG CHỮ GỐC)
        // =========================================================================
        private async void BtnTranslateTable_Click(object sender, EventArgs e)
        {
            if (dgvTexts.Rows.Count == 0) return;

            LanguageOptionItem langOpt = cboTargetLang.SelectedItem as LanguageOptionItem;
            string targetLang = langOpt != null ? langOpt.LangCode : "vi";

            btnTranslateTable.Enabled = false;
            int translatedCount = 0;

            for (int i = 0; i < dgvTexts.Rows.Count; i++)
            {
                DataGridViewRow row = dgvTexts.Rows[i];
                bool isChecked = Convert.ToBoolean(row.Cells["Checked"].Value ?? false);
                if (!isChecked) continue;

                string srcText = Convert.ToString(row.Cells["OriginalText"].Value ?? "");
                if (string.IsNullOrEmpty(srcText.Trim())) continue;

                lblStatus.Text = "Đang dịch (" + targetLang.ToUpperInvariant() + ") dòng " + (i + 1) + "/" + dgvTexts.Rows.Count + "...";
                string translated = await CachedTranslator.TranslateMultiLineText(srcText, targetLang);
                row.Cells["TranslatedText"].Value = translated;
                row.Cells["TranslatedText"].Style.BackColor = Color.FromArgb(235, 248, 255);
                row.Cells["TranslatedText"].Style.Alignment = DataGridViewContentAlignment.TopLeft;

                if (i < currentTextItems.Count)
                {
                    sessionTranslations[currentTextItems[i].EntityId] = translated;
                }

                dgvTexts.InvalidateRow(i);
                translatedCount++;
            }

            btnTranslateTable.Enabled = true;
            UpdateReplaceStatistics(false);
            lblStatus.Text = "Đã dịch xong " + translatedCount + " dòng!";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            StyleComboItem targetStyle = cboTargetStyle.SelectedItem as StyleComboItem;
            if (targetStyle == null)
            {
                MessageBox.Show("Vui lòng chọn Style đích!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            bool doFixEncoding = chkFixEncoding.Checked;
            int updatedCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < dgvTexts.Rows.Count; i++)
                {
                    DataGridViewRow row = dgvTexts.Rows[i];
                    bool isChecked = Convert.ToBoolean(row.Cells["Checked"].Value ?? false);
                    if (!isChecked) continue;

                    TextItemModel item = currentTextItems[i];
                    if (item.EntityId.IsNull || item.EntityId.IsErased) continue;

                    Entity ent = tr.GetObject(item.EntityId, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    string transText = Convert.ToString(row.Cells["TranslatedText"].Value ?? "").Trim();
                    string origText = Convert.ToString(row.Cells["OriginalText"].Value ?? "").Trim();

                    string finalText = !string.IsNullOrEmpty(transText)
                        ? transText
                        : (doFixEncoding ? SmartVietConverter.Convert(origText, item.IsUpperCaseFont) : origText);

                    UpdateEntityPreservingGeometry(db, ent, finalText, targetStyle.StyleId, true);

                    row.Cells["OriginalText"].Value = finalText;
                    row.Cells["CurrentStyle"].Value = targetStyle.Name;
                    row.Cells["CurrentStyle"].Style.BackColor = Color.LightGreen;
                    item.OriginalText = finalText;
                    item.CurrentStyleName = targetStyle.Name;
                    item.ParentStyleId = targetStyle.StyleId;
                    updatedCount++;
                }

                tr.Commit();
            }

            doc.Editor.Regen();
            UpdateReplaceStatistics(false);
            lblStatus.Text = "Đã áp dụng cho " + updatedCount + " đối tượng (giữ nguyên căn lề & khung chữ).";
        }

        /// <summary>
        /// Cập nhật nội dung và Style nhưng BẢO TOÀN 100% căn lề, tọa độ neo, khung MText và kích thước chữ đã cài trước đó
        /// </summary>
        private void UpdateEntityPreservingGeometry(Database db, Entity ent, string newText, ObjectId targetStyleId, bool changeStyle)
        {
            DBText txt = ent as DBText;
            if (txt != null)
            {
                double oldHeight = txt.Height;
                double oldWidthFactor = txt.WidthFactor;
                double oldOblique = txt.Oblique;
                TextHorizontalMode oldHMode = txt.HorizontalMode;
                TextVerticalMode oldVMode = txt.VerticalMode;
                Point3d oldPos = txt.Position;
                Point3d oldAlignPt = txt.AlignmentPoint;

                if (changeStyle && !targetStyleId.IsNull)
                    txt.TextStyleId = targetStyleId;

                if (!string.IsNullOrEmpty(newText))
                    txt.TextString = newText.Replace("\r\n", " ").Replace("\n", " ");

                txt.Height = oldHeight;
                txt.WidthFactor = oldWidthFactor;
                txt.Oblique = oldOblique;
                txt.HorizontalMode = oldHMode;
                txt.VerticalMode = oldVMode;

                if (oldHMode == TextHorizontalMode.TextLeft && oldVMode == TextVerticalMode.TextBase)
                {
                    txt.Position = oldPos;
                }
                else
                {
                    txt.AlignmentPoint = oldAlignPt;
                    txt.AdjustAlignment(db);
                }
                return;
            }

            MText mtxt = ent as MText;
            if (mtxt != null)
            {
                AttachmentPoint oldAttachment = mtxt.Attachment;
                Point3d oldLocation = mtxt.Location;
                double oldWidth = mtxt.Width;
                double oldTextHeight = mtxt.TextHeight;
                double oldRotation = mtxt.Rotation;
                double oldLineSpacing = mtxt.LineSpacingFactor;
                LineSpacingStyle oldSpaceStyle = mtxt.LineSpacingStyle;

                if (changeStyle && !targetStyleId.IsNull)
                    mtxt.TextStyleId = targetStyleId;

                if (!string.IsNullOrEmpty(newText))
                    mtxt.Contents = newText.Replace("\r\n", "\\P").Replace("\n", "\\P");

                mtxt.Attachment = oldAttachment;
                mtxt.Location = oldLocation;
                mtxt.Width = oldWidth;
                mtxt.TextHeight = oldTextHeight;
                mtxt.Rotation = oldRotation;
                mtxt.LineSpacingFactor = oldLineSpacing;
                mtxt.LineSpacingStyle = oldSpaceStyle;
                return;
            }

            AttributeReference att = ent as AttributeReference;
            if (att != null)
            {
                double oldHeight = att.Height;
                double oldWidthFactor = att.WidthFactor;
                double oldOblique = att.Oblique;
                TextHorizontalMode oldHMode = att.HorizontalMode;
                TextVerticalMode oldVMode = att.VerticalMode;
                Point3d oldPos = att.Position;
                Point3d oldAlignPt = att.AlignmentPoint;

                if (changeStyle && !targetStyleId.IsNull)
                    att.TextStyleId = targetStyleId;

                if (!string.IsNullOrEmpty(newText))
                    att.TextString = newText.Replace("\r\n", " ").Replace("\n", " ");

                att.Height = oldHeight;
                att.WidthFactor = oldWidthFactor;
                att.Oblique = oldOblique;
                att.HorizontalMode = oldHMode;
                att.VerticalMode = oldVMode;

                if (oldHMode == TextHorizontalMode.TextLeft && oldVMode == TextVerticalMode.TextBase)
                {
                    att.Position = oldPos;
                }
                else
                {
                    att.AlignmentPoint = oldAlignPt;
                    att.AdjustAlignment(db);
                }
            }
        }

        // =========================================================================
        // TÌM KIẾM, THỐNG KÊ SỐ ĐỐI TƯỢNG, ZOOM & THAY THẾ (CỤC BỘ / TOÀN BỘ)
        // =========================================================================
        private void UpdateReplaceStatistics(bool resetCursor)
        {
            matchedRowIndices.Clear();
            if (resetCursor) currentMatchCursor = -1;

            string keyword = txtSearch != null ? txtSearch.Text : "";
            if (string.IsNullOrEmpty(keyword))
            {
                for (int i = 0; i < dgvTexts.Rows.Count; i++)
                {
                    dgvTexts.Rows[i].DefaultCellStyle.BackColor = Color.White;
                }
                if (lblReplaceStat != null)
                {
                    lblReplaceStat.Text = "(" + dgvTexts.Rows.Count + " đối tượng)";
                }
                return;
            }

            StringComparison cmp = (chkMatchCase != null && chkMatchCase.Checked)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            for (int i = 0; i < dgvTexts.Rows.Count; i++)
            {
                DataGridViewRow row = dgvTexts.Rows[i];
                string orig = Convert.ToString(row.Cells["OriginalText"].Value ?? "");
                string trans = Convert.ToString(row.Cells["TranslatedText"].Value ?? "");

                if (orig.IndexOf(keyword, cmp) >= 0 || trans.IndexOf(keyword, cmp) >= 0)
                {
                    matchedRowIndices.Add(i);
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 196);
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.White;
                }
            }

            if (lblReplaceStat != null)
            {
                lblReplaceStat.Text = "(" + matchedRowIndices.Count + "/" + dgvTexts.Rows.Count + " đối tượng)";
            }
        }

        private void PerformSearchAndZoom()
        {
            string keyword = txtSearch.Text;
            if (string.IsNullOrEmpty(keyword))
            {
                lblStatus.Text = "Nhập từ khóa cần tìm!";
                return;
            }

            if (dgvTexts.Rows.Count == 0)
            {
                BtnLoadAllDrawing_Click(null, EventArgs.Empty);
            }

            UpdateReplaceStatistics(true);

            if (matchedRowIndices.Count > 0)
            {
                currentMatchCursor = 0;
                FocusAndZoomToRow(matchedRowIndices[currentMatchCursor]);
                lblStatus.Text = "Khớp " + matchedRowIndices.Count + " đối tượng (Đang xem 1/" + matchedRowIndices.Count + ").";
            }
            else
            {
                lblStatus.Text = "Không tìm thấy '" + keyword + "'.";
            }
        }

        private void NavigateSearchMatch(int direction)
        {
            if (matchedRowIndices.Count == 0)
            {
                PerformSearchAndZoom();
                return;
            }

            currentMatchCursor += direction;
            if (currentMatchCursor >= matchedRowIndices.Count) currentMatchCursor = 0;
            if (currentMatchCursor < 0) currentMatchCursor = matchedRowIndices.Count - 1;

            int rowIdx = matchedRowIndices[currentMatchCursor];
            FocusAndZoomToRow(rowIdx);
            lblStatus.Text = "Đang xem " + (currentMatchCursor + 1) + "/" + matchedRowIndices.Count + ".";
        }

        private void FocusAndZoomToRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvTexts.Rows.Count) return;

            dgvTexts.ClearSelection();
            dgvTexts.Rows[rowIndex].Selected = true;
            dgvTexts.FirstDisplayedScrollingRowIndex = rowIndex;

            ZoomToEntityInCad(currentTextItems[rowIndex].EntityId);
        }

        private void ZoomToEntityInCad(ObjectId entId)
        {
            if (entId.IsNull || entId.IsErased) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent == null) return;

                Point3d minPt = Point3d.Origin;
                Point3d maxPt = new Point3d(100, 100, 0);

                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    minPt = ext.MinPoint;
                    maxPt = ext.MaxPoint;
                }
                catch
                {
                    DBText t = ent as DBText;
                    if (t != null)
                    {
                        minPt = t.Position;
                        maxPt = new Point3d(t.Position.X + t.Height * 5, t.Position.Y + t.Height, t.Position.Z);
                    }
                }

                double width = Math.Max(maxPt.X - minPt.X, 5.0);
                double height = Math.Max(maxPt.Y - minPt.Y, 5.0);

                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    double viewAspect = view.Width / Math.Max(view.Height, 1.0);
                    double targetHeight = height * 4.5;
                    double targetWidth = width * 4.5;

                    if (targetWidth / targetHeight > viewAspect)
                        targetHeight = targetWidth / viewAspect;
                    else
                        targetWidth = targetHeight * viewAspect;

                    Point2d centerPt = new Point2d(
                        (minPt.X + maxPt.X) / 2.0 - view.Target.X,
                        (minPt.Y + maxPt.Y) / 2.0 - view.Target.Y);

                    view.CenterPoint = centerPt;
                    view.Height = targetHeight;
                    view.Width = targetWidth;
                    ed.SetCurrentView(view);
                }

                try { ent.Highlight(); } catch { }

                tr.Commit();
                ed.UpdateScreen();
            }
        }

        private void BtnReplaceLocal_Click(object sender, EventArgs e)
        {
            if (dgvTexts.SelectedRows.Count == 0)
            {
                MessageBox.Show("Chọn một dòng trong bảng để thay cục bộ!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int rowIdx = dgvTexts.SelectedRows[0].Index;
            int count = ExecuteReplaceOnRows(new List<int> { rowIdx });
            FocusAndZoomToRow(rowIdx);
            UpdateReplaceStatistics(false);

            lblStatus.Text = count > 0
                ? "Đã thay cục bộ dòng " + (rowIdx + 1) + " (giữ nguyên căn lề & khung chữ)."
                : "Dòng đang chọn không chứa '" + txtSearch.Text + "'.";
        }

        private void BtnReplaceAll_Click(object sender, EventArgs e)
        {
            List<int> targetRows = new List<int>();
            for (int i = 0; i < dgvTexts.Rows.Count; i++)
            {
                bool isChecked = Convert.ToBoolean(dgvTexts.Rows[i].Cells["Checked"].Value ?? false);
                if (isChecked) targetRows.Add(i);
            }

            int count = ExecuteReplaceOnRows(targetRows);
            UpdateReplaceStatistics(false);
            lblStatus.Text = "Đã thay toàn bộ " + count + " đối tượng (giữ nguyên căn lề & khung chữ).";
        }

        private int ExecuteReplaceOnRows(List<int> rowIndices)
        {
            string findStr = txtSearch.Text;
            string replaceStr = txtReplace.Text ?? "";
            if (string.IsNullOrEmpty(findStr))
            {
                MessageBox.Show("Nhập từ khóa vào ô 'Tìm'!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            Database db = doc.Database;

            RegexOptions regOpt = chkMatchCase.Checked ? RegexOptions.None : RegexOptions.IgnoreCase;
            int replacedCount = 0;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (int i in rowIndices)
                {
                    if (i < 0 || i >= dgvTexts.Rows.Count) continue;

                    DataGridViewRow row = dgvTexts.Rows[i];
                    TextItemModel item = currentTextItems[i];
                    if (item.EntityId.IsNull || item.EntityId.IsErased) continue;

                    string orig = Convert.ToString(row.Cells["OriginalText"].Value ?? "");
                    string trans = Convert.ToString(row.Cells["TranslatedText"].Value ?? "");

                    bool hasMatchOrig = Regex.IsMatch(orig, Regex.Escape(findStr), regOpt);
                    bool hasMatchTrans = !string.IsNullOrEmpty(trans) && Regex.IsMatch(trans, Regex.Escape(findStr), regOpt);

                    if (!hasMatchOrig && !hasMatchTrans) continue;

                    string newOrig = hasMatchOrig ? Regex.Replace(orig, Regex.Escape(findStr), replaceStr, regOpt) : orig;
                    string newTrans = hasMatchTrans ? Regex.Replace(trans, Regex.Escape(findStr), replaceStr, regOpt) : trans;

                    row.Cells["OriginalText"].Value = newOrig;
                    if (hasMatchTrans)
                    {
                        row.Cells["TranslatedText"].Value = newTrans;
                        sessionTranslations[item.EntityId] = newTrans;
                    }
                    row.DefaultCellStyle.BackColor = Color.FromArgb(212, 239, 223);

                    string textToWriteCad = !string.IsNullOrEmpty(newTrans.Trim()) ? newTrans : newOrig;
                    Entity ent = tr.GetObject(item.EntityId, OpenMode.ForWrite) as Entity;
                    if (ent != null)
                    {
                        UpdateEntityPreservingGeometry(db, ent, textToWriteCad, ObjectId.Null, false);
                    }

                    item.OriginalText = newOrig;
                    replacedCount++;
                }

                tr.Commit();
            }

            doc.Editor.Regen();
            return replacedCount;
        }

        private void DgvTexts_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || suppressNextCellClickAction) return;

            string colName = dgvTexts.Columns[e.ColumnIndex].Name;
            if (colName == "CopyBtn")
            {
                CopyRowTextToClipboard(e.RowIndex);
            }
            else if (colName == "ZoomBtn")
            {
                FocusAndZoomToRow(e.RowIndex);
                lblStatus.Text = "Đã Zoom tới dòng " + (e.RowIndex + 1) + ".";
            }
        }

        private void DgvTexts_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // Nếu vừa Double-click vào đáy ô "..." để mở rộng dòng thì không chạy lệnh Zoom/Copy
            if (suppressNextCellClickAction)
            {
                suppressNextCellClickAction = false;
                return;
            }

            if (e.RowIndex >= 0)
            {
                FocusAndZoomToRow(e.RowIndex);
                CopyRowTextToClipboard(e.RowIndex);
            }
        }

        private void CopyRowTextToClipboard(int rowIndex)
        {
            DataGridViewRow row = dgvTexts.Rows[rowIndex];
            string trans = Convert.ToString(row.Cells["TranslatedText"].Value ?? "");
            string orig = Convert.ToString(row.Cells["OriginalText"].Value ?? "");

            string textToCopy = !string.IsNullOrEmpty(trans.Trim()) ? trans : orig;

            if (!string.IsNullOrEmpty(textToCopy))
            {
                Clipboard.SetText(textToCopy);
                lblStatus.Text = "Đã Copy: " + textToCopy.Replace("\r\n", " ").Replace("\n", " ");
            }
        }

        // =========================================================================
        // TAB 2: ĐỒNG BỘ FONT & HATCH THIẾU TỪ CLOUD
        // =========================================================================
        private void ScanDrawingAssets()
        {
            dgvAssets.Rows.Clear();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in tst)
                {
                    TextStyleTableRecord tstr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    string fontFile = tstr.FileName;
                    if (string.IsNullOrEmpty(fontFile)) fontFile = "segoeui.ttf";
                    if (!Path.HasExtension(fontFile)) fontFile += ".shx";

                    bool exists = File.Exists(Path.Combine(cadFontDir, fontFile)) || File.Exists(Path.Combine(winFontDir, fontFile));
                    int idx = dgvAssets.Rows.Add("FONT", tstr.Name, fontFile, exists ? "Đã có" : "THIẾU FILE!");
                    dgvAssets.Rows[idx].Height = BaseRowHeight;
                    if (!exists) HighlightMissingRow(idx);
                }

                HashSet<string> checkedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    foreach (ObjectId entId in btr)
                    {
                        Hatch hatch = tr.GetObject(entId, OpenMode.ForRead) as Hatch;
                        if (hatch != null && hatch.PatternType == HatchPatternType.CustomDefined)
                        {
                            string patFile = hatch.PatternName + ".pat";
                            if (checkedPatterns.Add(patFile))
                            {
                                bool exists = File.Exists(Path.Combine(cadSupportDir, patFile));
                                int idx = dgvAssets.Rows.Add("HATCH", hatch.PatternName, patFile, exists ? "Đã có" : "THIẾU FILE!");
                                dgvAssets.Rows[idx].Height = BaseRowHeight;
                                if (!exists) HighlightMissingRow(idx);
                            }
                        }
                    }
                }
                tr.Commit();
            }

            ScaleGridColumnsProportionally(dgvAssets, dgvAssetsColRatios);
        }

        private void HighlightMissingRow(int rowIdx)
        {
            dgvAssets.Rows[rowIdx].DefaultCellStyle.BackColor = Color.MistyRose;
            dgvAssets.Rows[rowIdx].DefaultCellStyle.ForeColor = Color.DarkRed;
        }

        private async void BtnSyncCloud_Click(object sender, EventArgs e)
        {
            btnSyncCloud.Enabled = false;
            int syncedCount = 0;
            string baseUrl = txtCloudUrl.Text.TrimEnd('/') + "/";

            foreach (DataGridViewRow row in dgvAssets.Rows)
            {
                if (row.Cells["Status"].Value.ToString() != "THIẾU FILE!") continue;

                string type = row.Cells["AssetType"].Value.ToString();
                string fileName = row.Cells["FileName"].Value.ToString();
                string subFolder = type == "FONT" ? "Fonts/" : "HatchPatterns/";
                string destDir = type == "FONT" ? cadFontDir : cadSupportDir;
                string destPath = Path.Combine(destDir, fileName);

                try
                {
                    HttpResponseMessage res = await httpClient.GetAsync(baseUrl + subFolder + fileName);
                    if (res.IsSuccessStatusCode)
                    {
                        byte[] bytes = await res.Content.ReadAsByteArrayAsync();
                        File.WriteAllBytes(destPath, bytes);

                        if (Path.GetExtension(fileName).Equals(".ttf", StringComparison.OrdinalIgnoreCase))
                        {
                            AddFontResource(destPath);
                        }

                        syncedCount++;
                        row.Cells["Status"].Value = "Đã đồng bộ";
                        row.DefaultCellStyle.BackColor = Color.LightGreen;
                        row.DefaultCellStyle.ForeColor = Color.DarkGreen;
                    }
                    else
                    {
                        row.Cells["Status"].Value = "Chưa có trên GitHub";
                    }
                }
                catch
                {
                    row.Cells["Status"].Value = "Lỗi mạng";
                }
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                Database db = doc.Database;
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    foreach (ObjectId id in tst)
                    {
                        TextStyleTableRecord tstr = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                        if (!string.IsNullOrEmpty(tstr.FileName))
                            tstr.FileName = tstr.FileName;
                    }

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    foreach (ObjectId btrId in bt)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                        foreach (ObjectId entId in btr)
                        {
                            Hatch hatch = tr.GetObject(entId, OpenMode.ForRead) as Hatch;
                            if (hatch != null && hatch.PatternType == HatchPatternType.CustomDefined)
                            {
                                if (File.Exists(Path.Combine(cadSupportDir, hatch.PatternName + ".pat")))
                                {
                                    Hatch hWrite = (Hatch)tr.GetObject(entId, OpenMode.ForWrite);
                                    hWrite.SetHatchPattern(HatchPatternType.CustomDefined, hWrite.PatternName);
                                    hWrite.EvaluateHatch(true);
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
                doc.Editor.Regen();
            }

            btnSyncCloud.Enabled = true;
            lblStatus.Text = "Đã đồng bộ " + syncedCount + " Font/Hatch.";
        }
    }

    // =========================================================================
    // BỘ CHUYỂN MÃ TIẾNG VIỆT (VNI / TCVN3 -> UNICODE)
    // =========================================================================
    public static class SmartVietConverter
    {
        private static readonly string[] vniCodes = {
            "aù","aø","aû","aõ","aï","aê","aè","aé","aë","aü","aî","aâ","aà","aá","aå","aã","aä",
            "eù","eø","eû","eõ","eï","eâ","eà","eá","eå","eã","eä","où","oø","oû","oõ","oï",
            "oâ","oà","oá","oå","oã","oä","ôø","ôù","ôû","ôõ","ôï","ô","uù","uø","uû","uõ","uï",
            "öø","öù","öû","öõ","öï","ö","yù","yø","yû","yõ","yï","ñ",
            "AÙ","AØ","AÛ","AÕ","AÏ","AÊ","AÈ","AÉ","AË","AÜ","AÎ","AÂ","AÀ","AÁ","AÅ","AÃ","AÄ",
            "EÙ","EØ","EÛ","EÕ","EÏ","EÂ","EÀ","EÁ","EÅ","EÃ","EÄ","OÙ","OØ","OÛ","OÕ","OÏ",
            "OÂ","OÀ","OÁ","OÅ","OÃ","OÄ","ÔØ","ÔÙ","ÔÛ","ÔÕ","ÔÏ","Ô","UÙ","UØ","UÛ","UÕ","UÏ",
            "ÖØ","ÖÙ","ÖÛ","ÖÕ","ÖÏ","Ö","YÙ","YØ","YÛ","YÕ","YÏ","Ñ"
        };

        private static readonly string[] uniFromVni = {
            "á","à","ả","ã","ạ","ă","ằ","ắ","ẳ","ẵ","ặ","â","ầ","ấ","ẩ","ẫ","ậ",
            "é","è","ẻ","ẽ","ẹ","ê","ề","ế","ể","ễ","ệ","ó","ò","ỏ","õ","ọ",
            "ô","ồ","ố","ổ","ỗ","ộ","ờ","ớ","ở","ỡ","ợ","ơ","ú","ù","ủ","ũ","ụ",
            "ừ","ứ","ử","ữ","ự","ư","ý","ỳ","ỷ","ỹ","ỵ","đ",
            "Á","À","Ả","Ã","Ạ","Ă","Ằ","Ắ","Ẳ","Ẵ","Ặ","Â","Ầ","Ấ","Ẩ","Ẫ","Ậ",
            "É","È","Ẻ","Ẽ","Ẹ","Ê","Ề","Ế","Ể","Ễ","Ệ","Ó","Ò","Ỏ","Õ","Ọ",
            "Ô","Ồ","Ố","Ổ","Ỗ","Ộ","Ờ","Ớ","Ở","Ỡ","Ợ","Ơ","Ú","Ù","Ủ","Ũ","Ụ",
            "Ừ","Ứ","Ử","Ữ","Ự","Ư","Ý","Ỳ","Ỷ","Ỹ","Ỵ","Đ"
        };

        private static readonly char[] tcvnChars = {
            '¸','µ','¶','·','¹','¨','»','¾','¼','½','Æ','©','Ç','Ê','È','É','Ë','®',
            'Ì','Î','Ï','Ñ','ª','Ò','Õ','Ó','Ô','Ö','×','Ý','Ø','Ü','Þ','ß','ä','á',
            'ã','å','«','è','æ','ç','é','¬','ê','í','ë','ì','î','ï','ó','ñ','ò',
            'ô','­','õ','ø','ö','÷','ù','ú','ý','û','ü','þ','¡','¢','£','¤','¥','§','°'
        };

        private static readonly string[] uniFromTcvn = {
            "á","à","ả","ã","ạ","ă","ằ","ắ","ẳ","ẵ","ặ","â","ầ","ấ","ẩ","ẫ","ậ","đ",
            "è","ẻ","ẽ","ẹ","ê","ề","ế","ể","ễ","ệ","ì","í","ỉ","ĩ","ị","ò","ọ","ỏ",
            "õ","ọ","ô","ố","ổ","ỗ","ộ","ơ","ờ","ớ","ở","ỡ","ợ","ù","ú","ủ","ũ",
            "ụ","ư","ừ","ứ","ử","ữ","ự","ỳ","ý","ỷ","ỹ","ỵ","Ă","Â","Đ","Ê","Ô","Ơ","Ư"
        };

        private static readonly char[] tcvnSignatureChars = {
            '¸','µ','¶','·','¨','»','Æ','Ç','Ê','È','É','Ë','ª','«','¬','­'
        };

        private static readonly Regex alreadyUnicodeRegex = new Regex(
            @"[\u0102\u0103\u0110\u0111\u0128\u0129\u0168\u0169\u01A0\u01A1\u01AF\u01B0\u1EA0-\u1EF9\u4E00-\u9FFF]",
            RegexOptions.Compiled);

        public static string StripMTextFormatting(string mtext)
        {
            if (string.IsNullOrEmpty(mtext)) return mtext;

            string s = DecodeCadEscapes(mtext);
            s = Regex.Replace(s, @"\\P", Environment.NewLine);
            s = Regex.Replace(s, @"\\[fFAHWCQTp][^;]*;", "");
            s = Regex.Replace(s, @"\\S([^;^#]+)[#^/]([^;]+);", "$1/$2");
            s = Regex.Replace(s, @"\\[LlOoKk]", "");
            s = s.Replace("\\~", " ").Replace("{", "").Replace("}", "");
            return s;
        }

        public static string DecodeCadEscapes(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            input = Regex.Replace(input, @"\\[Uu]\+([0-9A-Fa-f]{4})", m =>
            {
                int code = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
                return ((char)code).ToString();
            });

            input = Regex.Replace(input, @"%%[cC]", "Ø");
            input = Regex.Replace(input, @"%%[dD]", "°");
            input = Regex.Replace(input, @"%%[pP]", "±");
            input = Regex.Replace(input, @"%%[uUoO]", "");

            input = Regex.Replace(input, @"%%(\d{2,3})", m =>
            {
                int code;
                if (int.TryParse(m.Groups[1].Value, out code) && code >= 32 && code <= 255)
                    return Encoding.GetEncoding(1252).GetString(new byte[] { (byte)code });
                return m.Value;
            });

            return input;
        }

        public static string Convert(string input, bool forceUpperCase)
        {
            if (string.IsNullOrEmpty(input)) return input;

            input = DecodeCadEscapes(input);

            if (alreadyUnicodeRegex.IsMatch(input))
            {
                return forceUpperCase ? input.ToUpperInvariant() : input;
            }

            bool isVni = false;
            for (int i = 0; i < vniCodes.Length; i++)
            {
                string code = vniCodes[i];
                if ((code.Length == 2 || code == "ö" || code == "Ö" || code == "ñ" || code == "Ñ") && input.Contains(code))
                {
                    isVni = true;
                    break;
                }
            }

            if (isVni)
            {
                for (int i = 0; i < vniCodes.Length; i++)
                {
                    if (vniCodes[i].Length == 2) input = input.Replace(vniCodes[i], uniFromVni[i]);
                }
                for (int i = 0; i < vniCodes.Length; i++)
                {
                    if (vniCodes[i].Length == 1) input = input.Replace(vniCodes[i], uniFromVni[i]);
                }
                return forceUpperCase ? input.ToUpperInvariant() : input;
            }

            if (input.IndexOfAny(tcvnSignatureChars) >= 0 || (forceUpperCase && input.IndexOfAny(tcvnChars) >= 0))
            {
                StringBuilder sb = new StringBuilder(input.Length);
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    if ((c == 'Ø' || c == '×' || c == '÷' || c == '°') &&
                        ((i + 1 < input.Length && char.IsDigit(input[i + 1])) || (i > 0 && char.IsDigit(input[i - 1]))))
                    {
                        sb.Append(c);
                        continue;
                    }

                    int idx = Array.IndexOf(tcvnChars, c);
                    sb.Append(idx >= 0 ? uniFromTcvn[idx] : c.ToString());
                }
                string res = sb.ToString();
                return forceUpperCase ? res.ToUpperInvariant() : res;
            }

            return forceUpperCase ? input.ToUpperInvariant() : input;
        }
    }

    // =========================================================================
    // BỘ DỊCH ĐA NGÔN NGỮ (VI / EN)
    // =========================================================================
    public static class CachedTranslator
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static async Task<string> TranslateMultiLineText(string text, string targetLang = "vi")
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(text.Trim())) return text;

            string[] lines = text.Split(new string[] { "\r\n", "\n", "\\P" }, StringSplitOptions.None);
            List<string> translatedLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrEmpty(lines[i].Trim()))
                {
                    string transLine = await TranslateSingleLine(lines[i], targetLang);
                    translatedLines.Add(transLine);
                }
            }

            return string.Join(Environment.NewLine, translatedLines);
        }

        public static async Task<string> TranslateSingleLine(string text, string targetLang = "vi")
        {
            string trimmed = SmartVietConverter.StripMTextFormatting(text).Trim();
            double dummy;
            if (string.IsNullOrEmpty(trimmed) || double.TryParse(trimmed, out dummy)) return trimmed;

            string cacheKey = targetLang + "::" + trimmed;
            string cachedResult;
            if (cache.TryGetValue(cacheKey, out cachedResult))
                return cachedResult;

            try
            {
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl="
                             + HttpUtility.UrlEncode(targetLang)
                             + "&dt=t&q=" + HttpUtility.UrlEncode(trimmed);

                byte[] responseBytes = await client.GetByteArrayAsync(url);
                string response = Encoding.UTF8.GetString(responseBytes);

                string finalStr = ParseGoogleTranslateJson(response);
                if (!string.IsNullOrEmpty(finalStr))
                {
                    cache[cacheKey] = finalStr;
                    return finalStr;
                }
            }
            catch { }
            return trimmed;
        }

        private static string ParseGoogleTranslateJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;

            int i = 0;
            int n = json.Length;

            while (i < n && json[i] != '[') i++;
            i++;

            while (i < n && char.IsWhiteSpace(json[i])) i++;
            if (i >= n || json[i] != '[') return string.Empty;
            i++;

            StringBuilder result = new StringBuilder();

            while (i < n)
            {
                while (i < n && (char.IsWhiteSpace(json[i]) || json[i] == ',')) i++;
                if (i >= n || json[i] == ']') break;

                if (json[i] == '[')
                {
                    i++;
                    while (i < n && char.IsWhiteSpace(json[i])) i++;

                    if (i < n && json[i] == '"')
                    {
                        string segTrans = ReadJsonString(json, ref i);
                        result.Append(segTrans);
                    }

                    int depth = 1;
                    while (i < n && depth > 0)
                    {
                        if (json[i] == '"')
                        {
                            ReadJsonString(json, ref i);
                        }
                        else if (json[i] == '[')
                        {
                            depth++;
                            i++;
                        }
                        else if (json[i] == ']')
                        {
                            depth--;
                            i++;
                        }
                        else
                        {
                            i++;
                        }
                    }
                }
                else
                {
                    i++;
                }
            }

            return result.ToString().Trim();
        }

        private static string ReadJsonString(string json, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            int n = json.Length;
            if (i < n && json[i] == '"') i++;

            while (i < n)
            {
                char c = json[i++];
                if (c == '"') break;

                if (c == '\\' && i < n)
                {
                    char esc = json[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= n)
                            {
                                string hex = json.Substring(i, 4);
                                int code;
                                if (int.TryParse(hex, NumberStyles.HexNumber, null, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}