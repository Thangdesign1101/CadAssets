// ============================================================================
// DB ARCHITECT - APARTMENT & AREA SCHEDULE STUDIO V2.3
// 1. Tách Tag Căn hộ thành 2 MText độc lập (Tên căn 750 Bold & Diện tích 500 Regular)
//    để dễ chỉnh sửa và dùng lệnh MA (Matchprop), nhưng chỉ cần Pick 1 lần duy nhất!
// 2. Gộp ô "Hiệu suất = ...%" thành nút bấm trực tiếp để đặt Text % ra CAD.
// 3. Text % ngoài Model gọn gàng (không ghi Tử số/Mẫu số) và TỰ NHẢY SỐ KHI GÕ RE!
// 4. Mặc định Tử số & Mẫu số đều là "Pick / Quét CAD", 2 nút "Chọn" kích thước bằng nhau.
// ============================================================================
#define ZWCAD

using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Colors;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.GraphicsInterface;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Windows;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadColor = ZwSoft.ZwCAD.Colors.Color;
#else
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CadColor = Autodesk.AutoCAD.Colors.Color;
#endif

// Định danh tường minh tuyệt đối chống lỗi Ambiguous Reference (CS0104)
using WinColor = System.Drawing.Color;
using WinFont = System.Drawing.Font;
using WinPoint = System.Drawing.Point;
using WinSize = System.Drawing.Size;
using WinPanel = System.Windows.Forms.Panel;
using WinLabel = System.Windows.Forms.Label;
using WinButton = System.Windows.Forms.Button;
using WinTextBox = System.Windows.Forms.TextBox;
using WinCheckBox = System.Windows.Forms.CheckBox;
using WinComboBox = System.Windows.Forms.ComboBox;
using WinGroupBox = System.Windows.Forms.GroupBox;
using WinDataTable = System.Data.DataTable;
using CadPolyline =
#if ZWCAD
    ZwSoft.ZwCAD.DatabaseServices.Polyline;
#else
    Autodesk.AutoCAD.DatabaseServices.Polyline;
#endif

[assembly: CommandClass(typeof(DB_AreaScheduleStudio.AreaStudioCommand))]

namespace DB_AreaScheduleStudio
{
    public class AreaStudioCommand
    {
        private static PaletteSet _ps = null;
        private static AreaStudioMainControl _mainCtrl = null;

        [CommandMethod("DBAREA")]
        public void OpenAreaStudio()
        {
            ShowAreaPalette();
        }

        [CommandMethod("DBDT")]
        public void OpenAreaStudioShort()
        {
            ShowAreaPalette();
        }

        private void ShowAreaPalette()
        {
            Rectangle screenArea = Screen.PrimaryScreen.WorkingArea;
            int targetW = Math.Max(800, (int)(screenArea.Width * 0.46));
            int targetH = Math.Max(780, (int)(screenArea.Height * 0.86));

            if (_ps == null)
            {
                _ps = new PaletteSet("DB Architect - Quản Lý & Thống Kê Diện Tích Căn Hộ",
                    new Guid("F2A7D934-6B1E-4C82-A590-3D7E1B9C4F62"));

                _ps.Style = PaletteSetStyles.ShowAutoHideButton |
                            PaletteSetStyles.ShowCloseButton |
                            PaletteSetStyles.ShowPropertiesMenu;

                _ps.DockEnabled = DockSides.None | DockSides.Left | DockSides.Right;
                _ps.MinimumSize = new WinSize(680, 650);

                _mainCtrl = new AreaStudioMainControl();
                _ps.Add("Area Studio", _mainCtrl);
            }

            _ps.Visible = true;
            try
            {
                _ps.Dock = DockSides.None;
                _ps.Size = new WinSize(targetW, targetH);
            }
            catch { }
        }
    }

    public class UnitAreaRecord
    {
        public string UnitType { get; set; } = "2BR";
        public string UnitCode { get; set; } = "2BR-A";
        public Dictionary<string, double> Areas { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public double GetArea(string key)
        {
            return Areas.ContainsKey(key) ? Areas[key] : 0.0;
        }
    }

    // =========================================================================
    // DRAW JIG KÉP: RÊ CHUỘT & ĐẶT CÙNG LÚC 2 MTEXT (TÊN CĂN + DIỆN TÍCH) CHỈ VỚI 1 CLICK
    // =========================================================================
    public class DualMTextPlacementJig : DrawJig
    {
        private MText _mtTitle;
        private MText _mtArea;
        private Point3d _currentPt;
        private Vector3d _titleOffset;

        public DualMTextPlacementJig(MText mtTitle, MText mtArea, Point3d initPt, Vector3d titleOffset)
        {
            _mtTitle = mtTitle;
            _mtArea = mtArea;
            _currentPt = initPt;
            _titleOffset = titleOffset;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions jppo = new JigPromptPointOptions("\n[DBAREA] Rê chuột và Click 1 điểm đặt Tag (Góc dưới-trái): ")
            {
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NoZeroResponseAccepted
            };

            PromptPointResult ppr = prompts.AcquirePoint(jppo);
            if (ppr.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            if (_currentPt.DistanceTo(ppr.Value) < 1e-3)
                return SamplerStatus.NoChange;

            _currentPt = ppr.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (_mtArea != null)
            {
                _mtArea.Location = _currentPt;
                draw.Geometry.Draw(_mtArea);
            }
            if (_mtTitle != null)
            {
                _mtTitle.Location = _currentPt + _titleOffset;
                draw.Geometry.Draw(_mtTitle);
            }
            return true;
        }

        public Point3d PickedPoint => _currentPt;
    }

    // =========================================================================
    // ENTITY JIG ĐƠN: DÙNG CHO TEXT HIỆU SUẤT (%)
    // =========================================================================
    public class MTextPlacementJig : EntityJig
    {
        private Point3d _currentPt;

        public MTextPlacementJig(MText mt, Point3d initPt) : base(mt)
        {
            _currentPt = initPt;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions jppo = new JigPromptPointOptions("\n[DBAREA] Rê chuột và Click chọn điểm đặt Text Hiệu suất % (Góc dưới-trái): ")
            {
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NoZeroResponseAccepted
            };

            PromptPointResult ppr = prompts.AcquirePoint(jppo);
            if (ppr.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            if (_currentPt.DistanceTo(ppr.Value) < 1e-3)
                return SamplerStatus.NoChange;

            _currentPt = ppr.Value;
            return SamplerStatus.OK;
        }

        protected override bool Update()
        {
            MText mt = Entity as MText;
            if (mt != null)
            {
                mt.Location = _currentPt;
            }
            return true;
        }

        public Point3d PickedPoint => _currentPt;
    }

    public class AreaStudioMainControl : UserControl
    {
        private const string RegAppName = "DB_AREA_STUDIO";
        private const string RegAppEffName = "DB_AREA_EFF";
        private readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DB_AreaStudio_UnitTypes.txt");

        // Bảng màu Nâu Trung Tính Pastel (Warm Taupe Pastel)
        private readonly WinColor ClrBgMain = WinColor.FromArgb(245, 239, 230);
        private readonly WinColor ClrBgCard = WinColor.FromArgb(252, 249, 245);
        private readonly WinColor ClrTaupeHeader = WinColor.FromArgb(141, 123, 104);
        private readonly WinColor ClrTaupeDark = WinColor.FromArgb(107, 91, 75);
        private readonly WinColor ClrTaupeAccent = WinColor.FromArgb(164, 144, 124);
        private readonly WinColor ClrTaupeSoftBtn = WinColor.FromArgb(232, 221, 208);
        private readonly WinColor ClrTextDark = WinColor.FromArgb(62, 50, 40);

        private List<string> _unitTypes = new List<string>()
        {
            "1BR", "2BR", "3BR", "STUDIO", "DUAL KEY", "PENTHOUSE", "DUPLEX", "TMDV", "SHCD"
        };
        private readonly string[] _areaTypes = { "GBA", "CFA", "GFA", "GSA", "NSA" };

        private SplitContainer _splitMain;
        private WinComboBox _cboUnitType;
        private WinButton _btnAddUnitType;
        private WinButton _btnRemoveUnitType;
        private WinComboBox _cboSuffixMode;
        private WinTextBox _txtSuffixVal;
        private WinTextBox _txtFullCodePreview;
        private WinCheckBox _chkAutoIncrement;

        private Dictionary<string, WinCheckBox> _chkAreas = new Dictionary<string, WinCheckBox>(StringComparer.OrdinalIgnoreCase);

        private WinTextBox _txtLayer;
        private WinTextBox _txtTitleHeight;
        private WinTextBox _txtSubHeight;
        private WinComboBox _cboDecimals;

        private PictureBox _picTagPreview;
        private WinButton _btnPickPolyline;
        private WinButton _btnResetSuffix;

        // Controls cho Mục 4: Công thức Hiệu Suất (%) (Tử số / Mẫu số)
        private WinComboBox _cboNumSource;
        private WinTextBox _txtNumValue;
        private WinButton _btnPickNum;
        private WinComboBox _cboDenSource;
        private WinTextBox _txtDenValue;
        private WinButton _btnPickDen;
        private WinButton _btnEffCombinedPlace; // Nút gộp chung: vừa hiển thị "Hiệu suất = ...%" vừa click để đặt ra CAD
        private WinCheckBox _chkIncludeEffInTable;

        // Lưu danh sách Handle của các đối tượng Tử số & Mẫu số để tự động nhảy % khi gõ RE (REGEN)
        private List<string> _numPickedHandles = new List<string>();
        private List<string> _denPickedHandles = new List<string>();

        // Controls cho Mục 5: Bảng Thống Kê
        private DataGridView _dgvSchedule;
        private WinDataTable _dtSchedule;
        private WinButton _btnScanCad;
        private WinButton _btnExportCadTable;
        private WinButton _btnExportCsv;
        private WinButton _btnRowUp;
        private WinButton _btnRowDown;
        private WinButton _btnRowAdd;
        private WinButton _btnRowDelete;
        private WinLabel _lblStatus;

        private bool _isUpdatingSum = false;
        private HashSet<IntPtr> _hookedDocs = new HashSet<IntPtr>();
        private Dictionary<string, double> _latestTotalAreasByType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public AreaStudioMainControl()
        {
            LoadCustomUnitTypes();
            InitializePastelUI();
            UpdateFullUnitCode();
            RefreshScheduleColumns();
            HookActiveDocumentRegenUpdater();
        }

        // =====================================================================
        // BỘ TỰ ĐỘNG CẬP NHẬT DIỆN TÍCH & HIỆU SUẤT (%) KHI CO KÉO VÀ GÕ RE (REGEN)
        // =====================================================================
        private void HookActiveDocumentRegenUpdater()
        {
            try
            {
                Document doc = CadApp.DocumentManager.MdiActiveDocument;
                if (doc != null && !_hookedDocs.Contains(doc.UnmanagedObject))
                {
                    _hookedDocs.Add(doc.UnmanagedObject);
                    doc.CommandWillStart += Doc_CommandWillStart;
                }
                CadApp.DocumentManager.DocumentActivated += (s, e) =>
                {
                    if (e.Document != null && !_hookedDocs.Contains(e.Document.UnmanagedObject))
                    {
                        _hookedDocs.Add(e.Document.UnmanagedObject);
                        e.Document.CommandWillStart += Doc_CommandWillStart;
                    }
                };
            }
            catch { }
        }

        private void Doc_CommandWillStart(object sender, CommandEventArgs e)
        {
            string cmd = (e.GlobalCommandName ?? "").ToUpperInvariant();
            if (cmd == "REGEN" || cmd == "REGENALL" || cmd == "RE" || cmd == "REA")
            {
                UpdateAllLinkedAreaAndEfficiencyTagsInCurrentDocument();
            }
        }

        private void UpdateAllLinkedAreaAndEfficiencyTagsInCurrentDocument()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "MTEXT") };
                PromptSelectionResult psr = ed.SelectAll(new SelectionFilter(filter));
                if (psr.Status != PromptStatus.OK || psr.Value == null) return;

                double hTitle = ParsePositiveDouble(_txtTitleHeight.Text, 750.0);
                double hSub = ParsePositiveDouble(_txtSubHeight.Text, 500.0);

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Bước 1: Cập nhật toàn bộ MText diện tích căn hộ liên kết với Polyline
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so == null) continue;
                        MText mt = tr.GetObject(so.ObjectId, OpenMode.ForRead) as MText;
                        if (mt == null) continue;

                        ResultBuffer rb = mt.GetXDataForApplication(RegAppName);
                        if (rb == null) continue;

                        TypedValue[] vals = rb.AsArray();
                        if (vals == null || vals.Length < 4) continue;

                        string unitType = CleanTagText(vals[1].Value?.ToString() ?? "");
                        string unitCode = CleanTagText(vals[2].Value?.ToString() ?? "");
                        string rawAreas = vals[3].Value?.ToString() ?? "";
                        string rawHandles = (vals.Length >= 5) ? (vals[4].Value?.ToString() ?? "") : "";
                        string titleHandle = (vals.Length >= 6) ? (vals[5].Value?.ToString() ?? "") : "";

                        // Nếu là MText Diện tích rời (có liên kết tới MText Tên căn qua titleHandle), đọc tên căn mới nhất từ MText Tên căn
                        if (!string.IsNullOrEmpty(titleHandle) && TryGetEntityByHandleString(db, tr, titleHandle, out Entity titleEnt) && titleEnt is MText mtTitleObj)
                        {
                            string editedTitle = CleanTagText(mtTitleObj.Text);
                            if (!string.IsNullOrEmpty(editedTitle))
                            {
                                unitCode = editedTitle;
                                unitType = InferUnitTypeFromCode(unitCode, unitType);
                            }
                        }

                        Dictionary<string, double> updatedAreas = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        foreach (string part in rawAreas.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string[] kv = part.Split('=');
                            if (kv.Length == 2 && double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                                updatedAreas[kv[0].Trim()] = d;
                        }

                        Dictionary<string, string> handleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        bool anyPolyChanged = false;

                        foreach (string hPart in rawHandles.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string[] kv = hPart.Split('=');
                            if (kv.Length == 2)
                            {
                                string modeKey = kv[0].Trim();
                                string hStr = kv[1].Trim();
                                handleMap[modeKey] = hStr;

                                if (TryGetEntityByHandleString(db, tr, hStr, out Entity polyEnt) && polyEnt != null)
                                {
                                    double newArea = ExtractEntityAreaInM2(polyEnt);
                                    if (newArea > 0.0)
                                    {
                                        updatedAreas[modeKey] = newArea;
                                        anyPolyChanged = true;
                                    }
                                }
                            }
                        }

                        if (anyPolyChanged)
                        {
                            mt.UpgradeOpen();
                            // Nếu là MText tách đôi (chỉ chứa dòng diện tích), chỉ cập nhật dòng diện tích thuần
                            bool isSplitAreaOnly = !string.IsNullOrEmpty(titleHandle) || IsAreaOnlyMText(mt.Text);
                            if (isSplitAreaOnly)
                            {
                                mt.Contents = BuildAreaOnlyMTextContents(updatedAreas);
                            }
                            else
                            {
                                mt.Contents = BuildCleanMTextContents(unitCode, updatedAreas, mt.TextHeight > 0 ? mt.TextHeight : hTitle, hSub);
                            }
                            AttachApartmentXData(db, tr, mt, unitType, unitCode, updatedAreas, handleMap, titleHandle);
                        }
                    }

                    // Bước 2: Cập nhật toàn bộ MText Hiệu suất (%) ngoài Model và trên Bảng khi co kéo Polyline + gõ RE
                    foreach (SelectedObject so in psr.Value)
                    {
                        if (so == null) continue;
                        MText mtEff = tr.GetObject(so.ObjectId, OpenMode.ForRead) as MText;
                        if (mtEff == null) continue;

                        ResultBuffer rbEff = mtEff.GetXDataForApplication(RegAppEffName);
                        if (rbEff == null) continue;

                        TypedValue[] eVals = rbEff.AsArray();
                        if (eVals == null || eVals.Length < 5) continue;

                        string numMode = eVals[1].Value?.ToString() ?? "";
                        string numHandlesStr = eVals[2].Value?.ToString() ?? "";
                        string denMode = eVals[3].Value?.ToString() ?? "";
                        string denHandlesStr = eVals[4].Value?.ToString() ?? "";

                        double newNum = ComputeTotalAreaFromHandles(db, tr, numHandlesStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), numMode);
                        double newDen = ComputeTotalAreaFromHandles(db, tr, denHandlesStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), denMode);

                        if (newNum > 0 && newDen > 1e-6)
                        {
                            double newPct = (newNum / newDen) * 100.0;
                            string pctStr = FormatEfficiencyPercentString(newPct);

                            mtEff.UpgradeOpen();
                            // Giữ nguyên tiền tố nếu người dùng chỉ để "80.7%" hoặc "Hiệu suất: 80.7%"
                            string curPlain = CleanTagText(mtEff.Text);
                            if (curPlain.IndexOf("Hiệu suất", StringComparison.OrdinalIgnoreCase) >= 0)
                                mtEff.Contents = $"Hiệu suất: {pctStr}";
                            else
                                mtEff.Contents = pctStr;
                        }
                    }

                    // Bước 3: Cập nhật luôn 2 ô Tử số & Mẫu số trên bảng nếu đã Pick đối tượng ngoài CAD
                    if (_numPickedHandles.Count > 0)
                    {
                        double liveNum = ComputeTotalAreaFromHandles(db, tr, _numPickedHandles, GetPreferredAreaKeyFromCombo(_cboNumSource));
                        if (liveNum > 0)
                            _txtNumValue.Text = liveNum.ToString(GetDecimalFormatString(), CultureInfo.InvariantCulture);
                    }
                    if (_denPickedHandles.Count > 0)
                    {
                        double liveDen = ComputeTotalAreaFromHandles(db, tr, _denPickedHandles, GetPreferredAreaKeyFromCombo(_cboDenSource));
                        if (liveDen > 0)
                            _txtDenValue.Text = liveDen.ToString(GetDecimalFormatString(), CultureInfo.InvariantCulture);
                    }

                    tr.Commit();
                }

                RecalculateEfficiencyPercentage();
            }
            catch { }
        }

        private double ComputeTotalAreaFromHandles(Database db, Transaction tr, IEnumerable<string> handles, string preferredAreaKey)
        {
            if (handles == null) return 0.0;
            double sum = 0.0;
            foreach (string hStr in handles)
            {
                if (!TryGetEntityByHandleString(db, tr, hStr, out Entity ent) || ent == null)
                    continue;

                if (ent is MText mt)
                {
                    UnitAreaRecord rec = TryReadApartmentRecordFromMText(mt);
                    if (rec != null && rec.Areas.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(preferredAreaKey) && rec.Areas.ContainsKey(preferredAreaKey))
                            sum += rec.Areas[preferredAreaKey];
                        else
                            sum += rec.Areas.Values.First();
                    }
                    else
                    {
                        double v = TryExtractAnyNumberFromText(mt.Text);
                        if (v > 0) sum += v;
                    }
                }
                else if (ent is DBText dbt)
                {
                    double v = TryExtractAnyNumberFromText(dbt.TextString);
                    if (v > 0) sum += v;
                }
                else if (ent is Curve)
                {
                    double a = ExtractEntityAreaInM2(ent);
                    if (a > 0) sum += a;
                }
            }
            return sum;
        }

        private bool IsAreaOnlyMText(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return false;
            string[] lines = plainText.Split(new[] { "\r\n", "\n", "\r", "\\P" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;
            string first = CleanTagText(lines[0]);
            return first.Contains(":");
        }

        private bool TryGetEntityByHandleString(Database db, Transaction tr, string handleHex, out Entity ent)
        {
            ent = null;
            if (string.IsNullOrWhiteSpace(handleHex)) return false;
            try
            {
                long hVal = Convert.ToInt64(handleHex, 16);
                Handle h = new Handle(hVal);
                if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                {
                    ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    return ent != null;
                }
            }
            catch { }
            return false;
        }

        private void LoadCustomUnitTypes()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var lines = File.ReadAllLines(_configPath, Encoding.UTF8)
                                    .Select(x => x.Trim())
                                    .Where(x => !string.IsNullOrEmpty(x))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();
                    if (lines.Count > 0) _unitTypes = lines;
                }
            }
            catch { }
        }

        private void SaveCustomUnitTypes()
        {
            try
            {
                File.WriteAllLines(_configPath, _unitTypes, Encoding.UTF8);
            }
            catch { }
        }

        private void InitializePastelUI()
        {
            this.Dock = DockStyle.Fill;
            this.BackColor = ClrBgMain;
            this.Font = new WinFont("Segoe UI", 8.75f, FontStyle.Regular);
            this.ForeColor = ClrTextDark;

            WinPanel pnlStatus = new WinPanel()
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                BackColor = ClrTaupeHeader,
                Padding = new Padding(8, 3, 8, 3)
            };
            _lblStatus = new WinLabel()
            {
                Text = "Sẵn sàng. Tag xuất ra tách làm 2 MText độc lập (dễ MA/sửa) và tự nhảy diện tích & % khi gõ RE!",
                Dock = DockStyle.Fill,
                ForeColor = WinColor.White,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Regular)
            };
            pnlStatus.Controls.Add(_lblStatus);

            _splitMain = new SplitContainer()
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance = 515,
                SplitterWidth = 6,
                BackColor = ClrTaupeAccent
            };

            // =================================================================
            // NỬA TRÊN: THIẾT LẬP CĂN HỘ, LOẠI ĐO, LIVE PREVIEW & HIỆU SUẤT (%)
            // =================================================================
            WinPanel pnlTop = new WinPanel()
            {
                Dock = DockStyle.Fill,
                BackColor = ClrBgMain,
                Padding = new Padding(8),
                AutoScroll = true
            };

            TableLayoutPanel tlpTop = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 5
            };
            tlpTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // --- MỤC 1: ĐỊNH DANH CĂN HỘ ---
            WinGroupBox grpUnit = CreatePastelGroupBox("1. Định Danh Căn Hộ (Auto-Increment & Thêm/Xóa Loại Căn + / -)");
            TableLayoutPanel tlpUnit = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 4,
                RowCount = 3,
                Padding = new Padding(4)
            };
            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f));
            tlpUnit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            WinPanel pnlUnitTypeRow = new WinPanel() { Dock = DockStyle.Fill, Height = 26, Margin = new Padding(3, 3, 3, 3) };
            _cboUnitType = new WinComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboUnitType.Items.AddRange(_unitTypes.ToArray());
            if (_cboUnitType.Items.Count > 1) _cboUnitType.SelectedIndex = 1;
            else if (_cboUnitType.Items.Count > 0) _cboUnitType.SelectedIndex = 0;

            _cboUnitType.SelectedIndexChanged += (s, e) =>
            {
                ResetSuffixToFirst();
                UpdateFullUnitCode();
            };

            _btnAddUnitType = new WinButton()
            {
                Text = "+",
                Dock = DockStyle.Right,
                Width = 25,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeSoftBtn,
                Font = new WinFont("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnAddUnitType.FlatAppearance.BorderColor = ClrTaupeAccent;
            _btnAddUnitType.Click += BtnAddUnitType_Click;

            _btnRemoveUnitType = new WinButton()
            {
                Text = "-",
                Dock = DockStyle.Right,
                Width = 25,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeSoftBtn,
                Font = new WinFont("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRemoveUnitType.FlatAppearance.BorderColor = ClrTaupeAccent;
            _btnRemoveUnitType.Click += BtnRemoveUnitType_Click;

            pnlUnitTypeRow.Controls.Add(_cboUnitType);
            pnlUnitTypeRow.Controls.Add(_btnAddUnitType);
            pnlUnitTypeRow.Controls.Add(_btnRemoveUnitType);

            _cboSuffixMode = new WinComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboSuffixMode.Items.AddRange(new object[] { "Chữ (-A, -B...)", "Số (-01, -02...)", "Không hậu tố" });
            _cboSuffixMode.SelectedIndex = 0;
            _cboSuffixMode.SelectedIndexChanged += (s, e) =>
            {
                ResetSuffixToFirst();
                UpdateFullUnitCode();
            };

            _txtSuffixVal = new WinTextBox() { Dock = DockStyle.Fill, Text = "A", TextAlign = HorizontalAlignment.Center };
            _txtSuffixVal.TextChanged += (s, e) => UpdateFullUnitCode();

            _txtFullCodePreview = new WinTextBox()
            {
                Dock = DockStyle.Fill,
                Text = "2BR-A",
                Font = new WinFont("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = WinColor.FromArgb(255, 253, 248),
                ForeColor = ClrTaupeDark
            };
            _txtFullCodePreview.TextChanged += (s, e) => _picTagPreview?.Invalidate();

            _chkAutoIncrement = new WinCheckBox()
            {
                Text = "Tự nhảy tên sau khi đo (A -> B). Nếu căn lặp lại: dùng lệnh COPY",
                Dock = DockStyle.Fill,
                Checked = true,
                AutoSize = true
            };

            _btnResetSuffix = new WinButton()
            {
                Text = "Reset A / 01",
                Dock = DockStyle.Fill,
                Height = 25,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeSoftBtn,
                Cursor = Cursors.Hand
            };
            _btnResetSuffix.FlatAppearance.BorderColor = ClrTaupeAccent;
            _btnResetSuffix.Click += (s, e) => { ResetSuffixToFirst(); UpdateFullUnitCode(); };

            tlpUnit.Controls.Add(CreateFieldLabel("Loại căn:"), 0, 0);
            tlpUnit.Controls.Add(pnlUnitTypeRow, 1, 0);
            tlpUnit.Controls.Add(CreateFieldLabel("Kiểu nhảy:"), 2, 0);
            tlpUnit.Controls.Add(_cboSuffixMode, 3, 0);

            tlpUnit.Controls.Add(CreateFieldLabel("Hậu tố:"), 0, 1);
            tlpUnit.Controls.Add(_txtSuffixVal, 1, 1);
            tlpUnit.Controls.Add(CreateFieldLabel("Mã xuất ra:"), 2, 1);
            tlpUnit.Controls.Add(_txtFullCodePreview, 3, 1);

            tlpUnit.Controls.Add(_chkAutoIncrement, 0, 2);
            tlpUnit.SetColumnSpan(_chkAutoIncrement, 3);
            tlpUnit.Controls.Add(_btnResetSuffix, 3, 2);

            grpUnit.Controls.Add(tlpUnit);

            // --- MỤC 2: TÍCH CHỌN 5 LOẠI DIỆN TÍCH ---
            WinGroupBox grpAreaModes = CreatePastelGroupBox("2. Tích Chọn Thông Số Đo (Chọn 1 hoặc Nhiều)");
            TableLayoutPanel tlpAreas = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(6, 2, 6, 2)
            };
            for (int i = 0; i < 5; i++)
                tlpAreas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            for (int i = 0; i < _areaTypes.Length; i++)
            {
                string aType = _areaTypes[i];
                WinCheckBox chk = new WinCheckBox()
                {
                    Text = aType,
                    Dock = DockStyle.Fill,
                    Font = new WinFont("Segoe UI", 9f, FontStyle.Bold),
                    Checked = (aType == "NSA"),
                    Cursor = Cursors.Hand
                };
                chk.CheckedChanged += (s, e) =>
                {
                    _picTagPreview?.Invalidate();
                    RefreshScheduleColumns();
                };
                _chkAreas[aType] = chk;
                tlpAreas.Controls.Add(chk, i, 0);
            }
            grpAreaModes.Controls.Add(tlpAreas);

            // --- MỤC 3: QUY CHUẨN CHỮ & LIVE PREVIEW (TÁCH 2 MTEXT ĐỘC LẬP ĐỂ DỄ MA / SỬA) ---
            WinGroupBox grpStyleAndPreview = CreatePastelGroupBox("3. Quy Chuẩn Text & Live Preview (Tách 2 MText Độc Lập để dễ MA/Chỉnh sửa)");
            TableLayoutPanel tlpStylePreview = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(4)
            };
            tlpStylePreview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            tlpStylePreview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));

            TableLayoutPanel tlpStyleInputs = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 4
            };
            tlpStyleInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95f));
            tlpStyleInputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _txtLayer = new WinTextBox() { Dock = DockStyle.Fill, Text = "DB-A-Text" };
            _txtTitleHeight = new WinTextBox() { Dock = DockStyle.Fill, Text = "750" };
            _txtSubHeight = new WinTextBox() { Dock = DockStyle.Fill, Text = "500" };
            _cboDecimals = new WinComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboDecimals.Items.AddRange(new object[] { "0 (VD: 70 m² | 55%)", "1 (VD: 70.5 m² | 55.0%)", "2 (VD: 70.55 m² | 55.25%)" });
            _cboDecimals.SelectedIndex = 1;

            _txtTitleHeight.TextChanged += (s, e) => _picTagPreview?.Invalidate();
            _txtSubHeight.TextChanged += (s, e) => _picTagPreview?.Invalidate();
            _cboDecimals.SelectedIndexChanged += (s, e) =>
            {
                _picTagPreview?.Invalidate();
                RefreshScheduleColumns();
                RecalculateEfficiencyPercentage();
            };

            tlpStyleInputs.Controls.Add(CreateFieldLabel("Layer chuẩn:"), 0, 0);
            tlpStyleInputs.Controls.Add(_txtLayer, 1, 0);
            tlpStyleInputs.Controls.Add(CreateFieldLabel("Cao chữ Tên:"), 0, 1);
            tlpStyleInputs.Controls.Add(_txtTitleHeight, 1, 1);
            tlpStyleInputs.Controls.Add(CreateFieldLabel("Cao chữ DT:"), 0, 2);
            tlpStyleInputs.Controls.Add(_txtSubHeight, 1, 2);
            tlpStyleInputs.Controls.Add(CreateFieldLabel("Số thập phân:"), 0, 3);
            tlpStyleInputs.Controls.Add(_cboDecimals, 1, 3);

            _picTagPreview = new PictureBox()
            {
                Dock = DockStyle.Fill,
                Height = 100,
                BackColor = WinColor.FromArgb(24, 26, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            _picTagPreview.Paint += PicTagPreview_Paint;
            _picTagPreview.Resize += (s, e) => _picTagPreview.Invalidate();

            tlpStylePreview.Controls.Add(tlpStyleInputs, 0, 0);
            tlpStylePreview.Controls.Add(_picTagPreview, 1, 0);
            grpStyleAndPreview.Controls.Add(tlpStylePreview);

            // --- NÚT ĐO POLYLINE & ĐẶT TAG (TÁCH 2 MTEXT ĐỘC LẬP NHƯNG PICK 1 LẦN) ---
            _btnPickPolyline = new WinButton()
            {
                Text = "ĐO POLYLINE & ĐẶT TAG (TÁCH 2 MTEXT ĐỘC LẬP + GÕ RE TỰ NHẢY SỐ)",
                Dock = DockStyle.Top,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeDark,
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 9.25f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 0, 6)
            };
            _btnPickPolyline.FlatAppearance.BorderColor = ClrTaupeHeader;
            _btnPickPolyline.Click += BtnPickPolyline_Click;

            // --- MỤC 4: CÔNG THỨC HIỆU SUẤT (%) (2 NÚT CHỌN BẰNG NHAU + GỘP NÚT HIỆU SUẤT) ---
            WinGroupBox grpEfficiency = CreatePastelGroupBox("4. Công Thức Tính Hiệu Suất (%) = Tử Số / Mẫu Số (Click vào ô Hiệu suất để đặt ra CAD)");
            TableLayoutPanel tlpEffMain = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(4, 2, 4, 2)
            };
            tlpEffMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66f));
            tlpEffMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));

            // Bảng bên trái: Khoá cứng chiều cao Row 0 = 30px, Row 1 = 8px, Row 2 = 30px
            // Đảm bảo nút "Chọn" ở Mẫu số có kích thước BẰNG HỆT nút "Chọn" ở Tử số!
            TableLayoutPanel tlpFraction = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                Height = 68,
                ColumnCount = 4,
                RowCount = 3,
                Margin = new Padding(0)
            };
            tlpFraction.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));
            tlpFraction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpFraction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tlpFraction.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));

            tlpFraction.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Hàng Tử số
            tlpFraction.RowStyles.Add(new RowStyle(SizeType.Absolute, 8f));  // Đường kẻ phân số
            tlpFraction.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f)); // Hàng Mẫu số

            object[] paramOptions = new object[]
            {
                "Pick / Quét CAD",
                "Tổng NSA (Bảng)",
                "Tổng GSA (Bảng)",
                "Tổng GFA (Bảng)",
                "Tổng CFA (Bảng)",
                "Tổng GBA (Bảng)"
            };

            // Hàng 0: TỬ SỐ (Ở TRÊN) - Mặc định là "Pick / Quét CAD" (Index = 0)
            _cboNumSource = new WinComboBox()
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3, 3, 3, 3)
            };
            _cboNumSource.Items.AddRange(paramOptions);
            _cboNumSource.SelectedIndex = 0; // Mặc định: Pick / Quét CAD
            _cboNumSource.SelectedIndexChanged += (s, e) => SyncParameterToEfficiencyInput(true);

            _txtNumValue = new WinTextBox()
            {
                Dock = DockStyle.Fill,
                Text = "55.0",
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(3, 3, 3, 3)
            };
            _txtNumValue.TextChanged += (s, e) => RecalculateEfficiencyPercentage();

            _btnPickNum = new WinButton()
            {
                Text = "Chọn",
                Dock = DockStyle.None,
                Size = new WinSize(66, 24),
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeHeader,
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 2, 3, 2)
            };
            _btnPickNum.FlatAppearance.BorderColor = ClrTaupeDark;
            _btnPickNum.Click += (s, e) => PickOrSelectAreaFromCadForEfficiency(true);

            // Hàng 1: Đường kẻ phân số ở giữa
            WinPanel pnlFractionLine = new WinPanel()
            {
                Dock = DockStyle.Fill,
                Height = 2,
                BackColor = ClrTaupeHeader,
                Margin = new Padding(4, 3, 4, 3)
            };

            // Hàng 2: MẪU SỐ (Ở DƯỚI) - Mặc định cũng là "Pick / Quét CAD" (Index = 0)
            _cboDenSource = new WinComboBox()
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(3, 3, 3, 3)
            };
            _cboDenSource.Items.AddRange(paramOptions);
            _cboDenSource.SelectedIndex = 0; // Mặc định: Pick / Quét CAD
            _cboDenSource.SelectedIndexChanged += (s, e) => SyncParameterToEfficiencyInput(false);

            _txtDenValue = new WinTextBox()
            {
                Dock = DockStyle.Fill,
                Text = "100.0",
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(3, 3, 3, 3)
            };
            _txtDenValue.TextChanged += (s, e) => RecalculateEfficiencyPercentage();

            _btnPickDen = new WinButton()
            {
                Text = "Chọn",
                Dock = DockStyle.None,
                Size = new WinSize(66, 24), // Kích thước bằng hệt nút Chọn của Tử số!
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeHeader,
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 2, 3, 2)
            };
            _btnPickDen.FlatAppearance.BorderColor = ClrTaupeDark;
            _btnPickDen.Click += (s, e) => PickOrSelectAreaFromCadForEfficiency(false);

            tlpFraction.Controls.Add(CreateFieldLabel("Tử số (Trên):"), 0, 0);
            tlpFraction.Controls.Add(_cboNumSource, 1, 0);
            tlpFraction.Controls.Add(_txtNumValue, 2, 0);
            tlpFraction.Controls.Add(_btnPickNum, 3, 0);

            tlpFraction.Controls.Add(pnlFractionLine, 0, 1);
            tlpFraction.SetColumnSpan(pnlFractionLine, 4);

            tlpFraction.Controls.Add(CreateFieldLabel("Mẫu số (Dưới):"), 0, 2);
            tlpFraction.Controls.Add(_cboDenSource, 1, 2);
            tlpFraction.Controls.Add(_txtDenValue, 2, 2);
            tlpFraction.Controls.Add(_btnPickDen, 3, 2);

            // Bảng bên phải: GỘP CHUNG ô hiển thị Hiệu Suất và nút Đặt Text % thành 1 nút bấm duy nhất!
            TableLayoutPanel tlpEffResult = new TableLayoutPanel()
            {
                Dock = DockStyle.Fill,
                Height = 68,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(6, 0, 2, 0),
                Margin = new Padding(0)
            };
            tlpEffResult.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tlpEffResult.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            tlpEffResult.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));

            _btnEffCombinedPlace = new WinButton()
            {
                Text = "Hiệu suất = 55.0%\n(Click đặt Text % ra CAD)",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeDark,
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 0, 2)
            };
            _btnEffCombinedPlace.FlatAppearance.BorderColor = ClrTaupeHeader;
            _btnEffCombinedPlace.Click += BtnPlaceEffText_Click;

            _chkIncludeEffInTable = new WinCheckBox()
            {
                Text = "Gắn dòng Hiệu suất (%) vào Bảng",
                Dock = DockStyle.Fill,
                Checked = true,
                AutoSize = true,
                Font = new WinFont("Segoe UI", 8f, FontStyle.Regular),
                Margin = new Padding(2, 0, 0, 0)
            };
            _chkIncludeEffInTable.CheckedChanged += (s, e) => RecalculateSumRow();

            tlpEffResult.Controls.Add(_btnEffCombinedPlace, 0, 0);
            tlpEffResult.Controls.Add(_chkIncludeEffInTable, 0, 1);

            tlpEffMain.Controls.Add(tlpFraction, 0, 0);
            tlpEffMain.Controls.Add(tlpEffResult, 1, 0);
            grpEfficiency.Controls.Add(tlpEffMain);

            tlpTop.Controls.Add(grpUnit, 0, 0);
            tlpTop.Controls.Add(grpAreaModes, 0, 1);
            tlpTop.Controls.Add(grpStyleAndPreview, 0, 2);
            tlpTop.Controls.Add(_btnPickPolyline, 0, 3);
            tlpTop.Controls.Add(grpEfficiency, 0, 4);

            pnlTop.Controls.Add(tlpTop);
            _splitMain.Panel1.Controls.Add(pnlTop);

            // =================================================================
            // NỬA DƯỚI: BẢNG THỐNG KÊ + NÚT UP / DOWN / + / - & TỔNG SUM + %
            // =================================================================
            WinPanel pnlBottom = new WinPanel()
            {
                Dock = DockStyle.Fill,
                BackColor = ClrBgMain,
                Padding = new Padding(8, 4, 8, 8)
            };

            WinGroupBox grpSchedule = CreatePastelGroupBox("5. Bảng Thống Kê Căn Hộ (Hỗ trợ Sắp xếp ▲/▼, Thêm/Xóa +/-, Tổng SUM & Hiệu Suất %)");
            grpSchedule.Dock = DockStyle.Fill;

            TableLayoutPanel tlpSchedBar = new TableLayoutPanel()
            {
                Dock = DockStyle.Top,
                Height = 36,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(2)
            };
            tlpSchedBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            tlpSchedBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            tlpSchedBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

            _btnScanCad = new WinButton()
            {
                Text = "Quét Thống Kê & Cập Nhật RE",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeHeader,
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnScanCad.Click += BtnScanCad_Click;

            _btnExportCadTable = new WinButton()
            {
                Text = "Xuất Bảng Ra CAD",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeDark,
                ForeColor = WinColor.White,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnExportCadTable.Click += BtnExportCadTable_Click;

            _btnExportCsv = new WinButton()
            {
                Text = "Xuất Excel",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeSoftBtn,
                ForeColor = ClrTextDark,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnExportCsv.Click += BtnExportCsv_Click;

            tlpSchedBar.Controls.Add(_btnScanCad, 0, 0);
            tlpSchedBar.Controls.Add(_btnExportCadTable, 1, 0);
            tlpSchedBar.Controls.Add(_btnExportCsv, 2, 0);

            TableLayoutPanel tlpRowTools = new TableLayoutPanel()
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(2)
            };
            tlpRowTools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115f));
            tlpRowTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tlpRowTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tlpRowTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tlpRowTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            _btnRowUp = CreateRowToolButton("▲ Đẩy Lên");
            _btnRowUp.Click += (s, e) => MoveSelectedScheduleRow(-1);

            _btnRowDown = CreateRowToolButton("▼ Đẩy Xuống");
            _btnRowDown.Click += (s, e) => MoveSelectedScheduleRow(1);

            _btnRowAdd = CreateRowToolButton("+ Thêm Mục");
            _btnRowAdd.Click += BtnRowAdd_Click;

            _btnRowDelete = CreateRowToolButton("- Xóa Mục");
            _btnRowDelete.Click += BtnRowDelete_Click;

            tlpRowTools.Controls.Add(CreateFieldLabel("Chỉnh bảng tính:"), 0, 0);
            tlpRowTools.Controls.Add(_btnRowUp, 1, 0);
            tlpRowTools.Controls.Add(_btnRowDown, 2, 0);
            tlpRowTools.Controls.Add(_btnRowAdd, 3, 0);
            tlpRowTools.Controls.Add(_btnRowDelete, 4, 0);

            _dgvSchedule = new DataGridView()
            {
                Dock = DockStyle.Fill,
                BackgroundColor = ClrBgCard,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                Font = new WinFont("Segoe UI", 8.5f, FontStyle.Regular)
            };
            _dgvSchedule.ColumnHeadersDefaultCellStyle.BackColor = ClrTaupeHeader;
            _dgvSchedule.ColumnHeadersDefaultCellStyle.ForeColor = WinColor.White;
            _dgvSchedule.ColumnHeadersDefaultCellStyle.Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold);
            _dgvSchedule.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _dgvSchedule.ColumnHeadersHeight = 28;

            _dgvSchedule.CellEndEdit += (s, e) =>
            {
                if (!_isUpdatingSum) RecalculateSumRow();
            };
            _dgvSchedule.CellFormatting += DgvSchedule_CellFormatting;

            grpSchedule.Controls.Add(_dgvSchedule);
            grpSchedule.Controls.Add(tlpRowTools);
            grpSchedule.Controls.Add(tlpSchedBar);

            pnlBottom.Controls.Add(grpSchedule);
            _splitMain.Panel2.Controls.Add(pnlBottom);

            this.Controls.Add(_splitMain);
            this.Controls.Add(pnlStatus);
        }

        // =========================================================================
        // LOGIC CÔNG THỨC HIỆU SUẤT (%) + LƯU HANDLE ĐỂ TỰ NHẢY SỐ KHI GÕ RE
        // =========================================================================
        private void SyncParameterToEfficiencyInput(bool isNumerator)
        {
            WinComboBox cbo = isNumerator ? _cboNumSource : _cboDenSource;
            WinTextBox txt = isNumerator ? _txtNumValue : _txtDenValue;
            if (cbo == null || txt == null) return;

            string sel = cbo.SelectedItem?.ToString() ?? "";
            foreach (string aType in _areaTypes)
            {
                if (sel.IndexOf(aType, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (_latestTotalAreasByType.TryGetValue(aType, out double totalVal) && totalVal > 0)
                    {
                        txt.Text = totalVal.ToString(GetDecimalFormatString(), CultureInfo.InvariantCulture);
                    }
                    break;
                }
            }
            RecalculateEfficiencyPercentage();
        }

        private void PickOrSelectAreaFromCadForEfficiency(bool isNumerator)
        {
            HookActiveDocumentRegenUpdater();

            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string targetRole = isNumerator ? "TỬ SỐ (Ở trên)" : "MẪU SỐ (Ở dưới)";
            string preferredAreaKey = GetPreferredAreaKeyFromCombo(isNumerator ? _cboNumSource : _cboDenSource);

            CadApp.MainWindow.Focus();
            PromptSelectionOptions pso = new PromptSelectionOptions()
            {
                MessageForAdding = $"\n[DBAREA] Chọn hoặc Quét đường bao Polyline / Tag MText cho {targetRole}: "
            };

            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK || psr.Value == null) return;

            double sumArea = 0.0;
            int countPicked = 0;
            List<string> pickedHandles = new List<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is MText mt)
                    {
                        UnitAreaRecord rec = TryReadApartmentRecordFromMText(mt);
                        if (rec != null && rec.Areas.Count > 0)
                        {
                            if (!string.IsNullOrEmpty(preferredAreaKey) && rec.Areas.ContainsKey(preferredAreaKey))
                                sumArea += rec.Areas[preferredAreaKey];
                            else
                                sumArea += rec.Areas.Values.First();

                            pickedHandles.Add(ent.Handle.ToString());
                            countPicked++;
                        }
                        else
                        {
                            double singleVal = TryExtractAnyNumberFromText(mt.Text);
                            if (singleVal > 0)
                            {
                                sumArea += singleVal;
                                pickedHandles.Add(ent.Handle.ToString());
                                countPicked++;
                            }
                        }
                    }
                    else if (ent is DBText dbt)
                    {
                        double singleVal = TryExtractAnyNumberFromText(dbt.TextString);
                        if (singleVal > 0)
                        {
                            sumArea += singleVal;
                            pickedHandles.Add(ent.Handle.ToString());
                            countPicked++;
                        }
                    }
                    else if (ent is Curve)
                    {
                        double polyArea = ExtractEntityAreaInM2(ent);
                        if (polyArea > 0)
                        {
                            sumArea += polyArea;
                            pickedHandles.Add(ent.Handle.ToString());
                            countPicked++;
                        }
                    }
                }
                tr.Commit();
            }

            if (countPicked > 0)
            {
                string fmt = GetDecimalFormatString();
                if (isNumerator)
                {
                    _numPickedHandles = pickedHandles;
                    _cboNumSource.SelectedIndex = 0;
                    _txtNumValue.Text = sumArea.ToString(fmt, CultureInfo.InvariantCulture);
                }
                else
                {
                    _denPickedHandles = pickedHandles;
                    _cboDenSource.SelectedIndex = 0;
                    _txtDenValue.Text = sumArea.ToString(fmt, CultureInfo.InvariantCulture);
                }

                RecalculateEfficiencyPercentage();
                RecalculateSumRow();
                _lblStatus.Text = $"Đã chọn {countPicked} đối tượng cho {targetRole} ({sumArea.ToString(fmt, CultureInfo.InvariantCulture)} m²). Co kéo Polyline + gõ RE sẽ tự nhảy %!";
            }
        }

        private string GetPreferredAreaKeyFromCombo(WinComboBox cbo)
        {
            string sel = cbo?.SelectedItem?.ToString() ?? "";
            foreach (string aType in _areaTypes)
            {
                if (sel.IndexOf(aType, StringComparison.OrdinalIgnoreCase) >= 0)
                    return aType;
            }
            return "";
        }

        private double TryExtractAnyNumberFromText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0.0;
            string clean = CleanTagText(raw).Replace("m2", "").Replace("M2", "").Replace("m\u00B2", "").Replace("M\u00B2", "");
            MatchCollection matches = Regex.Matches(clean, @"[-+]?\d+(\.\d+)?");
            if (matches.Count > 0)
            {
                string lastNum = matches[matches.Count - 1].Value;
                if (double.TryParse(lastNum, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    return val;
            }
            return 0.0;
        }

        private double CalculateEfficiencyPercentValue()
        {
            double num = ParsePositiveDouble(_txtNumValue?.Text, 0.0);
            double den = ParsePositiveDouble(_txtDenValue?.Text, 0.0);
            if (den <= 1e-6) return 0.0;
            return (num / den) * 100.0;
        }

        private string FormatEfficiencyPercentString(double pct)
        {
            string fmt = GetDecimalFormatString();
            return pct.ToString(fmt, CultureInfo.InvariantCulture) + "%";
        }

        private void RecalculateEfficiencyPercentage()
        {
            if (_btnEffCombinedPlace == null) return;
            double pct = CalculateEfficiencyPercentValue();
            string pctStr = FormatEfficiencyPercentString(pct);
            _btnEffCombinedPlace.Text = $"Hiệu suất = {pctStr}\n(Click đặt Text % ra CAD)";
        }

        // KHI CLICK VÀO Ô HIỆU SUẤT -> ĐẶT TEXT % RA CAD (KHÔNG GHI TỬ SỐ/MẪU SỐ & TỰ NHẢY KHI GÕ RE)
        private void BtnPlaceEffText_Click(object sender, EventArgs e)
        {
            HookActiveDocumentRegenUpdater();

            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            double pct = CalculateEfficiencyPercentValue();
            string pctStr = FormatEfficiencyPercentString(pct);
            double hSub = ParsePositiveDouble(_txtSubHeight.Text, 500.0);
            string targetLayer = string.IsNullOrWhiteSpace(_txtLayer.Text) ? "DB-A-Text" : _txtLayer.Text.Trim();

            CadApp.MainWindow.Focus();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureCompanyLayerAndTextStyles(db, tr, targetLayer, out ObjectId titleStyleId, out ObjectId regStyleId);

                Point3d initPt = (Point3d)CadApp.GetSystemVariable("VIEWCTR");
                MText mt = new MText()
                {
                    Layer = targetLayer,
                    TextStyleId = regStyleId,
                    TextHeight = hSub,
                    Attachment = AttachmentPoint.BottomLeft,
                    Location = initPt,
                    ColorIndex = 256,
                    // Không ghi (Tử số/Mẫu số) ngoài Model theo đúng yêu cầu số 1
                    Contents = $"Hiệu suất: {pctStr}"
                };

                MTextPlacementJig jig = new MTextPlacementJig(mt, initPt);
                PromptResult res = ed.Drag(jig);
                if (res.Status == PromptStatus.OK)
                {
                    mt.Location = jig.PickedPoint;
                    BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    space.AppendEntity(mt);
                    tr.AddNewlyCreatedDBObject(mt, true);

                    // Lưu danh sách Handle của Tử số & Mẫu số vào XData để khi co kéo Polyline và gõ RE, số % tự nhảy theo!
                    AttachEfficiencyXData(
                        db,
                        tr,
                        mt,
                        GetPreferredAreaKeyFromCombo(_cboNumSource),
                        _numPickedHandles,
                        GetPreferredAreaKeyFromCombo(_cboDenSource),
                        _denPickedHandles);

                    tr.Commit();
                    ed.Regen();
                    _lblStatus.Text = $"Đã đặt 'Hiệu suất: {pctStr}' ra Model (Tự động nhảy % khi co kéo đường bao và gõ RE)!";
                }
                else
                {
                    mt.Dispose();
                    tr.Abort();
                }
            }
        }

        private void AttachEfficiencyXData(
            Database db,
            Transaction tr,
            Entity ent,
            string numKey,
            List<string> numHandles,
            string denKey,
            List<string> denHandles)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(RegAppEffName))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord() { Name = RegAppEffName };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppEffName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, numKey ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, numHandles != null ? string.Join(";", numHandles) : ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, denKey ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, denHandles != null ? string.Join(";", denHandles) : "")
            );

            ent.XData = rb;
        }

        private WinButton CreateRowToolButton(string text)
        {
            WinButton btn = new WinButton()
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = ClrTaupeSoftBtn,
                ForeColor = ClrTextDark,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ClrTaupeAccent;
            return btn;
        }

        private WinGroupBox CreatePastelGroupBox(string title)
        {
            return new WinGroupBox()
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                BackColor = ClrBgCard,
                ForeColor = ClrTaupeDark,
                Font = new WinFont("Segoe UI", 8.75f, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 6),
                Margin = new Padding(0, 0, 0, 6)
            };
        }

        private WinLabel CreateFieldLabel(string text)
        {
            return new WinLabel()
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new WinFont("Segoe UI", 8.25f, FontStyle.Regular),
                ForeColor = ClrTextDark
            };
        }

        private void BtnAddUnitType_Click(object sender, EventArgs e)
        {
            string newType = PromptSimpleInputDialog("Thêm Loại Căn Hộ Mới", "Nhập tên loại căn hộ mới (VD: 4BR, SKY VILLA, OFFICETEL):", "");
            if (string.IsNullOrWhiteSpace(newType)) return;
            newType = newType.Trim().ToUpperInvariant();

            if (!_unitTypes.Contains(newType, StringComparer.OrdinalIgnoreCase))
            {
                _unitTypes.Add(newType);
                SaveCustomUnitTypes();
                _cboUnitType.Items.Add(newType);
            }
            _cboUnitType.SelectedItem = newType;
            _lblStatus.Text = $"Đã thêm loại căn hộ '{newType}' vào danh sách.";
        }

        private void BtnRemoveUnitType_Click(object sender, EventArgs e)
        {
            if (_cboUnitType.SelectedItem == null || _cboUnitType.Items.Count <= 1) return;
            string cur = _cboUnitType.SelectedItem.ToString();

            if (MessageBox.Show($"Bạn có muốn xóa loại căn '{cur}' khỏi danh sách không?",
                "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _unitTypes.RemoveAll(x => x.Equals(cur, StringComparison.OrdinalIgnoreCase));
                SaveCustomUnitTypes();
                _cboUnitType.Items.Remove(cur);
                if (_cboUnitType.Items.Count > 0) _cboUnitType.SelectedIndex = 0;
                _lblStatus.Text = $"Đã xóa loại căn '{cur}'.";
            }
        }

        private string PromptSimpleInputDialog(string title, string promptText, string defaultVal)
        {
            using (Form frm = new Form())
            {
                frm.Text = title;
                frm.Size = new WinSize(380, 155);
                frm.FormBorderStyle = FormBorderStyle.FixedDialog;
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.MaximizeBox = false;
                frm.MinimizeBox = false;
                frm.BackColor = ClrBgMain;
                frm.Font = new WinFont("Segoe UI", 9f);

                WinLabel lbl = new WinLabel() { Left = 14, Top = 14, Width = 340, Text = promptText };
                WinTextBox txt = new WinTextBox() { Left = 14, Top = 40, Width = 335, Text = defaultVal };
                WinButton btnOk = new WinButton()
                {
                    Text = "Thêm",
                    Left = 185,
                    Top = 75,
                    Width = 80,
                    Height = 28,
                    DialogResult = DialogResult.OK,
                    BackColor = ClrTaupeDark,
                    ForeColor = WinColor.White,
                    FlatStyle = FlatStyle.Flat
                };
                WinButton btnCancel = new WinButton()
                {
                    Text = "Hủy",
                    Left = 270,
                    Top = 75,
                    Width = 80,
                    Height = 28,
                    DialogResult = DialogResult.Cancel,
                    BackColor = ClrTaupeSoftBtn,
                    FlatStyle = FlatStyle.Flat
                };

                frm.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
                frm.AcceptButton = btnOk;
                frm.CancelButton = btnCancel;

                return frm.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }

        private void ResetSuffixToFirst()
        {
            if (_cboSuffixMode == null || _txtSuffixVal == null) return;
            if (_cboSuffixMode.SelectedIndex == 0) _txtSuffixVal.Text = "A";
            else if (_cboSuffixMode.SelectedIndex == 1) _txtSuffixVal.Text = "01";
            else _txtSuffixVal.Text = "";
        }

        private void UpdateFullUnitCode()
        {
            if (_cboUnitType == null || _txtSuffixVal == null || _txtFullCodePreview == null) return;
            string uType = _cboUnitType.SelectedItem?.ToString() ?? "2BR";
            string suf = _txtSuffixVal.Text.Trim();

            if (_cboSuffixMode.SelectedIndex == 2 || string.IsNullOrEmpty(suf))
                _txtFullCodePreview.Text = uType;
            else
                _txtFullCodePreview.Text = $"{uType}-{suf}";

            _picTagPreview?.Invalidate();
        }

        private void AdvanceNextUnitSuffix()
        {
            if (!_chkAutoIncrement.Checked) return;

            if (_cboSuffixMode.SelectedIndex == 0)
            {
                string cur = _txtSuffixVal.Text.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(cur)) cur = "A";
                char lastChar = cur[cur.Length - 1];
                if (lastChar >= 'A' && lastChar < 'Z')
                {
                    _txtSuffixVal.Text = cur.Substring(0, cur.Length - 1) + (char)(lastChar + 1);
                }
                else if (lastChar == 'Z')
                {
                    _txtSuffixVal.Text = cur + "A";
                }
            }
            else if (_cboSuffixMode.SelectedIndex == 1)
            {
                string cur = _txtSuffixVal.Text.Trim();
                int num;
                if (int.TryParse(cur, out num))
                {
                    int padLen = Math.Max(2, cur.Length);
                    _txtSuffixVal.Text = (num + 1).ToString().PadLeft(padLen, '0');
                }
                else
                {
                    _txtSuffixVal.Text = "02";
                }
            }
        }

        private List<string> GetCheckedAreaTypes()
        {
            List<string> list = new List<string>();
            foreach (string key in _areaTypes)
            {
                if (_chkAreas.ContainsKey(key) && _chkAreas[key].Checked)
                    list.Add(key);
            }
            if (list.Count == 0) list.Add("NSA");
            return list;
        }

        private void PicTagPreview_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(WinColor.FromArgb(24, 26, 30));

            string unitCode = _txtFullCodePreview.Text.Trim();
            if (string.IsNullOrEmpty(unitCode)) unitCode = "2BR-A";

            List<string> activeAreas = GetCheckedAreaTypes();
            string fmt = GetDecimalFormatString();

            WinColor cadBlue = WinColor.FromArgb(110, 155, 245);
            using (WinFont fTitle = new WinFont("Segoe UI", 13.5f, FontStyle.Bold))
            using (WinFont fSub = new WinFont("Segoe UI", 9.5f, FontStyle.Regular))
            using (Brush brText = new SolidBrush(cadBlue))
            {
                float lineHTitle = fTitle.GetHeight(g);
                float lineHSub = fSub.GetHeight(g);
                float totalH = lineHTitle + activeAreas.Count * lineHSub;

                float originX = 18f;
                float originY = Math.Max(totalH + 10f, _picTagPreview.Height - 14f);
                float startTopY = originY - totalH;

                g.DrawString(unitCode, fTitle, brText, new PointF(originX, startTopY));

                float curY = startTopY + lineHTitle;
                double sampleVal = 70.0;
                foreach (string aType in activeAreas)
                {
                    string lineStr = $"{aType}: {sampleVal.ToString(fmt, CultureInfo.InvariantCulture)} m\u00B2";
                    g.DrawString(lineStr, fSub, brText, new PointF(originX, curY));
                    curY += lineHSub;
                    sampleVal += 5.5;
                }

                using (Pen pCross = new Pen(WinColor.FromArgb(0, 180, 255), 1.5f))
                {
                    g.FillRectangle(Brushes.DodgerBlue, originX - 4, originY - 4, 8, 8);
                    g.DrawLine(pCross, originX - 9, originY, originX + 9, originY);
                    g.DrawLine(pCross, originX, originY - 9, originX, originY + 9);
                }
            }
        }

        private string GetDecimalFormatString()
        {
            int idx = _cboDecimals != null ? _cboDecimals.SelectedIndex : 1;
            if (idx == 0) return "0";
            if (idx == 2) return "0.00";
            return "0.0";
        }

        // =========================================================================
        // ĐO POLYLINE & TẠO 2 MTEXT ĐỘC LẬP (TÊN CĂN + DIỆN TÍCH) CHỈ VỚI 1 LẦN PICK
        // =========================================================================
        private void BtnPickPolyline_Click(object sender, EventArgs e)
        {
            HookActiveDocumentRegenUpdater();

            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            List<string> selectedAreaModes = GetCheckedAreaTypes();
            string unitType = _cboUnitType.SelectedItem?.ToString() ?? "2BR";
            string unitCode = _txtFullCodePreview.Text.Trim();
            if (string.IsNullOrEmpty(unitCode)) unitCode = unitType;

            double hTitle = ParsePositiveDouble(_txtTitleHeight.Text, 750.0);
            double hSub = ParsePositiveDouble(_txtSubHeight.Text, 500.0);
            string targetLayer = string.IsNullOrWhiteSpace(_txtLayer.Text) ? "DB-A-Text" : _txtLayer.Text.Trim();

            CadApp.MainWindow.Focus();

            while (true)
            {
                unitCode = _txtFullCodePreview.Text.Trim();
                Dictionary<string, double> measuredAreas = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> curveHandles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Point3d initialHintPt = Point3d.Origin;
                bool cancelled = false;

                for (int i = 0; i < selectedAreaModes.Count; i++)
                {
                    string mode = selectedAreaModes[i];
                    PromptEntityOptions peo = new PromptEntityOptions(
                        $"\n[DBAREA] Chọn Polyline đo [{mode}] cho căn '{unitCode}' (Nhấn Enter/ESC để dừng): ");
                    peo.SetRejectMessage("\nĐối tượng chọn phải là đường Polyline hoặc Circle/Region!");
                    peo.AddAllowedClass(typeof(Curve), false);

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                    {
                        cancelled = true;
                        break;
                    }

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                        double areaM2 = ExtractEntityAreaInM2(ent);
                        measuredAreas[mode] = areaM2;
                        if (ent != null)
                        {
                            curveHandles[mode] = ent.Handle.ToString();
                        }

                        if (i == 0 && ent != null)
                        {
                            try
                            {
                                Extents3d ext = ent.GeometricExtents;
                                initialHintPt = new Point3d(
                                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                                    0.0);
                            }
                            catch { initialHintPt = per.PickedPoint; }
                        }
                        tr.Commit();
                    }
                }

                if (cancelled || measuredAreas.Count == 0)
                    break;

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureCompanyLayerAndTextStyles(db, tr, targetLayer, out ObjectId titleStyleId, out ObjectId regStyleId);

                    // Khoảng cách trục Y từ đáy MText Diện tích lên đáy MText Tên căn hộ
                    int lineCount = Math.Max(1, measuredAreas.Count);
                    double offsetY = lineCount * (hSub * 1.35) + (hSub * 0.20);
                    Vector3d titleOffset = new Vector3d(0, offsetY, 0);

                    // MText 1 (Ở trên): Chứa Tên Căn Hộ (TextStyle = DB-Segoe UI - 300 - B, Cao = 750)
                    // Không dùng mã \f nội bộ để người dùng dùng lệnh MA (Matchprop) cực chuẩn!
                    MText mtTitle = new MText()
                    {
                        Layer = targetLayer,
                        TextStyleId = titleStyleId,
                        TextHeight = hTitle,
                        Attachment = AttachmentPoint.BottomLeft,
                        Location = initialHintPt + titleOffset,
                        ColorIndex = 256,
                        Contents = CleanTagText(unitCode)
                    };

                    // MText 2 (Ở dưới): Chứa Diện tích (TextStyle = DB-Segoe UI, Cao = 500, Điểm đặt Góc dưới-trái)
                    MText mtArea = new MText()
                    {
                        Layer = targetLayer,
                        TextStyleId = regStyleId,
                        TextHeight = hSub,
                        Attachment = AttachmentPoint.BottomLeft,
                        Location = initialHintPt,
                        ColorIndex = 256,
                        Contents = BuildAreaOnlyMTextContents(measuredAreas)
                    };

                    // Chạy DualMTextPlacementJig: Rê chuột hiển thị cả 2 MText cùng lúc và chỉ cần Click 1 điểm!
                    DualMTextPlacementJig dualJig = new DualMTextPlacementJig(mtTitle, mtArea, initialHintPt, titleOffset);
                    PromptResult jigRes = ed.Drag(dualJig);
                    if (jigRes.Status != PromptStatus.OK)
                    {
                        mtTitle.Dispose();
                        mtArea.Dispose();
                        tr.Abort();
                        break;
                    }

                    Point3d basePt = dualJig.PickedPoint;
                    mtArea.Location = basePt;
                    mtTitle.Location = basePt + titleOffset;

                    BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    space.AppendEntity(mtTitle);
                    tr.AddNewlyCreatedDBObject(mtTitle, true);

                    space.AppendEntity(mtArea);
                    tr.AddNewlyCreatedDBObject(mtArea, true);

                    // Gắn XData vào MText Diện tích (kèm Handle của MText Tên căn và Handle của Polyline)
                    AttachApartmentXData(
                        db,
                        tr,
                        mtArea,
                        unitType,
                        unitCode,
                        measuredAreas,
                        curveHandles,
                        mtTitle.Handle.ToString());

                    tr.Commit();
                }

                ed.Regen();
                _lblStatus.Text = $"Đã đặt 2 MText độc lập cho '{unitCode}' (Dễ dàng MA/sửa riêng từng chữ & gõ RE tự nhảy số)!";

                AdvanceNextUnitSuffix();
                UpdateFullUnitCode();
            }

            ScanAndPopulateSchedule(false);
        }

        private double ExtractEntityAreaInM2(Entity ent)
        {
            if (ent == null) return 0.0;
            double rawArea = 0.0;
            if (ent is Curve crv)
            {
                try { rawArea = crv.Area; } catch { rawArea = 0.0; }
            }

            if (rawArea > 10000.0)
                return rawArea / 1000000.0;
            return rawArea;
        }

        // Tạo nội dung thuần cho MText Diện tích (Không chèn mã \f đè bên trong để lệnh MA hoạt động 100%)
        private string BuildAreaOnlyMTextContents(Dictionary<string, double> areas)
        {
            string fmt = GetDecimalFormatString();
            List<string> lines = new List<string>();
            foreach (string key in _areaTypes)
            {
                if (areas.ContainsKey(key))
                {
                    string valStr = areas[key].ToString(fmt, CultureInfo.InvariantCulture);
                    lines.Add($"{key}: {valStr} m\u00B2");
                }
            }
            return string.Join("\\P", lines);
        }

        private string BuildCleanMTextContents(
            string unitCode,
            Dictionary<string, double> areas,
            double hTitle,
            double hSub)
        {
            string fmt = GetDecimalFormatString();
            double ratio = (hTitle > 0.0) ? (hSub / hTitle) : (500.0 / 750.0);
            string ratioStr = ratio.ToString("0.####", CultureInfo.InvariantCulture);

            StringBuilder sb = new StringBuilder();
            sb.Append(@"{\fSegoe UI|b1|i0|c0|p34;");
            sb.Append(CleanTagText(unitCode));
            sb.Append(@"}");

            foreach (string key in _areaTypes)
            {
                if (areas.ContainsKey(key))
                {
                    string valStr = areas[key].ToString(fmt, CultureInfo.InvariantCulture);
                    sb.Append(@"\P{\fSegoe UI|b0|i0|c0|p34;\H");
                    sb.Append(ratioStr);
                    sb.Append(@"x;");
                    sb.Append($"{key}: {valStr} m\u00B2");
                    sb.Append(@"}");
                }
            }

            return sb.ToString();
        }

        private void EnsureCompanyLayerAndTextStyles(
            Database db,
            Transaction tr,
            string layerName,
            out ObjectId titleStyleId,
            out ObjectId regStyleId)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord()
                {
                    Name = layerName,
                    Color = CadColor.FromColorIndex(ColorMethod.ByAci, 5)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);

            if (!tst.Has("DB-Segoe UI"))
            {
                tst.UpgradeOpen();
                TextStyleTableRecord tsrReg = new TextStyleTableRecord()
                {
                    Name = "DB-Segoe UI",
#if ZWCAD
                    Font = new ZwSoft.ZwCAD.GraphicsInterface.FontDescriptor("Segoe UI", false, false, 0, 34)
#else
                    Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Segoe UI", false, false, 0, 34)
#endif
                };
                regStyleId = tst.Add(tsrReg);
                tr.AddNewlyCreatedDBObject(tsrReg, true);
            }
            else
            {
                regStyleId = tst["DB-Segoe UI"];
            }

            if (!tst.Has("DB-Segoe UI - 300 - B"))
            {
                if (!tst.IsWriteEnabled) tst.UpgradeOpen();
                TextStyleTableRecord tsrBold = new TextStyleTableRecord()
                {
                    Name = "DB-Segoe UI - 300 - B",
#if ZWCAD
                    Font = new ZwSoft.ZwCAD.GraphicsInterface.FontDescriptor("Segoe UI", true, false, 0, 34)
#else
                    Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Segoe UI", true, false, 0, 34)
#endif
                };
                titleStyleId = tst.Add(tsrBold);
                tr.AddNewlyCreatedDBObject(tsrBold, true);
            }
            else
            {
                titleStyleId = tst["DB-Segoe UI - 300 - B"];
            }
        }

        private void AttachApartmentXData(
            Database db,
            Transaction tr,
            Entity ent,
            string unitType,
            string unitCode,
            Dictionary<string, double> areas,
            Dictionary<string, string> curveHandles,
            string linkedTitleHandle = "")
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(RegAppName))
            {
                rat.UpgradeOpen();
                RegAppTableRecord ratr = new RegAppTableRecord() { Name = RegAppName };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            List<string> pairs = new List<string>();
            foreach (var kv in areas)
            {
                pairs.Add($"{kv.Key}={kv.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            }

            List<string> handlePairs = new List<string>();
            if (curveHandles != null)
            {
                foreach (var kv in curveHandles)
                {
                    handlePairs.Add($"{kv.Key}={kv.Value}");
                }
            }

            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, CleanTagText(unitType)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, CleanTagText(unitCode)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, string.Join(";", pairs)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, string.Join(";", handlePairs)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, linkedTitleHandle ?? "")
            );

            ent.XData = rb;
        }

        private string CleanTagText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string s = Regex.Replace(input, @"\\f[^;]+;", "");
            s = Regex.Replace(s, @"\\H[^;]+;", "");
            s = s.Replace("{", "").Replace("}", "").Trim();
            return s;
        }

        private UnitAreaRecord TryReadApartmentRecordFromMText(MText mt)
        {
            if (mt == null) return null;

            ResultBuffer rb = mt.GetXDataForApplication(RegAppName);
            if (rb != null)
            {
                TypedValue[] vals = rb.AsArray();
                if (vals != null && vals.Length >= 4)
                {
                    UnitAreaRecord rec = new UnitAreaRecord()
                    {
                        UnitType = CleanTagText(vals[1].Value?.ToString() ?? ""),
                        UnitCode = CleanTagText(vals[2].Value?.ToString() ?? "")
                    };

                    string rawPairs = vals[3].Value?.ToString() ?? "";
                    foreach (string part in rawPairs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] kv = part.Split('=');
                        if (kv.Length == 2)
                        {
                            double d;
                            if (double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                                rec.Areas[kv[0].Trim()] = d;
                        }
                    }

                    // Nếu người dùng sửa trực tiếp MText Tên căn nằm ngay phía trên hoặc sửa con số trong MText Diện tích
                    SyncEditedMTextLinesToRecord(mt.Text, rec);
                    return rec;
                }
            }

            return ParsePlainMTextToRecord(mt.Text);
        }

        private void SyncEditedMTextLinesToRecord(string plainText, UnitAreaRecord rec)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return;
            string[] lines = plainText.Split(new[] { "\r\n", "\n", "\r", "\\P" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string clean = CleanTagText(lines[i]);
                if (string.IsNullOrEmpty(clean)) continue;

                if (clean.Contains(":"))
                {
                    ParseAreaLine(clean, rec.Areas);
                }
                else if (i == 0)
                {
                    rec.UnitCode = clean;
                    rec.UnitType = InferUnitTypeFromCode(rec.UnitCode, rec.UnitType);
                }
            }
        }

        private UnitAreaRecord ParsePlainMTextToRecord(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return null;
            string[] lines = plainText.Split(new[] { "\r\n", "\n", "\r", "\\P" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return null;

            UnitAreaRecord rec = new UnitAreaRecord();
            rec.UnitCode = CleanTagText(lines[0]);
            rec.UnitType = InferUnitTypeFromCode(rec.UnitCode, "OTHER");

            for (int i = 1; i < lines.Length; i++)
            {
                ParseAreaLine(lines[i], rec.Areas);
            }

            return rec.Areas.Count > 0 ? rec : null;
        }

        private void ParseAreaLine(string line, Dictionary<string, double> dict)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string clean = CleanTagText(line);
            int colonIdx = clean.IndexOf(':');
            if (colonIdx <= 0) return;

            string key = clean.Substring(0, colonIdx).Trim().ToUpperInvariant();
            if (!_areaTypes.Contains(key)) return;

            string numPart = clean.Substring(colonIdx + 1).ToUpperInvariant()
                                  .Replace("M2", "").Replace("M\u00B2", "").Trim();
            double val;
            if (double.TryParse(numPart, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
            {
                dict[key] = val;
            }
        }

        private string InferUnitTypeFromCode(string code, string fallback)
        {
            string u = CleanTagText(code).ToUpperInvariant();
            foreach (string t in _unitTypes)
            {
                if (u.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            int dash = u.IndexOf('-');
            if (dash > 0) return u.Substring(0, dash).Trim();
            return CleanTagText(fallback);
        }

        // =========================================================================
        // BẢNG THỐNG KÊ: SẮP XẾP LÊN/XUỐNG (▲/▼), THÊM/XÓA (+/-), TỔNG SUM & DÒNG %
        // =========================================================================
        private void RefreshScheduleColumns()
        {
            List<string> activeAreas = GetCheckedAreaTypes();
            _dtSchedule = new WinDataTable("ApartmentSchedule");

            _dtSchedule.Columns.Add("STT", typeof(string));
            _dtSchedule.Columns.Add("LOẠI CĂN", typeof(string));
            _dtSchedule.Columns.Add("MÃ CĂN", typeof(string));
            _dtSchedule.Columns.Add("SỐ LƯỢNG", typeof(string));

            foreach (string aType in activeAreas)
            {
                _dtSchedule.Columns.Add($"{aType} 1 CĂN (m\u00B2)", typeof(string));
                _dtSchedule.Columns.Add($"TỔNG {aType} (m\u00B2)", typeof(string));
            }

            _dgvSchedule.DataSource = _dtSchedule;

            for (int c = 4; c < _dgvSchedule.Columns.Count; c++)
            {
                _dgvSchedule.Columns[c].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            if (_dgvSchedule.Columns.Count >= 4)
            {
                _dgvSchedule.Columns[0].FillWeight = 10f;
                _dgvSchedule.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                _dgvSchedule.Columns[1].FillWeight = 16f;
                _dgvSchedule.Columns[2].FillWeight = 20f;
                _dgvSchedule.Columns[3].FillWeight = 13f;
                _dgvSchedule.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        private bool IsFooterSummaryRow(DataRow dr)
        {
            if (dr == null) return false;
            string sttVal = Convert.ToString(dr[0] ?? "");
            return sttVal.Equals("TỔNG", StringComparison.OrdinalIgnoreCase) ||
                   sttVal.Equals("HIỆU SUẤT", StringComparison.OrdinalIgnoreCase) ||
                   sttVal.Equals("SUM", StringComparison.OrdinalIgnoreCase);
        }

        private int GetDataRowCountExcludingFooter()
        {
            if (_dtSchedule == null) return 0;
            int count = 0;
            foreach (DataRow dr in _dtSchedule.Rows)
            {
                if (!IsFooterSummaryRow(dr)) count++;
            }
            return count;
        }

        private void MoveSelectedScheduleRow(int direction)
        {
            if (_dtSchedule == null || _dgvSchedule.CurrentRow == null) return;
            int idx = _dgvSchedule.CurrentRow.Index;
            int dataRowCount = GetDataRowCountExcludingFooter();
            if (idx < 0 || idx >= dataRowCount) return;

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= dataRowCount) return;

            _isUpdatingSum = true;
            try
            {
                DataRow sourceRow = _dtSchedule.Rows[idx];
                object[] values = sourceRow.ItemArray;
                _dtSchedule.Rows.RemoveAt(idx);

                DataRow cloneRow = _dtSchedule.NewRow();
                cloneRow.ItemArray = values;
                _dtSchedule.Rows.InsertAt(cloneRow, newIdx);
            }
            finally
            {
                _isUpdatingSum = false;
            }

            RecalculateSumRow();
            _dgvSchedule.ClearSelection();
            if (newIdx < _dgvSchedule.Rows.Count)
            {
                _dgvSchedule.Rows[newIdx].Selected = true;
                _dgvSchedule.CurrentCell = _dgvSchedule.Rows[newIdx].Cells[0];
            }
        }

        private void BtnRowAdd_Click(object sender, EventArgs e)
        {
            if (_dtSchedule == null) RefreshScheduleColumns();

            List<string> activeAreas = GetCheckedAreaTypes();
            string uType = _cboUnitType.SelectedItem?.ToString() ?? "2BR";
            string uCode = _txtFullCodePreview.Text.Trim();
            string fmt = GetDecimalFormatString();

            _isUpdatingSum = true;
            try
            {
                RemoveExistingSumRows();

                DataRow newRow = _dtSchedule.NewRow();
                newRow[0] = (_dtSchedule.Rows.Count + 1).ToString("00");
                newRow[1] = uType;
                newRow[2] = uCode;
                newRow[3] = "1";

                int cIdx = 4;
                for (int a = 0; a < activeAreas.Count; a++)
                {
                    newRow[cIdx++] = (0.0).ToString(fmt, CultureInfo.InvariantCulture);
                    newRow[cIdx++] = (0.0).ToString(fmt, CultureInfo.InvariantCulture);
                }

                _dtSchedule.Rows.Add(newRow);
            }
            finally
            {
                _isUpdatingSum = false;
            }

            RecalculateSumRow();
            _lblStatus.Text = "Đã thêm 1 dòng mới vào bảng tính. Bạn có thể click đúp vào ô để sửa số liệu.";
        }

        private void BtnRowDelete_Click(object sender, EventArgs e)
        {
            if (_dtSchedule == null || _dgvSchedule.CurrentRow == null) return;
            int idx = _dgvSchedule.CurrentRow.Index;
            if (idx < 0 || idx >= _dtSchedule.Rows.Count) return;

            if (IsFooterSummaryRow(_dtSchedule.Rows[idx])) return;

            _dtSchedule.Rows.RemoveAt(idx);
            RecalculateSumRow();
            _lblStatus.Text = "Đã xóa dòng được chọn và cập nhật lại tổng SUM & Hiệu suất %.";
        }

        private void BtnScanCad_Click(object sender, EventArgs e)
        {
            UpdateAllLinkedAreaAndEfficiencyTagsInCurrentDocument();
            ScanAndPopulateSchedule(true);
        }

        private void ScanAndPopulateSchedule(bool promptUserSelection)
        {
            UpdateAllLinkedAreaAndEfficiencyTagsInCurrentDocument();

            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptSelectionResult psr;
            TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "MTEXT") };

            if (promptUserSelection)
            {
                CadApp.MainWindow.Focus();
                PromptSelectionOptions pso = new PromptSelectionOptions()
                {
                    MessageForAdding = "\n[DBAREA] Quét chọn các Tag MText căn hộ để thống kê (Nhấn Enter để quét toàn bản vẽ): "
                };
                psr = ed.GetSelection(pso, new SelectionFilter(filter));
                if (psr.Status != PromptStatus.OK)
                {
                    psr = ed.SelectAll(new SelectionFilter(filter));
                }
            }
            else
            {
                psr = ed.SelectAll(new SelectionFilter(filter));
            }

            if (psr.Status != PromptStatus.OK || psr.Value == null) return;

            List<UnitAreaRecord> rawRecords = new List<UnitAreaRecord>();
            HashSet<string> detectedAreaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    MText mt = tr.GetObject(so.ObjectId, OpenMode.ForRead) as MText;
                    UnitAreaRecord rec = TryReadApartmentRecordFromMText(mt);
                    if (rec != null && !string.IsNullOrEmpty(rec.UnitCode) && rec.Areas.Count > 0)
                    {
                        rawRecords.Add(rec);
                        foreach (string k in rec.Areas.Keys)
                            detectedAreaTypes.Add(k);
                    }
                }
                tr.Commit();
            }

            bool checkChanged = false;
            foreach (string k in detectedAreaTypes)
            {
                if (_chkAreas.ContainsKey(k) && !_chkAreas[k].Checked)
                {
                    _chkAreas[k].Checked = true;
                    checkChanged = true;
                }
            }
            if (checkChanged) RefreshScheduleColumns();

            List<string> activeAreas = GetCheckedAreaTypes();
            RefreshScheduleColumns();
            string fmt = GetDecimalFormatString();

            var grouped = rawRecords
                .GroupBy(r => CleanTagText(r.UnitCode).ToUpperInvariant())
                .OrderBy(g => g.First().UnitType)
                .ThenBy(g => g.Key)
                .ToList();

            int stt = 1;
            foreach (var grp in grouped)
            {
                UnitAreaRecord first = grp.First();
                int qty = grp.Count();

                DataRow row = _dtSchedule.NewRow();
                row[0] = stt.ToString("00");
                row[1] = CleanTagText(first.UnitType);
                row[2] = CleanTagText(first.UnitCode);
                row[3] = qty.ToString();

                int colIdx = 4;
                foreach (string aType in activeAreas)
                {
                    double unitArea = first.GetArea(aType);
                    double totalArea = unitArea * qty;
                    row[colIdx++] = unitArea.ToString(fmt, CultureInfo.InvariantCulture);
                    row[colIdx++] = totalArea.ToString(fmt, CultureInfo.InvariantCulture);
                }

                _dtSchedule.Rows.Add(row);
                stt++;
            }

            RecalculateSumRow();
            _lblStatus.Text = $"Đã cập nhật & thống kê {rawRecords.Count} căn hộ ({grouped.Count} mã căn) vào bảng.";
        }

        private void RemoveExistingSumRows()
        {
            if (_dtSchedule == null) return;
            for (int i = _dtSchedule.Rows.Count - 1; i >= 0; i--)
            {
                if (IsFooterSummaryRow(_dtSchedule.Rows[i]))
                {
                    _dtSchedule.Rows.RemoveAt(i);
                }
            }
        }

        private void RecalculateSumRow()
        {
            if (_dtSchedule == null || _isUpdatingSum) return;
            _isUpdatingSum = true;

            try
            {
                RemoveExistingSumRows();
                if (_dtSchedule.Rows.Count == 0) return;

                List<string> activeAreas = GetCheckedAreaTypes();
                string fmt = GetDecimalFormatString();
                int totalQty = 0;
                double[] sumUnitAreas = new double[activeAreas.Count];
                double[] sumTotalAreas = new double[activeAreas.Count];

                for (int r = 0; r < _dtSchedule.Rows.Count; r++)
                {
                    DataRow dr = _dtSchedule.Rows[r];
                    dr[0] = (r + 1).ToString("00");

                    int q = 0;
                    int.TryParse(Convert.ToString(dr[3] ?? "0"), out q);
                    totalQty += q;

                    int colIdx = 4;
                    for (int a = 0; a < activeAreas.Count; a++)
                    {
                        double uArea = ParsePositiveDouble(Convert.ToString(dr[colIdx] ?? "0"), 0.0);
                        double tArea = uArea * q;
                        dr[colIdx] = uArea.ToString(fmt, CultureInfo.InvariantCulture);
                        dr[colIdx + 1] = tArea.ToString(fmt, CultureInfo.InvariantCulture);

                        sumUnitAreas[a] += uArea;
                        sumTotalAreas[a] += tArea;
                        colIdx += 2;
                    }
                }

                for (int a = 0; a < activeAreas.Count; a++)
                {
                    _latestTotalAreasByType[activeAreas[a]] = sumTotalAreas[a];
                }
                SyncParameterToEfficiencyInput(true);
                SyncParameterToEfficiencyInput(false);

                // 1. Dòng TỔNG CỘNG (SUM)
                DataRow sumRow = _dtSchedule.NewRow();
                sumRow[0] = "TỔNG";
                sumRow[1] = "SUM";
                sumRow[2] = "TỔNG CỘNG";
                sumRow[3] = totalQty.ToString();

                int cIdx = 4;
                for (int a = 0; a < activeAreas.Count; a++)
                {
                    sumRow[cIdx++] = sumUnitAreas[a].ToString(fmt, CultureInfo.InvariantCulture) + " m\u00B2";
                    sumRow[cIdx++] = sumTotalAreas[a].ToString(fmt, CultureInfo.InvariantCulture) + " m\u00B2";
                }
                _dtSchedule.Rows.Add(sumRow);

                // 2. Dòng HIỆU SUẤT (%) (Gọn gàng, không ghi Tử số/Mẫu số)
                if (_chkIncludeEffInTable != null && _chkIncludeEffInTable.Checked)
                {
                    double pct = CalculateEfficiencyPercentValue();
                    string pctStr = FormatEfficiencyPercentString(pct);

                    DataRow effRow = _dtSchedule.NewRow();
                    effRow[0] = "HIỆU SUẤT";
                    effRow[1] = "RATIO (%)";
                    effRow[2] = "HIỆU SUẤT";
                    effRow[3] = pctStr;

                    for (int c = 4; c < _dtSchedule.Columns.Count; c++)
                    {
                        effRow[c] = (c == _dtSchedule.Columns.Count - 1) ? pctStr : "";
                    }
                    _dtSchedule.Rows.Add(effRow);
                }
            }
            finally
            {
                _isUpdatingSum = false;
            }
        }

        private void DgvSchedule_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _dgvSchedule.Rows.Count) return;
            string firstCell = Convert.ToString(_dgvSchedule.Rows[e.RowIndex].Cells[0].Value ?? "");
            if (firstCell == "TỔNG")
            {
                e.CellStyle.BackColor = ClrTaupeSoftBtn;
                e.CellStyle.ForeColor = ClrTextDark;
                e.CellStyle.Font = new WinFont("Segoe UI", 8.75f, FontStyle.Bold);
            }
            else if (firstCell == "HIỆU SUẤT")
            {
                e.CellStyle.BackColor = WinColor.FromArgb(222, 208, 192);
                e.CellStyle.ForeColor = ClrTextDark;
                e.CellStyle.Font = new WinFont("Segoe UI", 8.75f, FontStyle.Bold);
            }
        }

        private void BtnExportCadTable_Click(object sender, EventArgs e)
        {
            if (_dtSchedule == null || _dtSchedule.Rows.Count == 0)
            {
                MessageBox.Show("Bảng thống kê đang trống! Hãy bấm 'Quét Thống Kê Bản Vẽ' trước.",
                    "DB Area Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            CadApp.MainWindow.Focus();
            PromptPointResult ppr = ed.GetPoint("\n[DBAREA] Chọn điểm đặt Bảng Thống Kê Diện Tích Căn Hộ: ");
            if (ppr.Status != PromptStatus.OK) return;

            string targetLayer = string.IsNullOrWhiteSpace(_txtLayer.Text) ? "DB-A-Text" : _txtLayer.Text.Trim();

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureCompanyLayerAndTextStyles(db, tr, targetLayer, out ObjectId titleStyleId, out ObjectId regStyleId);
                BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                int numCols = _dtSchedule.Columns.Count;
                int numRows = _dtSchedule.Rows.Count + 2;

                Table tb = new Table();
                tb.TableStyle = db.Tablestyle;
                tb.Layer = targetLayer;
                tb.Position = ppr.Value;
                tb.SetSize(numRows, numCols);
                tb.SetRowHeight(900.0);

                for (int c = 0; c < numCols; c++)
                {
                    double colW = 3400.0;
                    if (c == 0) colW = 2000.0;
                    else if (c == 1) colW = 2800.0;
                    else if (c == 2) colW = 3400.0;
                    else if (c == 3) colW = 2400.0;
                    tb.SetColumnWidth(c, colW);
                }

                tb.SetTextString(0, 0, "BẢNG THỐNG KÊ DIỆN TÍCH & HIỆU SUẤT CĂN HỘ");
                tb.SetTextHeight(0, 0, 500.0);
                tb.SetAlignment(0, 0, CellAlignment.MiddleCenter);

                for (int c = 0; c < numCols; c++)
                {
                    tb.SetTextString(1, c, _dtSchedule.Columns[c].ColumnName);
                    tb.SetTextHeight(1, c, 350.0);
                    tb.SetAlignment(1, c, CellAlignment.MiddleCenter);
                }

                for (int r = 0; r < _dtSchedule.Rows.Count; r++)
                {
                    bool isFooter = IsFooterSummaryRow(_dtSchedule.Rows[r]);
                    for (int c = 0; c < numCols; c++)
                    {
                        string cellText = Convert.ToString(_dtSchedule.Rows[r][c] ?? "");
                        tb.SetTextString(r + 2, c, cellText);
                        tb.SetTextHeight(r + 2, c, isFooter ? 380.0 : 320.0);
                        tb.SetAlignment(r + 2, c, (c >= 4) ? CellAlignment.MiddleRight : CellAlignment.MiddleCenter);
                    }
                }

                tb.GenerateLayout();
                space.AppendEntity(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                tr.Commit();
            }

            ed.Regen();
            _lblStatus.Text = "Đã xuất Bảng Thống Kê Căn Hộ (kèm dòng TỔNG CỘNG & HIỆU SUẤT %) ra bản vẽ CAD!";
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            if (_dtSchedule == null || _dtSchedule.Rows.Count == 0) return;

            using (System.Windows.Forms.SaveFileDialog sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Filter = "Excel CSV (*.csv)|*.csv";
                sfd.FileName = $"ThongKeDienTichCanHo_{DateTime.Now:yyyyMMdd_HHmm}.csv";

                if (sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    StringBuilder sb = new StringBuilder();
                    string[] headers = _dtSchedule.Columns.Cast<System.Data.DataColumn>().Select(col => $"\"{col.ColumnName}\"").ToArray();
                    sb.AppendLine(string.Join(",", headers));

                    foreach (DataRow row in _dtSchedule.Rows)
                    {
                        string[] fields = row.ItemArray.Select(val => $"\"{Convert.ToString(val ?? "")}\"").ToArray();
                        sb.AppendLine(string.Join(",", fields));
                    }

                    File.WriteAllBytes(sfd.FileName, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
                    _lblStatus.Text = "Đã xuất file Excel (.csv): " + Path.GetFileName(sfd.FileName);
                }
            }
        }

        private double ParsePositiveDouble(string text, double fallback)
        {
            string clean = (text ?? "").Replace("m²", "").Replace("m2", "").Replace("%", "").Trim();
            double d;
            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out d) && d >= 0)
                return d;
            return fallback;
        }
    }
}