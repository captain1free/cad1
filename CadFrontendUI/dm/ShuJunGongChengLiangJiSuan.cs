// =========================================================================
// ShuJunGongChengLiangJiSuan.cs — 疏浚工程量计算系统（工业级全栈最终版）
// 修复：重构断面大样图排版矩阵，完美复刻测绘院标准（十字中轴对齐、图幅彻底防重叠）
// 备注：全量保留了现有的 UI、P/Invoke、智能凹角设计等所有核心架构代码
// =========================================================================

#if AUTOCAD
using CADAppServices = Autodesk.AutoCAD.ApplicationServices;
using CADDbServices = Autodesk.AutoCAD.DatabaseServices;
using CADGeometry = Autodesk.AutoCAD.Geometry;
using CADEditorInput = Autodesk.AutoCAD.EditorInput;
using CADColors = Autodesk.AutoCAD.Colors;
using CADRuntime = Autodesk.AutoCAD.Runtime;
#elif ZWCAD
using CADAppServices = ZwSoft.ZwCAD.ApplicationServices;
using CADDbServices = ZwSoft.ZwCAD.DatabaseServices;
using CADGeometry = ZwSoft.ZwCAD.Geometry;
using CADEditorInput = ZwSoft.ZwCAD.EditorInput;
using CADColors = ZwSoft.ZwCAD.Colors;
using CADRuntime = ZwSoft.ZwCAD.Runtime;
#endif

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Exception = System.Exception;

namespace ShuJunGongChengLiangJiSuan
{
    // =========================================================================
    // 【架构师局部别名隔离沙盒】：彻底免疫双平台外部 using 交叉污染
    // =========================================================================
    using CADApp = CADAppServices.Application;
    using Document = CADAppServices.Document;
    using DocumentCollection = CADAppServices.DocumentCollection;
    using DocumentLock = CADAppServices.DocumentLock;

    using PromptEntityOptions = CADEditorInput.PromptEntityOptions;
    using PromptStatus = CADEditorInput.PromptStatus;

    using CommandMethod = CADRuntime.CommandMethodAttribute;
    using CommandFlags = CADRuntime.CommandFlags;

    using Database = CADDbServices.Database;
    using Transaction = CADDbServices.Transaction;
    using BlockTable = CADDbServices.BlockTable;
    using BlockTableRecord = CADDbServices.BlockTableRecord;
    using LayerTable = CADDbServices.LayerTable;
    using LayerTableRecord = CADDbServices.LayerTableRecord;
    using ObjectId = CADDbServices.ObjectId;
    using OpenMode = CADDbServices.OpenMode;
    using Entity = CADDbServices.Entity;
    using Line = CADDbServices.Line;
    using Polyline = CADDbServices.Polyline;
    using DBText = CADDbServices.DBText;
    using RegAppTable = CADDbServices.RegAppTable;
    using RegAppTableRecord = CADDbServices.RegAppTableRecord;
    using ResultBuffer = CADDbServices.ResultBuffer;
    using TypedValue = CADDbServices.TypedValue;
    using DxfCode = CADDbServices.DxfCode;

    using TextHorizontalMode = CADDbServices.TextHorizontalMode;
    using TextVerticalMode = CADDbServices.TextVerticalMode;

    using Point2d = CADGeometry.Point2d;
    using Point3d = CADGeometry.Point3d;
    using Vector2d = CADGeometry.Vector2d;
    using Vector3d = CADGeometry.Vector3d;
    using Plane = CADGeometry.Plane;
    using Line2d = CADGeometry.Line2d;

    using CADColor = CADColors.Color;
    using ColorMthd = CADColors.ColorMethod;

    public static class PolylineExtension
    {
        public static void AddVertexCrossPlatform(this Polyline pl, int index, Point2d pt)
        {
            pl.AddVertexAt(index, pt, 0.0, 0.0, 0.0);
        }
    }

    public static class DocumentCollectionExtensionHelper
    {
        public static Document AddDocumentCrossPlatform(this DocumentCollection docs, string templateName)
        {
#if AUTOCAD
            return CADAppServices.DocumentCollectionExtension.Add(docs, templateName);
#elif ZWCAD
            return docs.Add(templateName);
#endif
        }
    }

    internal static class NativeDredgeEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        [DllImport(DllName, EntryPoint = "SetDredgeLine", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetDredgeLine(int lineType, string handleStr);

        [DllImport(DllName, EntryPoint = "BuildMemoryTinMesh", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int BuildMemoryTinMesh(string datFilePath, double maxEdgeLen, [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);

        [DllImport(DllName, EntryPoint = "DrawTinMeshToCad", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int DrawTinMeshToCad(string layerName);

        [DllImport(DllName, EntryPoint = "RunDredgingVolumeAnalysis", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunDredgingVolumeAnalysis(
            double designDepth, double topDepth,
            double leftOffset, double rightOffset,
            double leftSlope, double rightSlope,
            double overDepth, double overWidthLeft, double overWidthRight,
            [In, Out] MainForm.SectionDataPayload[] outBuffer, int maxBufferCount,
            double[] stationDists, double[] cX, double[] cY, double[] dX, double[] dY,
            int sectionCount, [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    public class ShuJunCommand
    {
        [CommandMethod("SJCALC", CommandFlags.Modal)]
        public void ShowDredgingCalcUI()
        {
            try
            {
                using (var form = new MainForm())
                    CADApp.ShowModalDialog(form);
            }
            catch (Exception ex)
            {
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[界面启动异常]: {ex.Message}");
            }
        }
    }

    public class MainForm : Form
    {
        private class CornerZone
        {
            public int VertexIndex { get; set; }
            public double DistV { get; set; }
            public double DistA { get; set; }
            public double DistB { get; set; }
            public Point2d CenterO { get; set; }
            public bool IsConcaveLeft { get; set; }
            public double W_concave { get; set; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
        public struct SectionDataPayload
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string name;
            public double stationDist;
            public double centerX, centerY;
            public double dirX, dirY;
            public double areaNatural;
            public double areaOverdig;
            public int pointCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public double[] vX;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public double[] vY;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)] public double[] vZ;
        }

        private const string XDataAppName = "SJCALC_SECTION_ENGINE";

        // ---- UI 控件声明 ----
        private TextBox _txtJianJu, _txtLiCheng;
        private CheckBox _chkLiChengBiaoZhu, _chkHuiZhiBianPo;
        private TextBox _txtDingBuShenDu, _txtSheJiShenDu;
        private TextBox _txtZuoBianPo, _txtYouBianPo;
        private TextBox _txtChaoShen;
        private TextBox _txtZuoChaoKuan, _txtYouChaoKuan;
        private TextBox _txtZuoPianYi, _txtYouPianYi;
        private TextBox _txtScaleH, _txtScaleV;
        private TextBox _txtMaxEdge;
        private CheckBox _chkDrawTin;
        private DataGridView _dgv;
        private Button _btnZhongXinXian;

        private ObjectId _selectedPolylineId = ObjectId.Null;
        private Label _lblStatus;
        private string _mileagePrefix = "";
        private double _mileageBase = 0.0;

        private List<double> _cachedDists = new List<double>();
        private List<Point2d> _cachedPts = new List<Point2d>();
        private List<Vector2d> _cachedDirs = new List<Vector2d>();

        public MainForm()
        {
            Text = "疏浚断面工程量三维智能化计算系统 (工业出图专业版)";
            ClientSize = new Size(1120, 720);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(240, 240, 240);
            Font = new System.Drawing.Font("Microsoft YaHei", 9F);

            BuildUI();
        }

        private void BuildUI()
        {
            var gbDuanMian = MakeGroup("一、 断面分布与轴线设定", 12, 12, 340, 110);
            _btnZhongXinXian = MakeButton(gbDuanMian, "1. 拾取主控中心线", 15, 30, 310, 30);
            _btnZhongXinXian.BackColor = Color.FromArgb(230, 245, 255);
            _btnZhongXinXian.Click += (s, e) => PickLine(_btnZhongXinXian);

            MakeLabel(gbDuanMian, "断面步进间距 :", 15, 75);
            _txtJianJu = MakeTextBox(gbDuanMian, "20.0", 110, 72, 60);
            MakeLabel(gbDuanMian, "米", 175, 75);

            var btnDuanMianSheJi = MakeButton(gbDuanMian, "2. 生成二维平面设计", 210, 70, 115, 28);
            btnDuanMianSheJi.BackColor = Color.FromArgb(200, 230, 200);
            btnDuanMianSheJi.Click += BtnDuanMianSheJi_Click;

            var gbSheJi = MakeGroup("二、 标准断面开挖设计矩阵", 12, 130, 340, 310);
            MakeLabel(gbSheJi, "顶部水深:", 15, 35); _txtDingBuShenDu = MakeTextBox(gbSheJi, "0", 85, 32, 60); MakeLabel(gbSheJi, "m", 148, 35);
            MakeLabel(gbSheJi, "设计水深:", 170, 35); _txtSheJiShenDu = MakeTextBox(gbSheJi, "14.0", 240, 32, 60); MakeLabel(gbSheJi, "m", 303, 35);

            MakeLabel(gbSheJi, "左边坡度:", 15, 80); _txtZuoBianPo = MakeTextBox(gbSheJi, "1:8", 85, 77, 60);
            MakeLabel(gbSheJi, "右边坡度:", 170, 80); _txtYouBianPo = MakeTextBox(gbSheJi, "1:8", 240, 77, 60);

            MakeLabel(gbSheJi, "左基础偏移:", 15, 125); _txtZuoPianYi = MakeTextBox(gbSheJi, "20.0", 85, 122, 60); MakeLabel(gbSheJi, "m", 148, 125);
            MakeLabel(gbSheJi, "右基础偏移:", 170, 125); _txtYouPianYi = MakeTextBox(gbSheJi, "20.0", 240, 122, 60); MakeLabel(gbSheJi, "m", 303, 125);

            MakeLabel(gbSheJi, "左侧超宽:", 15, 170); _txtZuoChaoKuan = MakeTextBox(gbSheJi, "1.0", 85, 167, 60); MakeLabel(gbSheJi, "m", 148, 170);
            MakeLabel(gbSheJi, "右侧超宽:", 170, 170); _txtYouChaoKuan = MakeTextBox(gbSheJi, "1.0", 240, 167, 60); MakeLabel(gbSheJi, "m", 303, 170);

            MakeLabel(gbSheJi, "超深容差:", 15, 215); _txtChaoShen = MakeTextBox(gbSheJi, "0.5", 85, 212, 60); MakeLabel(gbSheJi, "m", 148, 215);

            var btnGaoJiPai = MakeButton(gbSheJi, "更新并重绘平面视图", 15, 255, 310, 35);
            btnGaoJiPai.Click += (s, e) => { ClearOldSections(); BtnDuanMianSheJi_Click(null, null); };

            var gbJiSuanFangShi = MakeGroup("三、 图纸排版与规范输出", 12, 450, 340, 195);
            _chkLiChengBiaoZhu = MakeCheckBox(gbJiSuanFangShi, "开启里程标注", 15, 35, true);
            MakeLabel(gbJiSuanFangShi, "起讫里程:", 160, 36); _txtLiCheng = MakeTextBox(gbJiSuanFangShi, "K0+000", 230, 33, 95);

            _chkHuiZhiBianPo = MakeCheckBox(gbJiSuanFangShi, "绘制二维边坡线", 15, 75, true);

            MakeLabel(gbJiSuanFangShi, "三角网约束边长:", 15, 120);
            _txtMaxEdge = MakeTextBox(gbJiSuanFangShi, "60.0", 115, 117, 60);
            _chkDrawTin = MakeCheckBox(gbJiSuanFangShi, "图面保留三角网", 190, 120, true);

            MakeLabel(gbJiSuanFangShi, "图面比例 ➔ 纵:", 15, 155);
            _txtScaleV = MakeTextBox(gbJiSuanFangShi, "100", 115, 152, 60);
            MakeLabel(gbJiSuanFangShi, "横:", 190, 155);
            _txtScaleH = MakeTextBox(gbJiSuanFangShi, "200", 220, 152, 60);

            var gbBiaoGe = MakeGroup("四、 断面生命周期数据一览表", 365, 12, 735, 628);
            _dgv = new DataGridView
            {
                Location = new Point(15, 25),
                Size = new Size(705, 585),
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeight = 35,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 245, 250) }
            };

            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "断面里程", Name = "ColName", FillWeight = 15 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "挖深(m)", Name = "ColDepth", FillWeight = 10 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "L/R坡比", Name = "ColSlope", FillWeight = 12 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "L/R基偏(m)", Name = "ColOffset", FillWeight = 15 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "L/R超宽(m)", Name = "ColOverW", FillWeight = 15 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "天然方(㎡)", Name = "ColAreaNat", FillWeight = 15 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "超深方(㎡)", Name = "ColAreaOver", FillWeight = 15 });
            gbBiaoGe.Controls.Add(_dgv);

            var btnJiSuan = MakeButton(this, "3. 载入地形、生成三维网格并输出标准图纸", 365, 650, 480, 40);
            btnJiSuan.Font = new System.Drawing.Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
            btnJiSuan.BackColor = Color.FromArgb(180, 220, 255);
            btnJiSuan.Click += BtnJiSuan_Click;

            var btnTuiChu = MakeButton(this, "退出系统", 860, 650, 240, 40);
            btnTuiChu.Font = new System.Drawing.Font("Microsoft YaHei", 10F, FontStyle.Bold);
            btnTuiChu.Click += (s, e) => Close();

            _lblStatus = new Label { Location = new Point(12, 695), Size = new Size(1088, 22), Text = "就绪", ForeColor = Color.DarkBlue };
            Controls.Add(_lblStatus);
        }

        #region UI 构造辅助
        private GroupBox MakeGroup(string text, int x, int y, int w, int h)
        {
            var g = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.System, Font = new System.Drawing.Font("Microsoft YaHei", 9F, FontStyle.Bold) };
            Controls.Add(g); return g;
        }
        private static Label MakeLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = new System.Drawing.Font("Microsoft YaHei", 9F, FontStyle.Regular) };
            parent.Controls.Add(l); return l;
        }
        private static TextBox MakeTextBox(Control parent, string val, int x, int y, int w)
        {
            var t = new TextBox { Text = val, Location = new Point(x, y), Size = new Size(w, 23), TextAlign = HorizontalAlignment.Right, Font = new System.Drawing.Font("Microsoft YaHei", 9F, FontStyle.Regular) };
            parent.Controls.Add(t); return t;
        }
        private static CheckBox MakeCheckBox(Control parent, string text, int x, int y, bool chk)
        {
            var c = new CheckBox { Text = text, Location = new Point(x, y), Checked = chk, AutoSize = true, Font = new System.Drawing.Font("Microsoft YaHei", 9F, FontStyle.Regular) };
            parent.Controls.Add(c); return c;
        }
        private static Button MakeButton(Control parent, string text, int x, int y, int w, int h)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.System, Font = new System.Drawing.Font("Microsoft YaHei", 9F, FontStyle.Regular) };
            parent.Controls.Add(b); return b;
        }
        private TextBox AddParamRow(GroupBox parent, string label, string val, string unit, int y)
        {
            MakeLabel(parent, label, 18, y);
            var txt = MakeTextBox(parent, val, 112, y - 3, 70);
            if (!string.IsNullOrEmpty(unit)) MakeLabel(parent, unit, 190, y);
            return txt;
        }
        #endregion

        private static double ParseSlope(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            var parts = s.Split(':');
            double v;
            if (parts.Length == 2 && double.TryParse(parts[1].Trim(), out v)) return v;
            if (double.TryParse(s.Trim(), out v)) return v;
            return 0;
        }

        private static (string prefix, double baseVal) ParseMileage(string s)
        {
            s = s?.Trim() ?? "0";
            int plusIdx = s.LastIndexOf('+');
            double v;
            if (plusIdx > 0)
            {
                string pre = s.Substring(0, plusIdx + 1);
                if (double.TryParse(s.Substring(plusIdx + 1), out v)) return (pre, v);
            }
            if (double.TryParse(s, out v)) return ("", v);
            return ("", 0);
        }

        private void SetStatus(string msg, Color? color = null)
        {
            if (_lblStatus.IsHandleCreated)
                Invoke((MethodInvoker)(() => { _lblStatus.Text = msg; _lblStatus.ForeColor = color ?? Color.DarkBlue; }));
        }

        private void ClearOldSections()
        {
            var doc = CADApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.XData != null)
                    {
                        var rb = ent.XData.AsArray().FirstOrDefault(x => x.TypeCode == (int)DxfCode.ExtendedDataRegAppName && x.Value.ToString() == XDataAppName);
                        if (rb.Value != null) { ent.UpgradeOpen(); ent.Erase(); }
                    }
                }
                tr.Commit();
            }
        }

        private void PickLine(Button sourceBtn)
        {
            var doc = CADApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Hide();
            try
            {
                using (doc.LockDocument())
                {
                    var peo = new PromptEntityOptions("\n请在屏幕上拾取【主控中心线】(Polyline): ");
                    peo.SetRejectMessage("\n只能选择多段线实体！");
                    peo.AddAllowedClass(typeof(Polyline), true);
                    var per = doc.Editor.GetEntity(peo);
                    if (per.Status == PromptStatus.OK)
                    {
                        _selectedPolylineId = per.ObjectId;
                        sourceBtn.Text = "✓ 1. 中心线已成功挂载"; sourceBtn.ForeColor = Color.DarkGreen;
                    }
                }
            }
            finally { Show(); }
        }

        private void BtnDuanMianSheJi_Click(object sender, EventArgs e)
        {
            var doc = CADApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (_selectedPolylineId == ObjectId.Null) { MessageBox.Show("请先拾取【主控中心线】！"); return; }

            double d, baseW_left, baseW_right;
            if (!double.TryParse(_txtJianJu.Text, out d) || d <= 0) return;
            if (!double.TryParse(_txtZuoPianYi.Text, out baseW_left) || !double.TryParse(_txtYouPianYi.Text, out baseW_right)) return;

            double designDepth = double.Parse(_txtSheJiShenDu.Text);
            double topDepth = double.Parse(_txtDingBuShenDu.Text);
            double height = Math.Abs(designDepth - topDepth);
            double slopeExtLeft = height * ParseSlope(_txtZuoBianPo.Text);
            double slopeExtRight = height * ParseSlope(_txtYouBianPo.Text);

            double W_left_total = baseW_left + slopeExtLeft + double.Parse(_txtZuoChaoKuan.Text);
            double W_right_total = baseW_right + slopeExtRight + double.Parse(_txtYouChaoKuan.Text);

            var mileageInfo = ParseMileage(_txtLiCheng.Text);
            _mileagePrefix = mileageInfo.prefix;
            _mileageBase = mileageInfo.baseVal;

            _cachedDists.Clear(); _cachedPts.Clear(); _cachedDirs.Clear();
            _dgv.Rows.Clear();
            ClearOldSections();

            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
                if (!rat.Has(XDataAppName)) { rat.Add(new RegAppTableRecord { Name = XDataAppName }); }

                Polyline poly = tr.GetObject(_selectedPolylineId, OpenMode.ForRead) as Polyline;
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                double totalLength = poly.Length;

                List<CornerZone> zones = new List<CornerZone>();
                for (int i = 1; i < poly.NumberOfVertices - 1; i++)
                {
                    Vector2d vIn = poly.GetPoint2dAt(i) - poly.GetPoint2dAt(i - 1);
                    Vector2d vOut = poly.GetPoint2dAt(i + 1) - poly.GetPoint2dAt(i);
                    double cp = vIn.X * vOut.Y - vIn.Y * vOut.X;
                    if (Math.Abs(cp) < 1e-6) continue;
                    bool isLeft = cp > 0;
                    double w = isLeft ? W_left_total : W_right_total;

                    double angle = vIn.GetAngleTo(vOut);
                    double theta = Math.PI - angle;
                    double halfTheta = theta / 2.0;
                    double lSafe = (w / Math.Tan(halfTheta)) * 1.03;

                    double distV = poly.GetDistanceAtParameter(i);
                    double distA = distV - lSafe;
                    double distB = distV + lSafe;

                    Vector2d dirIn = vIn.GetNormal();
                    Vector2d dirOut = vOut.GetNormal();
                    Point2d ptA = poly.GetPoint2dAt(i) - dirIn * lSafe;
                    Point2d ptB = poly.GetPoint2dAt(i) + dirOut * lSafe;

                    Vector2d perpA = isLeft ? dirIn.RotateBy(Math.PI / 2) : dirIn.RotateBy(-Math.PI / 2);
                    Vector2d perpB = isLeft ? dirOut.RotateBy(Math.PI / 2) : dirOut.RotateBy(-Math.PI / 2);

                    Line2d lineA = new Line2d(ptA, perpA);
                    Line2d lineB = new Line2d(ptB, perpB);
                    Point2d[] intersections = lineA.IntersectWith(lineB);

                    if (intersections != null && intersections.Length > 0)
                    {
                        zones.Add(new CornerZone
                        {
                            VertexIndex = i,
                            DistV = distV,
                            DistA = distA,
                            DistB = distB,
                            CenterO = intersections[0],
                            IsConcaveLeft = isLeft,
                            W_concave = w
                        });
                    }
                }

                for (int i = 0; i < zones.Count - 1; i++)
                {
                    if (zones[i].DistB > zones[i + 1].DistA)
                    {
                        double midDist = (zones[i].DistB + zones[i + 1].DistA) / 2.0;
                        zones[i].DistB = midDist; zones[i + 1].DistA = midDist;
                    }
                }

                for (double currentDist = 0; currentDist <= totalLength; currentDist += d) _cachedDists.Add(currentDist);
                if (totalLength - _cachedDists.Last() > 1e-4) _cachedDists.Add(totalLength);

                var xb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, XDataAppName));

                foreach (double dist in _cachedDists)
                {
                    Point3d pt3d = poly.GetPointAtDist(dist);
                    Point2d P = new Point2d(pt3d.X, pt3d.Y);

                    CornerZone activeZone = zones.FirstOrDefault(z => dist >= z.DistA && dist <= z.DistB);
                    Vector2d dirLeft, dirRight;

                    if (activeZone != null)
                    {
                        Vector2d OP = P - activeZone.CenterO;
                        Vector2d dirOP = OP.GetNormal();
                        dirLeft = activeZone.IsConcaveLeft ? -dirOP : dirOP;
                        dirRight = activeZone.IsConcaveLeft ? dirOP : -dirOP;
                    }
                    else
                    {
                        Vector3d deriv = poly.GetFirstDerivative(poly.GetParameterAtDistance(dist));
                        Vector2d tangent = new Vector3d(deriv.X, deriv.Y, 0).GetNormal().Convert2d(new Plane());
                        dirLeft = tangent.RotateBy(Math.PI / 2);
                        dirRight = tangent.RotateBy(-Math.PI / 2);
                    }

                    _cachedPts.Add(P);
                    _cachedDirs.Add(dirLeft);

                    Point2d pLeftBase = P + dirLeft * baseW_left;
                    Point2d pRightBase = P + dirRight * baseW_right;
                    Point2d pLeftSlope = P + dirLeft * (baseW_left + slopeExtLeft);
                    Point2d pRightSlope = P + dirRight * (baseW_right + slopeExtRight);

                    Line baseLine = new Line(new Point3d(pLeftBase.X, pLeftBase.Y, 0), new Point3d(pRightBase.X, pRightBase.Y, 0)) { ColorIndex = 1, XData = xb };
                    btr.AppendEntity(baseLine); tr.AddNewlyCreatedDBObject(baseLine, true);

                    if (_chkHuiZhiBianPo.Checked)
                    {
                        Line lSlopeLine = new Line(new Point3d(pLeftBase.X, pLeftBase.Y, 0), new Point3d(pLeftSlope.X, pLeftSlope.Y, 0)) { ColorIndex = 2, XData = xb };
                        btr.AppendEntity(lSlopeLine); tr.AddNewlyCreatedDBObject(lSlopeLine, true);
                        Line rSlopeLine = new Line(new Point3d(pRightBase.X, pRightBase.Y, 0), new Point3d(pRightSlope.X, pRightSlope.Y, 0)) { ColorIndex = 2, XData = xb };
                        btr.AppendEntity(rSlopeLine); tr.AddNewlyCreatedDBObject(rSlopeLine, true);
                    }

                    double currentMileage = _mileageBase + dist;
                    string sectionName = $"{_mileagePrefix}{currentMileage:F2}";
                    if (_chkLiChengBiaoZhu.Checked)
                    {
                        DBText txt = new DBText
                        {
                            Position = new Point3d(pLeftSlope.X, pLeftSlope.Y, 0),
                            TextString = sectionName,
                            Height = d * 0.25 < 1.5 ? 1.5 : d * 0.25,
                            ColorIndex = 3,
                            Rotation = dirLeft.Angle,
                            XData = xb
                        };
                        btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
                    }

                    _dgv.Rows.Add(sectionName, $"{designDepth:F1}", $"{_txtZuoBianPo.Text} / {_txtYouBianPo.Text}",
                        $"{_txtZuoPianYi.Text} / {_txtYouPianYi.Text}", $"{_txtZuoChaoKuan.Text} / {_txtYouChaoKuan.Text}", "0.00", "0.00");
                }
                tr.Commit();
            }
            doc.Editor.Regen();
            SetStatus($"[平面设计完毕] 已成功铺设 {_cachedDists.Count} 组平面控制网，等待载入地形进行体积分段", Color.DarkGreen);
        }

        // ==========================================================
        // 核心步骤 2：生成跨文档国标级测绘断面大样图（完美复刻十字坐标系）
        // ==========================================================
        private void BtnJiSuan_Click(object sender, EventArgs e)
        {
            if (_cachedDists.Count == 0) { MessageBox.Show("请先完成【生成二维平面设计】！"); return; }

            string datPath = "";
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "展点及测深数据 (*.dat;*.txt)|*.dat;*.txt", Title = "请选择外部地形展点数据" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                datPath = ofd.FileName;
            }

            Enabled = false;
            SetStatus("正在多核并发创建三角网并进行断面空间交切积分...", Color.DarkOrange);

            try
            {
                double maxEdge;
                if (!double.TryParse(_txtMaxEdge.Text, out maxEdge)) maxEdge = 60.0;

                int tinPoints = NativeDredgeEngine.BuildMemoryTinMesh(datPath, maxEdge, p => { });
                if (tinPoints <= 0) { MessageBox.Show("三角网构建失败，请检查点数据格式！"); return; }

                if (_chkDrawTin.Checked)
                {
                    NativeDredgeEngine.DrawTinMeshToCad("疏浚_地形三角网");
                    CADApp.DocumentManager.MdiActiveDocument.Editor.Regen();
                }

                int sectionCount = _cachedDists.Count;
                SectionDataPayload[] buffer = new SectionDataPayload[sectionCount];
                for (int i = 0; i < sectionCount; i++)
                {
                    buffer[i] = new SectionDataPayload
                    {
                        vX = new double[512],
                        vY = new double[512],
                        vZ = new double[512],
                        name = $"{_mileagePrefix}{_mileageBase + _cachedDists[i]:F2}"
                    };
                }

                double designDepth = double.Parse(_txtSheJiShenDu.Text);
                double topDepth = double.Parse(_txtDingBuShenDu.Text);
                double height = Math.Abs(designDepth - topDepth);

                double leftOffset = double.Parse(_txtZuoPianYi.Text);
                double rightOffset = double.Parse(_txtYouPianYi.Text);
                double leftSlope = ParseSlope(_txtZuoBianPo.Text);
                double rightSlope = ParseSlope(_txtYouBianPo.Text);
                double overDepth = double.Parse(_txtChaoShen.Text);
                double overWidthLeft = double.Parse(_txtZuoChaoKuan.Text);
                double overWidthRight = double.Parse(_txtYouChaoKuan.Text);

                int resCount = NativeDredgeEngine.RunDredgingVolumeAnalysis(
                    designDepth, topDepth, leftOffset, rightOffset, leftSlope, rightSlope, overDepth, overWidthLeft, overWidthRight,
                    buffer, sectionCount, _cachedDists.ToArray(), _cachedPts.Select(p => p.X).ToArray(), _cachedPts.Select(p => p.Y).ToArray(),
                    _cachedDirs.Select(d => d.X).ToArray(), _cachedDirs.Select(d => d.Y).ToArray(), sectionCount, p => { });

                DocumentCollection docLockManager = CADApp.DocumentManager;
                Document drawingDoc = docLockManager.AddDocumentCrossPlatform("");
                Database drawingDb = drawingDoc.Database;

                using (DocumentLock dl = drawingDoc.LockDocument())
                using (Transaction tr = drawingDb.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(drawingDb.LayerTableId, OpenMode.ForWrite);
                    if (!lt.Has("疏浚设计断面"))
                    {
                        LayerTableRecord ltr = new LayerTableRecord { Name = "疏浚设计断面" };
                        ltr.Color = CADColor.FromColorIndex(ColorMthd.ByAci, 1);
                        lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
                    }

                    BlockTable bt = (BlockTable)tr.GetObject(drawingDb.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double scaleH = double.Parse(_txtScaleH.Text);
                    double scaleV = double.Parse(_txtScaleV.Text);
                    double exagV = scaleH / scaleV;
                    double textScale = scaleH / 1000.0;

                    // 找出断面的最大横向延展距离，进行 10 米取整补齐
                    double maxW = Math.Max(leftOffset + overWidthLeft + (height + overDepth) * leftSlope + 10.0,
                                           rightOffset + overWidthRight + (height + overDepth) * rightSlope + 10.0);
                    maxW = Math.Ceiling(maxW / 10.0) * 10.0 + 10.0;

                    // 计算每个断面框格的物理占地范围
                    double maxD = Math.Ceiling(designDepth + overDepth + 2.0);
                    double stepX = maxW * 2.0 + 60.0;
                    double stepY = maxD * exagV + 60.0;

                    int colsPerRow = 3;
                    double totalVolNatural = 0.0;
                    double totalVolOverdig = 0.0;

                    for (int i = 0; i < resCount; i++)
                    {
                        var pay = buffer[i];

                        if (i < _dgv.Rows.Count)
                        {
                            _dgv.Rows[i].Cells[5].Value = $"{pay.areaNatural:F2}";
                            _dgv.Rows[i].Cells[6].Value = $"{pay.areaOverdig:F2}";
                        }

                        if (i > 0)
                        {
                            double dLen = pay.stationDist - buffer[i - 1].stationDist;
                            totalVolNatural += ((pay.areaNatural + buffer[i - 1].areaNatural) * 0.5) * dLen;
                            totalVolOverdig += ((pay.areaOverdig + buffer[i - 1].areaOverdig) * 0.5) * dLen;
                        }

                        if (pay.pointCount < 2) continue;

                        // =========================================================================
                        // 十字对齐绘图引擎：X=0 为中心轴线，Y=topAxisY 为深度 0 的顶部基准线
                        // =========================================================================
                        int row = i / colsPerRow;
                        int col = i % colsPerRow;

                        double axisX = col * stepX;
                        double topAxisY = -(row * stepY);

                        // 1. 顶部水平主坐标轴（白线，Y=0深度）
                        Line hzAxis = new Line(new Point3d(axisX - maxW, topAxisY, 0), new Point3d(axisX + maxW, topAxisY, 0)) { ColorIndex = 7 };
                        btr.AppendEntity(hzAxis); tr.AddNewlyCreatedDBObject(hzAxis, true);

                        // 底部横向刻度 (每隔10米)
                        for (double dx = -maxW + 10; dx <= maxW - 10; dx += 10.0)
                        {
                            double tickX = axisX + dx;
                            // 刻度线朝下画
                            Line tick = new Line(new Point3d(tickX, topAxisY, 0), new Point3d(tickX, topAxisY - 1.5 * textScale, 0)) { ColorIndex = 7 };
                            btr.AppendEntity(tick); tr.AddNewlyCreatedDBObject(tick, true);

                            if (Math.Abs(dx) > 0.1) // 避让中心零刻度
                            {
                                DBText textHz = new DBText
                                {
                                    Position = new Point3d(tickX, topAxisY - 2.5 * textScale, 0),
                                    TextString = $"{dx:F0}",
                                    Height = 2.0 * textScale,
                                    ColorIndex = 7,
                                    HorizontalMode = TextHorizontalMode.TextCenter,
                                    VerticalMode = TextVerticalMode.TextTop,
                                    AlignmentPoint = new Point3d(tickX, topAxisY - 2.5 * textScale, 0)
                                };
                                btr.AppendEntity(textHz); tr.AddNewlyCreatedDBObject(textHz, true);
                            }
                        }

                        // 2. 中央纵向刻度主轴（白线，X=0位置）
                        Line vtAxis = new Line(new Point3d(axisX, topAxisY, 0), new Point3d(axisX, topAxisY - maxD * exagV, 0)) { ColorIndex = 7 };
                        btr.AppendEntity(vtAxis); tr.AddNewlyCreatedDBObject(vtAxis, true);

                        // 深度/高程刻度 (每隔1米)
                        for (double depth = 0.0; depth <= maxD; depth += 1.0)
                        {
                            double tickY = topAxisY - depth * exagV;
                            // 刻度线朝右画
                            Line tick = new Line(new Point3d(axisX, tickY, 0), new Point3d(axisX + 1.5 * textScale, tickY, 0)) { ColorIndex = 7 };
                            btr.AppendEntity(tick); tr.AddNewlyCreatedDBObject(tick, true);

                            DBText textVt = new DBText
                            {
                                Position = new Point3d(axisX + 2.5 * textScale, tickY, 0),
                                TextString = $"{topDepth + depth:F1}",
                                Height = 2.0 * textScale,
                                ColorIndex = 7,
                                HorizontalMode = TextHorizontalMode.TextLeft,
                                VerticalMode = TextVerticalMode.TextVerticalMid,
                                AlignmentPoint = new Point3d(axisX + 2.5 * textScale, tickY, 0)
                            };
                            btr.AppendEntity(textVt); tr.AddNewlyCreatedDBObject(textVt, true);
                        }

                        // 3. 顶端工程里程标识（正中央十字上方）
                        DBText titleText = new DBText
                        {
                            Position = new Point3d(axisX, topAxisY + 2.0 * textScale, 0),
                            TextString = pay.name,
                            Height = 3.0 * textScale,
                            ColorIndex = 7,
                            HorizontalMode = TextHorizontalMode.TextCenter,
                            VerticalMode = TextVerticalMode.TextBottom,
                            AlignmentPoint = new Point3d(axisX, topAxisY + 2.0 * textScale, 0)
                        };
                        btr.AppendEntity(titleText); tr.AddNewlyCreatedDBObject(titleText, true);

                        // 4. 左上角方量统计图例（红黄面积标记）
                        double legendX = axisX - maxW + 10.0 * textScale;
                        DBText areaNat = new DBText
                        {
                            Position = new Point3d(legendX, topAxisY - 3.5 * textScale, 0),
                            TextString = $"设计断面面积 {pay.areaNatural:F2} 平方米",
                            Height = 2.0 * textScale,
                            ColorIndex = 1
                        };
                        btr.AppendEntity(areaNat); tr.AddNewlyCreatedDBObject(areaNat, true);

                        DBText areaOver = new DBText
                        {
                            Position = new Point3d(legendX, topAxisY - 7.0 * textScale, 0),
                            TextString = $"超挖断面面积 {pay.areaOverdig:F2} 平方米",
                            Height = 2.0 * textScale,
                            ColorIndex = 2
                        };
                        btr.AppendEntity(areaOver); tr.AddNewlyCreatedDBObject(areaOver, true);

                        // 5. 绘制自然水深地形线（绿色，严格映射深度）
                        Polyline topoPoly = new Polyline(); topoPoly.ColorIndex = 3;
                        for (int p = 0; p < pay.pointCount; p++)
                        {
                            double drawingX = axisX + pay.vX[p];
                            double drawingY = topAxisY - Math.Abs(pay.vY[p]) * exagV;
                            topoPoly.AddVertexCrossPlatform(p, new Point2d(drawingX, drawingY));
                        }
                        btr.AppendEntity(topoPoly); tr.AddNewlyCreatedDBObject(topoPoly, true);

                        // 6. 绘制设计底槽及坡面（红线段）
                        Polyline designPoly = new Polyline(); designPoly.ColorIndex = 1;
                        designPoly.AddVertexCrossPlatform(0, new Point2d(axisX - leftOffset - height * leftSlope, topAxisY - topDepth * exagV));
                        designPoly.AddVertexCrossPlatform(1, new Point2d(axisX - leftOffset, topAxisY - designDepth * exagV));
                        designPoly.AddVertexCrossPlatform(2, new Point2d(axisX + rightOffset, topAxisY - designDepth * exagV));
                        designPoly.AddVertexCrossPlatform(3, new Point2d(axisX + rightOffset + height * rightSlope, topAxisY - topDepth * exagV));
                        btr.AppendEntity(designPoly); tr.AddNewlyCreatedDBObject(designPoly, true);

                        // 7. 绘制超深超宽范围线（黄色）
                        Polyline overdigPoly = new Polyline(); overdigPoly.ColorIndex = 2;
                        double fullH = height + overDepth;
                        overdigPoly.AddVertexCrossPlatform(0, new Point2d(axisX - (leftOffset + overWidthLeft) - fullH * leftSlope, topAxisY - topDepth * exagV));
                        overdigPoly.AddVertexCrossPlatform(1, new Point2d(axisX - (leftOffset + overWidthLeft), topAxisY - (designDepth + overDepth) * exagV));
                        overdigPoly.AddVertexCrossPlatform(2, new Point2d(axisX + (rightOffset + overWidthRight), topAxisY - (designDepth + overDepth) * exagV));
                        overdigPoly.AddVertexCrossPlatform(3, new Point2d(axisX + (rightOffset + overWidthRight) + fullH * rightSlope, topAxisY - topDepth * exagV));
                        btr.AppendEntity(overdigPoly); tr.AddNewlyCreatedDBObject(overdigPoly, true);
                    }
                    tr.Commit();

                    MessageBox.Show($"图纸及计算模型已排版完毕！\n\n" +
                                    $"● 天然总设计方量：{totalVolNatural:F2} m³\n" +
                                    $"● 计入超深超宽总方量：{totalVolOverdig:F2} m³", "工程量报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                docLockManager.MdiActiveDocument = drawingDoc;
                drawingDoc.Editor.Regen();
            }
            catch (Exception ex) { MessageBox.Show($"排版渲染发生异常: {ex.Message}"); }
            finally { Enabled = true; SetStatus("全量排版及三维微积分闭环完成", Color.DarkGreen); }
        }
    }
}