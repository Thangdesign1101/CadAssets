// ============================================================================
// HƯỚNG DẪN BIÊN DỊCH ĐA NỀN TẢNG (ZWCAD & AUTOCAD):
// - Mặc định đang bật #define ZWCAD để Build cho ZWCAD.
// - Khi muốn Build cho AutoCAD: thêm dấu // trước dòng #define ZWCAD (thành //#define ZWCAD)
// ============================================================================
#define ZWCAD

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Colors;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Windows;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadColor = ZwSoft.ZwCAD.Colors.Color;
#else
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using CadColor = Autodesk.AutoCAD.Colors.Color;
#endif

// Khai báo tường minh tuyệt đối để tránh xung đột tên giữa WinForms và CAD SDK
using WinColor = System.Drawing.Color;
using WinFont = System.Drawing.Font;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GdiHatchStyle = System.Drawing.Drawing2D.HatchStyle;
using GdiDashStyle = System.Drawing.Drawing2D.DashStyle;
using WinFlowDirection = System.Windows.Forms.FlowDirection;
using WinOpenFileDialog = System.Windows.Forms.OpenFileDialog;
using WinFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WinDataTable = System.Data.DataTable;

[assembly: CommandClass(typeof(DB_ArchitectStudio.DBLibraryCommand))]

namespace DB_ArchitectStudio
{
    public enum DoorPlacementAlignMode
    {
        Center = 0,
        InnerEdge = 1,
        OuterEdge = 2
    }

    // =========================================================================
    // 1. LỆNH KHỞI ĐỘNG PALETTESET (MỞ RỘNG CHUẨN KÍCH THƯỚC HÌNH 2 NGAY KHI BẬT)
    // =========================================================================
    public class DBLibraryCommand
    {
        private static PaletteSet _ps = null;
        private static DBLibraryMainControl _mainCtrl = null;

        [CommandMethod("DBLIB")]
        public void OpenDBLibraryPalette()
        {
            ShowStudioPalette();
        }

        [CommandMethod("DBDOOR")]
        public void OpenDBDoorPalette()
        {
            ShowStudioPalette();
        }

        private void ShowStudioPalette()
        {
            if (_ps == null)
            {
                // Dùng Guid mới để xóa bỏ bộ nhớ cache khung hẹp cũ trong Registry của ZWCAD
                _ps = new PaletteSet("DB Architect - Dynamic Library & Schedule Studio",
                    new Guid("A9B8C7D6-E5F4-4321-8765-123456789ABC"));
                _ps.Style = PaletteSetStyles.ShowAutoHideButton |
                            PaletteSetStyles.ShowCloseButton |
                            PaletteSetStyles.ShowPropertiesMenu;

                _ps.DockEnabled = DockSides.None; // Mở dạng cửa sổ chuẩn Hình 2 để không bị ép nhỏ vào lề trái
                _ps.MinimumSize = new Size(980, 560);
                _ps.Size = new Size(1080, 620);

                _mainCtrl = new DBLibraryMainControl();
                _ps.Add("DB Library Studio", _mainCtrl);
            }

            _ps.Visible = true;

            // Đảm bảo kích thước luôn mở rộng chuẩn Hình 2 (không bao giờ bị co nhỏ như Hình 1)
            try
            {
                if (_ps.Size.Width < 980 || _ps.Size.Height < 560)
                {
                    _ps.Size = new Size(1080, 620);
                }
                _mainCtrl.ForceLayoutRefresh();
            }
            catch { }
        }
    }

    // =========================================================================
    // 2. DATA MODELS QUẢN LÝ BLOCK & BIẾN DYNAMIC
    // =========================================================================
    public class BlockItemInfo
    {
        public string BlockName { get; set; }
        public string Category { get; set; }
        public string SourceDwgPath { get; set; }
        public bool IsDynamic { get; set; }
        public Bitmap Thumbnail { get; set; }
        public List<Tuple<PointF, PointF, int>> PreviewLines { get; set; } = new List<Tuple<PointF, PointF, int>>();
    }

    public class DynParamModel
    {
        public string PropertyName { get; set; }
        public object CurrentValue { get; set; }
        public List<object> AllowedValues { get; set; } = new List<object>();
        public bool IsReadOnly { get; set; }
        public bool IsVisibilityOrLookup { get; set; }
    }

    // =========================================================================
    // 3. SMART PLACEMENT JIG: CON TRỎ CHUỘT ĐI LIỀN VỚI ĐỐI TƯỢNG Ở MỌI TAB
    // =========================================================================
    public class SmartWallPlacementJig : EntityJig
    {
        private Point3d _cursorPt;
        private Vector3d _gripToOriginOffset;
        private bool _autoAlignToWall;
        private List<Curve> _candidateWalls;
        private double _baseUserAngleRad;
        private int _manualRotateStep90 = 0;

        public Point3d PickedCursorPoint => _cursorPt;
        public Point3d FinalBlockOriginPoint { get; private set; }
        public double FinalRotation { get; private set; } = 0.0;
        public Curve MatchedWall { get; private set; } = null;

        public SmartWallPlacementJig(
            BlockReference br,
            Vector3d gripToOriginOffset,
            double baseUserAngleRad,
            bool autoAlignToWall,
            List<Curve> candidateWalls) : base(br)
        {
            _gripToOriginOffset = gripToOriginOffset;
            _baseUserAngleRad = baseUserAngleRad;
            _autoAlignToWall = autoAlignToWall;
            _candidateWalls = candidateWalls ?? new List<Curve>();
            _cursorPt = Point3d.Origin;
            FinalBlockOriginPoint = Point3d.Origin;
            FinalRotation = _baseUserAngleRad;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            string msg = _autoAlignToWall
                ? "\n[DBLIB] Pick point on Wall/Model (Keywords: [Flip] 180° / [Rotate] 90°): "
                : "\n[DBLIB] Pick insertion point on Model (Keywords: [Flip] 180° / [Rotate] 90°): ";

            JigPromptPointOptions jppo = new JigPromptPointOptions(msg);
            jppo.Keywords.Add("Flip");
            jppo.Keywords.Add("Rotate");
            jppo.UserInputControls = UserInputControls.Accept3dCoordinates |
                                     UserInputControls.NoZeroResponseAccepted |
                                     UserInputControls.NoNegativeResponseAccepted |
                                     UserInputControls.NullResponseAccepted;

            PromptPointResult ppr = prompts.AcquirePoint(jppo);
            if (ppr.Status == PromptStatus.Keyword)
            {
                if (ppr.StringResult.Equals("Flip", StringComparison.OrdinalIgnoreCase))
                {
                    _manualRotateStep90 = (_manualRotateStep90 + 2) % 4;
                    return SamplerStatus.OK;
                }
                if (ppr.StringResult.Equals("Rotate", StringComparison.OrdinalIgnoreCase))
                {
                    _manualRotateStep90 = (_manualRotateStep90 + 1) % 4;
                    return SamplerStatus.OK;
                }
                return SamplerStatus.NoChange;
            }

            if (ppr.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            if (ppr.Value.DistanceTo(_cursorPt) < 1e-4)
                return SamplerStatus.NoChange;

            _cursorPt = ppr.Value;
            return SamplerStatus.OK;
        }

        protected override bool Update()
        {
            BlockReference br = (BlockReference)Entity;
            double computedAngle = _baseUserAngleRad;

            if (_autoAlignToWall && _candidateWalls.Count > 0)
            {
                Curve nearestWall = null;
                Point3d nearestPtOnWall = _cursorPt;
                double minDist = 600.0;

                foreach (Curve crv in _candidateWalls)
                {
                    try
                    {
                        Point3d pt = crv.GetClosestPointTo(_cursorPt, false);
                        double d = pt.DistanceTo(_cursorPt);
                        if (d < minDist)
                        {
                            minDist = d;
                            nearestWall = crv;
                            nearestPtOnWall = pt;
                        }
                    }
                    catch { }
                }

                if (nearestWall != null)
                {
                    MatchedWall = nearestWall;
                    try
                    {
                        Vector3d tangent = nearestWall.GetFirstDerivative(nearestPtOnWall);
                        Vector3d tangent2d = new Vector3d(tangent.X, tangent.Y, 0).GetNormal();

                        if (tangent2d.X < -1e-4 || (Math.Abs(tangent2d.X) <= 1e-4 && tangent2d.Y < -1e-4))
                        {
                            tangent2d = -tangent2d;
                        }

                        computedAngle = Math.Atan2(tangent2d.Y, tangent2d.X) + _baseUserAngleRad;
                    }
                    catch { }
                }
                else
                {
                    MatchedWall = null;
                }
            }

            if (_manualRotateStep90 != 0)
            {
                computedAngle += _manualRotateStep90 * (Math.PI / 2.0);
            }

            FinalRotation = computedAngle;
            br.Rotation = FinalRotation;

            Vector3d rotatedOffset = _gripToOriginOffset.RotateBy(FinalRotation, Vector3d.ZAxis);
            FinalBlockOriginPoint = _cursorPt + rotatedOffset;
            br.Position = FinalBlockOriginPoint;
            return true;
        }
    }

    // =========================================================================
    // 4. GIAO DIỆN CHÍNH 3 CỘT CHUẨN HÌNH 2 + ICON 75% + DOUBLE CLICK ĐẶT THẢ LUÔN
    // =========================================================================
    public class DBLibraryMainControl : UserControl
    {
        private string _libraryRootFolder = @"D:\DB_CAD_Library";
        private string _categoriesConfigFile => Path.Combine(_libraryRootFolder, "_CategoriesConfig.txt");

        private List<string> _categories = new List<string>
        {
            "Door", "Window", "Furniture", "Tree", "Kitchen", "Bathroom", "WC", "Symbol", "People", "Vehicle"
        };

        private readonly HashSet<string> _ignoredSystemBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_ARCHTICK", "_OPEN", "_OPEN30", "_OPEN90", "_DOT", "_DOTSMALL", "_DOTBLANK",
            "_CLOSED", "_CLOSEDBLANK", "_NONE", "_OBLIQUE", "_BOXFILLED", "_BOXBLANK",
            "_DATUMFILLED", "_DATUMBLANK", "_INTEGRAL", "_ORIGIN", "_ORIGIN2", "_SMALL"
        };

        private SplitContainer splitCol1And23;
        private SplitContainer splitCol2And3;
        private bool _isUpdatingSplitters = false;
        private double _col1Ratio = 0.22;
        private double _col2Ratio = 0.23;

        // Cột 1
        private ComboBox cboCategory;
        private Button btnAddCategoryTab, btnRemoveCategoryTab;
        private TextBox txtSearchBlock;
        private FlowLayoutPanel flpBlockIcons;
        private Button btnAddFromCad, btnLoadDwg, btnSetFolder;

        // Cột 2
        private Label lblSelectedBlockTitle;
        private TableLayoutPanel tblParams;
        private GroupBox grpInsertOptions;
        private Label lblOpeningTitle, lblDepthTitle;
        private TextBox txtOpeningWidth;
        private TextBox txtWallDepth;
        private ComboBox cboPlacementAlign;
        private TextBox txtDoorTag;
        private Button btnInsertToCad;

        // Cột 3
        private TabControl tabRightPanel;
        private CheckBox chkShowAutoDims;
        private Button btnResetZoom;
        private ZoomablePreviewCanvas picCanvas;

        // Schedule Excel
        private DataGridView dgvSchedule;
        private WinDataTable _scheduleTable;
        private ComboBox cboScheduleFilter;
        private Button btnPickScheduleFromCad, btnAddRow, btnDeleteRow, btnClearSchedule, btnExportTableToCad;

        private Label lblStatus;

        private List<BlockItemInfo> _libraryBlocks = new List<BlockItemInfo>();
        private BlockItemInfo _activeBlock = null;
        private List<DynParamModel> _activeParams = new List<DynParamModel>();
        private List<Tuple<PointF, PointF, int>> _liveEvaluatedLines = new List<Tuple<PointF, PointF, int>>();
        private double _currentBlockScale = 1.0;

        public DBLibraryMainControl()
        {
            LoadAndEnsureCategoryStructure();
            InitializeStudioUI();
            InitScheduleDataTable();
            if (cboCategory.SelectedItem != null)
                ScanCategoryFolder(cboCategory.SelectedItem.ToString());
        }

        public void ForceLayoutRefresh()
        {
            ApplyResponsiveColumnProportions();
            AutoFitCurrentPreview();
        }

        private string SanitizeSafeFileName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "Block_Item";
            string clean = rawName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                clean = clean.Replace(c, '_');
            }
            foreach (char c in Path.GetInvalidPathChars())
            {
                clean = clean.Replace(c, '_');
            }
            clean = Regex.Replace(clean, @"[<>:""/\\|?*$]", "_");
            return string.IsNullOrWhiteSpace(clean) ? "Block_Item" : clean;
        }

        // Chỉ duy nhất Tab Door và Window mới sử dụng Wipeout che tường; các Tab khác (Furniture, Tree...) KHÔNG tạo Wipeout!
        private bool IsDoorOrWindowCategory(string category)
        {
            string c = (category ?? "").Trim().ToUpperInvariant();
            return c.Contains("DOOR") || c.Contains("WIN") || c.Contains("CUA");
        }

        private void LoadAndEnsureCategoryStructure()
        {
            try
            {
                if (!Directory.Exists(_libraryRootFolder))
                    Directory.CreateDirectory(_libraryRootFolder);

                if (File.Exists(_categoriesConfigFile))
                {
                    var savedCats = File.ReadAllLines(_categoriesConfigFile)
                        .Select(x => SanitizeSafeFileName(x))
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (savedCats.Count > 0)
                        _categories = savedCats;
                }

                foreach (string subDir in Directory.GetDirectories(_libraryRootFolder))
                {
                    string folderName = Path.GetFileName(subDir).Trim();
                    if (!string.IsNullOrEmpty(folderName) && !folderName.StartsWith("_") &&
                        !_categories.Any(c => c.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _categories.Add(folderName);
                    }
                }

                SaveCategoryConfigFile();

                foreach (string cat in _categories)
                {
                    string safeCat = SanitizeSafeFileName(cat);
                    string catDir = Path.Combine(_libraryRootFolder, safeCat);
                    if (!Directory.Exists(catDir))
                        Directory.CreateDirectory(catDir);
                }
            }
            catch { }
        }

        private void SaveCategoryConfigFile()
        {
            try
            {
                if (!Directory.Exists(_libraryRootFolder))
                    Directory.CreateDirectory(_libraryRootFolder);
                File.WriteAllLines(_categoriesConfigFile, _categories);
            }
            catch { }
        }

        private void InitializeStudioUI()
        {
            this.Dock = DockStyle.Fill;
            this.Size = new Size(1080, 620);
            this.BackColor = WinColor.FromArgb(244, 246, 250);
            this.Font = new WinFont("Segoe UI", 8.75f);

            Panel pnlStatus = new Panel() { Dock = DockStyle.Bottom, Height = 28, BackColor = WinColor.FromArgb(225, 230, 238), Padding = new Padding(8, 4, 8, 4) };
            lblStatus = new Label()
            {
                Text = "Tip: Double-click Icon image to insert directly to Model! Double-click Block text to rename.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = WinColor.FromArgb(30, 45, 70)
            };
            pnlStatus.Controls.Add(lblStatus);

            splitCol1And23 = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical,
                SplitterWidth = 6,
                IsSplitterFixed = false,
                BackColor = WinColor.FromArgb(195, 202, 215)
            };

            splitCol2And3 = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical,
                SplitterWidth = 6,
                IsSplitterFixed = false,
                BackColor = WinColor.FromArgb(195, 202, 215)
            };

            splitCol1And23.SplitterMoved += (s, e) =>
            {
                if (!_isUpdatingSplitters && this.Width > 500)
                    _col1Ratio = Math.Max(0.15, Math.Min(0.45, (double)splitCol1And23.SplitterDistance / this.Width));
            };
            splitCol2And3.SplitterMoved += (s, e) =>
            {
                if (!_isUpdatingSplitters && this.Width > 500)
                    _col2Ratio = Math.Max(0.15, Math.Min(0.45, (double)splitCol2And3.SplitterDistance / this.Width));
            };

            splitCol1And23.Panel2.Controls.Add(splitCol2And3);
            this.SizeChanged += (s, e) => ApplyResponsiveColumnProportions();
            this.Load += (s, e) => ApplyResponsiveColumnProportions();

            // ================= CỘT 1: CATEGORY TABS & BLOCK ICONS (75% COMPACT SIZE) =================
            Panel pnlCol1 = new Panel() { Dock = DockStyle.Fill, BackColor = WinColor.White, Padding = new Padding(6) };

            GroupBox grpCat = new GroupBox()
            {
                Text = "1. Category & Library Blocks",
                Dock = DockStyle.Top,
                Height = 114,
                Font = new WinFont("Segoe UI", 8.75f, FontStyle.Bold)
            };

            TableLayoutPanel tlpCat = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(2, 2, 2, 0)
            };
            tlpCat.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
            tlpCat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpCat.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tlpCat.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tlpCat.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            TableLayoutPanel tlpTabLine = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            tlpTabLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpTabLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24f));
            tlpTabLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24f));
            tlpTabLine.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            cboCategory = new ComboBox()
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Regular),
                Margin = new Padding(0, 1, 2, 1)
            };
            cboCategory.Items.AddRange(_categories.ToArray());
            if (cboCategory.Items.Count > 0) cboCategory.SelectedIndex = 0;
            cboCategory.SelectedIndexChanged += (s, e) =>
            {
                if (cboCategory.SelectedItem != null)
                {
                    string cat = cboCategory.SelectedItem.ToString();
                    UpdateWipeoutSectionVisibilityByTab(cat);
                    ScanCategoryFolder(cat);
                }
            };

            btnAddCategoryTab = new Button()
            {
                Text = "+",
                Size = new Size(22, 22),
                Margin = new Padding(1, 1, 1, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(232, 248, 238),
                ForeColor = WinColor.FromArgb(16, 137, 62),
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnAddCategoryTab.FlatAppearance.BorderColor = WinColor.FromArgb(180, 220, 195);
            btnAddCategoryTab.Click += BtnAddCategoryTab_Click;

            btnRemoveCategoryTab = new Button()
            {
                Text = "-",
                Size = new Size(22, 22),
                Margin = new Padding(1, 1, 0, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(255, 236, 236),
                ForeColor = WinColor.Crimson,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnRemoveCategoryTab.FlatAppearance.BorderColor = WinColor.FromArgb(235, 185, 185);
            btnRemoveCategoryTab.Click += BtnRemoveCategoryTab_Click;

            tlpTabLine.Controls.Add(cboCategory, 0, 0);
            tlpTabLine.Controls.Add(btnAddCategoryTab, 1, 0);
            tlpTabLine.Controls.Add(btnRemoveCategoryTab, 2, 0);

            txtSearchBlock = new TextBox()
            {
                Dock = DockStyle.Fill,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Regular),
                Margin = new Padding(0, 2, 0, 2)
            };
            txtSearchBlock.TextChanged += (s, e) => RenderBlockIconsInCol1();

            TableLayoutPanel tlpImportBtns = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 1, 0, 1)
            };
            tlpImportBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            tlpImportBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            tlpImportBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26f));
            tlpImportBtns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            btnAddFromCad = new Button()
            {
                Text = "+ Add CAD",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 2, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(230, 242, 255),
                Font = new WinFont("Segoe UI", 7.6f, FontStyle.Bold)
            };
            btnLoadDwg = new Button()
            {
                Text = "+ Load DWG",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 2, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new WinFont("Segoe UI", 7.6f, FontStyle.Regular)
            };
            btnSetFolder = new Button()
            {
                Text = "...",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                FlatStyle = FlatStyle.Flat,
                Font = new WinFont("Segoe UI", 7.6f, FontStyle.Regular)
            };

            btnAddFromCad.Click += BtnAddFromCad_Click;
            btnLoadDwg.Click += BtnLoadDwg_Click;
            btnSetFolder.Click += BtnSetFolder_Click;

            tlpImportBtns.Controls.Add(btnAddFromCad, 0, 0);
            tlpImportBtns.Controls.Add(btnLoadDwg, 1, 0);
            tlpImportBtns.Controls.Add(btnSetFolder, 2, 0);

            tlpCat.Controls.Add(new Label() { Text = "Tab:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new WinFont("Segoe UI", 8.25f) }, 0, 0);
            tlpCat.Controls.Add(tlpTabLine, 1, 0);
            tlpCat.Controls.Add(new Label() { Text = "Search:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new WinFont("Segoe UI", 8.25f) }, 0, 1);
            tlpCat.Controls.Add(txtSearchBlock, 1, 1);
            tlpCat.Controls.Add(new Label() { Text = "Import:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new WinFont("Segoe UI", 8.25f) }, 0, 2);
            tlpCat.Controls.Add(tlpImportBtns, 1, 2);
            grpCat.Controls.Add(tlpCat);

            flpBlockIcons = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = WinColor.FromArgb(248, 249, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(4),
                AllowDrop = true
            };
            flpBlockIcons.DragEnter += FlpBlockIcons_DragEnter;
            flpBlockIcons.DragDrop += FlpBlockIcons_DragDrop;

            pnlCol1.Controls.Add(flpBlockIcons);
            pnlCol1.Controls.Add(grpCat);
            splitCol1And23.Panel1.Controls.Add(pnlCol1);

            // ================= CỘT 2: DYNAMIC PROPERTIES + PLACEMENT ALIGN (ĐÃ BỎ YES/NO WIPEOUT) =================
            Panel pnlCol2 = new Panel() { Dock = DockStyle.Fill, BackColor = WinColor.White, Padding = new Padding(6) };

            GroupBox grpProps = new GroupBox()
            {
                Text = "2. Dynamic Properties",
                Dock = DockStyle.Fill,
                Font = new WinFont("Segoe UI", 8.75f, FontStyle.Bold)
            };

            lblSelectedBlockTitle = new Label()
            {
                Text = "No Block Selected",
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = WinColor.FromArgb(235, 243, 255),
                ForeColor = WinColor.FromArgb(0, 80, 160),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold),
                AutoEllipsis = true
            };

            Panel pnlScrollProps = new Panel() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4) };
            tblParams = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Regular)
            };
            tblParams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            tblParams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
            pnlScrollProps.Controls.Add(tblParams);

            grpProps.Controls.Add(pnlScrollProps);
            grpProps.Controls.Add(lblSelectedBlockTitle);

            // Mục 3: Đã bỏ Yes/No. Với Door/Window thì tự động đặt Wipeout bên dưới; với Furniture/Tree thì tự động ẩn ô Wipeout!
            grpInsertOptions = new GroupBox()
            {
                Text = "3. Placement Align & Wipeout (Door/Window)",
                Dock = DockStyle.Bottom,
                Height = 142,
                Font = new WinFont("Segoe UI", 8.75f, FontStyle.Bold)
            };

            TableLayoutPanel tlpOpts = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(6, 4, 6, 2),
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Regular)
            };
            tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128f));
            tlpOpts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpOpts.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tlpOpts.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tlpOpts.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tlpOpts.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            lblOpeningTitle = new Label()
            {
                Text = "Opening (Wipeout W):",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = WinColor.FromArgb(180, 50, 0)
            };
            txtOpeningWidth = new TextBox()
            {
                Text = "1000",
                Dock = DockStyle.Fill,
                BackColor = WinColor.FromArgb(255, 250, 240),
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold)
            };
            txtOpeningWidth.TextChanged += (s, e) => picCanvas.Invalidate();

            lblDepthTitle = new Label()
            {
                Text = "Wall Depth (Wipeout D):",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = WinColor.FromArgb(180, 50, 0)
            };
            txtWallDepth = new TextBox()
            {
                Text = "200",
                Dock = DockStyle.Fill,
                BackColor = WinColor.FromArgb(255, 250, 240),
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold)
            };
            txtWallDepth.TextChanged += (s, e) => picCanvas.Invalidate();

            Label lblAlign = new Label()
            {
                Text = "Placement Align:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                ForeColor = WinColor.FromArgb(0, 90, 160)
            };
            cboPlacementAlign = new ComboBox()
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboPlacementAlign.Items.AddRange(new object[]
            {
                "Center (Tim)",
                "Inner Edge (Mép trong)",
                "Outer Edge (Mép ngoài)"
            });
            cboPlacementAlign.SelectedIndex = 0;
            cboPlacementAlign.SelectedIndexChanged += (s, e) => picCanvas.Invalidate();

            Label lblTag = new Label()
            {
                Text = "Schedule Tag / Mark:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            txtDoorTag = new TextBox()
            {
                Text = "D-01",
                Dock = DockStyle.Fill
            };

            tlpOpts.Controls.Add(lblOpeningTitle, 0, 0);
            tlpOpts.Controls.Add(txtOpeningWidth, 1, 0);
            tlpOpts.Controls.Add(lblDepthTitle, 0, 1);
            tlpOpts.Controls.Add(txtWallDepth, 1, 1);
            tlpOpts.Controls.Add(lblAlign, 0, 2);
            tlpOpts.Controls.Add(cboPlacementAlign, 1, 2);
            tlpOpts.Controls.Add(lblTag, 0, 3);
            tlpOpts.Controls.Add(txtDoorTag, 1, 3);

            grpInsertOptions.Controls.Add(tlpOpts);

            btnInsertToCad = new Button()
            {
                Text = "INSERT TO CAD",
                Dock = DockStyle.Bottom,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(0, 120, 215),
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnInsertToCad.Click += BtnInsertToCad_Click;

            pnlCol2.Controls.Add(grpProps);
            pnlCol2.Controls.Add(grpInsertOptions);
            pnlCol2.Controls.Add(btnInsertToCad);
            splitCol2And3.Panel1.Controls.Add(pnlCol2);

            // ================= CỘT 3: LIVE PREVIEW & SCHEDULE TABLE =================
            Panel pnlCol3 = new Panel() { Dock = DockStyle.Fill, BackColor = WinColor.White, Padding = new Padding(6) };

            tabRightPanel = new TabControl()
            {
                Dock = DockStyle.Fill,
                Font = new WinFont("Segoe UI", 9f, FontStyle.Bold)
            };

            TabPage tabPreview = new TabPage("Live Preview (Zoom & Pan)");

            Panel pnlPreviewToolbar = new Panel() { Dock = DockStyle.Top, Height = 34, BackColor = WinColor.FromArgb(240, 243, 248), Padding = new Padding(8, 4, 8, 4) };
            chkShowAutoDims = new CheckBox()
            {
                Text = "Show Dimensions & Wipeout Boundary",
                Location = new Point(10, 6),
                AutoSize = true,
                Checked = true,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Regular)
            };
            chkShowAutoDims.CheckedChanged += (s, e) => picCanvas.Invalidate();

            btnResetZoom = new Button()
            {
                Text = "Reset Zoom (Medium Fit)",
                Dock = DockStyle.Right,
                Width = 148,
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.White,
                Font = new WinFont("Segoe UI", 8f, FontStyle.Regular)
            };
            btnResetZoom.Click += (s, e) => AutoFitCurrentPreview();

            pnlPreviewToolbar.Controls.AddRange(new Control[] { chkShowAutoDims, btnResetZoom });

            picCanvas = new ZoomablePreviewCanvas() { Dock = DockStyle.Fill, BackColor = WinColor.White };
            picCanvas.Paint += PicCanvas_Paint;
            picCanvas.DoubleClick += (s, e) => AutoFitCurrentPreview();
            picCanvas.SizeChanged += (s, e) => AutoFitCurrentPreview();

            tabPreview.Controls.Add(picCanvas);
            tabPreview.Controls.Add(pnlPreviewToolbar);

            TabPage tabSchedule = new TabPage("Schedule Table (Excel Mode)");

            Panel pnlSchedTop = new Panel() { Dock = DockStyle.Top, Height = 40, BackColor = WinColor.FromArgb(240, 243, 248), Padding = new Padding(6) };

            cboScheduleFilter = new ComboBox() { Location = new Point(6, 8), Width = 145, DropDownStyle = ComboBoxStyle.DropDownList, Font = new WinFont("Segoe UI", 8.5f, FontStyle.Regular) };
            cboScheduleFilter.Items.AddRange(new object[] { "All Selected Blocks", "Doors & Windows Only", "Furniture & Equipment", "Landscape / Plant" });
            cboScheduleFilter.SelectedIndex = 0;

            btnPickScheduleFromCad = new Button()
            {
                Text = "Pick / Select in CAD",
                Location = new Point(158, 6),
                Size = new Size(135, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(0, 120, 215),
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold)
            };
            btnPickScheduleFromCad.Click += BtnPickScheduleFromCad_Click;

            btnAddRow = new Button() { Text = "+ Row", Location = new Point(300, 6), Size = new Size(58, 28), FlatStyle = FlatStyle.Flat, BackColor = WinColor.White, Font = new WinFont("Segoe UI", 8.25f) };
            btnDeleteRow = new Button() { Text = "- Row", Location = new Point(362, 6), Size = new Size(58, 28), FlatStyle = FlatStyle.Flat, BackColor = WinColor.White, Font = new WinFont("Segoe UI", 8.25f) };
            btnClearSchedule = new Button() { Text = "Clear", Location = new Point(424, 6), Size = new Size(55, 28), FlatStyle = FlatStyle.Flat, BackColor = WinColor.White, Font = new WinFont("Segoe UI", 8.25f) };

            btnAddRow.Click += BtnAddRow_Click;
            btnDeleteRow.Click += BtnDeleteRow_Click;
            btnClearSchedule.Click += (s, e) => { _scheduleTable.Rows.Clear(); RenumberScheduleRows(); };

            pnlSchedTop.Controls.AddRange(new Control[] { cboScheduleFilter, btnPickScheduleFromCad, btnAddRow, btnDeleteRow, btnClearSchedule });

            dgvSchedule = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = WinColor.White,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Font = new WinFont("Segoe UI", 8.75f, FontStyle.Regular)
            };

            btnExportTableToCad = new Button()
            {
                Text = "EXPORT SCHEDULE TABLE TO CAD (DB-A-Line thin / DB-A-Text)",
                Dock = DockStyle.Bottom,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(16, 137, 62),
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 9f, FontStyle.Bold)
            };
            btnExportTableToCad.Click += BtnExportTableToCad_Click;

            tabSchedule.Controls.Add(dgvSchedule);
            tabSchedule.Controls.Add(pnlSchedTop);
            tabSchedule.Controls.Add(btnExportTableToCad);

            tabRightPanel.TabPages.Add(tabPreview);
            tabRightPanel.TabPages.Add(tabSchedule);

            pnlCol3.Controls.Add(tabRightPanel);
            splitCol2And3.Panel2.Controls.Add(pnlCol3);

            this.Controls.Add(splitCol1And23);
            this.Controls.Add(pnlStatus);

            ApplyResponsiveColumnProportions();
        }

        // Tự động hiện ô Wipeout khi ở Tab Door/Window, và ẩn đi khi ở Tab Furniture/Tree/...
        private void UpdateWipeoutSectionVisibilityByTab(string category)
        {
            bool isDoorWin = IsDoorOrWindowCategory(category);
            lblOpeningTitle.Visible = isDoorWin;
            txtOpeningWidth.Visible = isDoorWin;
            lblDepthTitle.Visible = isDoorWin;
            txtWallDepth.Visible = isDoorWin;
            grpInsertOptions.Text = isDoorWin
                ? "3. Wall Wipeout & Placement Align"
                : "3. Placement Align & Schedule Tag";
            picCanvas.Invalidate();
        }

        private void ApplyResponsiveColumnProportions()
        {
            if (this.Width < 400 || splitCol1And23 == null || splitCol2And3 == null) return;
            try
            {
                _isUpdatingSplitters = true;

                int totalW1 = splitCol1And23.Width;
                int min1 = splitCol1And23.Panel1MinSize + 5;
                int max1 = totalW1 - splitCol1And23.Panel2MinSize - splitCol1And23.SplitterWidth - 5;
                if (max1 > min1)
                {
                    int target1 = (int)(this.Width * _col1Ratio);
                    splitCol1And23.SplitterDistance = Math.Max(min1, Math.Min(max1, target1));
                }

                int totalW2 = splitCol2And3.Width;
                int min2 = splitCol2And3.Panel1MinSize + 5;
                int max2 = totalW2 - splitCol2And3.Panel2MinSize - splitCol2And3.SplitterWidth - 5;
                if (max2 > min2)
                {
                    int target2 = (int)(this.Width * _col2Ratio);
                    splitCol2And3.SplitterDistance = Math.Max(min2, Math.Min(max2, target2));
                }
            }
            catch { }
            finally
            {
                _isUpdatingSplitters = false;
            }
        }

        // =========================================================================
        // 5. THÊM (+) HOẶC XÓA (-) CÁC TAB THƯ MỤC CHÍNH
        // =========================================================================
        private void BtnAddCategoryTab_Click(object sender, EventArgs e)
        {
            string newCatName = PromptSimpleTextDialog("Add New Category Tab", "Enter new Category folder name (e.g. Ceiling, Fence, Lighting):", "");
            if (string.IsNullOrWhiteSpace(newCatName)) return;

            newCatName = SanitizeSafeFileName(newCatName.Trim());
            if (_categories.Any(c => c.Equals(newCatName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"Category '{newCatName}' already exists!", "DB Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                cboCategory.SelectedItem = _categories.First(c => c.Equals(newCatName, StringComparison.OrdinalIgnoreCase));
                return;
            }

            _categories.Add(newCatName);
            SaveCategoryConfigFile();

            string newDir = Path.Combine(_libraryRootFolder, newCatName);
            if (!Directory.Exists(newDir))
                Directory.CreateDirectory(newDir);

            cboCategory.Items.Add(newCatName);
            cboCategory.SelectedItem = newCatName;
            lblStatus.Text = $"Created new Category Tab [{newCatName}] at: {newDir}";
        }

        private void BtnRemoveCategoryTab_Click(object sender, EventArgs e)
        {
            if (cboCategory.SelectedItem == null) return;
            if (_categories.Count <= 1)
            {
                MessageBox.Show("At least one Category Tab must remain in the library.", "DB Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string curCat = cboCategory.SelectedItem.ToString();
            DialogResult dr = MessageBox.Show(
                $"Remove Category Tab [{curCat}] from the list?\n(Your DWG files on disk will be kept safe in {Path.Combine(_libraryRootFolder, SanitizeSafeFileName(curCat))})",
                "Confirm Remove Tab",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr != DialogResult.Yes) return;

            _categories.RemoveAll(c => c.Equals(curCat, StringComparison.OrdinalIgnoreCase));
            SaveCategoryConfigFile();

            cboCategory.Items.Remove(curCat);
            if (cboCategory.Items.Count > 0)
                cboCategory.SelectedIndex = 0;

            lblStatus.Text = $"Removed Category Tab [{curCat}] from the list.";
        }

        private string PromptSimpleTextDialog(string title, string promptText, string defaultVal)
        {
            using (Form frm = new Form())
            {
                frm.Text = title;
                frm.Size = new Size(380, 155);
                frm.FormBorderStyle = FormBorderStyle.FixedDialog;
                frm.StartPosition = FormStartPosition.CenterParent;
                frm.MaximizeBox = false;
                frm.MinimizeBox = false;

                Label lbl = new Label() { Left = 14, Top = 14, Width = 340, Text = promptText };
                TextBox txt = new TextBox() { Left = 14, Top = 40, Width = 335, Text = defaultVal };
                Button btnOk = new Button() { Text = "OK", Left = 185, Width = 78, Top = 76, DialogResult = DialogResult.OK, BackColor = WinColor.FromArgb(0, 120, 215), ForeColor = WinColor.White, FlatStyle = FlatStyle.Flat };
                Button btnCancel = new Button() { Text = "Cancel", Left = 271, Width = 78, Top = 76, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };

                frm.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
                frm.AcceptButton = btnOk;
                frm.CancelButton = btnCancel;

                return frm.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }

        // =========================================================================
        // 6. CHUẨN HÓA LAYER ĐẦU RA THEO BẢNG CÔNG TY
        // =========================================================================
        private string GetTargetCompanyLayerByTab(string category, out short colorIndex)
        {
            string cat = (category ?? "").Trim().ToUpperInvariant();
            if (cat.Contains("DOOR")) { colorIndex = 40; return "DB-A-Door"; }
            if (cat.Contains("WIN")) { colorIndex = 42; return "DB-A-Window"; }
            if (cat.Contains("FUR")) { colorIndex = 140; return "DB-A-Furniture"; }
            if (cat.Contains("TREE") || cat.Contains("LAND") || cat.Contains("PLANT")) { colorIndex = 70; return "DB-L-Plant"; }
            if (cat.Contains("KIT") || cat.Contains("BATH") || cat.Contains("WC") || cat.Contains("EQUIP")) { colorIndex = 140; return "DB-A-Equipment"; }
            if (cat.Contains("SYM")) { colorIndex = 3; return "DB-A-Symbol"; }

            colorIndex = 250;
            return "DB-A-Line thin";
        }

        private ObjectId EnsureCompanyLayer(Database db, Transaction tr, string layerName, short colorIdx)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord()
                {
                    Name = layerName,
                    Color = CadColor.FromColorIndex(ColorMethod.ByAci, colorIdx)
                };
                ObjectId id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }
            return lt[layerName];
        }

        // =========================================================================
        // 7. QUẢN LÝ CỘT 1: ICON THU NHỎ 75% (74x82) + DOUBLE-CLICK ĐẶT VÀO MODEL LUÔN
        // =========================================================================
        private bool IsJunkOrSystemBlockName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.Trim();
            if (n.StartsWith("*") || n.StartsWith("_") || n.StartsWith("A$C", StringComparison.OrdinalIgnoreCase))
                return true;
            if (_ignoredSystemBlocks.Contains(n))
                return true;
            return false;
        }

        private void ScanCategoryFolder(string category)
        {
            try
            {
                string safeCat = SanitizeSafeFileName(category);
                string catDir = Path.Combine(_libraryRootFolder, safeCat);
                if (Directory.Exists(catDir))
                {
                    string[] dwgFiles = Directory.GetFiles(catDir, "*.dwg");
                    foreach (string file in dwgFiles)
                    {
                        LoadBlocksFromExternalDwg(file, category, false);
                    }
                }
            }
            catch { }
            RenderBlockIconsInCol1();
        }

        private void RenderBlockIconsInCol1()
        {
            flpBlockIcons.SuspendLayout();
            flpBlockIcons.Controls.Clear();

            string currentCat = cboCategory.SelectedItem != null ? cboCategory.SelectedItem.ToString() : "Door";
            string keyword = txtSearchBlock.Text.Trim().ToUpperInvariant();

            var filtered = _libraryBlocks.Where(b =>
                b.Category.Equals(currentCat, StringComparison.OrdinalIgnoreCase) &&
                !IsJunkOrSystemBlockName(b.BlockName) &&
                (string.IsNullOrEmpty(keyword) || b.BlockName.ToUpperInvariant().Contains(keyword))).ToList();

            foreach (BlockItemInfo item in filtered)
            {
                Panel card = CreateCompactBlockIconCard(item);
                flpBlockIcons.Controls.Add(card);
            }

            flpBlockIcons.ResumeLayout();

            if (filtered.Count > 0)
            {
                if (_activeBlock == null || !filtered.Contains(_activeBlock))
                    SelectBlockItem(filtered[0]);
            }
            else
            {
                _activeBlock = null;
                _activeParams.Clear();
                _liveEvaluatedLines.Clear();
                lblSelectedBlockTitle.Text = $"No Block in [{currentCat}]";
                PopulateColumn2ParameterUI();
                picCanvas.Invalidate();
            }
        }

        // ĐÃ THU NHỎ ICON XUỐNG 75% (74 x 82 px):
        // - Double-click vào hình Icon (pic) -> Đặt thả vào Model luôn!
        // - Double-click vào chữ Tên Block (lblName) -> Bật ô sửa tên trực tiếp!
        private Panel CreateCompactBlockIconCard(BlockItemInfo item)
        {
            bool isSelected = (_activeBlock == item);
            Panel card = new Panel()
            {
                Width = 74,   // 75% của 98px
                Height = 82,  // 75% của 108px
                Margin = new Padding(3),
                BackColor = isSelected ? WinColor.FromArgb(228, 242, 255) : WinColor.White,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand,
                Tag = item
            };

            Button btnRemove = new Button()
            {
                Text = "-",
                Size = new Size(15, 15),
                Location = new Point(card.Width - 17, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = WinColor.FromArgb(255, 235, 235),
                ForeColor = WinColor.Crimson,
                Font = new WinFont("Segoe UI", 7f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            btnRemove.FlatAppearance.BorderSize = 0;
            btnRemove.Click += (s, e) => RemoveBlockFromLibrary(item);

            PictureBox pic = new PictureBox()
            {
                Dock = DockStyle.Top,
                Height = 52, // 75% của 70px
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = item.Thumbnail ?? RenderVectorLinesToBitmap(item.PreviewLines, 48, 44),
                BackColor = WinColor.FromArgb(250, 251, 254)
            };

            Label lblName = new Label()
            {
                Text = (item.IsDynamic ? "⚡" : "") + item.BlockName,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new WinFont("Segoe UI", 7.2f, item.IsDynamic ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = item.IsDynamic ? WinColor.FromArgb(0, 90, 180) : WinColor.FromArgb(40, 40, 40),
                AutoEllipsis = true
            };

            ToolTip tt = new ToolTip();
            tt.SetToolTip(pic, $"{item.BlockName}\n• Double-click Icon: Insert to Model\n• Drag Icon: Insert to Model");
            tt.SetToolTip(lblName, $"{item.BlockName}\n• Double-click Name: Rename Block");

            // 1. DOUBLE-CLICK VÀO HÌNH ICON -> ĐẶT THẢ VÀO MODEL NGAY LẬP TỨC!
            pic.DoubleClick += (s, e) =>
            {
                SelectBlockItem(item);
                InsertSelectedBlockFlow();
            };

            // 2. DOUBLE-CLICK VÀO TÊN BLOCK -> SỬA TÊN NGAY TẠI CHỖ!
            lblName.DoubleClick += (s, e) => BeginInlineRenameOnCard(card, lblName, item);

            Point mouseDownPos = Point.Empty;
            MouseEventHandler mdHandler = (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    mouseDownPos = e.Location;
                    SelectBlockItem(item);
                }
            };

            MouseEventHandler mmHandler = (s, e) =>
            {
                if (e.Button == MouseButtons.Left && mouseDownPos != Point.Empty)
                {
                    if (Math.Abs(e.X - mouseDownPos.X) > 8 || Math.Abs(e.Y - mouseDownPos.Y) > 8)
                    {
                        mouseDownPos = Point.Empty;
                        InsertSelectedBlockFlow();
                    }
                }
            };

            card.MouseDown += mdHandler;
            pic.MouseDown += mdHandler;
            lblName.MouseDown += mdHandler;

            card.MouseMove += mmHandler;
            pic.MouseMove += mmHandler;
            lblName.MouseMove += mmHandler;

            card.Controls.Add(btnRemove);
            card.Controls.Add(lblName);
            card.Controls.Add(pic);
            btnRemove.BringToFront();

            return card;
        }

        private void BeginInlineRenameOnCard(Panel card, Label lblName, BlockItemInfo item)
        {
            TextBox txtEdit = new TextBox()
            {
                Text = item.BlockName,
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new WinFont("Segoe UI", 7.5f, FontStyle.Bold),
                BackColor = WinColor.FromArgb(255, 255, 210),
                ForeColor = WinColor.Black,
                TextAlign = HorizontalAlignment.Center
            };

            bool isCommitted = false;
            Action commitRename = () =>
            {
                if (isCommitted) return;
                isCommitted = true;

                string proposedName = txtEdit.Text.Trim();
                if (card.Controls.Contains(txtEdit))
                {
                    card.Controls.Remove(txtEdit);
                    txtEdit.Dispose();
                }
                lblName.Visible = true;

                if (!string.IsNullOrEmpty(proposedName) && !proposedName.Equals(item.BlockName, StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteSafeBlockRenameEverywhere(item, proposedName);
                }
            };

            txtEdit.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter)
                {
                    ev.SuppressKeyPress = true;
                    commitRename();
                }
                else if (ev.KeyCode == Keys.Escape)
                {
                    isCommitted = true;
                    if (card.Controls.Contains(txtEdit))
                    {
                        card.Controls.Remove(txtEdit);
                        txtEdit.Dispose();
                    }
                    lblName.Visible = true;
                }
            };

            txtEdit.Leave += (s, ev) => commitRename();

            lblName.Visible = false;
            card.Controls.Add(txtEdit);
            txtEdit.BringToFront();
            txtEdit.Focus();
            txtEdit.SelectAll();
        }

        private void ExecuteSafeBlockRenameEverywhere(BlockItemInfo item, string rawNewName)
        {
            if (Regex.IsMatch(rawNewName, @"[<>\/\\"":;?*|,=`]"))
            {
                MessageBox.Show("Block name cannot contain special characters (< > / \\ \" : ; ? * | , = `).", "Invalid Block Name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string oldName = item.BlockName;
            string newName = rawNewName.Trim();

            if (_libraryBlocks.Any(b => b != item && b.Category.Equals(item.Category, StringComparison.OrdinalIgnoreCase) && b.BlockName.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A block named '{newName}' already exists in [{item.Category}] tab!", "Duplicate Name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                try
                {
                    using (DocumentLock docLock = doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                        if (bt.Has(newName))
                        {
                            MessageBox.Show($"Block '{newName}' already exists in the current CAD drawing! Please choose another name.", "Duplicate in CAD", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            tr.Abort();
                            return;
                        }

                        if (bt.Has(oldName))
                        {
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[oldName], OpenMode.ForWrite);
                            btr.Name = newName;
                        }
                        tr.Commit();
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(item.SourceDwgPath) && File.Exists(item.SourceDwgPath))
            {
                try
                {
                    string oldFilePath = item.SourceDwgPath;
                    using (Database extDb = new Database(false, true))
                    {
                        extDb.ReadDwgFile(oldFilePath, FileShare.ReadWrite, true, "");
                        using (Transaction tr = extDb.TransactionManager.StartTransaction())
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(extDb.BlockTableId, OpenMode.ForRead);
                            if (bt.Has(oldName) && !bt.Has(newName))
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[oldName], OpenMode.ForWrite);
                                btr.Name = newName;
                            }
                            tr.Commit();
                        }

                        string dir = Path.GetDirectoryName(oldFilePath);
                        string oldFileNoExt = Path.GetFileNameWithoutExtension(oldFilePath);
                        string newFilePath = oldFilePath;

                        if (oldFileNoExt.Equals(oldName, StringComparison.OrdinalIgnoreCase) ||
                            oldFileNoExt.Equals(SanitizeSafeFileName(oldName), StringComparison.OrdinalIgnoreCase))
                        {
                            newFilePath = Path.Combine(dir, SanitizeSafeFileName(newName) + ".dwg");
                        }

                        extDb.SaveAs(newFilePath, DwgVersion.Current);
                        if (!oldFilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldFilePath))
                        {
                            try { File.Delete(oldFilePath); } catch { }
                        }
                        item.SourceDwgPath = newFilePath;
                    }
                }
                catch { }
            }

            item.BlockName = newName;
            RenderBlockIconsInCol1();
            SelectBlockItem(item);
            lblStatus.Text = $"Renamed Block '{oldName}' -> '{newName}' successfully!";
        }

        private void RemoveBlockFromLibrary(BlockItemInfo item)
        {
            if (item == null) return;
            _libraryBlocks.Remove(item);

            try
            {
                if (!string.IsNullOrEmpty(item.SourceDwgPath) && File.Exists(item.SourceDwgPath))
                {
                    string fileNoExt = Path.GetFileNameWithoutExtension(item.SourceDwgPath);
                    string safeBlk = SanitizeSafeFileName(item.BlockName);
                    if (fileNoExt.Equals(item.BlockName, StringComparison.OrdinalIgnoreCase) ||
                        fileNoExt.Equals(safeBlk, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(item.SourceDwgPath);
                    }
                }
            }
            catch { }

            if (_activeBlock == item) _activeBlock = null;
            RenderBlockIconsInCol1();
            lblStatus.Text = $"Removed '{item.BlockName}' from [{item.Category}] library.";
        }

        private void FlpBlockIcons_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void FlpBlockIcons_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string currentCat = cboCategory.SelectedItem.ToString();
                foreach (string f in files)
                {
                    if (Path.GetExtension(f).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadBlocksFromExternalDwg(f, currentCat, true);
                    }
                }
                RenderBlockIconsInCol1();
            }
        }

        private void BtnLoadDwg_Click(object sender, EventArgs e)
        {
            using (WinOpenFileDialog ofd = new WinOpenFileDialog())
            {
                ofd.Filter = "CAD Drawing (*.dwg)|*.dwg";
                ofd.Title = "Select DWG Library File to Import into Current Tab";
                ofd.Multiselect = true;
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string currentCat = cboCategory.SelectedItem.ToString();
                    int countBefore = _libraryBlocks.Count;
                    foreach (string file in ofd.FileNames)
                    {
                        LoadBlocksFromExternalDwg(file, currentCat, true);
                    }
                    RenderBlockIconsInCol1();
                    lblStatus.Text = $"Loaded DWG file(s) into [{currentCat}] tab ({_libraryBlocks.Count - countBefore} valid block(s) added).";
                }
            }
        }

        private void BtnSetFolder_Click(object sender, EventArgs e)
        {
            using (WinFolderBrowserDialog fbd = new WinFolderBrowserDialog())
            {
                fbd.Description = "Select Root Folder for DB CAD Library";
                fbd.SelectedPath = _libraryRootFolder;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _libraryRootFolder = fbd.SelectedPath;
                    LoadAndEnsureCategoryStructure();
                    cboCategory.Items.Clear();
                    cboCategory.Items.AddRange(_categories.ToArray());
                    if (cboCategory.Items.Count > 0) cboCategory.SelectedIndex = 0;
                    lblStatus.Text = "Library root folder updated: " + _libraryRootFolder;
                }
            }
        }

        private void BtnAddFromCad_Click(object sender, EventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            Application.MainWindow.Focus();
            PromptEntityOptions peo = new PromptEntityOptions("\n[DBLIB] Pick a Block in Model space to add into current Library Tab: ");
            peo.SetRejectMessage("\nSelected object must be a Block Reference!");
            peo.AddAllowedClass(typeof(BlockReference), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            string currentCat = cboCategory.SelectedItem != null ? cboCategory.SelectedItem.ToString() : "Door";

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockReference br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                ObjectId defBtrId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(defBtrId, OpenMode.ForRead);

                string blkName = btr.Name;
                string safeCatFolder = SanitizeSafeFileName(currentCat);
                string safeFileName = SanitizeSafeFileName(blkName);

                string catDir = Path.Combine(_libraryRootFolder, safeCatFolder);
                string savePath = "";

                try
                {
                    if (!Directory.Exists(catDir)) Directory.CreateDirectory(catDir);
                    savePath = Path.Combine(catDir, safeFileName + ".dwg");

                    using (Database newDb = new Database(true, true))
                    {
                        ObjectIdCollection ids = new ObjectIdCollection(new ObjectId[] { defBtrId });
                        IdMapping map = new IdMapping();
                        doc.Database.WblockCloneObjects(ids, newDb.BlockTableId, map, DuplicateRecordCloning.Replace, false);
                        newDb.SaveAs(savePath, DwgVersion.Current);
                    }
                }
                catch { savePath = ""; }

                List<Tuple<PointF, PointF, int>> lines = new List<Tuple<PointF, PointF, int>>();
                BlockTableRecord evalBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                ExtractRawLinesFromBtr(tr, evalBtr, Matrix3d.Identity, lines, 0);
                if (lines.Count == 0)
                    ExtractRawLinesFromBtr(tr, btr, Matrix3d.Identity, lines, 0);

                BlockItemInfo newItem = new BlockItemInfo()
                {
                    BlockName = blkName,
                    Category = currentCat,
                    SourceDwgPath = savePath,
                    IsDynamic = br.IsDynamicBlock || btr.IsDynamicBlock || HasDynamicEvaluationGraph(btr),
                    PreviewLines = lines,
                    Thumbnail = RenderVectorLinesToBitmap(lines, 48, 44)
                };

                _libraryBlocks.RemoveAll(x => x.BlockName.Equals(blkName, StringComparison.OrdinalIgnoreCase) && x.Category.Equals(currentCat, StringComparison.OrdinalIgnoreCase));
                _libraryBlocks.Add(newItem);

                tr.Commit();

                RenderBlockIconsInCol1();
                SelectBlockItem(newItem);
                lblStatus.Text = $"Added Block '{blkName}' into [{currentCat}] tab!";
            }
        }

        private void LoadBlocksFromExternalDwg(string dwgPath, string category, bool copyToLibFolder)
        {
            if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath)) return;

            try
            {
                if (copyToLibFolder)
                {
                    string safeCat = SanitizeSafeFileName(category);
                    string catDir = Path.Combine(_libraryRootFolder, safeCat);
                    if (!Directory.Exists(catDir)) Directory.CreateDirectory(catDir);

                    string safeFile = SanitizeSafeFileName(Path.GetFileNameWithoutExtension(dwgPath)) + ".dwg";
                    string destFile = Path.Combine(catDir, safeFile);
                    if (!Path.GetFullPath(dwgPath).Equals(Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(dwgPath, destFile, true);
                        dwgPath = destFile;
                    }
                }

                using (Database extDb = new Database(false, true))
                {
                    extDb.ReadDwgFile(dwgPath, FileShare.ReadWrite, true, "");
                    using (Transaction tr = extDb.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(extDb.BlockTableId, OpenMode.ForRead);
                        List<BlockItemInfo> candidates = new List<BlockItemInfo>();

                        foreach (ObjectId btrId in bt)
                        {
                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                            if (btr.IsAnonymous || btr.IsLayout || btr.IsFromExternalReference || btr.IsFromOverlayReference)
                                continue;
                            if (IsJunkOrSystemBlockName(btr.Name))
                                continue;

                            List<Tuple<PointF, PointF, int>> lines = new List<Tuple<PointF, PointF, int>>();
                            ExtractRawLinesFromBtr(tr, btr, Matrix3d.Identity, lines, 0);
                            if (lines.Count == 0) continue;

                            bool isDyn = btr.IsDynamicBlock || HasDynamicEvaluationGraph(btr);

                            BlockItemInfo info = new BlockItemInfo()
                            {
                                BlockName = btr.Name,
                                Category = category,
                                SourceDwgPath = dwgPath,
                                IsDynamic = isDyn,
                                PreviewLines = lines,
                                Thumbnail = RenderVectorLinesToBitmap(lines, 48, 44)
                            };
                            candidates.Add(info);
                        }

                        string fileBaseName = Path.GetFileNameWithoutExtension(dwgPath);
                        var dynamicOnes = candidates.Where(c =>
                            c.IsDynamic ||
                            c.BlockName.Equals(fileBaseName, StringComparison.OrdinalIgnoreCase) ||
                            SanitizeSafeFileName(c.BlockName).Equals(fileBaseName, StringComparison.OrdinalIgnoreCase)).ToList();

                        var finalToLoad = dynamicOnes.Count > 0 ? dynamicOnes : candidates;

                        foreach (var info in finalToLoad)
                        {
                            _libraryBlocks.RemoveAll(x => x.BlockName.Equals(info.BlockName, StringComparison.OrdinalIgnoreCase) && x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
                            _libraryBlocks.Add(info);
                        }

                        tr.Commit();
                    }
                }
            }
            catch { }
        }

        private bool HasDynamicEvaluationGraph(BlockTableRecord btr)
        {
            if (btr == null || btr.ExtensionDictionary == ObjectId.Null) return false;
            try
            {
                Database db = btr.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary extDict = tr.GetObject(btr.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                    bool hasEval = extDict != null && extDict.Contains("ACAD_ENHANCEDBLOCK");
                    tr.Commit();
                    return hasEval;
                }
            }
            catch { return false; }
        }

        // =========================================================================
        // 8. CỘT 2: ĐỒNG BỘ THÔNG SỐ DYNAMIC CHO MỌI TAB
        // =========================================================================
        private void SelectBlockItem(BlockItemInfo item)
        {
            _activeBlock = item;
            _currentBlockScale = 1.0;

            short colIdx;
            string outLayer = GetTargetCompanyLayerByTab(item.Category, out colIdx);
            lblSelectedBlockTitle.Text = $"{item.BlockName}  ->  [{outLayer}]";

            UpdateWipeoutSectionVisibilityByTab(item.Category);

            if (item.Category.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0)
                txtDoorTag.Text = "W-01";
            else if (item.Category.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0)
                txtDoorTag.Text = "D-01";
            else
                txtDoorTag.Text = item.Category.Substring(0, Math.Min(2, item.Category.Length)).ToUpperInvariant() + "-01";

            foreach (Control c in flpBlockIcons.Controls)
            {
                if (c is Panel p)
                    p.BackColor = (p.Tag == item) ? WinColor.FromArgb(225, 240, 255) : WinColor.White;
            }

            _activeParams = ReadDynamicPropertiesAndRefreshGeometry(item, null);
            PopulateColumn2ParameterUI();
            SyncOpeningAndDepthBoxesWithCurrentGeometry();
            AutoFitCurrentPreview();
        }

        private void SyncOpeningAndDepthBoxesWithCurrentGeometry()
        {
            var lines = _liveEvaluatedLines.Count > 0 ? _liveEvaluatedLines : (_activeBlock != null ? _activeBlock.PreviewLines : null);
            double bboxOuterW = 0.0;
            double bboxOuterH = 0.0;
            if (lines != null && lines.Count > 0)
            {
                float minX = lines.Min(l => Math.Min(l.Item1.X, l.Item2.X));
                float maxX = lines.Max(l => Math.Max(l.Item1.X, l.Item2.X));
                float minY = lines.Min(l => Math.Min(l.Item1.Y, l.Item2.Y));
                float maxY = lines.Max(l => Math.Max(l.Item1.Y, l.Item2.Y));
                bboxOuterW = Math.Round((maxX - minX) * _currentBlockScale, 1);
                bboxOuterH = Math.Round((maxY - minY) * _currentBlockScale, 1);
            }

            double paramW = GetParamNumericValue(new string[] { "WIDTH", "WITH", "DISTANCE", "RONG", "LENGTH", "DAI", "W" }, -1.0);
            double defaultOpening = Math.Max(bboxOuterW, paramW);
            if (defaultOpening > 10.0)
            {
                txtOpeningWidth.Text = Math.Round(defaultOpening, 1).ToString();
            }

            bool isDoorWin = _activeBlock != null && IsDoorOrWindowCategory(_activeBlock.Category);
            double paramD = GetParamNumericValue(new string[] { "DEPTH", "WALL", "THICK", "DAY" }, -1.0);
            if (paramD > 10.0)
            {
                txtWallDepth.Text = Math.Round(paramD, 1).ToString();
            }
            else if (!isDoorWin && bboxOuterH > 10.0)
            {
                txtWallDepth.Text = Math.Round(bboxOuterH, 1).ToString();
            }
            else if (bboxOuterH >= 30.0 && bboxOuterH <= 400.0)
            {
                txtWallDepth.Text = Math.Round(bboxOuterH, 1).ToString();
            }
        }

        private void SyncActiveParamsFromUIControls()
        {
            foreach (Control ctrl in tblParams.Controls)
            {
                DynParamModel boundParam = ctrl.Tag as DynParamModel;
                if (boundParam == null) continue;

                if (ctrl is ComboBox cbo && cbo.SelectedItem != null)
                {
                    boundParam.CurrentValue = cbo.SelectedItem.ToString();
                }
                else if (ctrl is TextBox txt)
                {
                    double dVal;
                    if (double.TryParse(txt.Text.Trim(), out dVal))
                        boundParam.CurrentValue = dVal;
                }
            }
        }

        private List<DynParamModel> ReadDynamicPropertiesAndRefreshGeometry(BlockItemInfo item, List<DynParamModel> userEditedParams)
        {
            List<DynParamModel> result = new List<DynParamModel>();
            _liveEvaluatedLines.Clear();
            if (item == null) return result;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                _liveEvaluatedLines.AddRange(item.PreviewLines);
                return result;
            }

            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                {
                    Database db = doc.Database;
                    EnsureBlockImportedToDatabase(db, item);

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        if (bt.Has(item.BlockName))
                        {
                            ObjectId btrId = bt[item.BlockName];
                            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                            using (BlockReference tempBr = new BlockReference(Point3d.Origin, btrId))
                            {
                                ms.AppendEntity(tempBr);
                                tr.AddNewlyCreatedDBObject(tempBr, true);

                                if (tempBr.IsDynamicBlock)
                                {
                                    item.IsDynamic = true;

                                    if (userEditedParams != null && userEditedParams.Count > 0)
                                    {
                                        ApplyEditedParamsToBlockRef(tempBr, userEditedParams);
                                    }

                                    foreach (DynamicBlockReferenceProperty prop in tempBr.DynamicBlockReferencePropertyCollection)
                                    {
                                        if (prop.PropertyName.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                                            continue;

                                        object[] allowed = null;
                                        try { allowed = prop.GetAllowedValues(); } catch { }

                                        bool hasAllowedList = (allowed != null && allowed.Length > 0);
                                        if (!prop.Show && !hasAllowedList) continue;
                                        if (prop.ReadOnly && !hasAllowedList) continue;

                                        bool isVis = hasAllowedList && (
                                            prop.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            prop.PropertyName.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            prop.PropertyName.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            prop.PropertyName.IndexOf("Style", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            allowed.Any(a => a != null && a.ToString().Length > 1 && !(a is short)));

                                        DynParamModel pm = new DynParamModel()
                                        {
                                            PropertyName = prop.PropertyName,
                                            CurrentValue = prop.Value,
                                            IsReadOnly = prop.ReadOnly,
                                            IsVisibilityOrLookup = isVis
                                        };

                                        if (hasAllowedList)
                                            pm.AllowedValues.AddRange(allowed);

                                        result.Add(pm);
                                    }
                                }

                                BlockTableRecord evalBtr = (BlockTableRecord)tr.GetObject(tempBr.BlockTableRecord, OpenMode.ForRead);
                                ExtractRawLinesFromBtr(tr, evalBtr, Matrix3d.Identity, _liveEvaluatedLines, 0);

                                tempBr.Erase(true);
                            }
                        }
                        tr.Commit();
                    }
                }
            }
            catch { }

            if (_liveEvaluatedLines.Count == 0 && item.PreviewLines.Count > 0)
                _liveEvaluatedLines.AddRange(item.PreviewLines);

            result = result.OrderByDescending(p => p.IsVisibilityOrLookup).ToList();
            return result;
        }

        private void ApplyEditedParamsToBlockRef(BlockReference br, List<DynParamModel> editedParams)
        {
            if (!br.IsDynamicBlock || editedParams == null) return;

            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
            {
                var match = editedParams.FirstOrDefault(x => x.PropertyName.Equals(prop.PropertyName, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.IsVisibilityOrLookup && match.CurrentValue != null)
                {
                    SetDynamicPropertyValueSafe(prop, match.CurrentValue);
                }
            }

            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
            {
                var match = editedParams.FirstOrDefault(x => x.PropertyName.Equals(prop.PropertyName, StringComparison.OrdinalIgnoreCase));
                if (match != null && !match.IsVisibilityOrLookup && match.CurrentValue != null)
                {
                    SetDynamicPropertyValueSafe(prop, match.CurrentValue);
                }
            }
        }

        private void SetDynamicPropertyValueSafe(DynamicBlockReferenceProperty prop, object newVal)
        {
            try
            {
                object[] allowed = prop.GetAllowedValues();
                if (allowed != null && allowed.Length > 0)
                {
                    foreach (object item in allowed)
                    {
                        if (item != null && item.ToString().Equals(newVal.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            prop.Value = item;
                            return;
                        }
                    }
                }

                if (prop.Value is double)
                {
                    double d;
                    if (double.TryParse(newVal.ToString(), out d)) prop.Value = d;
                }
                else if (prop.Value is short)
                {
                    short s;
                    if (short.TryParse(newVal.ToString(), out s)) prop.Value = s;
                }
                else if (prop.Value is int)
                {
                    int iv;
                    if (int.TryParse(newVal.ToString(), out iv)) prop.Value = iv;
                }
                else
                {
                    prop.Value = newVal.ToString();
                }
            }
            catch { }
        }

        private void PopulateColumn2ParameterUI()
        {
            tblParams.SuspendLayout();
            tblParams.Controls.Clear();
            tblParams.RowStyles.Clear();

            if (_activeParams.Count == 0)
            {
                tblParams.RowCount = 2;
                tblParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
                tblParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

                Label lblInfo = new Label()
                {
                    Text = "Standard Block (Double-click icon to place):",
                    Dock = DockStyle.Fill,
                    ForeColor = WinColor.DimGray,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                tblParams.Controls.Add(lblInfo, 0, 0);
                tblParams.SetColumnSpan(lblInfo, 2);

                Label lblScale = new Label() { Text = "Scale Factor:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                TextBox txtScale = new TextBox() { Dock = DockStyle.Fill, Text = _currentBlockScale.ToString("0.##") };
                txtScale.TextChanged += (s, e) =>
                {
                    double sc;
                    if (double.TryParse(txtScale.Text.Trim(), out sc) && sc > 0.01 && sc <= 1000)
                    {
                        _currentBlockScale = sc;
                        SyncOpeningAndDepthBoxesWithCurrentGeometry();
                        picCanvas.Invalidate();
                    }
                };
                tblParams.Controls.Add(lblScale, 0, 1);
                tblParams.Controls.Add(txtScale, 1, 1);

                tblParams.ResumeLayout();
                return;
            }

            tblParams.RowCount = _activeParams.Count;
            for (int i = 0; i < _activeParams.Count; i++)
            {
                DynParamModel param = _activeParams[i];
                tblParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

                Label lbl = new Label()
                {
                    Text = (param.IsVisibilityOrLookup ? "👁 " : "") + param.PropertyName + ":",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new WinFont("Segoe UI", 8.5f, param.IsVisibilityOrLookup ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = param.IsVisibilityOrLookup ? WinColor.FromArgb(0, 90, 175) : WinColor.Black,
                    AutoEllipsis = true
                };

                Control inputCtrl;
                if (param.AllowedValues != null && param.AllowedValues.Count > 0)
                {
                    ComboBox cbo = new ComboBox()
                    {
                        Dock = DockStyle.Fill,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        BackColor = param.IsVisibilityOrLookup ? WinColor.FromArgb(240, 248, 255) : WinColor.White,
                        Tag = param
                    };
                    foreach (object v in param.AllowedValues)
                    {
                        if (v != null) cbo.Items.Add(v.ToString());
                    }

                    string cur = Convert.ToString(param.CurrentValue ?? "");
                    if (cbo.Items.Contains(cur)) cbo.SelectedItem = cur;
                    else if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;

                    cbo.SelectedIndexChanged += (s, e) =>
                    {
                        param.CurrentValue = cbo.SelectedItem.ToString();
                        SyncActiveParamsFromUIControls();
                        ReadDynamicPropertiesAndRefreshGeometry(_activeBlock, _activeParams);
                        SyncOpeningAndDepthBoxesWithCurrentGeometry();
                        if (param.IsVisibilityOrLookup) AutoFitCurrentPreview();
                        else picCanvas.Invalidate();
                    };
                    inputCtrl = cbo;
                }
                else
                {
                    TextBox txt = new TextBox()
                    {
                        Dock = DockStyle.Fill,
                        Text = Convert.ToString(param.CurrentValue ?? ""),
                        Tag = param
                    };

                    bool isWidthOrDepthParam =
                        param.PropertyName.IndexOf("width", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.IndexOf("with", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.IndexOf("distance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.IndexOf("depth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.IndexOf("thick", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        param.PropertyName.Equals("W", StringComparison.OrdinalIgnoreCase);

                    EventHandler applyTextChange = (s, e) =>
                    {
                        double dVal;
                        if (double.TryParse(txt.Text.Trim(), out dVal) && dVal >= 0)
                        {
                            param.CurrentValue = dVal;
                            SyncActiveParamsFromUIControls();
                            ReadDynamicPropertiesAndRefreshGeometry(_activeBlock, _activeParams);
                            if (isWidthOrDepthParam && dVal > 10)
                            {
                                SyncOpeningAndDepthBoxesWithCurrentGeometry();
                            }
                            picCanvas.Invalidate();
                        }
                    };
                    txt.KeyDown += (s, ev) => { if (ev.KeyCode == Keys.Enter) { applyTextChange(s, ev); ev.SuppressKeyPress = true; } };
                    txt.Leave += applyTextChange;
                    txt.TextChanged += (s, ev) =>
                    {
                        double dVal;
                        if (double.TryParse(txt.Text.Trim(), out dVal) && dVal > 10)
                        {
                            param.CurrentValue = dVal;
                            SyncActiveParamsFromUIControls();
                            ReadDynamicPropertiesAndRefreshGeometry(_activeBlock, _activeParams);
                            if (isWidthOrDepthParam)
                            {
                                SyncOpeningAndDepthBoxesWithCurrentGeometry();
                            }
                            picCanvas.Invalidate();
                        }
                    };
                    inputCtrl = txt;
                }

                tblParams.Controls.Add(lbl, 0, i);
                tblParams.Controls.Add(inputCtrl, 1, i);
            }

            tblParams.ResumeLayout();
        }

        // =========================================================================
        // 9. CỘT 3 (TAB 1): LIVE PREVIEW (CHỈ HIỆN KHUNG WIPEOUT CHO DOOR/WINDOW)
        // =========================================================================
        private void AutoFitCurrentPreview()
        {
            var lines = _liveEvaluatedLines.Count > 0 ? _liveEvaluatedLines : (_activeBlock != null ? _activeBlock.PreviewLines : null);
            if (lines == null || lines.Count == 0)
            {
                picCanvas.ResetView(0.42f);
                return;
            }

            float minX = lines.Min(l => Math.Min(l.Item1.X, l.Item2.X)) * (float)_currentBlockScale;
            float maxX = lines.Max(l => Math.Max(l.Item1.X, l.Item2.X)) * (float)_currentBlockScale;
            float minY = lines.Min(l => Math.Min(l.Item1.Y, l.Item2.Y)) * (float)_currentBlockScale;
            float maxY = lines.Max(l => Math.Max(l.Item1.Y, l.Item2.Y)) * (float)_currentBlockScale;

            float bw = Math.Max(50f, maxX - minX);
            float bh = Math.Max(50f, maxY - minY);

            float targetW = Math.Max(260f, picCanvas.Width * 0.68f);
            float targetH = Math.Max(240f, picCanvas.Height * 0.68f);

            float mediumZoom = Math.Min(targetW / bw, targetH / (bh + 180f));
            mediumZoom = Math.Max(0.05f, Math.Min(6.0f, mediumZoom));

            picCanvas.ResetView(mediumZoom);
        }

        private void CalculateLocalWipeoutBox(
            double localMinX, double localMaxX, double localMinY, double localMaxY,
            out double wipeX1, out double wipeX2, out double wipeY1, out double wipeY2)
        {
            double fullOuterSpanX = Math.Max(50.0, localMaxX - localMinX);
            double fullOuterSpanY = Math.Max(20.0, localMaxY - localMinY);

            double openingW = fullOuterSpanX;
            if (double.TryParse(txtOpeningWidth.Text.Trim(), out double userOpening) && userOpening > 10.0)
            {
                openingW = userOpening;
            }

            double wipeDepth = fullOuterSpanY;
            if (double.TryParse(txtWallDepth.Text.Trim(), out double userDepth) && userDepth > 10.0)
            {
                wipeDepth = userDepth;
            }

            double centerBBoxX = (localMinX + localMaxX) / 2.0;
            double centerBBoxY = (localMinY + localMaxY) / 2.0;

            wipeX1 = centerBBoxX - (openingW / 2.0);
            wipeX2 = centerBBoxX + (openingW / 2.0);

            bool isDoorOrWin = (_activeBlock != null && IsDoorOrWindowCategory(_activeBlock.Category));

            if (!isDoorOrWin || fullOuterSpanY <= wipeDepth * 1.35)
            {
                wipeY1 = centerBBoxY - (wipeDepth / 2.0);
                wipeY2 = centerBBoxY + (wipeDepth / 2.0);
            }
            else if (Math.Abs(localMinY) < 15.0 && localMaxY > wipeDepth * 1.35)
            {
                wipeY1 = 0.0;
                wipeY2 = wipeDepth;
            }
            else if (localMinY < -20.0 && Math.Abs(localMinY) <= wipeDepth * 1.6)
            {
                wipeY1 = -wipeDepth;
                wipeY2 = 0.0;
            }
            else
            {
                wipeY1 = localMinY;
                wipeY2 = localMinY + wipeDepth;
            }
        }

        private Point3d GetLocalPlacementGripPoint(double wipeX1, double wipeX2, double wipeY1, double wipeY2)
        {
            int alignIdx = cboPlacementAlign != null ? cboPlacementAlign.SelectedIndex : 0;
            double midX = (wipeX1 + wipeX2) / 2.0;
            double midY = (wipeY1 + wipeY2) / 2.0;

            double innerY = Math.Max(wipeY1, wipeY2);
            double outerY = Math.Min(wipeY1, wipeY2);

            if (alignIdx == (int)DoorPlacementAlignMode.InnerEdge)
            {
                return new Point3d(midX, innerY, 0);
            }
            else if (alignIdx == (int)DoorPlacementAlignMode.OuterEdge)
            {
                return new Point3d(midX, outerY, 0);
            }
            else
            {
                return new Point3d(midX, midY, 0);
            }
        }

        private void PicCanvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(WinColor.White);

            var lines = _liveEvaluatedLines.Count > 0 ? _liveEvaluatedLines : (_activeBlock != null ? _activeBlock.PreviewLines : null);
            if (lines == null || lines.Count == 0)
            {
                g.DrawString("Select a Block in Column 1 or click '+ Load DWG' / '+ Add CAD' to preview.",
                    new WinFont("Segoe UI", 9.5f, FontStyle.Italic), Brushes.Gray, new PointF(20, 40));
                return;
            }

            GraphicsState originalState = g.Save();
            g.Transform = picCanvas.GetWorldToScreenMatrix();

            float sc = (float)_currentBlockScale;
            float minX = lines.Min(l => Math.Min(l.Item1.X, l.Item2.X)) * sc;
            float maxX = lines.Max(l => Math.Max(l.Item1.X, l.Item2.X)) * sc;
            float minY = lines.Min(l => Math.Min(l.Item1.Y, l.Item2.Y)) * sc;
            float maxY = lines.Max(l => Math.Max(l.Item1.Y, l.Item2.Y)) * sc;

            float cx = (minX + maxX) / 2f;
            float cy = (minY + maxY) / 2f;
            float totalW = maxX - minX;
            float totalH = maxY - minY;

            bool isDoorWin = (_activeBlock != null && IsDoorOrWindowCategory(_activeBlock.Category));

            double wx1, wx2, wy1, wy2;
            CalculateLocalWipeoutBox(minX, maxX, minY, maxY, out wx1, out wx2, out wy1, out wy2);
            Point3d gripLocal = GetLocalPlacementGripPoint(wx1, wx2, wy1, wy2);

            // 1. Chỉ vẽ khung Wipeout màu cam khi ở Tab Door hoặc Window!
            if (isDoorWin && chkShowAutoDims.Checked)
            {
                float rx = (float)wx1 - cx;
                float ry = -((float)wy2 - cy);
                float rw = (float)(wx2 - wx1);
                float rh = (float)(wy2 - wy1);

                using (SolidBrush wipeFill = new SolidBrush(WinColor.FromArgb(40, 255, 165, 0)))
                using (Pen wipeBorder = new Pen(WinColor.FromArgb(220, 120, 0), 1.6f / picCanvas.ZoomFactor) { DashStyle = GdiDashStyle.Dash })
                {
                    g.FillRectangle(wipeFill, rx, ry, rw, rh);
                    g.DrawRectangle(wipeBorder, rx, ry, rw, rh);
                }
            }

            // 2. Vẽ toàn bộ nét thực tế của Block
            float penWidth = 1.8f / picCanvas.ZoomFactor;
            foreach (var seg in lines)
            {
                WinColor segColor = GetDisplayColorByCadIndex(seg.Item3);
                using (Pen p = new Pen(segColor, penWidth))
                {
                    float x1 = seg.Item1.X * sc - cx;
                    float y1 = -(seg.Item1.Y * sc - cy);
                    float x2 = seg.Item2.X * sc - cx;
                    float y2 = -(seg.Item2.Y * sc - cy);
                    g.DrawLine(p, x1, y1, x2, y2);
                }
            }

            // 3. Vẽ Điểm Cầm Chuột (Cursor Grip - Dấu tâm xanh lá)
            float gripScreenX = (float)gripLocal.X - cx;
            float gripScreenY = -((float)gripLocal.Y - cy);
            float crossSize = 10f / picCanvas.ZoomFactor;
            using (Pen gripPen = new Pen(WinColor.FromArgb(0, 160, 60), 2.0f / picCanvas.ZoomFactor))
            {
                g.DrawLine(gripPen, gripScreenX - crossSize, gripScreenY, gripScreenX + crossSize, gripScreenY);
                g.DrawLine(gripPen, gripScreenX, gripScreenY - crossSize, gripScreenX, gripScreenY + crossSize);
                g.DrawEllipse(gripPen, gripScreenX - crossSize * 0.55f, gripScreenY - crossSize * 0.55f, crossSize * 1.1f, crossSize * 1.1f);
            }

            // 4. Vẽ Đường Dim kích thước
            if (chkShowAutoDims.Checked && totalW > 1e-2 && totalH > 1e-2)
            {
                float leftX = minX - cx;
                float rightX = maxX - cx;
                float topY = -(maxY - cy);
                float botY = -(minY - cy);
                float dimOffset = 38f / picCanvas.ZoomFactor;

                if (isDoorWin)
                {
                    float wipeLeftX = (float)wx1 - cx;
                    float wipeRightX = (float)wx2 - cx;
                    double openingVal = wx2 - wx1;

                    DrawWorldDimension(g,
                        new PointF(wipeLeftX, botY + dimOffset),
                        new PointF(wipeRightX, botY + dimOffset),
                        new PointF(wipeLeftX, botY),
                        new PointF(wipeRightX, botY),
                        $"Opening (Wipeout W) = {openingVal:0.##}");
                }
                else
                {
                    DrawWorldDimension(g,
                        new PointF(leftX, botY + dimOffset),
                        new PointF(rightX, botY + dimOffset),
                        new PointF(leftX, botY),
                        new PointF(rightX, botY),
                        $"W = {totalW:0.##}");
                }

                DrawWorldDimension(g,
                    new PointF(leftX - dimOffset, topY),
                    new PointF(leftX - dimOffset, botY),
                    new PointF(leftX, topY),
                    new PointF(leftX, botY),
                    $"D/H = {totalH:0.##}", true);
            }

            g.Restore(originalState);

            string zoomPct = $"{(picCanvas.ZoomFactor * 100):0}%";
            string alignText = cboPlacementAlign != null && cboPlacementAlign.SelectedItem != null ? cboPlacementAlign.SelectedItem.ToString() : "Center";
            string wipeNote = isDoorWin ? $" | Dashed Orange Box: Wipeout ({txtOpeningWidth.Text} x {txtWallDepth.Text})" : " | No Wipeout (Pure Block)";
            g.DrawString($"Zoom: {zoomPct} | Green Target (+): Cursor Grip [{alignText}]{wipeNote}",
                new WinFont("Segoe UI", 8f, FontStyle.Italic), Brushes.DimGray, new PointF(10, 8));
        }

        private WinColor GetDisplayColorByCadIndex(int aci)
        {
            switch (aci)
            {
                case 1: return WinColor.FromArgb(210, 30, 30);
                case 2: return WinColor.FromArgb(200, 150, 0);
                case 3: return WinColor.FromArgb(20, 150, 40);
                case 4: return WinColor.FromArgb(0, 140, 170);
                case 5: return WinColor.FromArgb(0, 80, 190);
                case 6: return WinColor.FromArgb(170, 0, 170);
                case 8:
                case 9:
                case 250:
                case 251:
                case 252: return WinColor.FromArgb(120, 125, 135);
                case 40:
                case 42: return WinColor.FromArgb(210, 95, 0);
                default: return WinColor.FromArgb(0, 95, 180);
            }
        }

        private void DrawWorldDimension(Graphics g, PointF d1, PointF d2, PointF ext1, PointF ext2, string text, bool vertical = false)
        {
            float invScale = 1f / picCanvas.ZoomFactor;
            using (Pen dimPen = new Pen(WinColor.Firebrick, 1.3f * invScale))
            using (Pen extPen = new Pen(WinColor.FromArgb(165, 165, 165), 0.9f * invScale))
            using (WinFont f = new WinFont("Segoe UI", 9.0f * invScale, FontStyle.Bold))
            {
                g.DrawLine(extPen, ext1, d1);
                g.DrawLine(extPen, ext2, d2);
                g.DrawLine(dimPen, d1, d2);

                float tick = 6f * invScale;
                g.DrawLine(dimPen, d1.X - tick, d1.Y + tick, d1.X + tick, d1.Y - tick);
                g.DrawLine(dimPen, d2.X - tick, d2.Y + tick, d2.X + tick, d2.Y - tick);

                PointF mid = new PointF((d1.X + d2.X) / 2f, (d1.Y + d2.Y) / 2f);
                SizeF sz = g.MeasureString(text, f);

                if (!vertical)
                {
                    RectangleF rect = new RectangleF(mid.X - sz.Width / 2f, mid.Y - sz.Height / 2f, sz.Width, sz.Height);
                    g.FillRectangle(Brushes.White, rect);
                    g.DrawString(text, f, Brushes.Firebrick, rect.Location);
                }
                else
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(mid.X, mid.Y);
                    g.RotateTransform(-90f);
                    RectangleF rect = new RectangleF(-sz.Width / 2f, -sz.Height / 2f, sz.Width, sz.Height);
                    g.FillRectangle(Brushes.White, rect);
                    g.DrawString(text, f, Brushes.Firebrick, rect.Location);
                    g.Restore(st);
                }
            }
        }

        // =========================================================================
        // 10. CHÈN BLOCK: DOOR/WINDOW TỰ ĐẶT WIPEOUT PHÍA SAU; FURNITURE/TREE KHÔNG ĐẶT WIPEOUT
        // =========================================================================
        private void BtnInsertToCad_Click(object sender, EventArgs e)
        {
            InsertSelectedBlockFlow();
        }

        private void InsertSelectedBlockFlow()
        {
            if (_activeBlock == null)
            {
                MessageBox.Show("Please select a Block from Column 1 first!", "DB Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SyncActiveParamsFromUIControls();

            bool isDoorWin = IsDoorOrWindowCategory(_activeBlock.Category);

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            Application.MainWindow.Focus();

            using (DocumentLock docLock = doc.LockDocument())
            {
                EnsureBlockImportedToDatabase(db, _activeBlock);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (!bt.Has(_activeBlock.BlockName))
                    {
                        ed.WriteMessage($"\n[DBLIB] Error: Block '{_activeBlock.BlockName}' not found!");
                        return;
                    }

                    short layColor;
                    string targetLayer = GetTargetCompanyLayerByTab(_activeBlock.Category, out layColor);
                    EnsureCompanyLayer(db, tr, targetLayer, layColor);

                    ObjectId btrId = bt[_activeBlock.BlockName];
                    BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    BlockReference br = new BlockReference(Point3d.Origin, btrId)
                    {
                        Layer = targetLayer,
                        ScaleFactors = new Scale3d(_currentBlockScale, _currentBlockScale, _currentBlockScale)
                    };
                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);

                    if (br.IsDynamicBlock && _activeParams.Count > 0)
                    {
                        ApplyEditedParamsToBlockRef(br, _activeParams);
                    }

                    // Đo chính xác Bounding Box thực tế của đối tượng
                    double localMinX = 0.0, localMaxX = 1000.0, localMinY = -100.0, localMaxY = 0.0;
                    bool gotExtents = false;
                    try
                    {
                        Extents3d ext = br.GeometricExtents;
                        localMinX = ext.MinPoint.X;
                        localMaxX = ext.MaxPoint.X;
                        localMinY = ext.MinPoint.Y;
                        localMaxY = ext.MaxPoint.Y;
                        gotExtents = (localMaxX - localMinX > 1e-2);
                    }
                    catch { }

                    if (!gotExtents && _liveEvaluatedLines.Count > 0)
                    {
                        localMinX = _liveEvaluatedLines.Min(l => Math.Min(l.Item1.X, l.Item2.X)) * _currentBlockScale;
                        localMaxX = _liveEvaluatedLines.Max(l => Math.Max(l.Item1.X, l.Item2.X)) * _currentBlockScale;
                        localMinY = _liveEvaluatedLines.Min(l => Math.Min(l.Item1.Y, l.Item2.Y)) * _currentBlockScale;
                        localMaxY = _liveEvaluatedLines.Max(l => Math.Max(l.Item1.Y, l.Item2.Y)) * _currentBlockScale;
                    }

                    double wx1, wx2, wy1, wy2;
                    CalculateLocalWipeoutBox(localMinX, localMaxX, localMinY, localMaxY, out wx1, out wx2, out wy1, out wy2);
                    Point3d localGripPt = GetLocalPlacementGripPoint(wx1, wx2, wy1, wy2);

                    Vector3d gripToOriginOffset = Point3d.Origin - localGripPt;

                    double userAngleDeg = GetParamNumericValue(new string[] { "ANGLE", "GOC", "ROTATION" }, 0.0);
                    double baseUserAngleRad = userAngleDeg * Math.PI / 180.0;

                    List<Curve> candidateWalls = new List<Curve>();
                    if (isDoorWin)
                    {
                        foreach (ObjectId id in space)
                        {
                            if (id == br.ObjectId) continue;
                            Curve crv = tr.GetObject(id, OpenMode.ForRead) as Curve;
                            if (crv == null || !(crv is Line || crv is Polyline)) continue;

                            string lay = (crv.Layer ?? "").ToUpperInvariant();
                            if (lay.Contains("WALL") || lay.Contains("TUONG") || lay.Contains("HIDDEN") || lay.Contains("VACH") || lay == "0")
                            {
                                candidateWalls.Add(crv);
                            }
                        }
                    }

                    SmartWallPlacementJig jig = new SmartWallPlacementJig(br, gripToOriginOffset, baseUserAngleRad, isDoorWin, candidateWalls);
                    PromptResult jigRes = ed.Drag(jig);
                    if (jigRes.Status != PromptStatus.OK)
                    {
                        br.Erase(true);
                        tr.Abort();
                        return;
                    }

                    // CHỈ TẠO WIPEOUT ĐẶT PHÍA SAU BLOCK ĐỐI VỚI TAB DOOR & WINDOW!
                    // Các Tab khác (Furniture, Tree, Kitchen, WC...) KHÔNG tạo Wipeout.
                    if (isDoorWin)
                    {
                        CreateExactOpeningWipeoutBehindDoorWindow(
                            db, tr, space, br,
                            jig.FinalBlockOriginPoint,
                            jig.FinalRotation,
                            wx1, wx2, wy1, wy2,
                            targetLayer);
                    }

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (btr.HasAttributeDefinitions)
                    {
                        foreach (ObjectId id in btr)
                        {
                            AttributeDefinition ad = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                            if (ad != null && !ad.Constant)
                            {
                                using (AttributeReference ar = new AttributeReference())
                                {
                                    ar.SetAttributeFromBlock(ad, br.BlockTransform);
                                    ar.TextString = txtDoorTag.Text.Trim();
                                    br.AttributeCollection.AppendAttribute(ar);
                                    tr.AddNewlyCreatedDBObject(ar, true);
                                }
                            }
                        }
                    }

                    tr.Commit();
                    ed.Regen();
                    lblStatus.Text = isDoorWin
                        ? $"Placed '{_activeBlock.BlockName}' on [{targetLayer}] with Wipeout ({(wx2 - wx1):0.##} x {(wy2 - wy1):0.##}) behind block."
                        : $"Placed '{_activeBlock.BlockName}' on [{targetLayer}] (No Wipeout).";
                }
            }
        }

        // Đặt tấm Wipeout + 2 nét đóng mép tường nằm ngay phía sau (bên dưới) Block Cửa đi / Cửa sổ
        private void CreateExactOpeningWipeoutBehindDoorWindow(
            Database db,
            Transaction tr,
            BlockTableRecord space,
            BlockReference br,
            Point3d finalBlockOrigin,
            double rotation,
            double wx1,
            double wx2,
            double wy1,
            double wy2,
            string targetLayer)
        {
            try
            {
                double yMin = Math.Min(wy1, wy2) - 0.5;
                double yMax = Math.Max(wy1, wy2) + 0.5;

                Point2dCollection pts2d = new Point2dCollection();
                Point3d[] localCorners = new Point3d[]
                {
                    new Point3d(wx1, yMin, 0),
                    new Point3d(wx2, yMin, 0),
                    new Point3d(wx2, yMax, 0),
                    new Point3d(wx1, yMax, 0),
                    new Point3d(wx1, yMin, 0)
                };

                Matrix3d mat = Matrix3d.Displacement(finalBlockOrigin.GetAsVector()) * Matrix3d.Rotation(rotation, Vector3d.ZAxis, Point3d.Origin);
                Point3d[] worldCorners = new Point3d[5];
                for (int i = 0; i < 5; i++)
                {
                    worldCorners[i] = localCorners[i].TransformBy(mat);
                    pts2d.Add(new Point2d(worldCorners[i].X, worldCorners[i].Y));
                }

                Wipeout wo = new Wipeout();
                wo.SetDatabaseDefaults(db);
                wo.SetFrom(pts2d, Vector3d.ZAxis);
                wo.Layer = targetLayer;

                space.AppendEntity(wo);
                tr.AddNewlyCreatedDBObject(wo, true);

                EnsureCompanyLayer(db, tr, "DB-A-Wall", 2);
                Line capLeft = new Line(worldCorners[0], worldCorners[3]) { Layer = "DB-A-Wall" };
                Line capRight = new Line(worldCorners[1], worldCorners[2]) { Layer = "DB-A-Wall" };
                space.AppendEntity(capLeft); tr.AddNewlyCreatedDBObject(capLeft, true);
                space.AppendEntity(capRight); tr.AddNewlyCreatedDBObject(capRight, true);

                // Sắp xếp DrawOrder: Tường/Hatch dưới cùng -> Wipeout ở giữa -> Block Cửa & 2 nét đóng mép tường nằm trên cùng
                DrawOrderTable dot = (DrawOrderTable)tr.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
                ObjectIdCollection wipeIds = new ObjectIdCollection(new ObjectId[] { wo.ObjectId });
                dot.MoveToTop(wipeIds);

                ObjectIdCollection topIds = new ObjectIdCollection(new ObjectId[] { capLeft.ObjectId, capRight.ObjectId, br.ObjectId });
                dot.MoveToTop(topIds);
            }
            catch { }
        }

        private double GetParamNumericValue(string[] keywords, double fallbackValue)
        {
            foreach (var p in _activeParams)
            {
                string n = (p.PropertyName ?? "").ToUpperInvariant();
                foreach (string kw in keywords)
                {
                    if (n.Contains(kw))
                    {
                        double d;
                        if (double.TryParse(Convert.ToString(p.CurrentValue), out d) && d >= 0.0)
                            return d;
                    }
                }
            }
            return fallbackValue;
        }

        private void EnsureBlockImportedToDatabase(Database targetDb, BlockItemInfo item)
        {
            if (item == null || string.IsNullOrEmpty(item.SourceDwgPath) || !File.Exists(item.SourceDwgPath))
                return;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                if (bt.Has(item.BlockName))
                {
                    tr.Commit();
                    return;
                }
            }

            try
            {
                using (Database extDb = new Database(false, true))
                {
                    extDb.ReadDwgFile(item.SourceDwgPath, FileShare.ReadWrite, true, "");
                    ObjectIdCollection ids = new ObjectIdCollection();
                    using (Transaction trExt = extDb.TransactionManager.StartTransaction())
                    {
                        BlockTable btExt = (BlockTable)trExt.GetObject(extDb.BlockTableId, OpenMode.ForRead);
                        if (btExt.Has(item.BlockName))
                            ids.Add(btExt[item.BlockName]);
                        trExt.Commit();
                    }

                    if (ids.Count > 0)
                    {
                        IdMapping map = new IdMapping();
                        extDb.WblockCloneObjects(ids, targetDb.BlockTableId, map, DuplicateRecordCloning.Ignore, false);
                    }
                }
            }
            catch { }
        }

        // =========================================================================
        // 11. CỘT 3 (TAB 2): BẢNG THỐNG KÊ TƯƠNG TÁC NHƯ EXCEL
        // =========================================================================
        private void InitScheduleDataTable()
        {
            _scheduleTable = new WinDataTable("ScheduleBOQ");
            _scheduleTable.Columns.Add("No.", typeof(int));
            _scheduleTable.Columns.Add("Type (Block Name)", typeof(string));
            _scheduleTable.Columns.Add("Location", typeof(string));
            _scheduleTable.Columns.Add("Qty", typeof(int));
            _scheduleTable.Columns.Add("Note / Dimensions", typeof(string));

            dgvSchedule.DataSource = _scheduleTable;

            dgvSchedule.Columns[0].FillWeight = 10f;
            dgvSchedule.Columns[1].FillWeight = 28f;
            dgvSchedule.Columns[2].FillWeight = 24f;
            dgvSchedule.Columns[3].FillWeight = 12f;
            dgvSchedule.Columns[4].FillWeight = 26f;
        }

        private void BtnAddRow_Click(object sender, EventArgs e)
        {
            int nextNo = _scheduleTable.Rows.Count + 1;
            _scheduleTable.Rows.Add(nextNo, "New Item", "Floor 1", 1, "900 x 2200");
        }

        private void BtnDeleteRow_Click(object sender, EventArgs e)
        {
            if (dgvSchedule.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow r in dgvSchedule.SelectedRows)
                {
                    if (!r.IsNewRow) dgvSchedule.Rows.Remove(r);
                }
                RenumberScheduleRows();
            }
        }

        private void RenumberScheduleRows()
        {
            for (int i = 0; i < _scheduleTable.Rows.Count; i++)
            {
                _scheduleTable.Rows[i][0] = i + 1;
            }
        }

        private void BtnPickScheduleFromCad_Click(object sender, EventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            Application.MainWindow.Focus();
            PromptSelectionOptions pso = new PromptSelectionOptions()
            {
                MessageForAdding = "\n[DBLIB Schedule] Select or Window-select Blocks to quantify: "
            };
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT") };
            PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));
            if (psr.Status != PromptStatus.OK || psr.Value == null) return;

            string filterMode = cboScheduleFilter.SelectedItem.ToString();
            var grouped = new Dictionary<string, Tuple<string, string, int, string>>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    BlockReference br = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    string lay = (br.Layer ?? "").ToUpperInvariant();
                    if (filterMode.Contains("Doors") && !lay.Contains("DOOR") && !lay.Contains("WINDOW")) continue;
                    if (filterMode.Contains("Furniture") && !lay.Contains("FURNITURE") && !lay.Contains("EQUIP")) continue;
                    if (filterMode.Contains("Landscape") && !lay.Contains("PLANT") && !lay.Contains("TREE")) continue;

                    BlockTableRecord btr = br.IsDynamicBlock
                        ? (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)
                        : (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                    string blkName = btr.Name;
                    string sizeNote = ExtractDynamicSizeSummary(br);
                    string key = blkName + "|" + sizeNote;

                    if (grouped.ContainsKey(key))
                    {
                        var cur = grouped[key];
                        grouped[key] = Tuple.Create(cur.Item1, cur.Item2, cur.Item3 + 1, cur.Item4);
                    }
                    else
                    {
                        grouped[key] = Tuple.Create(blkName, br.Layer, 1, sizeNote);
                    }
                }
                tr.Commit();
            }

            _scheduleTable.Rows.Clear();
            int stt = 1;
            foreach (var kv in grouped.Values)
            {
                _scheduleTable.Rows.Add(stt++, kv.Item1, kv.Item2, kv.Item3, kv.Item4);
            }

            tabRightPanel.SelectedIndex = 1;
            lblStatus.Text = $"Quantified {psr.Value.Count} block instances into {_scheduleTable.Rows.Count} schedule rows.";
        }

        private string ExtractDynamicSizeSummary(BlockReference br)
        {
            List<string> parts = new List<string>();
            if (br.IsDynamicBlock)
            {
                foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                {
                    string n = p.PropertyName.ToUpperInvariant();
                    if (n.Contains("WIDTH") || n.Contains("WITH") || n.Contains("HEIGHT") || n.Contains("DEPTH") || n.Contains("DISTANCE") || n.Contains("VISIBILITY") || n.Contains("STYLE"))
                    {
                        if (p.Value is double)
                            parts.Add($"{p.PropertyName}: {Convert.ToDouble(p.Value):0}");
                        else if (p.Value != null)
                            parts.Add($"{p.Value}");
                    }
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "Standard";
        }

        private void BtnExportTableToCad_Click(object sender, EventArgs e)
        {
            if (_scheduleTable.Rows.Count == 0)
            {
                MessageBox.Show("Schedule table is empty! Please pick blocks from CAD or add rows first.", "DB Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            Application.MainWindow.Focus();
            PromptPointResult ppr = ed.GetPoint("\n[DBLIB Schedule] Pick point to place Schedule Table in CAD: ");
            if (ppr.Status != PromptStatus.OK) return;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureCompanyLayer(db, tr, "DB-A-Line thin", 250);
                EnsureCompanyLayer(db, tr, "DB-A-Text", 7);

                BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                int numRows = _scheduleTable.Rows.Count + 2;
                int numCols = 5;

                Table tb = new Table();
                tb.TableStyle = db.Tablestyle;
                tb.Layer = "DB-A-Line thin";
                tb.Position = ppr.Value;
                tb.SetSize(numRows, numCols);
                tb.SetRowHeight(350.0);

                double[] colWidths = new double[] { 900.0, 2800.0, 2400.0, 1100.0, 3200.0 };
                for (int c = 0; c < numCols; c++)
                {
                    tb.SetColumnWidth(c, colWidths[c]);
                }

                tb.SetTextString(0, 0, "BLOCK & EQUIPMENT SCHEDULE");
                tb.SetTextHeight(0, 0, 200.0);
                tb.SetAlignment(0, 0, CellAlignment.MiddleCenter);

                string[] headers = new string[] { "NO.", "TYPE / BLOCK", "LOCATION", "QTY", "NOTE / DIMENSIONS" };
                for (int c = 0; c < numCols; c++)
                {
                    tb.SetTextString(1, c, headers[c]);
                    tb.SetTextHeight(1, c, 150.0);
                    tb.SetAlignment(1, c, CellAlignment.MiddleCenter);
                }

                for (int r = 0; r < _scheduleTable.Rows.Count; r++)
                {
                    for (int c = 0; c < numCols; c++)
                    {
                        string val = Convert.ToString(_scheduleTable.Rows[r][c] ?? "");
                        tb.SetTextString(r + 2, c, val);
                        tb.SetTextHeight(r + 2, c, 140.0);
                        tb.SetAlignment(r + 2, c, (c == 0 || c == 3) ? CellAlignment.MiddleCenter : CellAlignment.MiddleLeft);
                    }
                }

                tb.GenerateLayout();
                space.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tr.Commit();
            }

            ed.Regen();
            lblStatus.Text = "Schedule Table exported to CAD successfully!";
        }

        // =========================================================================
        // 12. TRÍCH XUẤT HÌNH HỌC VECTOR ĐỆ QUY
        // =========================================================================
        private void ExtractRawLinesFromBtr(
            Transaction tr,
            BlockTableRecord btr,
            Matrix3d parentTransform,
            List<Tuple<PointF, PointF, int>> outLines,
            int depth)
        {
            if (btr == null || depth > 4) return;

            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;
                int cIdx = ent.ColorIndex;

                if (ent is BlockReference nestedBr)
                {
                    try
                    {
                        ObjectId subBtrId = nestedBr.BlockTableRecord;
                        BlockTableRecord subBtr = tr.GetObject(subBtrId, OpenMode.ForRead) as BlockTableRecord;
                        Matrix3d combinedMat = parentTransform * nestedBr.BlockTransform;
                        ExtractRawLinesFromBtr(tr, subBtr, combinedMat, outLines, depth + 1);
                    }
                    catch { }
                }
                else if (ent is Line ln)
                {
                    Point3d p1 = ln.StartPoint.TransformBy(parentTransform);
                    Point3d p2 = ln.EndPoint.TransformBy(parentTransform);
                    outLines.Add(Tuple.Create(new PointF((float)p1.X, (float)p1.Y),
                                              new PointF((float)p2.X, (float)p2.Y), cIdx));
                }
                else if (ent is Polyline pl)
                {
                    for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                    {
                        double bulge = pl.GetBulgeAt(i);
                        Point3d pt1 = pl.GetPoint3dAt(i).TransformBy(parentTransform);
                        Point3d pt2 = pl.GetPoint3dAt(i + 1).TransformBy(parentTransform);
                        if (Math.Abs(bulge) > 1e-4)
                            AddBulgeArcSegments(new Point2d(pt1.X, pt1.Y), new Point2d(pt2.X, pt2.Y), bulge, cIdx, outLines);
                        else
                            outLines.Add(Tuple.Create(new PointF((float)pt1.X, (float)pt1.Y),
                                                      new PointF((float)pt2.X, (float)pt2.Y), cIdx));
                    }
                    if (pl.Closed && pl.NumberOfVertices > 2)
                    {
                        Point3d pt1 = pl.GetPoint3dAt(pl.NumberOfVertices - 1).TransformBy(parentTransform);
                        Point3d pt2 = pl.GetPoint3dAt(0).TransformBy(parentTransform);
                        outLines.Add(Tuple.Create(new PointF((float)pt1.X, (float)pt1.Y),
                                                  new PointF((float)pt2.X, (float)pt2.Y), cIdx));
                    }
                }
                else if (ent is Arc arc)
                {
                    int segs = 20;
                    double span = arc.EndAngle >= arc.StartAngle ? (arc.EndAngle - arc.StartAngle) : (arc.EndAngle + Math.PI * 2 - arc.StartAngle);
                    for (int i = 0; i < segs; i++)
                    {
                        double a1 = arc.StartAngle + span * i / segs;
                        double a2 = arc.StartAngle + span * (i + 1) / segs;
                        Point3d p1 = new Point3d(arc.Center.X + arc.Radius * Math.Cos(a1), arc.Center.Y + arc.Radius * Math.Sin(a1), 0).TransformBy(parentTransform);
                        Point3d p2 = new Point3d(arc.Center.X + arc.Radius * Math.Cos(a2), arc.Center.Y + arc.Radius * Math.Sin(a2), 0).TransformBy(parentTransform);
                        outLines.Add(Tuple.Create(new PointF((float)p1.X, (float)p1.Y),
                                                  new PointF((float)p2.X, (float)p2.Y), cIdx));
                    }
                }
                else if (ent is Circle cir)
                {
                    int segs = 24;
                    for (int i = 0; i < segs; i++)
                    {
                        double a1 = Math.PI * 2 * i / segs;
                        double a2 = Math.PI * 2 * (i + 1) / segs;
                        Point3d p1 = new Point3d(cir.Center.X + cir.Radius * Math.Cos(a1), cir.Center.Y + cir.Radius * Math.Sin(a1), 0).TransformBy(parentTransform);
                        Point3d p2 = new Point3d(cir.Center.X + cir.Radius * Math.Cos(a2), cir.Center.Y + cir.Radius * Math.Sin(a2), 0).TransformBy(parentTransform);
                        outLines.Add(Tuple.Create(new PointF((float)p1.X, (float)p1.Y),
                                                  new PointF((float)p2.X, (float)p2.Y), cIdx));
                    }
                }
                else if (ent is Curve genericCrv)
                {
                    try
                    {
                        double stP = genericCrv.StartParam;
                        double enP = genericCrv.EndParam;
                        int segs = 16;
                        for (int i = 0; i < segs; i++)
                        {
                            double pA = stP + (enP - stP) * i / segs;
                            double pB = stP + (enP - stP) * (i + 1) / segs;
                            Point3d pt1 = genericCrv.GetPointAtParameter(pA).TransformBy(parentTransform);
                            Point3d pt2 = genericCrv.GetPointAtParameter(pB).TransformBy(parentTransform);
                            outLines.Add(Tuple.Create(new PointF((float)pt1.X, (float)pt1.Y),
                                                      new PointF((float)pt2.X, (float)pt2.Y), cIdx));
                        }
                    }
                    catch { }
                }
            }
        }

        private void AddBulgeArcSegments(Point2d p1, Point2d p2, double bulge, int cIdx, List<Tuple<PointF, PointF, int>> outLines)
        {
            double chord = p1.GetDistanceTo(p2);
            if (chord < 1e-4) return;
            double includedAngle = 4.0 * Math.Atan(bulge);
            double radius = Math.Abs(chord / (2.0 * Math.Sin(includedAngle / 2.0)));

            Vector2d dir = (p2 - p1) / chord;
            Vector2d normal = bulge > 0 ? new Vector2d(-dir.Y, dir.X) : new Vector2d(dir.Y, -dir.X);
            Point2d mid = new Point2d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
            double distToCenter = Math.Sqrt(Math.Max(0.0, radius * radius - (chord * chord) / 4.0));
            Point2d center = mid + normal * (Math.Abs(bulge) <= 1.0 ? distToCenter : -distToCenter);

            double startAng = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
            int segs = 12;
            for (int i = 0; i < segs; i++)
            {
                double a1 = startAng + includedAngle * i / segs;
                double a2 = startAng + includedAngle * (i + 1) / segs;
                PointF pt1 = new PointF((float)(center.X + radius * Math.Cos(a1)), (float)(center.Y + radius * Math.Sin(a1)));
                PointF pt2 = new PointF((float)(center.X + radius * Math.Cos(a2)), (float)(center.Y + radius * Math.Sin(a2)));
                outLines.Add(Tuple.Create(pt1, pt2, cIdx));
            }
        }

        private Bitmap RenderVectorLinesToBitmap(List<Tuple<PointF, PointF, int>> lines, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h);
            if (lines == null || lines.Count == 0) return bmp;

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(WinColor.FromArgb(250, 251, 254));

                float minX = lines.Min(l => Math.Min(l.Item1.X, l.Item2.X));
                float maxX = lines.Max(l => Math.Max(l.Item1.X, l.Item2.X));
                float minY = lines.Min(l => Math.Min(l.Item1.Y, l.Item2.Y));
                float maxY = lines.Max(l => Math.Max(l.Item1.Y, l.Item2.Y));

                float bw = Math.Max(1f, maxX - minX);
                float bh = Math.Max(1f, maxY - minY);
                float scale = Math.Min((w - 8f) / bw, (h - 8f) / bh);

                float ox = (w - bw * scale) / 2f;
                float oy = (h - bh * scale) / 2f;

                using (Pen pen = new Pen(WinColor.FromArgb(0, 100, 190), 1.2f))
                {
                    foreach (var seg in lines)
                    {
                        float x1 = ox + (seg.Item1.X - minX) * scale;
                        float y1 = h - (oy + (seg.Item1.Y - minY) * scale);
                        float x2 = ox + (seg.Item2.X - minX) * scale;
                        float y2 = h - (oy + (seg.Item2.Y - minY) * scale);
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
            }
            return bmp;
        }
    }

    // =========================================================================
    // 13. CONTROL CANVAS HỖ TRỢ LĂN CHUỘT ZOOM TO / THU NHỎ & KÉO PAN MƯỢT MÀ
    // =========================================================================
    public class ZoomablePreviewCanvas : PictureBox
    {
        public float ZoomFactor { get; private set; } = 0.42f;
        private PointF _panOffset = new PointF(0, 0);
        private bool _isPanning = false;
        private Point _lastMousePt;

        public ZoomablePreviewCanvas()
        {
            this.DoubleBuffered = true;
            this.MouseWheel += OnCanvasMouseWheel;
            this.MouseDown += OnCanvasMouseDown;
            this.MouseMove += OnCanvasMouseMove;
            this.MouseUp += OnCanvasMouseUp;
        }

        public void ResetView(float defaultZoom = 0.42f)
        {
            ZoomFactor = defaultZoom;
            _panOffset = new PointF(0, 0);
            this.Invalidate();
        }

        public GdiMatrix GetWorldToScreenMatrix()
        {
            GdiMatrix mat = new GdiMatrix();
            mat.Translate(this.Width / 2f + _panOffset.X, this.Height / 2f + _panOffset.Y);
            mat.Scale(ZoomFactor, ZoomFactor);
            return mat;
        }

        private void OnCanvasMouseWheel(object sender, MouseEventArgs e)
        {
            float oldZoom = ZoomFactor;
            if (e.Delta > 0)
                ZoomFactor *= 1.15f;
            else
                ZoomFactor /= 1.15f;

            ZoomFactor = Math.Max(0.01f, Math.Min(15.0f, ZoomFactor));

            float cx = this.Width / 2f + _panOffset.X;
            float cy = this.Height / 2f + _panOffset.Y;
            float ratio = ZoomFactor / oldZoom;

            _panOffset.X = (e.X - (e.X - cx) * ratio) - this.Width / 2f;
            _panOffset.Y = (e.Y - (e.Y - cy) * ratio) - this.Height / 2f;

            this.Invalidate();
        }

        private void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            this.Focus();
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _lastMousePt = e.Location;
                this.Cursor = Cursors.SizeAll;
            }
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _panOffset.X += (e.X - _lastMousePt.X);
                _panOffset.Y += (e.Y - _lastMousePt.Y);
                _lastMousePt = e.Location;
                this.Invalidate();
            }
        }

        private void OnCanvasMouseUp(object sender, MouseEventArgs e)
        {
            _isPanning = false;
            this.Cursor = Cursors.Default;
        }
    }
}