using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// Namespace chuẩn của ZWCAD .NET API
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.PlottingServices;
using ZwSoft.ZwCAD.Runtime;
using Application = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadColor = ZwSoft.ZwCAD.Colors.Color;
using ColorMethod = ZwSoft.ZwCAD.Colors.ColorMethod;
using Color = System.Drawing.Color;

[assembly: CommandClass(typeof(ZwCadBatchPlotter.BatchPlotCommand))]

namespace ZwCadBatchPlotter
{
    // ================= 1. CẤU TRÚC LƯU BỘ NHỚ TẠM (PERSISTENT SETUP MEMORY) =================
    public class PlotterSetupMemory
    {
        public string DeviceName { get; set; } = "";
        public string MediaName { get; set; } = "";
        public string CtbName { get; set; } = "";
        public int FilterTypeIndex { get; set; } = 0;
        public bool IsFitScale { get; set; } = true;
        public decimal ScaleDenominator { get; set; } = 100M;
        public bool IsCentered { get; set; } = true;
        public decimal OffsetX { get; set; } = 0M;
        public decimal OffsetY { get; set; } = 0M;
        public int SortDirectionIndex { get; set; } = 0; // 0: Ngang, 1: Dọc, 2: Thứ tự chọn
        public decimal Tolerance { get; set; } = 500M;
        public int ExportFormatIndex { get; set; } = 2;  // 0: PNG, 1: PDF, 2: PDF + PNG
        public int CombineModeIndex { get; set; } = 0;   // 0: Gộp 1 file, 1: Tách rời, 2: Cả Gộp + Rời
        public int PngDpiIndex { get; set; } = 1;        // 1: 300 DPI
        public string OutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public bool OpenFolderAfterPlot { get; set; } = true;

        private static PlotterSetupMemory _sessionCache;
        private static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZwCadBatchPlotter",
            "user_setup_v5.cfg");

        public static PlotterSetupMemory Load()
        {
            if (_sessionCache != null) return _sessionCache;

            PlotterSetupMemory mem = new PlotterSetupMemory();
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string[] lines = File.ReadAllLines(ConfigFilePath, Encoding.UTF8);
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in lines)
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) dict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }

                    if (dict.TryGetValue("DeviceName", out string v)) mem.DeviceName = v;
                    if (dict.TryGetValue("MediaName", out v)) mem.MediaName = v;
                    if (dict.TryGetValue("CtbName", out v)) mem.CtbName = v;
                    if (dict.TryGetValue("FilterTypeIndex", out v) && int.TryParse(v, out int iVal)) mem.FilterTypeIndex = iVal;
                    if (dict.TryGetValue("IsFitScale", out v) && bool.TryParse(v, out bool bVal)) mem.IsFitScale = bVal;
                    if (dict.TryGetValue("ScaleDenominator", out v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal dVal)) mem.ScaleDenominator = dVal;
                    if (dict.TryGetValue("IsCentered", out v) && bool.TryParse(v, out bVal)) mem.IsCentered = bVal;
                    if (dict.TryGetValue("OffsetX", out v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dVal)) mem.OffsetX = dVal;
                    if (dict.TryGetValue("OffsetY", out v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dVal)) mem.OffsetY = dVal;
                    if (dict.TryGetValue("SortDirectionIndex", out v) && int.TryParse(v, out iVal)) mem.SortDirectionIndex = iVal;
                    if (dict.TryGetValue("Tolerance", out v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out dVal)) mem.Tolerance = dVal;
                    if (dict.TryGetValue("ExportFormatIndex", out v) && int.TryParse(v, out iVal)) mem.ExportFormatIndex = iVal;
                    if (dict.TryGetValue("CombineModeIndex", out v) && int.TryParse(v, out iVal)) mem.CombineModeIndex = iVal;
                    if (dict.TryGetValue("PngDpiIndex", out v) && int.TryParse(v, out iVal)) mem.PngDpiIndex = iVal;
                    if (dict.TryGetValue("OutputDirectory", out v) && !string.IsNullOrWhiteSpace(v)) mem.OutputDirectory = v;
                    if (dict.TryGetValue("OpenFolderAfterPlot", out v) && bool.TryParse(v, out bVal)) mem.OpenFolderAfterPlot = bVal;
                }
            }
            catch { }

            _sessionCache = mem;
            return mem;
        }

        public void Save()
        {
            _sessionCache = this;
            try
            {
                string dir = Path.GetDirectoryName(ConfigFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine($"DeviceName={DeviceName}");
                sb.AppendLine($"MediaName={MediaName}");
                sb.AppendLine($"CtbName={CtbName}");
                sb.AppendLine($"FilterTypeIndex={FilterTypeIndex}");
                sb.AppendLine($"IsFitScale={IsFitScale}");
                sb.AppendLine($"ScaleDenominator={ScaleDenominator.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"IsCentered={IsCentered}");
                sb.AppendLine($"OffsetX={OffsetX.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"OffsetY={OffsetY.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"SortDirectionIndex={SortDirectionIndex}");
                sb.AppendLine($"Tolerance={Tolerance.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"ExportFormatIndex={ExportFormatIndex}");
                sb.AppendLine($"CombineModeIndex={CombineModeIndex}");
                sb.AppendLine($"PngDpiIndex={PngDpiIndex}");
                sb.AppendLine($"OutputDirectory={OutputDirectory}");
                sb.AppendLine($"OpenFolderAfterPlot={OpenFolderAfterPlot}");

                File.WriteAllText(ConfigFilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ================= 2. CẤU TRÚC LƯU KIỂU NÉT ĐỨT (CAD LINETYPE) =================
    public class CadLinetypeInfo
    {
        public string Name { get; set; } = "Continuous";
        public bool IsContinuous { get; set; } = true;
        public double PatternLength { get; set; } = 0.0;
        public List<double> DashLengths { get; set; } = new List<double>();
    }

    // ================= 3. BỘ ĐỌC BẢNG NÉT CÔNG TY (.CTB PARSER CHUẨN ZWCAD/AUTOCAD) =================
    public class CtbPenRule
    {
        public bool UseObjectColor { get; set; } = true;
        public Color MappedColor { get; set; } = Color.Black;
        public bool ConvertToGrayscale { get; set; } = false;
        public int Screening { get; set; } = 100; // 0..100%
        public double LineweightMm { get; set; } = -1.0; // < 0 nghĩa là Use Object Lineweight
        public int CtbLinetype { get; set; } = 31; // 31 = Use Object Linetype
    }

    public class CtbPlotStyleTable
    {
        private readonly CtbPenRule[] _pens = new CtbPenRule[256];
        public bool IsLoaded { get; private set; }

        public CtbPlotStyleTable()
        {
            for (int i = 0; i < 256; i++) _pens[i] = new CtbPenRule();
        }

        public CtbPenRule GetPen(short aci)
        {
            if (aci >= 1 && aci <= 255) return _pens[aci];
            return _pens[7];
        }

        public static CtbPlotStyleTable LoadFromCtbName(string ctbName)
        {
            CtbPlotStyleTable table = new CtbPlotStyleTable();
            if (string.IsNullOrWhiteSpace(ctbName) || ctbName.StartsWith("(None", StringComparison.OrdinalIgnoreCase))
                return table;

            string ctbPath = FindCtbFilePath(ctbName);
            if (!string.IsNullOrEmpty(ctbPath) && File.Exists(ctbPath))
            {
                try
                {
                    string content = ReadAndDecompressCtbFile(ctbPath);
                    if (!string.IsNullOrEmpty(content))
                    {
                        table.ParseDecompressedContent(content);
                        table.IsLoaded = true;
                        return table;
                    }
                }
                catch { }
            }

            bool isMono = ctbName.IndexOf("monochrome", StringComparison.OrdinalIgnoreCase) >= 0;
            for (int i = 1; i <= 255; i++)
            {
                if (isMono || (i >= 1 && i <= 7))
                {
                    table._pens[i].UseObjectColor = false;
                    table._pens[i].MappedColor = Color.Black;
                }
            }
            table.IsLoaded = true;
            return table;
        }

        private static string FindCtbFilePath(string ctbFileName)
        {
            List<string> searchDirs = new List<string>();
            try
            {
                string roam = Convert.ToString(Application.GetSystemVariable("ROAMABLEROOTPREFIX"));
                if (!string.IsNullOrEmpty(roam))
                {
                    searchDirs.Add(Path.Combine(roam, "Plot Styles"));
                    searchDirs.Add(Path.Combine(roam, "PrintStyles"));
                    searchDirs.Add(Path.Combine(roam, "Plotters", "Plot Styles"));
                }
            }
            catch { }

            try
            {
                string acadPrefix = Convert.ToString(Application.GetSystemVariable("ACADPREFIX"));
                if (!string.IsNullOrEmpty(acadPrefix))
                {
                    foreach (string p in acadPrefix.Split(';'))
                        if (!string.IsNullOrWhiteSpace(p)) searchDirs.Add(p.Trim());
                }
            }
            catch { }

            foreach (string dir in searchDirs)
            {
                try
                {
                    string candidate = Path.Combine(dir, ctbFileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            try
            {
                string zwAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZWSOFT");
                if (Directory.Exists(zwAppData))
                {
                    string[] files = Directory.GetFiles(zwAppData, ctbFileName, SearchOption.AllDirectories);
                    if (files.Length > 0) return files[0];
                }
            }
            catch { }

            return null;
        }

        private static string ReadAndDecompressCtbFile(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            string headerPreview = Encoding.ASCII.GetString(raw, 0, Math.Min(raw.Length, 128));

            if (headerPreview.Contains("plot_style"))
                return Encoding.UTF8.GetString(raw);

            byte[] marker = Encoding.ASCII.GetBytes("pmzlibcodec");
            int markerPos = -1;
            for (int i = 0; i < Math.Min(raw.Length - marker.Length, 200); i++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++)
                {
                    if (raw[i + j] != marker[j]) { match = false; break; }
                }
                if (match) { markerPos = i; break; }
            }

            if (markerPos >= 0)
            {
                int searchStart = markerPos + marker.Length + 10;
                int searchEnd = Math.Min(raw.Length - 4, markerPos + marker.Length + 30);
                for (int i = searchStart; i < searchEnd; i++)
                {
                    if (raw[i] == 0x78 && (raw[i + 1] == 0x01 || raw[i + 1] == 0x5E || raw[i + 1] == 0x9C || raw[i + 1] == 0xDA))
                    {
                        using (MemoryStream ms = new MemoryStream(raw, i + 2, raw.Length - (i + 2)))
                        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                        using (StreamReader sr = new StreamReader(ds, Encoding.ASCII))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }

            return null;
        }

        private void ParseDecompressedContent(string text)
        {
            Dictionary<int, double> lwTable = new Dictionary<int, double>();
            Match lwMatch = Regex.Match(text, @"custom_lineweight_table\s*\{([^}]*)\}", RegexOptions.Singleline);
            if (lwMatch.Success)
            {
                foreach (Match m in Regex.Matches(lwMatch.Groups[1].Value, @"(\d+)\s*=\s*([0-9.]+)"))
                {
                    if (int.TryParse(m.Groups[1].Value, out int idx) &&
                        double.TryParse(m.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double mm))
                    {
                        lwTable[idx] = mm;
                    }
                }
            }

            int plotStylePos = text.IndexOf("plot_style", StringComparison.OrdinalIgnoreCase);
            if (plotStylePos < 0) return;

            string plotStyleSection = text.Substring(plotStylePos);
            MatchCollection penMatches = Regex.Matches(plotStyleSection, @"\b(\d+)\s*\{([^}]*)\}", RegexOptions.Singleline);

            foreach (Match pm in penMatches)
            {
                if (!int.TryParse(pm.Groups[1].Value, out int penIdx)) continue;
                int aci = penIdx + 1;
                if (aci < 1 || aci > 255) continue;

                string body = pm.Groups[2].Value;
                CtbPenRule rule = _pens[aci];

                int colorPolicy = 1;
                Match cpMatch = Regex.Match(body, @"\bcolor_policy\s*=\s*(-?\d+)");
                if (cpMatch.Success) int.TryParse(cpMatch.Groups[1].Value, out colorPolicy);

                long colorVal = -1006632960;
                Match mcMatch = Regex.Match(body, @"\bmode_color\s*=\s*(-?\d+)");
                if (!mcMatch.Success) mcMatch = Regex.Match(body, @"\bcolor\s*=\s*(-?\d+)");
                if (mcMatch.Success) long.TryParse(mcMatch.Groups[1].Value, out colorVal);

                uint ucol = unchecked((uint)(int)colorVal);
                byte methodByte = (byte)((ucol >> 24) & 0xFF);

                rule.ConvertToGrayscale = (colorPolicy == 3 || (colorPolicy & 4) != 0);

                if (methodByte == 0xC4 || ucol == 0xFFFFFFFF || ((colorPolicy & 1) == 1 && methodByte != 0xC2))
                {
                    rule.UseObjectColor = true;
                }
                else if (methodByte == 0xC2)
                {
                    rule.UseObjectColor = false;
                    int r = (int)((ucol >> 16) & 0xFF);
                    int g = (int)((ucol >> 8) & 0xFF);
                    int b = (int)(ucol & 0xFF);
                    rule.MappedColor = Color.FromArgb(r, g, b);
                }

                Match scrMatch = Regex.Match(body, @"\bscreen\s*=\s*(\d+)");
                if (scrMatch.Success && int.TryParse(scrMatch.Groups[1].Value, out int scr))
                {
                    rule.Screening = Math.Max(0, Math.Min(100, scr));
                }

                Match lwPenMatch = Regex.Match(body, @"\blineweight\s*=\s*(\d+)");
                if (lwPenMatch.Success && int.TryParse(lwPenMatch.Groups[1].Value, out int lwIdx) && lwIdx > 0)
                {
                    if (lwTable.TryGetValue(lwIdx - 1, out double mmVal))
                        rule.LineweightMm = mmVal;
                }

                Match ltPenMatch = Regex.Match(body, @"\blinetype\s*=\s*(\d+)");
                if (ltPenMatch.Success && int.TryParse(ltPenMatch.Groups[1].Value, out int ltIdx))
                {
                    rule.CtbLinetype = ltIdx;
                }
            }
        }
    }

    // ================= 4. CẤU TRÚC KHUNG BẢN VẼ & LỆNH ZPLOT =================
    public class FrameItem
    {
        public int Index { get; set; }
        public ObjectId Id { get; set; }
        public string EntityType { get; set; }
        public string NameOrLayer { get; set; }
        public Extents3d ExtentsWcs { get; set; }
        public double Width => Math.Round(ExtentsWcs.MaxPoint.X - ExtentsWcs.MinPoint.X, 1);
        public double Height => Math.Round(ExtentsWcs.MaxPoint.Y - ExtentsWcs.MinPoint.Y, 1);
        public Point3d CenterWcs => new Point3d(
            (ExtentsWcs.MinPoint.X + ExtentsWcs.MaxPoint.X) * 0.5,
            (ExtentsWcs.MinPoint.Y + ExtentsWcs.MaxPoint.Y) * 0.5,
            0);
        public bool IsLandscape => Width >= Height;
        public string OrientationText => IsLandscape ? "Ngang" : "Dọc";
    }

    public class BatchPlotCommand
    {
        [CommandMethod("ZPLOT", CommandFlags.Modal)]
        public void ShowBatchPlotDialog()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (BatchPlotForm form = new BatchPlotForm(doc))
            {
                Application.ShowModalDialog(form);
            }
        }
    }

    // ================= 5. GIAO DIỆN CHÍNH (BATCH PLOT FORM v5.0) =================
    public class BatchPlotForm : Form
    {
        private readonly Document _doc;
        private readonly Database _db;
        private readonly Editor _ed;
        private List<FrameItem> _frames = new List<FrameItem>();
        private PlotterSetupMemory _memory;
        private bool _isLoadingUi = false;

        // Bộ nhớ đệm Layer & Linetype
        private Dictionary<ObjectId, short> _layerAciCache = new Dictionary<ObjectId, short>();
        private Dictionary<ObjectId, CadColor> _layerCadColorCache = new Dictionary<ObjectId, CadColor>();
        private Dictionary<ObjectId, LineWeight> _layerLwCache = new Dictionary<ObjectId, LineWeight>();
        private Dictionary<ObjectId, ObjectId> _layerLinetypeCache = new Dictionary<ObjectId, ObjectId>();
        private Dictionary<ObjectId, CadLinetypeInfo> _linetypeCache = new Dictionary<ObjectId, CadLinetypeInfo>();
        private double _globalLtScale = 1.0;

        private CtbPlotStyleTable _activeCtbTable = new CtbPlotStyleTable();

        // Controls
        private ComboBox cboDevice, cboMedia, cboCtb, cboFilterType, cboSortOrder;
        private ComboBox cboExportFormat, cboCombineMode, cboPngDpi;
        private RadioButton radFit, radCustomScale;
        private NumericUpDown numScaleDenominator, numOffsetX, numOffsetY, numTolerance;
        private CheckBox chkCenterPlot, chkOpenFolder;
        private TextBox txtOutputDir, txtPrefix;

        private DataGridView dgvFrames;
        private PictureBox picMiniPreview;
        private Label lblPreviewInfo, lblStatus, lblMemoryNote;
        private ProgressBar progressBar;
        private Button btnSelectAuto, btnSelectManual, btnPreviewCadOrder, btnPreviewNativePlot;
        private Button btnMoveUp, btnMoveDown, btnReverseOrder, btnRemoveFrame, btnPlot;

        public BatchPlotForm(Document doc)
        {
            _doc = doc;
            _db = doc.Database;
            _ed = doc.Editor;
            _memory = PlotterSetupMemory.Load();

            InitializeComponents();
            LoadPlotterDevicesAndRestoreSetup();
        }

        private void InitializeComponents()
        {
            this.Text = $"ZWCAD Quick Batch Plotter v5.0 (Full XCLIP | Nét đứt | CTB | Combine) - Không gian: {LayoutManager.Current.CurrentLayout}";
            this.Size = new Size(1065, 685);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular);
            this.FormClosing += (s, e) => SaveCurrentUiToMemory();

            // ================= 1. GROUP: MÁY IN & BẢNG NÉT CÔNG TY =================
            GroupBox grpPrinter = new GroupBox { Text = "1. Máy in & Bảng nét Công ty (.CTB)", Location = new Point(12, 10), Size = new Size(340, 178) };

            grpPrinter.Controls.Add(new Label { Text = "Máy in:", Location = new Point(10, 26), AutoSize = true });
            cboDevice = new ComboBox { Location = new Point(95, 23), Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            cboDevice.SelectedIndexChanged += (s, e) => {
                if (!_isLoadingUi) { LoadMediaNames(null); SaveCurrentUiToMemory(); }
            };
            grpPrinter.Controls.Add(cboDevice);

            grpPrinter.Controls.Add(new Label { Text = "Khổ giấy:", Location = new Point(10, 56), AutoSize = true });
            cboMedia = new ComboBox { Location = new Point(95, 53), Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            cboMedia.SelectedIndexChanged += (s, e) => { if (!_isLoadingUi) SaveCurrentUiToMemory(); };
            grpPrinter.Controls.Add(cboMedia);

            grpPrinter.Controls.Add(new Label { Text = "Bảng nét:", Location = new Point(10, 86), AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) });
            cboCtb = new ComboBox { Location = new Point(95, 83), Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCtb.SelectedIndexChanged += (s, e) => {
                ReloadActiveCtbTable();
                if (!_isLoadingUi) { SaveCurrentUiToMemory(); RenderMiniPreview(); }
            };
            grpPrinter.Controls.Add(cboCtb);

            grpPrinter.Controls.Add(new Label { Text = "Loại khung:", Location = new Point(10, 116), AutoSize = true });
            cboFilterType = new ComboBox { Location = new Point(95, 113), Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            cboFilterType.Items.AddRange(new object[] { "Block & Polyline kín (Tất cả)", "Chỉ Block khung tên (INSERT)", "Chỉ Polyline kín (LWPOLYLINE)" });
            cboFilterType.SelectedIndexChanged += (s, e) => { if (!_isLoadingUi) SaveCurrentUiToMemory(); };
            grpPrinter.Controls.Add(cboFilterType);

            lblMemoryNote = new Label
            {
                Text = "✓ Tự động lưu Setup | Hỗ trợ đầy đủ XCLIP & Nét đứt",
                Location = new Point(10, 148),
                AutoSize = true,
                ForeColor = Color.SeaGreen,
                Font = new System.Drawing.Font("Segoe UI", 8.2F, FontStyle.Italic)
            };
            grpPrinter.Controls.Add(lblMemoryNote);

            // ================= 2. GROUP: TỶ LỆ, TÂM IN & CHIỀU IN (NGANG / DỌC) =================
            GroupBox grpScale = new GroupBox { Text = "2. Tỷ lệ (Scale), Tâm in (Center) & Chiều in", Location = new Point(360, 10), Size = new Size(365, 178) };

            radFit = new RadioButton { Text = "Fit to Paper", Location = new Point(12, 25), AutoSize = true, Checked = true };
            radCustomScale = new RadioButton { Text = "Tỷ lệ 1 :", Location = new Point(135, 25), AutoSize = true };
            numScaleDenominator = new NumericUpDown { Location = new Point(210, 23), Width = 80, Minimum = 0.001M, Maximum = 100000M, DecimalPlaces = 2, Value = 100, Enabled = false };
            radCustomScale.CheckedChanged += (s, e) => {
                numScaleDenominator.Enabled = radCustomScale.Checked;
                if (!_isLoadingUi) { SaveCurrentUiToMemory(); RefreshGridScalePreview(); }
            };
            numScaleDenominator.ValueChanged += (s, e) => { if (!_isLoadingUi) { SaveCurrentUiToMemory(); RefreshGridScalePreview(); } };
            grpScale.Controls.AddRange(new Control[] { radFit, radCustomScale, numScaleDenominator });

            chkCenterPlot = new CheckBox { Text = "Đặt khung in ở chính giữa (Center the Plot)", Location = new Point(12, 55), AutoSize = true, Checked = true, Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) };
            Label lblOffX = new Label { Text = "Lệch X (mm):", Location = new Point(15, 83), AutoSize = true };
            numOffsetX = new NumericUpDown { Location = new Point(100, 80), Width = 65, Minimum = -500, Maximum = 500, DecimalPlaces = 1, Value = 0, Enabled = false };
            Label lblOffY = new Label { Text = "Lệch Y (mm):", Location = new Point(180, 83), AutoSize = true };
            numOffsetY = new NumericUpDown { Location = new Point(265, 80), Width = 65, Minimum = -500, Maximum = 500, DecimalPlaces = 1, Value = 0, Enabled = false };
            chkCenterPlot.CheckedChanged += (s, e) => {
                numOffsetX.Enabled = !chkCenterPlot.Checked;
                numOffsetY.Enabled = !chkCenterPlot.Checked;
                if (!_isLoadingUi) { SaveCurrentUiToMemory(); RefreshGridScalePreview(); }
            };
            grpScale.Controls.AddRange(new Control[] { chkCenterPlot, lblOffX, numOffsetX, lblOffY, numOffsetY });

            grpScale.Controls.Add(new Label { Text = "Chiều in:", Location = new Point(12, 120), AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) });
            cboSortOrder = new ComboBox { Location = new Point(85, 117), Width = 185, DropDownStyle = ComboBoxStyle.DropDownList };
            cboSortOrder.Items.AddRange(new object[] {
                "Ngang",
                "Dọc",
                "Thứ tự chọn"
            });
            cboSortOrder.SelectedIndexChanged += (s, e) => {
                if (!_isLoadingUi) { SaveCurrentUiToMemory(); SortAndBindFrames(); }
            };

            numTolerance = new NumericUpDown { Location = new Point(280, 117), Width = 70, Minimum = 1, Maximum = 100000, Value = 500 };
            grpScale.Controls.Add(new Label { Text = "Sai số hàng:", Location = new Point(280, 99), AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 7.5F) });
            grpScale.Controls.AddRange(new Control[] { cboSortOrder, numTolerance });

            // ================= 3. GROUP: XUẤT FILE (PDF / PNG) & COMBINE =================
            GroupBox grpFormat = new GroupBox { Text = "3. Định dạng Xuất & Gộp bản vẽ (Combine)", Location = new Point(733, 10), Size = new Size(305, 178) };

            grpFormat.Controls.Add(new Label { Text = "Định dạng:", Location = new Point(10, 25), AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) });
            cboExportFormat = new ComboBox { Location = new Point(100, 22), Width = 193, DropDownStyle = ComboBoxStyle.DropDownList };
            cboExportFormat.Items.AddRange(new object[] {
                "Chỉ xuất PNG (.png)",
                "Chỉ xuất PDF (.pdf)",
                "Xuất đồng thời PDF + PNG"
            });
            cboExportFormat.SelectedIndexChanged += (s, e) => {
                cboPngDpi.Enabled = (cboExportFormat.SelectedIndex != 1);
                if (!_isLoadingUi) SaveCurrentUiToMemory();
            };
            grpFormat.Controls.Add(cboExportFormat);

            grpFormat.Controls.Add(new Label { Text = "Gộp bản vẽ:", Location = new Point(10, 55), AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = Color.DarkBlue });
            cboCombineMode = new ComboBox { Location = new Point(100, 52), Width = 193, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCombineMode.Items.AddRange(new object[] {
                "Gộp chung 1 file (Combine)",
                "Tách rời từng bản vẽ (01, 02...)",
                "Xuất cả File Gộp + File Rời"
            });
            cboCombineMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingUi) SaveCurrentUiToMemory(); };
            grpFormat.Controls.Add(cboCombineMode);

            grpFormat.Controls.Add(new Label { Text = "Độ nét PNG:", Location = new Point(10, 85), AutoSize = true });
            cboPngDpi = new ComboBox { Location = new Point(100, 82), Width = 193, DropDownStyle = ComboBoxStyle.DropDownList };
            cboPngDpi.Items.AddRange(new object[] {
                "150 DPI (2480 x 1754 px)",
                "300 DPI (4960 x 3508 px - 4K)",
                "600 DPI (9920 x 7016 px - 8K)"
            });
            cboPngDpi.SelectedIndexChanged += (s, e) => { if (!_isLoadingUi) SaveCurrentUiToMemory(); };
            grpFormat.Controls.Add(cboPngDpi);

            grpFormat.Controls.Add(new Label { Text = "Thư mục lưu:", Location = new Point(10, 115), AutoSize = true });
            txtOutputDir = new TextBox { Location = new Point(100, 112), Width = 155 };
            txtOutputDir.TextChanged += (s, e) => { if (!_isLoadingUi) SaveCurrentUiToMemory(); };
            Button btnBrowse = new Button { Text = "...", Location = new Point(258, 111), Width = 35, Height = 25 };
            btnBrowse.Click += (s, e) => {
                using (var fbd = new FolderBrowserDialog { SelectedPath = txtOutputDir.Text })
                {
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        txtOutputDir.Text = fbd.SelectedPath;
                        SaveCurrentUiToMemory();
                    }
                }
            };
            grpFormat.Controls.AddRange(new Control[] { txtOutputDir, btnBrowse });

            grpFormat.Controls.Add(new Label { Text = "Tên file:", Location = new Point(10, 145), AutoSize = true });
            txtPrefix = new TextBox { Location = new Point(100, 142), Width = 193, Text = Path.GetFileNameWithoutExtension(_doc.Name) };
            grpFormat.Controls.Add(txtPrefix);

            // ================= 4. THANH CÔNG CỤ QUÉT KHUNG & PREVIEW =================
            btnSelectAuto = new Button { Text = "1. Quét chọn hàng loạt Khung (Auto Sort)", Location = new Point(12, 195), Size = new Size(265, 36), BackColor = Color.FromArgb(225, 240, 255), Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) };
            btnSelectAuto.Click += (s, e) => SelectFramesOnScreen(false);

            btnSelectManual = new Button { Text = "2. Chọn thủ công / Fence (Theo thứ tự)", Location = new Point(283, 195), Size = new Size(265, 36), BackColor = Color.FromArgb(230, 250, 230), Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) };
            btnSelectManual.Click += (s, e) => SelectFramesOnScreen(true);

            btnPreviewCadOrder = new Button { Text = "👁 Kiểm tra Số thứ tự trên CAD (1->2->3)", Location = new Point(555, 195), Size = new Size(255, 36), BackColor = Color.FromArgb(255, 245, 205), Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) };
            btnPreviewCadOrder.Click += (s, e) => PreviewOrderOnCadScreen();

            btnPreviewNativePlot = new Button { Text = "🔍 Preview Bản in ZWCAD", Location = new Point(816, 195), Size = new Size(222, 36), BackColor = Color.FromArgb(245, 235, 255), Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) };
            btnPreviewNativePlot.Click += (s, e) => PreviewSelectedSheetNative();

            // ================= 5. BẢNG DANH SÁCH BẢN VẼ =================
            dgvFrames = new DataGridView
            {
                Location = new Point(12, 239),
                Size = new Size(713, 315),
                AllowUserToAddRows = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                RowHeadersVisible = false
            };
            dgvFrames.Columns.Add("Index", "STT in");
            dgvFrames.Columns["Index"].FillWeight = 40;
            dgvFrames.Columns.Add("EntityType", "Loại Khung");
            dgvFrames.Columns["EntityType"].FillWeight = 65;
            dgvFrames.Columns.Add("NameOrLayer", "Tên Block / Layer");
            dgvFrames.Columns["NameOrLayer"].FillWeight = 120;
            dgvFrames.Columns.Add("Size", "Kích thước (W x H)");
            dgvFrames.Columns["Size"].FillWeight = 95;
            dgvFrames.Columns.Add("Orientation", "Khổ xoay");
            dgvFrames.Columns["Orientation"].FillWeight = 65;
            dgvFrames.Columns.Add("ScalePreview", "Thiết lập Tỷ lệ & Tâm");
            dgvFrames.Columns["ScalePreview"].FillWeight = 115;
            dgvFrames.SelectionChanged += (s, e) => RenderMiniPreview();

            Label lblOrderTools = new Label { Text = "Chỉnh thứ tự in:", Location = new Point(12, 561), AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Bold) };
            btnMoveUp = new Button { Text = "▲ Đẩy lên", Location = new Point(115, 556), Size = new Size(95, 28) };
            btnMoveUp.Click += (s, e) => MoveSelectedFrame(-1);

            btnMoveDown = new Button { Text = "▼ Đẩy xuống", Location = new Point(216, 556), Size = new Size(100, 28) };
            btnMoveDown.Click += (s, e) => MoveSelectedFrame(1);

            btnReverseOrder = new Button { Text = "⇄ Đảo ngược toàn bộ", Location = new Point(322, 556), Size = new Size(145, 28) };
            btnReverseOrder.Click += (s, e) => {
                if (_frames.Count > 1) { _frames.Reverse(); cboSortOrder.SelectedIndex = 2; RefreshGridScalePreview(); }
            };

            btnRemoveFrame = new Button { Text = "✖ Bỏ khung đang chọn", Location = new Point(473, 556), Size = new Size(150, 28) };
            btnRemoveFrame.Click += (s, e) => {
                if (dgvFrames.SelectedRows.Count > 0)
                {
                    int idx = dgvFrames.SelectedRows[0].Index;
                    _frames.RemoveAt(idx);
                    RefreshGridScalePreview();
                }
            };

            chkOpenFolder = new CheckBox { Text = "Mở thư mục sau khi xuất", Location = new Point(635, 560), AutoSize = true, Checked = true };
            chkOpenFolder.CheckedChanged += (s, e) => { if (!_isLoadingUi) SaveCurrentUiToMemory(); };

            // ================= 6. KHUNG XEM NHANH (VISUAL PREVIEW THEO ĐÚNG CTB, XCLIP & NÉT ĐỨT) =================
            GroupBox grpMiniPreview = new GroupBox { Text = "Xem nhanh Bản in (Đầy đủ CTB, XCLIP & Nét đứt)", Location = new Point(733, 235), Size = new Size(305, 350) };
            picMiniPreview = new PictureBox
            {
                Location = new Point(10, 22),
                Size = new Size(285, 275),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            lblPreviewInfo = new Label
            {
                Text = "Chọn 1 bản vẽ để xem trước kết quả áp dụng Bảng nét (.ctb), XCLIP, Nét đứt và Hatch.",
                Location = new Point(10, 303),
                Size = new Size(285, 40),
                ForeColor = Color.DarkBlue
            };
            grpMiniPreview.Controls.AddRange(new Control[] { picMiniPreview, lblPreviewInfo });

            // ================= 7. THANH TIẾN TRÌNH & NÚT XUẤT =================
            progressBar = new ProgressBar { Location = new Point(12, 595), Size = new Size(713, 24) };
            lblStatus = new Label { Text = "Sẵn sàng. Đã khôi phục Setup lần trước.", Location = new Point(12, 623), AutoSize = true, ForeColor = Color.DarkSlateGray };

            btnPlot = new Button
            {
                Text = "XUẤT BẢN VẼ HÀNG LOẠT",
                Location = new Point(733, 593),
                Size = new Size(305, 46),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font = new System.Drawing.Font("Segoe UI", 10.5F, FontStyle.Bold)
            };
            btnPlot.Click += (s, e) => ExecuteBatchExport();

            this.Controls.AddRange(new Control[] {
                grpPrinter, grpScale, grpFormat,
                btnSelectAuto, btnSelectManual, btnPreviewCadOrder, btnPreviewNativePlot,
                dgvFrames, lblOrderTools, btnMoveUp, btnMoveDown, btnReverseOrder, btnRemoveFrame, chkOpenFolder,
                grpMiniPreview, progressBar, lblStatus, btnPlot
            });
        }

        // ================= TẢI MÁY IN & KHÔI PHỤC BỘ NHỚ TẠM =================
        private void LoadPlotterDevicesAndRestoreSetup()
        {
            _isLoadingUi = true;
            try
            {
                PlotSettingsValidator psv = PlotSettingsValidator.Current;
                StringCollection devices = psv.GetPlotDeviceList();
                cboDevice.Items.Clear();
                foreach (string dev in devices) cboDevice.Items.Add(dev);

                int devIdx = !string.IsNullOrEmpty(_memory.DeviceName) ? cboDevice.Items.IndexOf(_memory.DeviceName) : -1;
                if (devIdx < 0)
                {
                    string[] preferredPdf = { "ZWPLOT_PDF.pc5", "DWG To PDF.pc3", "ZWCAD PDF(High Quality Print).pc3", "Microsoft Print to PDF" };
                    foreach (string pref in preferredPdf)
                    {
                        devIdx = cboDevice.Items.IndexOf(pref);
                        if (devIdx >= 0) break;
                    }
                }
                if (devIdx >= 0) cboDevice.SelectedIndex = devIdx;
                else if (cboDevice.Items.Count > 0) cboDevice.SelectedIndex = 0;

                LoadMediaNames(_memory.MediaName);

                StringCollection ctbs = psv.GetPlotStyleSheetList();
                cboCtb.Items.Clear();
                foreach (string ctb in ctbs) cboCtb.Items.Add(ctb);
                cboCtb.Items.Add("(None)");

                int ctbIdx = !string.IsNullOrEmpty(_memory.CtbName) ? cboCtb.Items.IndexOf(_memory.CtbName) : -1;
                if (ctbIdx >= 0) cboCtb.SelectedIndex = ctbIdx;
                else if (cboCtb.Items.Count > 0) cboCtb.SelectedIndex = 0;

                ReloadActiveCtbTable();

                cboFilterType.SelectedIndex = Math.Max(0, Math.Min(cboFilterType.Items.Count - 1, _memory.FilterTypeIndex));
                radFit.Checked = _memory.IsFitScale;
                radCustomScale.Checked = !_memory.IsFitScale;
                numScaleDenominator.Enabled = !_memory.IsFitScale;
                numScaleDenominator.Value = Math.Max(numScaleDenominator.Minimum, Math.Min(numScaleDenominator.Maximum, _memory.ScaleDenominator));

                chkCenterPlot.Checked = _memory.IsCentered;
                numOffsetX.Enabled = !_memory.IsCentered;
                numOffsetY.Enabled = !_memory.IsCentered;
                numOffsetX.Value = Math.Max(numOffsetX.Minimum, Math.Min(numOffsetX.Maximum, _memory.OffsetX));
                numOffsetY.Value = Math.Max(numOffsetY.Minimum, Math.Min(numOffsetY.Maximum, _memory.OffsetY));

                cboSortOrder.SelectedIndex = Math.Max(0, Math.Min(cboSortOrder.Items.Count - 1, _memory.SortDirectionIndex));
                numTolerance.Value = Math.Max(numTolerance.Minimum, Math.Min(numTolerance.Maximum, _memory.Tolerance));

                cboExportFormat.SelectedIndex = Math.Max(0, Math.Min(cboExportFormat.Items.Count - 1, _memory.ExportFormatIndex));
                cboCombineMode.SelectedIndex = Math.Max(0, Math.Min(cboCombineMode.Items.Count - 1, _memory.CombineModeIndex));
                cboPngDpi.SelectedIndex = Math.Max(0, Math.Min(cboPngDpi.Items.Count - 1, _memory.PngDpiIndex));
                cboPngDpi.Enabled = (cboExportFormat.SelectedIndex != 1);

                txtOutputDir.Text = _memory.OutputDirectory;
                chkOpenFolder.Checked = _memory.OpenFolderAfterPlot;
            }
            finally
            {
                _isLoadingUi = false;
            }
        }

        private void LoadMediaNames(string preferredMedia)
        {
            if (cboDevice.SelectedItem == null) return;
            string device = cboDevice.SelectedItem.ToString();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                LayoutManager lm = LayoutManager.Current;
                Layout layout = (Layout)tr.GetObject(lm.GetLayoutId(lm.CurrentLayout), OpenMode.ForRead);
                using (PlotSettings ps = new PlotSettings(layout.ModelType))
                {
                    ps.CopyFrom(layout);
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;
                    psv.SetPlotConfigurationName(ps, device, null);
                    psv.RefreshLists(ps);

                    StringCollection medias = psv.GetCanonicalMediaNameList(ps);
                    cboMedia.Items.Clear();
                    int targetIdx = -1;
                    int a3Index = -1;

                    for (int i = 0; i < medias.Count; i++)
                    {
                        cboMedia.Items.Add(medias[i]);
                        if (!string.IsNullOrEmpty(preferredMedia) && string.Equals(medias[i], preferredMedia, StringComparison.OrdinalIgnoreCase))
                            targetIdx = i;
                        if (a3Index < 0 && (medias[i].IndexOf("ISO_full_bleed_A3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            medias[i].IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0))
                            a3Index = i;
                    }

                    if (targetIdx >= 0) cboMedia.SelectedIndex = targetIdx;
                    else if (a3Index >= 0) cboMedia.SelectedIndex = a3Index;
                    else if (cboMedia.Items.Count > 0) cboMedia.SelectedIndex = 0;
                }
                tr.Commit();
            }
        }

        private void SaveCurrentUiToMemory()
        {
            if (_isLoadingUi || _memory == null) return;

            if (cboDevice.SelectedItem != null) _memory.DeviceName = cboDevice.SelectedItem.ToString();
            if (cboMedia.SelectedItem != null) _memory.MediaName = cboMedia.SelectedItem.ToString();
            if (cboCtb.SelectedItem != null) _memory.CtbName = cboCtb.SelectedItem.ToString();

            _memory.FilterTypeIndex = cboFilterType.SelectedIndex;
            _memory.IsFitScale = radFit.Checked;
            _memory.ScaleDenominator = numScaleDenominator.Value;
            _memory.IsCentered = chkCenterPlot.Checked;
            _memory.OffsetX = numOffsetX.Value;
            _memory.OffsetY = numOffsetY.Value;
            _memory.SortDirectionIndex = cboSortOrder.SelectedIndex;
            _memory.Tolerance = numTolerance.Value;
            _memory.ExportFormatIndex = cboExportFormat.SelectedIndex;
            _memory.CombineModeIndex = cboCombineMode.SelectedIndex;
            _memory.PngDpiIndex = cboPngDpi.SelectedIndex;
            _memory.OutputDirectory = txtOutputDir.Text;
            _memory.OpenFolderAfterPlot = chkOpenFolder.Checked;

            _memory.Save();
        }

        private void ReloadActiveCtbTable()
        {
            string ctbName = cboCtb.SelectedItem != null ? cboCtb.SelectedItem.ToString() : "(None)";
            _activeCtbTable = CtbPlotStyleTable.LoadFromCtbName(ctbName);
        }

        // ================= QUÉT CHỌN KHUNG BẢN VẼ =================
        private void SelectFramesOnScreen(bool isManualOrder)
        {
            SaveCurrentUiToMemory();
            this.Hide();
            try
            {
                if (isManualOrder) cboSortOrder.SelectedIndex = 2;

                TypedValue[] filterList;
                if (cboFilterType.SelectedIndex == 1)
                    filterList = new[] { new TypedValue((int)DxfCode.Start, "INSERT") };
                else if (cboFilterType.SelectedIndex == 2)
                    filterList = new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") };
                else
                    filterList = new[] { new TypedValue((int)DxfCode.Start, "INSERT,LWPOLYLINE") };

                SelectionFilter filter = new SelectionFilter(filterList);
                PromptSelectionOptions pso = new PromptSelectionOptions
                {
                    MessageForAdding = isManualOrder
                        ? "\nChọn từng khung hoặc gõ 'F' (Fence) để kẻ đường dẫn qua các khung theo thứ tự in: "
                        : "\nQuét chọn các khung bản vẽ cần in: "
                };

                PromptSelectionResult psr = _ed.GetSelection(pso, filter);
                if (psr.Status == PromptStatus.OK)
                {
                    _frames.Clear();
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so == null) continue;
                            Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;

                            if (ent is Polyline pline && !pline.Closed) continue;

                            Extents3d ext;
                            try { ext = ent.GeometricExtents; }
                            catch { continue; }

                            double w = ext.MaxPoint.X - ext.MinPoint.X;
                            double h = ext.MaxPoint.Y - ext.MinPoint.Y;
                            if (w < 10 || h < 10) continue;

                            string nameOrLayer = ent.Layer;
                            if (ent is BlockReference br)
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                                    br.DynamicBlockTableRecord != ObjectId.Null ? br.DynamicBlockTableRecord : br.BlockTableRecord,
                                    OpenMode.ForRead);
                                nameOrLayer = $"{btr.Name} ({ent.Layer})";
                            }

                            _frames.Add(new FrameItem
                            {
                                Id = ent.ObjectId,
                                EntityType = ent is BlockReference ? "Block" : "Polyline",
                                NameOrLayer = nameOrLayer,
                                ExtentsWcs = ext
                            });
                        }
                        tr.Commit();
                    }
                    SortAndBindFrames();
                }
            }
            finally
            {
                this.Show();
            }
        }

        private void SortAndBindFrames()
        {
            if (_frames.Count == 0) return;
            double tol = (double)numTolerance.Value;

            if (cboSortOrder.SelectedIndex == 0) // Ngang
            {
                _frames = _frames
                    .OrderByDescending(f => Math.Round(f.ExtentsWcs.MinPoint.Y / tol) * tol)
                    .ThenBy(f => f.ExtentsWcs.MinPoint.X)
                    .ToList();
            }
            else if (cboSortOrder.SelectedIndex == 1) // Dọc
            {
                _frames = _frames
                    .OrderBy(f => Math.Round(f.ExtentsWcs.MinPoint.X / tol) * tol)
                    .ThenByDescending(f => f.ExtentsWcs.MinPoint.Y)
                    .ToList();
            }

            RefreshGridScalePreview();
        }

        private void MoveSelectedFrame(int direction)
        {
            if (dgvFrames.SelectedRows.Count == 0) return;
            int idx = dgvFrames.SelectedRows[0].Index;
            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _frames.Count) return;

            cboSortOrder.SelectedIndex = 2;
            FrameItem temp = _frames[idx];
            _frames[idx] = _frames[newIdx];
            _frames[newIdx] = temp;

            RefreshGridScalePreview();
            dgvFrames.ClearSelection();
            dgvFrames.Rows[newIdx].Selected = true;
        }

        private void RefreshGridScalePreview()
        {
            dgvFrames.Rows.Clear();
            string scaleInfo = radFit.Checked ? "Fit to Paper" : $"1 : {numScaleDenominator.Value}";
            string centerInfo = chkCenterPlot.Checked ? "Center" : $"Offset ({numOffsetX.Value},{numOffsetY.Value})";

            for (int i = 0; i < _frames.Count; i++)
            {
                _frames[i].Index = i + 1;
                var f = _frames[i];
                dgvFrames.Rows.Add(
                    f.Index.ToString("D2"),
                    f.EntityType,
                    f.NameOrLayer,
                    $"{f.Width} x {f.Height}",
                    f.OrientationText,
                    $"{scaleInfo} | {centerInfo}"
                );
            }
            lblStatus.Text = $"Đã nhận diện {_frames.Count} bản vẽ. Bảng nét áp dụng: {cboCtb.Text}";
            RenderMiniPreview();
        }

        // ================= PREVIEW 1: HIỆN SỐ THỨ TỰ TRÊN MÀN HÌNH CAD =================
        private void PreviewOrderOnCadScreen()
        {
            if (_frames.Count == 0)
            {
                MessageBox.Show("Chưa có khung bản vẽ nào để kiểm tra thứ tự!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.Hide();
            try
            {
                using (DocumentLock docLock = _doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord curSpace = (BlockTableRecord)tr.GetObject(_db.CurrentSpaceId, OpenMode.ForWrite);

                    if (_frames.Count > 1)
                    {
                        Polyline pathLine = new Polyline();
                        for (int i = 0; i < _frames.Count; i++)
                        {
                            Point3d pt = _frames[i].CenterWcs;
                            pathLine.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0, 0, 0);
                        }
                        pathLine.ColorIndex = 2;
                        curSpace.AppendEntity(pathLine);
                        tr.AddNewlyCreatedDBObject(pathLine, true);
                    }

                    foreach (var f in _frames)
                    {
                        double minDim = Math.Min(f.Width, f.Height);
                        double textHeight = Math.Max(minDim * 0.22, 10.0);

                        Polyline border = new Polyline(4);
                        border.AddVertexAt(0, new Point2d(f.ExtentsWcs.MinPoint.X, f.ExtentsWcs.MinPoint.Y), 0, 0, 0);
                        border.AddVertexAt(1, new Point2d(f.ExtentsWcs.MaxPoint.X, f.ExtentsWcs.MinPoint.Y), 0, 0, 0);
                        border.AddVertexAt(2, new Point2d(f.ExtentsWcs.MaxPoint.X, f.ExtentsWcs.MaxPoint.Y), 0, 0, 0);
                        border.AddVertexAt(3, new Point2d(f.ExtentsWcs.MinPoint.X, f.ExtentsWcs.MaxPoint.Y), 0, 0, 0);
                        border.Closed = true;
                        border.ColorIndex = 3;
                        curSpace.AppendEntity(border);
                        tr.AddNewlyCreatedDBObject(border, true);

                        DBText numText = new DBText
                        {
                            TextString = f.Index.ToString(),
                            Height = textHeight,
                            ColorIndex = 3,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextVerticalMid,
                            Position = f.CenterWcs,
                            AlignmentPoint = f.CenterWcs
                        };
                        curSpace.AppendEntity(numText);
                        tr.AddNewlyCreatedDBObject(numText, true);
                    }

                    tr.TransactionManager.QueueForGraphicsFlush();
                    _ed.UpdateScreen();

                    _ed.GetString("\n[KIỂM TRA THỨ TỰ IN]: Lăn chuột Zoom/Pan để xem số thứ tự (1 -> 2 -> 3...). Bấm ENTER hoặc SPACE để quay lại: ");
                    tr.Abort();
                }
                _ed.Regen();
            }
            finally
            {
                this.Show();
            }
        }

        // ================= PREVIEW 2: XEM NHANH TRÊN FORM (ĐẦY ĐỦ XCLIP, NÉT ĐỨT & CTB) =================
        private void RenderMiniPreview()
        {
            if (dgvFrames.SelectedRows.Count == 0 || _frames.Count == 0)
            {
                picMiniPreview.Image = null;
                lblPreviewInfo.Text = "Chưa chọn khung bản vẽ.";
                return;
            }

            int idx = dgvFrames.SelectedRows[0].Index;
            if (idx < 0 || idx >= _frames.Count) return;

            FrameItem item = _frames[idx];
            lblPreviewInfo.Text = $"Thứ tự: #{item.Index:D2}/{_frames.Count:D2} | Bảng nét: {cboCtb.Text}\nKhung: {item.NameOrLayer} ({item.OrientationText})";

            try
            {
                Bitmap bmp = RenderFrameWithCtbToBitmap(item, 960, 678, true);
                var oldImg = picMiniPreview.Image;
                picMiniPreview.Image = bmp;
                oldImg?.Dispose();
            }
            catch { }
        }

        // ================= PREVIEW 3: ZWCAD NATIVE PLOT PREVIEW =================
        private void PreviewSelectedSheetNative()
        {
            if (_frames.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 khung bản vẽ trước khi Preview!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveCurrentUiToMemory();
            int idx = dgvFrames.SelectedRows.Count > 0 ? dgvFrames.SelectedRows[0].Index : 0;
            FrameItem item = _frames[idx];

            string device = cboDevice.SelectedItem.ToString();
            string media = cboMedia.SelectedItem.ToString();
            string ctb = GetSelectedCtbFileName();

            short oldBgPlot = Convert.ToInt16(Application.GetSystemVariable("BACKGROUNDPLOT"));
            Application.SetSystemVariable("BACKGROUNDPLOT", 0);

            this.Hide();
            try
            {
                using (DocumentLock docLock = _doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    LayoutManager lm = LayoutManager.Current;
                    Layout layout = (Layout)tr.GetObject(lm.GetLayoutId(lm.CurrentLayout), OpenMode.ForRead);

                    using (PlotSettings ps = ConfigurePlotSettings(layout, item, device, media, ctb))
                    using (PlotInfo pi = new PlotInfo { Layout = layout.ObjectId, OverrideSettings = ps })
                    using (PlotInfoValidator piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled })
                    {
                        piv.Validate(pi);

                        if (PlotFactory.ProcessPlotState == ProcessPlotState.NotPlotting)
                        {
                            using (PlotEngine pe = PlotFactory.CreatePreviewEngine((int)PreviewEngineFlags.Plot))
                            using (PlotProgressDialog ppd = new PlotProgressDialog(true, 1, true))
                            {
                                ppd.set_PlotMsgString(PlotMessageIndex.DialogTitle, $"Preview Bản vẽ #{item.Index:D2}");
                                ppd.set_PlotMsgString(PlotMessageIndex.SheetName, $"{txtPrefix.Text}_{item.Index:D2}");
                                ppd.LowerPlotProgressRange = 0;
                                ppd.UpperPlotProgressRange = 100;
                                ppd.IsVisible = true;

                                pe.BeginPlot(ppd, null);
                                pe.BeginDocument(pi, _doc.Name, null, 1, false, null);

                                using (PlotPageInfo ppi = new PlotPageInfo())
                                {
                                    pe.BeginPage(ppi, pi, true, null);
                                    pe.BeginGenerateGraphics(null);
                                    pe.EndGenerateGraphics(null);
                                    pe.EndPage(null);
                                }

                                pe.EndDocument(null);
                                pe.EndPlot(null);
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Không thể mở ZWCAD Plot Preview: {ex.Message}", "Lỗi Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Application.SetSystemVariable("BACKGROUNDPLOT", oldBgPlot);
                this.Show();
            }
        }

        // =================================================================================================
        // BỘ DỰNG ẢNH PNG & PREVIEW v5.0: ĐẦY ĐỦ XCLIP (SPATIAL FILTER), NÉT ĐỨT (LINETYPE), HATCH & CTB
        // =================================================================================================
        private double GetMatrixScale(Matrix3d mat)
        {
            try
            {
                Vector3d vx = new Vector3d(1.0, 0.0, 0.0).TransformBy(mat);
                Vector3d vy = new Vector3d(0.0, 1.0, 0.0).TransformBy(mat);
                return Math.Max(vx.Length, vy.Length);
            }
            catch
            {
                return 1.0;
            }
        }

        private Bitmap RenderFrameWithCtbToBitmap(FrameItem item, int targetLongEdgePx, int targetShortEdgePx, bool isMiniPreview)
        {
            int bmpWidth = item.IsLandscape ? targetLongEdgePx : targetShortEdgePx;
            int bmpHeight = item.IsLandscape ? targetShortEdgePx : targetLongEdgePx;

            double effectiveDpi = Math.Max(bmpWidth, bmpHeight) / (420.0 / 25.4);

            Bitmap bmp = new Bitmap(bmpWidth, bmpHeight, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.White);

                double marginRatio = 0.025;
                double availW = bmpWidth * (1.0 - 2 * marginRatio);
                double availH = bmpHeight * (1.0 - 2 * marginRatio);

                double scaleX = availW / Math.Max(item.Width, 1e-6);
                double scaleY = availH / Math.Max(item.Height, 1e-6);
                double scale = Math.Min(scaleX, scaleY);

                double drawW = item.Width * scale;
                double drawH = item.Height * scale;
                double offsetX = chkCenterPlot.Checked
                    ? (bmpWidth - drawW) * 0.5
                    : (bmpWidth * marginRatio) + ((double)numOffsetX.Value / 420.0 * bmpWidth);
                double offsetY = chkCenterPlot.Checked
                    ? (bmpHeight - drawH) * 0.5
                    : (bmpHeight * marginRatio) + ((double)numOffsetY.Value / 297.0 * bmpHeight);

                PointF clipTL = new PointF((float)offsetX - 2f, (float)(bmpHeight - (offsetY + drawH)) - 2f);
                g.SetClip(new RectangleF(clipTL.X, clipTL.Y, (float)drawW + 4f, (float)drawH + 4f));

                Func<Point3d, PointF> toScreen = (pt) =>
                {
                    float sx = (float)(offsetX + (pt.X - item.ExtentsWcs.MinPoint.X) * scale);
                    float sy = (float)(bmpHeight - (offsetY + (pt.Y - item.ExtentsWcs.MinPoint.Y) * scale));
                    return new PointF(sx, sy);
                };

                float basePenWidth = isMiniPreview ? 1.0f : Math.Max(1.25f, bmpWidth / 2600f);

                using (DocumentLock docLock = _doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    BuildLayerAndLinetypeCache(tr);

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(_db.CurrentSpaceId, OpenMode.ForRead);
                    List<ObjectId> orderedIds = GetOrderedEntityIds(tr, btr);

                    List<Entity> visibleEntities = new List<Entity>();
                    foreach (ObjectId objId in orderedIds)
                    {
                        Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                        if (ent == null || !ent.Visible) continue;

                        Extents3d entExt;
                        try { entExt = ent.GeometricExtents; }
                        catch { continue; }

                        if (entExt.MaxPoint.X < item.ExtentsWcs.MinPoint.X || entExt.MinPoint.X > item.ExtentsWcs.MaxPoint.X ||
                            entExt.MaxPoint.Y < item.ExtentsWcs.MinPoint.Y || entExt.MinPoint.Y > item.ExtentsWcs.MaxPoint.Y)
                            continue;

                        visibleEntities.Add(ent);
                    }

                    // LƯỢT 1 (PASS 0): Vẽ toàn bộ Hatch, Solid & Wipeout (có cắt XCLIP đầy đủ)
                    foreach (Entity ent in visibleEntities)
                    {
                        RenderEntityWithCtbGdi(g, tr, ent, Matrix3d.Identity, 7, Color.Black, ObjectId.Null, 1.0,
                            toScreen, scale, effectiveDpi, basePenWidth, true);
                    }

                    // LƯỢT 2 (PASS 1): Vẽ toàn bộ Nét kiến trúc, Nét đứt, Đường cong, Chữ, Kích thước (có cắt XCLIP đầy đủ)
                    foreach (Entity ent in visibleEntities)
                    {
                        RenderEntityWithCtbGdi(g, tr, ent, Matrix3d.Identity, 7, Color.Black, ObjectId.Null, 1.0,
                            toScreen, scale, effectiveDpi, basePenWidth, false);
                    }

                    tr.Commit();
                }

                g.ResetClip();

                if (isMiniPreview)
                {
                    using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(215, 0, 120, 215)))
                    using (System.Drawing.Font badgeFont = new System.Drawing.Font("Segoe UI", 13F, FontStyle.Bold))
                    {
                        g.FillRectangle(badgeBrush, 10, 10, 115, 34);
                        g.DrawString($"STT #{item.Index:D2}", badgeFont, Brushes.White, 16, 13);
                    }
                }
            }
            return bmp;
        }

        private List<ObjectId> GetOrderedEntityIds(Transaction tr, BlockTableRecord btr)
        {
            List<ObjectId> result = new List<ObjectId>();
            try
            {
                if (btr.DrawOrderTableId != ObjectId.Null)
                {
                    DrawOrderTable dot = tr.GetObject(btr.DrawOrderTableId, OpenMode.ForRead) as DrawOrderTable;
                    if (dot != null)
                    {
                        ObjectIdCollection col = dot.GetFullDrawOrder(0);
                        foreach (ObjectId id in col) result.Add(id);
                        if (result.Count > 0) return result;
                    }
                }
            }
            catch { }

            foreach (ObjectId id in btr) result.Add(id);
            return result;
        }

        // Xây dựng bộ nhớ đệm Layer + Linetype (Nét đứt) của toàn bộ bản vẽ
        private void BuildLayerAndLinetypeCache(Transaction tr)
        {
            _layerAciCache.Clear();
            _layerCadColorCache.Clear();
            _layerLwCache.Clear();
            _layerLinetypeCache.Clear();
            _linetypeCache.Clear();

            try
            {
                _globalLtScale = Convert.ToDouble(Application.GetSystemVariable("LTSCALE"));
                if (_globalLtScale <= 1e-6) _globalLtScale = 1.0;
            }
            catch { _globalLtScale = 1.0; }

            // 1. Đọc bảng LayerTable
            LayerTable lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lyrId in lt)
            {
                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lyrId, OpenMode.ForRead);
                _layerCadColorCache[lyrId] = ltr.Color;
                _layerAciCache[lyrId] = ltr.Color.ColorIndex;
                _layerLwCache[lyrId] = ltr.LineWeight;
                _layerLinetypeCache[lyrId] = ltr.LinetypeObjectId;
            }

            // 2. Đọc bảng LinetypeTable (Tất cả các kiểu nét đứt: HIDDEN, DASHED, CENTER, DOT, ISO...)
            LinetypeTable ltt = (LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId ltId in ltt)
            {
                LinetypeTableRecord ltRec = tr.GetObject(ltId, OpenMode.ForRead) as LinetypeTableRecord;
                if (ltRec == null) continue;

                string name = ltRec.Name ?? "Continuous";
                bool isCont = string.Equals(name, "Continuous", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(name, "ByLayer", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(name, "ByBlock", StringComparison.OrdinalIgnoreCase);

                CadLinetypeInfo info = new CadLinetypeInfo
                {
                    Name = name,
                    IsContinuous = isCont,
                    PatternLength = ltRec.PatternLength
                };

                if (!isCont)
                {
                    int nDashes = ltRec.NumDashes;
                    for (int i = 0; i < nDashes; i++)
                    {
                        try { info.DashLengths.Add(ltRec.DashLengthAt(i)); }
                        catch { }
                    }

                    // Nếu Linetype có tên là nét đứt nhưng chưa có mảng DashLengths thì tạo mẫu chuẩn ISO
                    if (info.DashLengths.Count < 2)
                    {
                        PopulateFallbackDashPatternByName(info);
                    }
                    else if (info.PatternLength <= 1e-6)
                    {
                        info.PatternLength = info.DashLengths.Sum(d => Math.Abs(d));
                    }
                }

                _linetypeCache[ltId] = info;
            }
        }

        private void PopulateFallbackDashPatternByName(CadLinetypeInfo info)
        {
            string upper = (info.Name ?? "").ToUpperInvariant();
            if (upper.Contains("CENTER"))
            {
                info.DashLengths = new List<double> { 31.75, -6.35, 6.35, -6.35 };
                info.PatternLength = 50.8;
            }
            else if (upper.Contains("PHANTOM") || upper.Contains("DIVIDE"))
            {
                info.DashLengths = new List<double> { 31.75, -6.35, 6.35, -6.35, 6.35, -6.35 };
                info.PatternLength = 63.5;
            }
            else if (upper.Contains("DASHDOT"))
            {
                info.DashLengths = new List<double> { 12.7, -6.35, 0.0, -6.35 };
                info.PatternLength = 25.4;
            }
            else if (upper.Contains("DOT"))
            {
                info.DashLengths = new List<double> { 0.0, -6.35 };
                info.PatternLength = 6.35;
            }
            else if (upper.Contains("HID") || upper.Contains("DASH") || upper.Contains("ISO") || upper.Contains("BORDER"))
            {
                info.DashLengths = new List<double> { 12.7, -6.35 };
                info.PatternLength = 19.05;
            }
            else
            {
                info.IsContinuous = true;
            }
        }

        // ================= GIẢI MÃ VÙNG CẮT XCLIP (SPATIAL FILTER) TRÊN BLOCK / XREF =================
        private System.Drawing.Region TryCreateBlockXClipRegion(
            Transaction tr,
            BlockReference br,
            Matrix3d parentTransform,
            Func<Point3d, PointF> toScreen)
        {
            try
            {
                if (br.ExtensionDictionary == ObjectId.Null) return null;

                DBDictionary extDict = tr.GetObject(br.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                if (extDict == null || !extDict.Contains("ACAD_FILTER")) return null;

                DBDictionary filterDict = tr.GetObject(extDict.GetAt("ACAD_FILTER"), OpenMode.ForRead) as DBDictionary;
                if (filterDict == null || !filterDict.Contains("SPATIAL")) return null;

                DBObject spObj = tr.GetObject(filterDict.GetAt("SPATIAL"), OpenMode.ForRead);
                if (spObj == null) return null;

                Type spType = spObj.GetType();
                object defObj = spType.GetProperty("Definition")?.GetValue(spObj, null);
                if (defObj == null) return null;

                Type defType = defObj.GetType();

                // Kiểm tra XCLIP có đang bật (Enabled) hay không
                PropertyInfo enabledPropDef = defType.GetProperty("Enabled");
                if (enabledPropDef != null && !(bool)enabledPropDef.GetValue(defObj, null))
                    return null;

                PropertyInfo enabledPropSp = spType.GetProperty("Enabled");
                if (enabledPropSp != null && !(bool)enabledPropSp.GetValue(spObj, null))
                    return null;

                // Kiểm tra chế độ Invert XCLIP (Đảo vùng cắt)
                bool isInverted = false;
                PropertyInfo invPropSp = spType.GetProperty("Inverted");
                PropertyInfo invPropDef = defType.GetProperty("Inverted");
                if (invPropSp != null) isInverted = (bool)invPropSp.GetValue(spObj, null);
                else if (invPropDef != null) isInverted = (bool)invPropDef.GetValue(defObj, null);

                // Lấy tập hợp đỉnh 2D của biên XCLIP
                Point2dCollection rawPts = null;
                MethodInfo getPtsMethod = defType.GetMethod("GetPoints", Type.EmptyTypes);
                if (getPtsMethod != null)
                {
                    rawPts = getPtsMethod.Invoke(defObj, null) as Point2dCollection;
                }
                else
                {
                    PropertyInfo ptsProp = defType.GetProperty("Points");
                    if (ptsProp != null) rawPts = ptsProp.GetValue(defObj, null) as Point2dCollection;
                }

                if (rawPts == null || rawPts.Count < 2) return null;

                List<Point2d> clipPoly2d = new List<Point2d>();
                if (rawPts.Count == 2)
                {
                    // Trong CAD: Nếu XCLIP chỉ có 2 đỉnh thì đó là 2 góc đối diện của Hình chữ nhật
                    double minX = Math.Min(rawPts[0].X, rawPts[1].X);
                    double maxX = Math.Max(rawPts[0].X, rawPts[1].X);
                    double minY = Math.Min(rawPts[0].Y, rawPts[1].Y);
                    double maxY = Math.Max(rawPts[0].Y, rawPts[1].Y);
                    clipPoly2d.Add(new Point2d(minX, minY));
                    clipPoly2d.Add(new Point2d(maxX, minY));
                    clipPoly2d.Add(new Point2d(maxX, maxY));
                    clipPoly2d.Add(new Point2d(minX, maxY));
                }
                else
                {
                    for (int i = 0; i < rawPts.Count; i++) clipPoly2d.Add(rawPts[i]);
                }

                // Xác định ma trận chuyển đổi từ không gian XCLIP sang WCS hiện tại
                Matrix3d clipToWcs = ResolveBestXClipMatrix(spObj, defObj, br, parentTransform, clipPoly2d);

                PointF[] screenPts = new PointF[clipPoly2d.Count];
                for (int i = 0; i < clipPoly2d.Count; i++)
                {
                    Point3d wPt = new Point3d(clipPoly2d[i].X, clipPoly2d[i].Y, 0.0).TransformBy(clipToWcs);
                    screenPts[i] = toScreen(wPt);
                }

                GraphicsPath path = new GraphicsPath(FillMode.Winding);
                path.AddPolygon(screenPts);

                if (!isInverted)
                {
                    return new System.Drawing.Region (path);
                }
                else
                {
                    System.Drawing.Region fullReg = new System.Drawing.Region(new RectangleF(-20000f, -20000f, 60000f, 60000f));
                    fullReg.Exclude(path);
                    path.Dispose();
                    return fullReg;
                }
            }
            catch
            {
                return null;
            }
        }

        private Matrix3d ResolveBestXClipMatrix(
            DBObject spObj,
            object defObj,
            BlockReference br,
            Matrix3d parentTransform,
            List<Point2d> clipPoly2d)
        {
            Type spType = spObj.GetType();
            Type defType = defObj.GetType();

            List<Matrix3d> candidates = new List<Matrix3d>();

            // Ứng viên 1: Công thức chuẩn ObjectARX (parent * BlockTransform * OriginalInverseBlockTransform * ClipSpaceToWorldTransform)
            try
            {
                PropertyInfo origInvProp = spType.GetProperty("OriginalInverseBlockTransform");
                PropertyInfo clipWorldProp = spType.GetProperty("ClipSpaceToWorldTransform");
                if (origInvProp != null && clipWorldProp != null)
                {
                    Matrix3d origInv = (Matrix3d)origInvProp.GetValue(spObj, null);
                    Matrix3d clipToWorld = (Matrix3d)clipWorldProp.GetValue(spObj, null);
                    candidates.Add(parentTransform * br.BlockTransform * origInv * clipToWorld);
                    candidates.Add(parentTransform * clipToWorld);
                }
            }
            catch { }

            // Ứng viên 2: Thuộc tính Transform trực tiếp trên SpatialFilterDefinition
            try
            {
                PropertyInfo defTrProp = defType.GetProperty("Transform");
                if (defTrProp != null)
                {
                    Matrix3d defMat = (Matrix3d)defTrProp.GetValue(defObj, null);
                    candidates.Add(parentTransform * br.BlockTransform * defMat);
                    candidates.Add(parentTransform * defMat);
                }
            }
            catch { }

            // Ứng viên 3: Tọa độ trong không gian Block hoặc WCS trực tiếp
            candidates.Add(parentTransform * br.BlockTransform);
            candidates.Add(parentTransform);

            // Đối chiếu với GeometricExtents thực tế của BlockReference đã cắt để chọn ma trận khớp tuyệt đối
            try
            {
                Extents3d brExt = br.GeometricExtents;
                brExt.TransformBy(parentTransform);
                Point3d targetCenter = new Point3d(
                    (brExt.MinPoint.X + brExt.MaxPoint.X) * 0.5,
                    (brExt.MinPoint.Y + brExt.MaxPoint.Y) * 0.5,
                    0);
                double targetW = brExt.MaxPoint.X - brExt.MinPoint.X;
                double targetH = brExt.MaxPoint.Y - brExt.MinPoint.Y;

                Matrix3d bestMat = candidates[0];
                double bestScore = double.MaxValue;

                foreach (Matrix3d cand in candidates)
                {
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    foreach (Point2d p2 in clipPoly2d)
                    {
                        Point3d wp = new Point3d(p2.X, p2.Y, 0).TransformBy(cand);
                        if (wp.X < minX) minX = wp.X;
                        if (wp.Y < minY) minY = wp.Y;
                        if (wp.X > maxX) maxX = wp.X;
                        if (wp.Y > maxY) maxY = wp.Y;
                    }
                    Point3d candCenter = new Point3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0);
                    double candW = maxX - minX;
                    double candH = maxY - minY;

                    double distErr = (candCenter - targetCenter).Length;
                    double sizeErr = Math.Abs(candW - targetW) + Math.Abs(candH - targetH);
                    double score = distErr + sizeErr * 0.5;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestMat = cand;
                    }
                }
                return bestMat;
            }
            catch
            {
                return candidates[0];
            }
        }

        // ================= GIẢI MÃ MÀU, ĐỘ DÀY NÉT (.CTB) & KIỂU NÉT ĐỨT (LINETYPE) =================
        private void ResolveEntityPlotStyleAndLinetype(
            Entity ent,
            short parentBlockAci,
            Color parentBlockPlotColor,
            ObjectId parentBlockLinetypeId,
            double parentBlockLtScale,
            double effectiveDpi,
            float basePenWidth,
            out short effectiveAci,
            out Color finalPlotColor,
            out float finalPenWidth,
            out ObjectId effectiveLinetypeId,
            out double effectiveLtScale,
            out CadLinetypeInfo linetypeInfo)
        {
            CadColor c = ent.Color;
            bool isTrueColor = (c.ColorMethod == ColorMethod.ByColor);
            Color rawObjectRgb = Color.Black;
            effectiveAci = 7;

            if (c.IsByLayer)
            {
                if (_layerCadColorCache.TryGetValue(ent.LayerId, out CadColor lyrCol))
                {
                    isTrueColor = (lyrCol.ColorMethod == ColorMethod.ByColor);
                    effectiveAci = lyrCol.ColorIndex;
                    rawObjectRgb = isTrueColor ? Color.FromArgb(lyrCol.Red, lyrCol.Green, lyrCol.Blue) : AciToRgb(effectiveAci);
                }
            }
            else if (c.IsByBlock)
            {
                effectiveAci = parentBlockAci;
                rawObjectRgb = parentBlockPlotColor;
            }
            else if (isTrueColor)
            {
                rawObjectRgb = Color.FromArgb(c.Red, c.Green, c.Blue);
                effectiveAci = c.ColorIndex;
            }
            else
            {
                effectiveAci = c.ColorIndex;
                rawObjectRgb = AciToRgb(effectiveAci);
            }

            double ctbLineweightMm = -1.0;
            if (!isTrueColor && _activeCtbTable != null && _activeCtbTable.IsLoaded && effectiveAci >= 1 && effectiveAci <= 255)
            {
                CtbPenRule rule = _activeCtbTable.GetPen(effectiveAci);
                finalPlotColor = rule.UseObjectColor ? rawObjectRgb : rule.MappedColor;

                if (rule.ConvertToGrayscale)
                {
                    int gray = (int)Math.Round(0.299 * finalPlotColor.R + 0.587 * finalPlotColor.G + 0.114 * finalPlotColor.B);
                    finalPlotColor = Color.FromArgb(gray, gray, gray);
                }

                if (rule.Screening < 100)
                {
                    double factor = rule.Screening / 100.0;
                    int r = (int)Math.Round(255 - (255 - finalPlotColor.R) * factor);
                    int g = (int)Math.Round(255 - (255 - finalPlotColor.G) * factor);
                    int b = (int)Math.Round(255 - (255 - finalPlotColor.B) * factor);
                    finalPlotColor = Color.FromArgb(r, g, b);
                }

                ctbLineweightMm = rule.LineweightMm;
            }
            else
            {
                finalPlotColor = rawObjectRgb;
            }

            try
            {
                if (ent.Transparency.IsByAlpha)
                {
                    byte alpha = ent.Transparency.Alpha;
                    if (alpha > 0 && alpha < 255)
                        finalPlotColor = Color.FromArgb(alpha, finalPlotColor.R, finalPlotColor.G, finalPlotColor.B);
                }
            }
            catch { }

            if (ctbLineweightMm > 0)
            {
                float pxFromMm = (float)(ctbLineweightMm * effectiveDpi / 25.4);
                finalPenWidth = Math.Max(basePenWidth * 0.7f, pxFromMm);
            }
            else
            {
                LineWeight lw = ent.LineWeight;
                if (lw == LineWeight.ByLayer) _layerLwCache.TryGetValue(ent.LayerId, out lw);
                int lwVal = (int)lw;
                if (lwVal > 0)
                {
                    double mm = lwVal / 100.0;
                    finalPenWidth = Math.Max(basePenWidth * 0.7f, (float)(mm * effectiveDpi / 25.4));
                }
                else
                {
                    finalPenWidth = basePenWidth;
                }
            }

            // Nội suy Kiểu nét đứt (Linetype) của Entity: Hỗ trợ ByLayer, ByBlock và Explicit Linetype
            effectiveLtScale = ent.LinetypeScale * parentBlockLtScale;
            string ltName = ent.Linetype ?? "ByLayer";
            if (string.Equals(ltName, "ByLayer", StringComparison.OrdinalIgnoreCase))
            {
                if (!_layerLinetypeCache.TryGetValue(ent.LayerId, out effectiveLinetypeId))
                    effectiveLinetypeId = ent.LinetypeId;
            }
            else if (string.Equals(ltName, "ByBlock", StringComparison.OrdinalIgnoreCase))
            {
                effectiveLinetypeId = parentBlockLinetypeId != ObjectId.Null ? parentBlockLinetypeId : ent.LinetypeId;
            }
            else
            {
                effectiveLinetypeId = ent.LinetypeId;
            }

            if (effectiveLinetypeId != ObjectId.Null && _linetypeCache.TryGetValue(effectiveLinetypeId, out CadLinetypeInfo cachedLt))
            {
                linetypeInfo = cachedLt;
            }
            else
            {
                linetypeInfo = null;
            }
        }

        // Cấu hình Pen.DashPattern để hiển thị chính xác Nét đứt trên cả Preview lẫn ảnh PNG 4K/8K
        private void ApplyCadLinetypeToPen(
            Pen pen,
            CadLinetypeInfo ltInfo,
            double entLtScale,
            double blockWorldScale,
            double worldToPixelScale,
            double effectiveDpi)
        {
            if (ltInfo == null || ltInfo.IsContinuous || ltInfo.DashLengths == null || ltInfo.DashLengths.Count < 2)
                return;

            double rawWorldPattern = ltInfo.PatternLength * _globalLtScale * entLtScale * blockWorldScale;
            double rawPxPattern = rawWorldPattern * worldToPixelScale;

            // Ngưỡng hiển thị chuẩn trên trang giấy A3 (Đảm bảo nét đứt không bao giờ quá nhỏ thành nét liền hoặc quá to)
            double minPatternPx = Math.Max(10.0, effectiveDpi * 0.14); // ~3.5 mm trên giấy
            double maxPatternPx = Math.Max(140.0, effectiveDpi * 1.5); // ~38 mm trên giấy

            double dashToPixelFactor;
            if (rawPxPattern >= minPatternPx && rawPxPattern <= maxPatternPx && ltInfo.PatternLength > 1e-6)
            {
                dashToPixelFactor = rawPxPattern / ltInfo.PatternLength;
            }
            else
            {
                // Quy đổi chuẩn theo milimet giấy in (giống PSLTSCALE = 1 của ZWCAD PlotEngine)
                double clampedScale = Math.Max(0.5, Math.Min(2.5, entLtScale));
                double targetPaperMm = 8.0 * clampedScale;
                double targetPatternPx = Math.Max(minPatternPx, Math.Min(maxPatternPx, targetPaperMm * effectiveDpi / 25.4));
                dashToPixelFactor = targetPatternPx / Math.Max(ltInfo.PatternLength, 1.0);
            }

            float safePenWidth = Math.Max(pen.Width, 1.0f);
            List<float> pattern = new List<float>();

            foreach (double d in ltInfo.DashLengths)
            {
                float segPx;
                if (Math.Abs(d) < 1e-5)
                {
                    // Chấm tròn (Dot)
                    segPx = Math.Max(safePenWidth * 1.2f, 2.0f);
                }
                else if (d > 0)
                {
                    // Vạch nét đứt (Dash)
                    segPx = Math.Max(safePenWidth * 2.2f, (float)(d * dashToPixelFactor));
                }
                else
                {
                    // Khoảng trống (Gap)
                    segPx = Math.Max(safePenWidth * 1.8f, (float)(Math.Abs(d) * dashToPixelFactor));
                }

                // Trong GDI+, đơn vị của DashPattern là bội số của Pen.Width
                pattern.Add(Math.Max(1.0f, segPx / safePenWidth));
            }

            if (pattern.Count % 2 != 0)
            {
                pattern.Add(pattern[pattern.Count - 1]);
            }

            if (pattern.Count >= 2)
            {
                pen.DashCap = DashCap.Flat;
                pen.DashPattern = pattern.ToArray();
            }
        }

        private Color AciToRgb(short aci)
        {
            switch (aci)
            {
                case 1: return Color.FromArgb(255, 0, 0);
                case 2: return Color.FromArgb(230, 195, 0);
                case 3: return Color.FromArgb(0, 205, 0);
                case 4: return Color.FromArgb(0, 210, 210);
                case 5: return Color.FromArgb(0, 0, 255);
                case 6: return Color.FromArgb(255, 0, 255);
                case 7: return Color.Black;
                case 8: return Color.FromArgb(128, 128, 128);
                case 9: return Color.FromArgb(192, 192, 192);
                default:
                    if (aci >= 250 && aci <= 255)
                    {
                        int g = 50 + (aci - 250) * 40;
                        return Color.FromArgb(Math.Min(g, 240), Math.Min(g, 240), Math.Min(g, 240));
                    }
                    if (aci >= 10 && aci <= 249)
                    {
                        int hueIdx = (aci - 10) / 10;
                        int rem = (aci - 10) % 10;
                        double hue = hueIdx * 15.0;
                        double sat = (rem % 2 == 1) ? 0.55 : 1.0;
                        double val = 1.0 - (rem / 2) * 0.14;
                        return HsvToRgb(hue, sat, val);
                    }
                    return Color.Black;
            }
        }

        private Color HsvToRgb(double h, double s, double v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            v = v * 255;
            int iv = Math.Min(255, Math.Max(0, Convert.ToInt32(v)));
            int p = Math.Min(255, Math.Max(0, Convert.ToInt32(v * (1 - s))));
            int q = Math.Min(255, Math.Max(0, Convert.ToInt32(v * (1 - f * s))));
            int t = Math.Min(255, Math.Max(0, Convert.ToInt32(v * (1 - (1 - f) * s))));

            switch (hi)
            {
                case 0: return Color.FromArgb(iv, t, p);
                case 1: return Color.FromArgb(q, iv, p);
                case 2: return Color.FromArgb(p, iv, t);
                case 3: return Color.FromArgb(p, q, iv);
                case 4: return Color.FromArgb(t, p, iv);
                default: return Color.FromArgb(iv, p, q);
            }
        }

        // ================= VẼ ĐỆ QUY ĐỐI TƯỢNG, XCLIP, NÉT ĐỨT & HATCH THEO BẢNG NÉT CTB =================
        private void RenderEntityWithCtbGdi(
            Graphics g,
            Transaction tr,
            Entity ent,
            Matrix3d transform,
            short parentBlockAci,
            Color parentBlockPlotColor,
            ObjectId parentBlockLinetypeId,
            double parentBlockLtScale,
            Func<Point3d, PointF> toScreen,
            double worldToPixelScale,
            double effectiveDpi,
            float basePenWidth,
            bool isHatchPass)
        {
            if (ent == null || !ent.Visible) return;

            ResolveEntityPlotStyleAndLinetype(
                ent, parentBlockAci, parentBlockPlotColor, parentBlockLinetypeId, parentBlockLtScale,
                effectiveDpi, basePenWidth,
                out short entAci, out Color entPlotColor, out float penWidth,
                out ObjectId entLinetypeId, out double entLtScale, out CadLinetypeInfo ltInfo);

            // 1. Duyệt đệ quy BlockReference & ÁP DỤNG VÙNG CẮT XCLIP (SPATIAL FILTER)
            if (ent is BlockReference br)
            {
                System.Drawing.Region xclipRegion = TryCreateBlockXClipRegion(tr, br, transform, toScreen);
                GraphicsState clipState = null;
                if (xclipRegion != null)
                {
                    clipState = g.Save();
                    g.SetClip(xclipRegion, CombineMode.Intersect);
                }

                try
                {
                    BlockTableRecord blkRec = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (blkRec != null)
                    {
                        Matrix3d nextMat = transform * br.BlockTransform;
                        List<ObjectId> subIds = GetOrderedEntityIds(tr, blkRec);
                        foreach (ObjectId subId in subIds)
                        {
                            Entity subEnt = tr.GetObject(subId, OpenMode.ForRead) as Entity;
                            if (subEnt != null && subEnt.Visible && !(subEnt is AttributeDefinition))
                            {
                                RenderEntityWithCtbGdi(
                                    g, tr, subEnt, nextMat,
                                    entAci, entPlotColor, entLinetypeId, entLtScale,
                                    toScreen, worldToPixelScale, effectiveDpi, basePenWidth, isHatchPass);
                            }
                        }
                    }

                    if (!isHatchPass)
                    {
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (attRef != null && attRef.Visible)
                            {
                                RenderEntityWithCtbGdi(
                                    g, tr, attRef, transform,
                                    entAci, entPlotColor, entLinetypeId, entLtScale,
                                    toScreen, worldToPixelScale, effectiveDpi, basePenWidth, false);
                            }
                        }
                    }
                }
                finally
                {
                    if (clipState != null) g.Restore(clipState);
                    if (xclipRegion != null) xclipRegion.Dispose();
                }
                return;
            }

            // 2. Lượt 1: Vẽ Hatch, Solid & Wipeout
            if (ent is Hatch hatch)
            {
                if (isHatchPass)
                {
                    RenderHatchEntity(g, tr, hatch, transform, entAci, entPlotColor, entLinetypeId, entLtScale,
                        toScreen, worldToPixelScale, effectiveDpi, penWidth);
                }
                return;
            }

            if (ent is Wipeout wipeout)
            {
                if (isHatchPass)
                {
                    try
                    {
                        Point3dCollection wpPts = new Point3dCollection();
                        Point2dCollection pts = wipeout.GetClipBoundary();
                        if (pts != null)
                        {
                            foreach (Point2d p in pts)
                            {
                                wpPts.Add(new Point3d(p.X, p.Y, 0));
                            }
                        }
                        if (wpPts.Count >= 3)
                        {
                            PointF[] sPts = new PointF[wpPts.Count];
                            for (int i = 0; i < wpPts.Count; i++)
                                sPts[i] = toScreen(wpPts[i].TransformBy(transform));
                            using (SolidBrush wb = new SolidBrush(Color.White))
                            {
                                g.FillPolygon(wb, sPts);
                            }
                        }
                    }
                    catch { }
                }
                return;
            }

            if (ent is Solid solid2d)
            {
                if (isHatchPass)
                {
                    PointF p0 = toScreen(solid2d.GetPointAt(0).TransformBy(transform));
                    PointF p1 = toScreen(solid2d.GetPointAt(1).TransformBy(transform));
                    PointF p2 = toScreen(solid2d.GetPointAt(3).TransformBy(transform));
                    PointF p3 = toScreen(solid2d.GetPointAt(2).TransformBy(transform));
                    using (SolidBrush sb = new SolidBrush(entPlotColor))
                    {
                        g.FillPolygon(sb, new[] { p0, p1, p2, p3 });
                    }
                }
                return;
            }

            if (isHatchPass) return;

            // 3. Lượt 2: Vẽ toàn bộ Nét kiến trúc, Nét đứt, Đường cong, Kích thước, Chữ
            double blockWorldScale = GetMatrixScale(transform);

            using (Pen pen = new Pen(entPlotColor, penWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                // Áp dụng kiểu Nét đứt (Linetype) vào Pen
                ApplyCadLinetypeToPen(pen, ltInfo, entLtScale, blockWorldScale, worldToPixelScale, effectiveDpi);

                if (ent is Line line)
                {
                    PointF p1 = toScreen(line.StartPoint.TransformBy(transform));
                    PointF p2 = toScreen(line.EndPoint.TransformBy(transform));
                    g.DrawLine(pen, p1, p2);
                }
                else if (ent is Polyline pline)
                {
                    PointF[] pts = SamplePolylineWithBulges(pline, transform, toScreen);
                    if (pts.Length > 1)
                    {
                        float plineWidth = penWidth;
                        try
                        {
                            if (pline.ConstantWidth > 0)
                            {
                                plineWidth = Math.Max(penWidth, (float)(pline.ConstantWidth * blockWorldScale * worldToPixelScale));
                            }
                        }
                        catch { }

                        using (Pen plPen = new Pen(entPlotColor, Math.Min(plineWidth, 25f)))
                        {
                            plPen.LineJoin = LineJoin.Round;
                            // Quan trọng: Áp dụng đầy đủ Nét đứt (Linetype) cho cả Polyline
                            ApplyCadLinetypeToPen(plPen, ltInfo, entLtScale, blockWorldScale, worldToPixelScale, effectiveDpi);
                            g.DrawLines(plPen, pts);
                        }
                    }
                }
                else if (ent is Circle circle)
                {
                    int segs = 48;
                    PointF[] pts = new PointF[segs + 1];
                    for (int i = 0; i <= segs; i++)
                    {
                        double ang = 2.0 * Math.PI * i / segs;
                        pts[i] = toScreen(circle.GetPointAtParameter(ang).TransformBy(transform));
                    }
                    g.DrawLines(pen, pts);
                }
                else if (ent is Arc arc)
                {
                    int segs = 32;
                    PointF[] pts = new PointF[segs + 1];
                    double sParam = arc.StartParam;
                    double eParam = arc.EndParam;
                    if (eParam < sParam) eParam += 2.0 * Math.PI;
                    for (int i = 0; i <= segs; i++)
                    {
                        double param = sParam + (eParam - sParam) * i / segs;
                        pts[i] = toScreen(arc.GetPointAtParameter(param).TransformBy(transform));
                    }
                    g.DrawLines(pen, pts);
                }
                else if (ent is DBText dbText)
                {
                    DrawCadText(g, dbText.TextString, dbText.Position, dbText.Height, dbText.Rotation, transform, toScreen, entPlotColor);
                }
                else if (ent is MText mText)
                {
                    DrawCadText(g, mText.Text, mText.Location, mText.TextHeight, mText.Rotation, transform, toScreen, entPlotColor);
                }
                else if (ent is Curve genericCurve)
                {
                    try
                    {
                        double sParam = genericCurve.StartParam;
                        double eParam = genericCurve.EndParam;
                        int segs = 40;
                        List<PointF> pts = new List<PointF>(segs + 1);
                        for (int i = 0; i <= segs; i++)
                        {
                            double t = sParam + (eParam - sParam) * i / segs;
                            pts.Add(toScreen(genericCurve.GetPointAtParameter(t).TransformBy(transform)));
                        }
                        if (pts.Count > 1) g.DrawLines(pen, pts.ToArray());
                    }
                    catch { }
                }
                else if (ent is Dimension || ent is Leader || ent is MLeader)
                {
                    try
                    {
                        using (DBObjectCollection exploded = new DBObjectCollection())
                        {
                            ent.Explode(exploded);
                            foreach (DBObject obj in exploded)
                            {
                                if (obj is Entity subEnt)
                                {
                                    RenderEntityWithCtbGdi(
                                        g, tr, subEnt, transform,
                                        entAci, entPlotColor, entLinetypeId, entLtScale,
                                        toScreen, worldToPixelScale, effectiveDpi, basePenWidth, false);
                                }
                                obj.Dispose();
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void RenderHatchEntity(
            Graphics g,
            Transaction tr,
            Hatch hatch,
            Matrix3d transform,
            short hatchAci,
            Color hatchPlotColor,
            ObjectId hatchLinetypeId,
            double hatchLtScale,
            Func<Point3d, PointF> toScreen,
            double worldToPixelScale,
            double effectiveDpi,
            float penWidth)
        {
            bool isSolid = hatch.IsSolidFill ||
                           hatch.IsGradient ||
                           string.Equals(hatch.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase);

            using (GraphicsPath path = BuildHatchGraphicsPath(hatch, transform, toScreen))
            {
                if (path != null && path.PointCount > 2)
                {
                    if (isSolid)
                    {
                        using (SolidBrush brush = new SolidBrush(hatchPlotColor))
                        {
                            g.FillPath(brush, path);
                        }
                        return;
                    }
                    // ĐÃ KHẮC PHỤC LỖI CS1061 TRÊN ZWCAD API (Thay thế hatch.UseBackgroundColor)
                    else if (hatch.BackgroundColor != null && hatch.BackgroundColor.ColorMethod != ColorMethod.None)
                    {
                        CadColor bgCad = hatch.BackgroundColor;
                        Color bgCol = (bgCad.ColorMethod == ColorMethod.ByColor)
                            ? Color.FromArgb(bgCad.Red, bgCad.Green, bgCad.Blue)
                            : AciToRgb(bgCad.ColorIndex);
                        using (SolidBrush bgBrush = new SolidBrush(bgCol))
                        {
                            g.FillPath(bgBrush, path);
                        }
                    }
                }
            }

            if (!isSolid)
            {
                try
                {
                    using (DBObjectCollection exploded = new DBObjectCollection())
                    {
                        hatch.Explode(exploded);
                        if (exploded.Count > 0 && exploded.Count <= 25000)
                        {
                            using (Pen patPen = new Pen(hatchPlotColor, Math.Max(0.8f, penWidth * 0.85f)))
                            {
                                foreach (DBObject obj in exploded)
                                {
                                    if (obj is Line ln)
                                    {
                                        PointF p1 = toScreen(ln.StartPoint.TransformBy(transform));
                                        PointF p2 = toScreen(ln.EndPoint.TransformBy(transform));
                                        g.DrawLine(patPen, p1, p2);
                                    }
                                    else if (obj is Entity subEnt)
                                    {
                                        RenderEntityWithCtbGdi(
                                            g, tr, subEnt, transform,
                                            hatchAci, hatchPlotColor, hatchLinetypeId, hatchLtScale,
                                            toScreen, worldToPixelScale, effectiveDpi, penWidth * 0.85f, false);
                                    }
                                    obj.Dispose();
                                }
                            }
                        }
                        else if (exploded.Count > 25000)
                        {
                            foreach (DBObject obj in exploded) obj.Dispose();
                            using (GraphicsPath path = BuildHatchGraphicsPath(hatch, transform, toScreen))
                            {
                                if (path != null && path.PointCount > 2)
                                {
                                    using (SolidBrush denseBrush = new SolidBrush(Color.FromArgb(190, hatchPlotColor.R, hatchPlotColor.G, hatchPlotColor.B)))
                                    {
                                        g.FillPath(denseBrush, path);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private GraphicsPath BuildHatchGraphicsPath(Hatch hatch, Matrix3d transform, Func<Point3d, PointF> toScreen)
        {
            GraphicsPath path = new GraphicsPath(FillMode.Alternate);
            Matrix3d ocsToWcs = Matrix3d.PlaneToWorld(hatch.Normal);
            double elev = hatch.Elevation;

            int numLoops = 0;
            try { numLoops = hatch.NumberOfLoops; }
            catch { return path; }

            for (int i = 0; i < numLoops; i++)
            {
                try
                {
                    HatchLoop loop = hatch.GetLoopAt(i);
                    List<PointF> loopPts = new List<PointF>();

                    if (loop.IsPolyline)
                    {
                        BulgeVertexCollection bvc = loop.Polyline;
                        int count = bvc.Count;
                        for (int k = 0; k < count; k++)
                        {
                            Point2d pStart = bvc[k].Vertex;
                            Point2d pEnd = bvc[(k + 1) % count].Vertex;
                            double bulge = bvc[k].Bulge;
                            AddTessellatedSegment2d(loopPts, pStart, pEnd, bulge, elev, ocsToWcs, transform, toScreen);
                        }
                    }
                    else
                    {
                        Curve2dCollection curves = loop.Curves;
                        foreach (Curve2d c2d in curves)
                        {
                            if (c2d is LineSegment2d lineSeg)
                            {
                                Point3d wPt1 = new Point3d(lineSeg.StartPoint.X, lineSeg.StartPoint.Y, elev).TransformBy(ocsToWcs).TransformBy(transform);
                                Point3d wPt2 = new Point3d(lineSeg.EndPoint.X, lineSeg.EndPoint.Y, elev).TransformBy(ocsToWcs).TransformBy(transform);
                                loopPts.Add(toScreen(wPt1));
                                loopPts.Add(toScreen(wPt2));
                            }
                            else
                            {
                                try
                                {
                                    Interval iv = c2d.GetInterval();
                                    int segs = 18;
                                    for (int s = 0; s <= segs; s++)
                                    {
                                        double param = iv.LowerBound + (iv.UpperBound - iv.LowerBound) * s / segs;
                                        Point2d pt2d = c2d.EvaluatePoint(param);
                                        Point3d wPt = new Point3d(pt2d.X, pt2d.Y, elev).TransformBy(ocsToWcs).TransformBy(transform);
                                        loopPts.Add(toScreen(wPt));
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    if (loopPts.Count >= 3)
                    {
                        path.StartFigure();
                        path.AddPolygon(loopPts.ToArray());
                        path.CloseFigure();
                    }
                }
                catch { }
            }

            return path;
        }

        private void AddTessellatedSegment2d(
            List<PointF> outPts,
            Point2d p1,
            Point2d p2,
            double bulge,
            double elev,
            Matrix3d ocsToWcs,
            Matrix3d transform,
            Func<Point3d, PointF> toScreen)
        {
            Point3d w1 = new Point3d(p1.X, p1.Y, elev).TransformBy(ocsToWcs).TransformBy(transform);
            outPts.Add(toScreen(w1));

            if (Math.Abs(bulge) > 1e-4 && (p2 - p1).Length > 1e-6)
            {
                double chord = (p2 - p1).Length;
                double theta = 4.0 * Math.Atan(bulge);
                double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) * 0.5));
                Vector2d dir = (p2 - p1).GetNormal();
                Vector2d normal = bulge > 0 ? new Vector2d(-dir.Y, dir.X) : new Vector2d(dir.Y, -dir.X);
                Point2d mid = p1 + (p2 - p1) * 0.5;
                double distToCenter = radius * Math.Cos(Math.Abs(theta) * 0.5);
                Point2d center = mid + normal * (Math.Abs(bulge) <= 1.0 ? distToCenter : -distToCenter);

                double startAng = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
                int arcSteps = 12;
                for (int s = 1; s < arcSteps; s++)
                {
                    double ang = startAng + theta * s / arcSteps;
                    Point2d arcPt = new Point2d(center.X + radius * Math.Cos(ang), center.Y + radius * Math.Sin(ang));
                    Point3d wArc = new Point3d(arcPt.X, arcPt.Y, elev).TransformBy(ocsToWcs).TransformBy(transform);
                    outPts.Add(toScreen(wArc));
                }
            }
        }

        private PointF[] SamplePolylineWithBulges(Polyline pline, Matrix3d transform, Func<Point3d, PointF> toScreen)
        {
            int vn = pline.NumberOfVertices;
            if (vn < 2) return Array.Empty<PointF>();

            List<PointF> pts = new List<PointF>(vn + 4);
            Matrix3d ocsToWcs = Matrix3d.PlaneToWorld(pline.Normal);
            double elev = pline.Elevation;

            int segCount = pline.Closed ? vn : vn - 1;
            for (int i = 0; i < segCount; i++)
            {
                Point2d p1 = pline.GetPoint2dAt(i);
                Point2d p2 = pline.GetPoint2dAt((i + 1) % vn);
                double bulge = pline.GetBulgeAt(i);
                AddTessellatedSegment2d(pts, p1, p2, bulge, elev, ocsToWcs, transform, toScreen);
            }

            Point3d lastPt = pline.GetPoint3dAt(pline.Closed ? 0 : vn - 1).TransformBy(transform);
            pts.Add(toScreen(lastPt));

            return pts.ToArray();
        }

        private void DrawCadText(
            Graphics g,
            string rawText,
            Point3d position,
            double height,
            double rotationRad,
            Matrix3d transform,
            Func<Point3d, PointF> toScreen,
            Color color)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return;

            PointF pt = toScreen(position.TransformBy(transform));
            PointF ptTop = toScreen((position + new Vector3d(0, height, 0)).TransformBy(transform));
            float fontPx = Math.Abs(pt.Y - ptTop.Y);
            if (fontPx < 2.2f) return;

            string cleanText = Regex.Replace(rawText, @"\\[A-Za-z][^;]*;", "");
            cleanText = cleanText.Replace("\\P", "\n").Replace("{", "").Replace("}", "");

            using (System.Drawing.Font f = new System.Drawing.Font("Arial", Math.Min(fontPx, 80f), FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush br = new SolidBrush(color))
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(pt.X, pt.Y);
                float angleDeg = (float)(-rotationRad * 180.0 / Math.PI);
                if (Math.Abs(angleDeg) > 0.5f) g.RotateTransform(angleDeg);
                g.DrawString(cleanText, f, br, 0, -fontPx);
                g.Restore(state);
            }
        }

        // ================= CẤU HÌNH PLOTSETTINGS CHUẨN CHO ZWCAD =================
        private string GetSelectedCtbFileName()
        {
            if (cboCtb.SelectedItem == null) return null;
            string s = cboCtb.SelectedItem.ToString();
            if (s.StartsWith("(None", StringComparison.OrdinalIgnoreCase)) return null;
            return s;
        }

        private Extents2d ConvertWcsToDcs(Extents3d wcsExtents)
        {
            using (ViewTableRecord vtr = _ed.GetCurrentView())
            {
                Matrix3d matWcs2Dcs = Matrix3d.PlaneToWorld(vtr.ViewDirection);
                matWcs2Dcs = Matrix3d.Displacement(vtr.Target - Point3d.Origin) * matWcs2Dcs;
                matWcs2Dcs = Matrix3d.Rotation(-vtr.ViewTwist, vtr.ViewDirection, vtr.Target) * matWcs2Dcs;
                matWcs2Dcs = matWcs2Dcs.Inverse();

                Point3d p1 = wcsExtents.MinPoint.TransformBy(matWcs2Dcs);
                Point3d p2 = wcsExtents.MaxPoint.TransformBy(matWcs2Dcs);

                return new Extents2d(
                    Math.Min(p1.X, p2.X),
                    Math.Min(p1.Y, p2.Y),
                    Math.Max(p1.X, p2.X),
                    Math.Max(p1.Y, p2.Y)
                );
            }
        }

        private PlotSettings ConfigurePlotSettings(Layout layout, FrameItem item, string device, string media, string ctb)
        {
            PlotSettings ps = new PlotSettings(layout.ModelType);
            ps.CopyFrom(layout);

            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            psv.SetPlotConfigurationName(ps, device, media);
            psv.RefreshLists(ps);

            if (!string.IsNullOrEmpty(ctb))
            {
                ps.PlotPlotStyles = true;
                psv.SetCurrentStyleSheet(ps, ctb);
            }
            else
            {
                ps.PlotPlotStyles = false;
            }

            Extents2d windowDcs = ConvertWcsToDcs(item.ExtentsWcs);
            psv.SetPlotWindowArea(ps, windowDcs);
            psv.SetPlotType(ps, ZwSoft.ZwCAD.DatabaseServices.PlotType.Window);

            bool isRasterPixelDevice = device.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       device.IndexOf("JPG", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isRasterPixelDevice)
            {
                try { psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters); }
                catch { }
            }

            if (radFit.Checked || isRasterPixelDevice)
            {
                psv.SetUseStandardScale(ps, true);
                psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
            }
            else
            {
                psv.SetUseStandardScale(ps, false);
                psv.SetCustomPrintScale(ps, new CustomScale(1.0, (double)numScaleDenominator.Value));
            }

            psv.SetPlotRotation(ps, item.IsLandscape ? PlotRotation.Degrees090 : PlotRotation.Degrees000);

            if (chkCenterPlot.Checked)
            {
                psv.SetPlotCentered(ps, true);
            }
            else
            {
                psv.SetPlotCentered(ps, false);
                psv.SetPlotOrigin(ps, new Point2d((double)numOffsetX.Value, (double)numOffsetY.Value));
            }

            return ps;
        }

        // ================= KẾT XUẤT PDF GỐC CỦA ZWCAD SANG PNG 4K/8K BẰNG WINDOWS NATIVE PDF RASTERIZER =================
        private static object WaitWinRtAsyncOperation(object asyncOp)
        {
            if (asyncOp == null) return null;
            Type opType = asyncOp.GetType();
            PropertyInfo statusProp = null;
            MethodInfo getResultsMethod = null;

            foreach (Type iface in opType.GetInterfaces())
            {
                if (statusProp == null) statusProp = iface.GetProperty("Status");
                if (getResultsMethod == null) getResultsMethod = iface.GetMethod("GetResults", Type.EmptyTypes);
            }
            if (statusProp == null) statusProp = opType.GetProperty("Status");
            if (getResultsMethod == null) getResultsMethod = opType.GetMethod("GetResults", Type.EmptyTypes);

            int waitLoops = 0;
            while (statusProp != null && Convert.ToInt32(statusProp.GetValue(asyncOp, null)) == 0 && waitLoops < 1500)
            {
                Thread.Sleep(10);
                waitLoops++;
            }

            return getResultsMethod?.Invoke(asyncOp, null);
        }

        private bool TryConvertPdfPageToPngWinRt(string pdfPath, string pngPath, int targetLongEdgePx, int targetShortEdgePx, bool isLandscape)
        {
            try
            {
                if (!File.Exists(pdfPath)) return false;

                Type storageFileType = Type.GetType("Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime");
                Type pdfDocType = Type.GetType("Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType=WindowsRuntime");
                Type renderOptsType = Type.GetType("Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType=WindowsRuntime");
                Type memStreamType = Type.GetType("Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime");
                Type dataReaderType = Type.GetType("Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType=WindowsRuntime");

                if (storageFileType == null || pdfDocType == null || renderOptsType == null || memStreamType == null || dataReaderType == null)
                    return false;

                MethodInfo getFileMethod = storageFileType.GetMethod("GetFileFromPathAsync", new[] { typeof(string) });
                object storageFile = WaitWinRtAsyncOperation(getFileMethod.Invoke(null, new object[] { Path.GetFullPath(pdfPath) }));
                if (storageFile == null) return false;

                MethodInfo loadPdfMethod = pdfDocType.GetMethods().FirstOrDefault(m => m.Name == "LoadFromFileAsync" && m.GetParameters().Length == 1);
                object pdfDoc = WaitWinRtAsyncOperation(loadPdfMethod.Invoke(null, new object[] { storageFile }));
                if (pdfDoc == null) return false;

                MethodInfo getPageMethod = pdfDocType.GetMethod("GetPage", new[] { typeof(uint) });
                object pdfPage = getPageMethod.Invoke(pdfDoc, new object[] { 0u });
                if (pdfPage == null) return false;

                try
                {
                    object renderOpts = Activator.CreateInstance(renderOptsType);
                    uint destW = (uint)(isLandscape ? targetLongEdgePx : targetShortEdgePx);
                    uint destH = (uint)(isLandscape ? targetShortEdgePx : targetLongEdgePx);
                    renderOptsType.GetProperty("DestinationWidth")?.SetValue(renderOpts, destW, null);
                    renderOptsType.GetProperty("DestinationHeight")?.SetValue(renderOpts, destH, null);

                    object memStream = Activator.CreateInstance(memStreamType);
                    try
                    {
                        MethodInfo renderMethod = pdfPage.GetType().GetMethods()
                            .FirstOrDefault(m => m.Name == "RenderToStreamAsync" && m.GetParameters().Length == 2);
                        WaitWinRtAsyncOperation(renderMethod.Invoke(pdfPage, new object[] { memStream, renderOpts }));

                        ulong streamSize = Convert.ToUInt64(memStreamType.GetProperty("Size").GetValue(memStream, null));
                        if (streamSize == 0) return false;

                        MethodInfo getInputMethod = memStreamType.GetMethod("GetInputStreamAt", new[] { typeof(ulong) });
                        object inputStream = getInputMethod.Invoke(memStream, new object[] { 0UL });

                        ConstructorInfo drCtor = dataReaderType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 1);
                        object dataReader = drCtor.Invoke(new object[] { inputStream });
                        MethodInfo loadAsyncMethod = dataReaderType.GetMethod("LoadAsync", new[] { typeof(uint) });
                        WaitWinRtAsyncOperation(loadAsyncMethod.Invoke(dataReader, new object[] { (uint)streamSize }));

                        byte[] rawPngBytes = new byte[streamSize];
                        MethodInfo readBytesMethod = dataReaderType.GetMethod("ReadBytes", new[] { typeof(byte[]) });
                        readBytesMethod.Invoke(dataReader, new object[] { rawPngBytes });

                        // Đảm bảo nền giấy trắng tuyệt đối trên file PNG xuất ra
                        using (MemoryStream ms = new MemoryStream(rawPngBytes))
                        using (Bitmap rawBmp = new Bitmap(ms))
                        using (Bitmap finalBmp = new Bitmap(rawBmp.Width, rawBmp.Height, PixelFormat.Format24bppRgb))
                        using (Graphics g = Graphics.FromImage(finalBmp))
                        {
                            g.Clear(Color.White);
                            g.DrawImage(rawBmp, 0, 0, rawBmp.Width, rawBmp.Height);
                            finalBmp.Save(pngPath, ImageFormat.Png);
                        }
                        return File.Exists(pngPath);
                    }
                    finally
                    {
                        (memStream as IDisposable)?.Dispose();
                    }
                }
                finally
                {
                    (pdfPage as IDisposable)?.Dispose();
                }
            }
            catch
            {
                return false;
            }
        }

        private bool PlotSingleSheetPdfViaEngine(Layout layout, FrameItem item, string device, string media, string ctb, string pdfPath)
        {
            try
            {
                if (File.Exists(pdfPath)) File.Delete(pdfPath);

                using (PlotSettings ps = ConfigurePlotSettings(layout, item, device, media, ctb))
                using (PlotInfo pi = new PlotInfo { Layout = layout.ObjectId, OverrideSettings = ps })
                using (PlotInfoValidator piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled })
                {
                    piv.Validate(pi);

                    if (PlotFactory.ProcessPlotState == ProcessPlotState.NotPlotting)
                    {
                        using (PlotEngine pe = PlotFactory.CreatePublishEngine())
                        using (PlotProgressDialog ppd = new PlotProgressDialog(false, 1, true))
                        {
                            ppd.LowerPlotProgressRange = 0;
                            ppd.UpperPlotProgressRange = 100;
                            ppd.IsVisible = false;

                            pe.BeginPlot(ppd, null);
                            pe.BeginDocument(pi, _doc.Name, null, 1, true, pdfPath);

                            using (PlotPageInfo ppi = new PlotPageInfo())
                            {
                                pe.BeginPage(ppi, pi, true, null);
                                pe.BeginGenerateGraphics(null);
                                pe.EndGenerateGraphics(null);
                                pe.EndPage(null);
                            }

                            pe.EndDocument(null);
                            pe.EndPlot(null);
                            return File.Exists(pdfPath);
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        // ================= BỘ TẠO FILE PDF NHIỀU TRANG (COMBINE MULTI-PAGE PDF) =================
        private void BuildCombinedMultiPagePdfFromImages(List<string> imageFiles, List<FrameItem> frames, string combinedPdfPath)
        {
            using (FileStream fs = new FileStream(combinedPdfPath, FileMode.Create, FileAccess.Write))
            {
                List<long> objOffsets = new List<long> { 0 };
                Action<string> writeAscii = (s) =>
                {
                    byte[] b = Encoding.ASCII.GetBytes(s);
                    fs.Write(b, 0, b.Length);
                };

                writeAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

                int pageCount = imageFiles.Count;
                objOffsets.Add(fs.Position);
                writeAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                StringBuilder kidsSb = new StringBuilder("[");
                for (int i = 0; i < pageCount; i++) kidsSb.Append($"{3 + i * 3} 0 R ");
                kidsSb.Append("]");

                objOffsets.Add(fs.Position);
                writeAscii($"2 0 obj\n<< /Type /Pages /Count {pageCount} /Kids {kidsSb} >>\nendobj\n");

                ImageCodecInfo jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                EncoderParameters encParams = new EncoderParameters(1);
                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 96L);

                for (int i = 0; i < pageCount; i++)
                {
                    int pageObjId = 3 + i * 3;
                    int contentObjId = 4 + i * 3;
                    int imgObjId = 5 + i * 3;

                    bool isLandscape = frames[i].IsLandscape;
                    double pageW = isLandscape ? 1190.55 : 841.89;
                    double pageH = isLandscape ? 841.89 : 1190.55;

                    byte[] jpgBytes;
                    int pxW, pxH;
                    using (Bitmap bmp = new Bitmap(imageFiles[i]))
                    {
                        pxW = bmp.Width;
                        pxH = bmp.Height;
                        using (MemoryStream ms = new MemoryStream())
                        {
                            bmp.Save(ms, jpegEncoder, encParams);
                            jpgBytes = ms.ToArray();
                        }
                    }

                    objOffsets.Add(fs.Position);
                    writeAscii(string.Format(CultureInfo.InvariantCulture,
                        "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1:F2} {2:F2}] /Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>\nendobj\n",
                        pageObjId, pageW, pageH, imgObjId, contentObjId));

                    string contentStream = string.Format(CultureInfo.InvariantCulture, "q {0:F2} 0 0 {1:F2} 0 0 cm /Im0 Do Q\n", pageW, pageH);
                    objOffsets.Add(fs.Position);
                    writeAscii($"{contentObjId} 0 obj\n<< /Length {contentStream.Length} >>\nstream\n{contentStream}endstream\nendobj\n");

                    objOffsets.Add(fs.Position);
                    writeAscii($"{imgObjId} 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpgBytes.Length} >>\nstream\n");
                    fs.Write(jpgBytes, 0, jpgBytes.Length);
                    writeAscii("\nendstream\nendobj\n");
                }

                long xrefPos = fs.Position;
                int totalObjs = objOffsets.Count;
                writeAscii($"xref\n0 {totalObjs}\n0000000000 65535 f \n");
                for (int i = 1; i < totalObjs; i++)
                {
                    writeAscii($"{objOffsets[i]:D10} 00000 n \n");
                }

                writeAscii($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
            }
        }

        // ================= THỰC THI XUẤT BẢN VẼ HÀNG LOẠT (PDF / PNG / COMBINE) =================
        private void ExecuteBatchExport()
        {
            if (_frames.Count == 0)
            {
                MessageBox.Show("Vui lòng quét chọn ít nhất 1 khung bản vẽ trước khi xuất!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveCurrentUiToMemory();

            if (!Directory.Exists(txtOutputDir.Text))
                Directory.CreateDirectory(txtOutputDir.Text);

            bool exportPng = (cboExportFormat.SelectedIndex == 0 || cboExportFormat.SelectedIndex == 2);
            bool exportPdf = (cboExportFormat.SelectedIndex == 1 || cboExportFormat.SelectedIndex == 2);

            bool combineFiles = (cboCombineMode.SelectedIndex == 0 || cboCombineMode.SelectedIndex == 2);
            bool keepIndividualFiles = (cboCombineMode.SelectedIndex == 1 || cboCombineMode.SelectedIndex == 2);

            int pngLongEdge = 4960, pngShortEdge = 3508; // 300 DPI (4K)
            if (cboPngDpi.SelectedIndex == 0) { pngLongEdge = 2480; pngShortEdge = 1754; }
            else if (cboPngDpi.SelectedIndex == 2) { pngLongEdge = 9920; pngShortEdge = 7016; }

            string device = cboDevice.SelectedItem.ToString();
            string media = cboMedia.SelectedItem.ToString();
            string ctb = GetSelectedCtbFileName();

            short oldBgPlot = Convert.ToInt16(Application.GetSystemVariable("BACKGROUNDPLOT"));
            Application.SetSystemVariable("BACKGROUNDPLOT", 0);

            progressBar.Minimum = 0;
            progressBar.Maximum = _frames.Count;
            progressBar.Value = 0;

            List<string> tempRenderedPngs = new List<string>();
            int singlePdfCount = 0, singlePngCount = 0;
            bool combinedPdfSuccess = false;

            try
            {
                using (DocumentLock docLock = _doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    LayoutManager lm = LayoutManager.Current;
                    Layout layout = (Layout)tr.GetObject(lm.GetLayoutId(lm.CurrentLayout), OpenMode.ForRead);

                    string combinedPdfPath = Path.Combine(txtOutputDir.Text, $"{txtPrefix.Text}_Combined.pdf");
                    if (exportPdf && combineFiles)
                    {
                        if (File.Exists(combinedPdfPath)) File.Delete(combinedPdfPath);
                        combinedPdfSuccess = TryNativeMultiSheetPdfPlot(layout, device, media, ctb, combinedPdfPath);
                    }

                    for (int i = 0; i < _frames.Count; i++)
                    {
                        FrameItem item = _frames[i];
                        string baseName = $"{txtPrefix.Text}_{item.Index:D2}";

                        lblStatus.Text = $"Đang xuất bản vẽ {item.Index}/{_frames.Count}: {baseName}...";
                        System.Windows.Forms.Application.DoEvents();

                        string pdfPath = Path.Combine(txtOutputDir.Text, baseName + ".pdf");
                        string pngPath = Path.Combine(txtOutputDir.Text, baseName + ".png");
                        bool plottedNativePdf = false;

                        // 1. Tạo PDF qua ZWCAD PlotEngine (Khi cần xuất PDF rời hoặc làm nguồn chuẩn 100% cho PNG)
                        if ((exportPdf && keepIndividualFiles) || exportPng)
                        {
                            plottedNativePdf = PlotSingleSheetPdfViaEngine(layout, item, device, media, ctb, pdfPath);
                            if (plottedNativePdf && exportPdf && keepIndividualFiles)
                                singlePdfCount++;
                        }

                        // 2. Tạo PNG (Ưu tiên kết xuất trực tiếp từ bản PlotEngine để khớp 100% Preview, dự phòng bằng GDI+ v5.0 có đủ XCLIP + Nét đứt)
                        bool needRasterRender = exportPng || (exportPdf && combineFiles && !combinedPdfSuccess);
                        if (needRasterRender)
                        {
                            if (File.Exists(pngPath)) File.Delete(pngPath);

                            bool pngCreatedFromNativePdf = false;
                            if (plottedNativePdf && File.Exists(pdfPath))
                            {
                                pngCreatedFromNativePdf = TryConvertPdfPageToPngWinRt(pdfPath, pngPath, pngLongEdge, pngShortEdge, item.IsLandscape);
                            }

                            if (!pngCreatedFromNativePdf)
                            {
                                using (Bitmap hiResBmp = RenderFrameWithCtbToBitmap(item, pngLongEdge, pngShortEdge, false))
                                {
                                    hiResBmp.Save(pngPath, ImageFormat.Png);
                                }
                            }

                            if (File.Exists(pngPath))
                            {
                                tempRenderedPngs.Add(pngPath);
                                if (exportPng && keepIndividualFiles) singlePngCount++;
                            }
                        }

                        // Nếu người dùng chỉ chọn xuất PNG (không giữ PDF lẻ) thì xóa file PDF trung gian
                        if (plottedNativePdf && !(exportPdf && keepIndividualFiles))
                        {
                            try { if (File.Exists(pdfPath)) File.Delete(pdfPath); } catch { }
                        }

                        progressBar.Value = i + 1;
                    }

                    // 3. Nếu chọn Combine PDF và cần gộp các trang thành 1 file PDF nhiều trang
                    if (combineFiles && tempRenderedPngs.Count > 0 && !combinedPdfSuccess)
                    {
                        lblStatus.Text = "Đang gộp tất cả bản vẽ thành 1 file PDF nhiều trang (_Combined.pdf)...";
                        System.Windows.Forms.Application.DoEvents();
                        BuildCombinedMultiPagePdfFromImages(tempRenderedPngs, _frames, combinedPdfPath);
                        combinedPdfSuccess = true;
                    }

                    // 4. Nếu chọn Combine cho PNG -> Ghép thêm 1 file PNG tổng hợp (_Combined.png)
                    if (exportPng && combineFiles && tempRenderedPngs.Count > 1)
                    {
                        string combinedPngPath = Path.Combine(txtOutputDir.Text, $"{txtPrefix.Text}_Combined.png");
                        StitchMultiplePngsVertically(tempRenderedPngs, combinedPngPath);

                        if (!keepIndividualFiles)
                        {
                            foreach (string tmp in tempRenderedPngs)
                            {
                                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                            }
                        }
                    }
                    else if (!exportPng && tempRenderedPngs.Count > 0)
                    {
                        foreach (string tmp in tempRenderedPngs)
                        {
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                        }
                    }

                    tr.Commit();
                }

                string combineMsg = combineFiles ? $"Đã gộp file '{txtPrefix.Text}_Combined' thành công! " : "";
                lblStatus.Text = $"Hoàn tất! {combineMsg}(PDF rời: {singlePdfCount}, PNG rời: {singlePngCount})";

                if (chkOpenFolder.Checked && Directory.Exists(txtOutputDir.Text))
                {
                    System.Diagnostics.Process.Start("explorer.exe", txtOutputDir.Text);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Lỗi trong quá trình xuất bản vẽ: {ex.Message}", "Lỗi ZWCAD Batch Plotter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Application.SetSystemVariable("BACKGROUNDPLOT", oldBgPlot);
            }
        }

        private bool TryNativeMultiSheetPdfPlot(Layout layout, string device, string media, string ctb, string combinedPdfPath)
        {
            if (_frames.Count == 0 || PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                return false;

            List<PlotSettings> settingsList = new List<PlotSettings>();
            List<PlotInfo> infoList = new List<PlotInfo>();

            try
            {
                PlotInfoValidator piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
                for (int i = 0; i < _frames.Count; i++)
                {
                    PlotSettings ps = ConfigurePlotSettings(layout, _frames[i], device, media, ctb);
                    PlotInfo pi = new PlotInfo { Layout = layout.ObjectId, OverrideSettings = ps };
                    piv.Validate(pi);
                    settingsList.Add(ps);
                    infoList.Add(pi);
                }

                using (PlotEngine pe = PlotFactory.CreatePublishEngine())
                using (PlotProgressDialog ppd = new PlotProgressDialog(false, _frames.Count, true))
                {
                    ppd.LowerPlotProgressRange = 0;
                    ppd.UpperPlotProgressRange = 100;
                    ppd.IsVisible = false;

                    pe.BeginPlot(ppd, null);
                    pe.BeginDocument(infoList[0], _doc.Name, null, 1, true, combinedPdfPath);

                    for (int i = 0; i < _frames.Count; i++)
                    {
                        ppd.OnBeginSheet();
                        using (PlotPageInfo ppi = new PlotPageInfo())
                        {
                            bool isLastPage = (i == _frames.Count - 1);
                            pe.BeginPage(ppi, infoList[i], isLastPage, null);
                            pe.BeginGenerateGraphics(null);
                            pe.EndGenerateGraphics(null);
                            pe.EndPage(null);
                        }
                        ppd.OnEndSheet();
                    }

                    pe.EndDocument(null);
                    pe.EndPlot(null);
                }

                if (File.Exists(combinedPdfPath))
                {
                    string rawPdf = File.ReadAllText(combinedPdfPath, Encoding.Default);
                    int pageMatches = Regex.Matches(rawPdf, @"/Type\s*/Page\b").Count;
                    if (pageMatches >= _frames.Count)
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (var pi in infoList) pi.Dispose();
                foreach (var ps in settingsList) ps.Dispose();
            }
        }

        private void StitchMultiplePngsVertically(List<string> pngPaths, string outputCombinedPng)
        {
            List<Bitmap> bitmaps = new List<Bitmap>();
            try
            {
                int maxWidth = 0;
                int totalHeight = 0;
                int spacing = 24;

                foreach (string p in pngPaths)
                {
                    Bitmap b = new Bitmap(p);
                    bitmaps.Add(b);
                    if (b.Width > maxWidth) maxWidth = b.Width;
                    totalHeight += b.Height;
                }
                totalHeight += spacing * (bitmaps.Count - 1);

                double scaleDown = 1.0;
                if (totalHeight > 24000)
                {
                    scaleDown = 24000.0 / totalHeight;
                    maxWidth = (int)(maxWidth * scaleDown);
                    totalHeight = 24000;
                    spacing = (int)(spacing * scaleDown);
                }

                using (Bitmap combined = new Bitmap(maxWidth, totalHeight, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(combined))
                {
                    g.Clear(Color.FromArgb(230, 230, 230));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    int currentY = 0;
                    foreach (Bitmap b in bitmaps)
                    {
                        int drawW = (int)(b.Width * scaleDown);
                        int drawH = (int)(b.Height * scaleDown);
                        int drawX = (maxWidth - drawW) / 2;
                        g.DrawImage(b, new Rectangle(drawX, currentY, drawW, drawH));
                        currentY += drawH + spacing;
                    }

                    combined.Save(outputCombinedPng, ImageFormat.Png);
                }
            }
            catch { }
            finally
            {
                foreach (Bitmap b in bitmaps) b.Dispose();
            }
        }
    }
}