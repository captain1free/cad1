#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using AcadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else // 默认 AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#endif

using System;
using System.Drawing;
using System.Windows.Forms;
using SysException = System.Exception;

namespace SheetFramePlugin
{
    public class SheetFrameForm : Form
    {
        readonly Extents3d _ext;
        bool _hasPick, _uiReady;
        Point2d _pickMin, _pickMax;

        ComboBox cmbScale, cmbSize;
        NumericUpDown numW, numH, numMargin, numGrid;
        RadioButton rbAll, rbPick, rbNumCoord, rbNumSeq;
        RadioButton rbModel, rbLayout, rbSplit;
        Button btnPick, btnOK, btnCancel, btnDir;
        Label lblRange, lblPreview;
        CheckBox chkAlign, chkCross, chkFullGrid, chkLabel, chkScaleBar, chkRebuild, chkSaveDwg;
        TextBox txtUnit, txtTitle, txtDate, txtCoordSys, txtDatum,
                txtSurveyor, txtPlotter, txtChecker, txtPrefix, txtDir;

        public SheetOptions Options { get; private set; }

        public SheetFrameForm(Extents3d ext)
        {
            _ext = ext;
            Text = "水运工程标准分幅图框（JTS 131 附录S 图式）";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 860);
            BuildUi();
            _uiReady = true;
            UpdatePreview();
        }

        void BuildUi()
        {
            int y = 10;

            var g1 = new GroupBox { Text = "一、比例尺与图幅", Bounds = new Rectangle(10, y, 560, 130) };
            Controls.Add(g1);
            g1.Controls.Add(new Label { Text = "比例尺:", AutoSize = true, Location = new Point(15, 30) });
            cmbScale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Bounds = new Rectangle(95, 26, 110, 24) };
            cmbScale.Items.AddRange(new object[] { "1:500", "1:1000", "1:2000", "1:5000", "1:10000" });
            cmbScale.Text = "1:2000";
            cmbScale.SelectedIndexChanged += (s, e) => UpdatePreview();
            g1.Controls.Add(cmbScale);
            g1.Controls.Add(new Label { Text = "图幅尺寸:", AutoSize = true, Location = new Point(280, 30) });
            cmbSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(360, 26, 110, 24) };
            cmbSize.Items.AddRange(new object[] { "50cm×50cm", "50cm×40cm", "40cm×40cm", "自定义" });
            cmbSize.SelectedIndex = 0;
            cmbSize.SelectedIndexChanged += (s, e) =>
            {
                if (cmbSize.SelectedIndex == 0) { numW.Value = 50; numH.Value = 50; }
                else if (cmbSize.SelectedIndex == 1) { numW.Value = 50; numH.Value = 40; }
                else if (cmbSize.SelectedIndex == 2) { numW.Value = 40; numH.Value = 40; }
                UpdatePreview();
            };
            g1.Controls.Add(cmbSize);

            g1.Controls.Add(new Label { Text = "图幅宽:", AutoSize = true, Location = new Point(15, 66) });
            numW = new NumericUpDown { Bounds = new Rectangle(95, 62, 55, 24), Minimum = 10, Maximum = 200, DecimalPlaces = 1, Value = 50 };
            g1.Controls.Add(new Label { Text = "cm   图幅高:", AutoSize = true, Location = new Point(155, 66) });
            numH = new NumericUpDown { Bounds = new Rectangle(250, 62, 55, 24), Minimum = 10, Maximum = 200, DecimalPlaces = 1, Value = 50 };
            numW.ValueChanged += (s, e) => UpdatePreview();
            numH.ValueChanged += (s, e) => UpdatePreview();
            g1.Controls.Add(numW); g1.Controls.Add(numH);

            g1.Controls.Add(new Label { Text = "图廓带宽:", AutoSize = true, Location = new Point(330, 66) });
            numMargin = new NumericUpDown
            {
                Bounds = new Rectangle(410, 62, 55, 24),
                Minimum = 1,
                Maximum = 30,
                Value = 6
            };   // ★修复②：内外图廓间距 12→6mm
            numMargin.ValueChanged += (s, e) => UpdatePreview();
            g1.Controls.Add(numMargin);
            g1.Controls.Add(new Label { Text = "mm", AutoSize = true, Location = new Point(470, 66) });

            g1.Controls.Add(new Label { Text = "格网间隔:", AutoSize = true, Location = new Point(15, 100) });
            numGrid = new NumericUpDown { Bounds = new Rectangle(95, 96, 55, 24), Minimum = 1, Maximum = 50, Value = 10 };
            numGrid.ValueChanged += (s, e) => UpdatePreview();
            g1.Controls.Add(numGrid);
            g1.Controls.Add(new Label { Text = "cm(图上，常规取10cm)", AutoSize = true, Location = new Point(155, 100) });

            y += 138;
            var g2 = new GroupBox { Text = "二、分幅范围", Bounds = new Rectangle(10, y, 560, 105) };
            Controls.Add(g2);
            rbAll = new RadioButton { Text = "全图范围（所有实体总包围盒）", AutoSize = true, Location = new Point(15, 26), Checked = true };
            rbPick = new RadioButton { Text = "框选范围", AutoSize = true, Location = new Point(15, 52) };
            rbAll.CheckedChanged += (s, e) => UpdatePreview();
            rbPick.CheckedChanged += (s, e) => UpdatePreview();
            g2.Controls.Add(rbAll); g2.Controls.Add(rbPick);
            btnPick = new Button { Text = "拾取范围...", Bounds = new Rectangle(120, 48, 95, 26) };
            btnPick.Click += OnPickRange;
            g2.Controls.Add(btnPick);
            lblRange = new Label { AutoSize = false, Bounds = new Rectangle(230, 24, 320, 46) };
            g2.Controls.Add(lblRange);
            chkAlign = new CheckBox { Text = "图幅西南角对齐整幅坐标（推荐，便于接幅）", AutoSize = true, Location = new Point(15, 76), Checked = true };
            chkAlign.CheckedChanged += (s, e) => UpdatePreview();
            g2.Controls.Add(chkAlign);

            y += 112;
            var g3 = new GroupBox { Text = "三、图廓整饰内容（图S.2.2式样）", Bounds = new Rectangle(10, y, 560, 200) };
            Controls.Add(g3);

            // 【修复遮挡重贴】：重新分配 X 坐标与 Width，使其均匀对齐不重叠
            txtUnit = MkText(g3, "测绘单位:", 15, 28, 455, "××海事测绘中心");
            txtTitle = MkText(g3, "图  名:", 15, 60, 455, "");
            txtDate = MkText(g3, "测量日期:", 15, 92, 120, DateTime.Now.ToString("yyyy年MM月"));
            txtCoordSys = MkText(g3, "坐标系:", 230, 92, 240, "2000国家大地坐标系");
            txtDatum = MkText(g3, "基  准:", 15, 124, 455, "1985国家高程基准");
            txtSurveyor = MkText(g3, "测 量:", 15, 156, 85, "");
            txtPlotter = MkText(g3, "绘 图:", 195, 156, 85, "");
            txtChecker = MkText(g3, "审 核:", 375, 156, 85, "");

            y += 207;
            var g4 = new GroupBox { Text = "四、图幅编号与格网选项", Bounds = new Rectangle(10, y, 560, 100) };
            Controls.Add(g4);
            rbNumCoord = new RadioButton { Text = "西南角坐标编号(如 5512.0-4328.0)", AutoSize = true, Location = new Point(15, 26), Checked = true };
            rbNumSeq = new RadioButton { Text = "顺序编号(左上→右下) 前缀:", AutoSize = true, Location = new Point(280, 26) };
            g4.Controls.Add(rbNumCoord); g4.Controls.Add(rbNumSeq);
            txtPrefix = new TextBox { Bounds = new Rectangle(460, 22, 75, 24), Text = "" };
            g4.Controls.Add(txtPrefix);
            chkCross = new CheckBox { Text = "十字格网线", AutoSize = true, Location = new Point(15, 56), Checked = true };
            chkFullGrid = new CheckBox { Text = "贯通格网线", AutoSize = true, Location = new Point(120, 56) };
            chkLabel = new CheckBox { Text = "图廓间坐标注记", AutoSize = true, Location = new Point(225, 56), Checked = true };
            chkScaleBar = new CheckBox { Text = "直线比例尺", AutoSize = true, Location = new Point(360, 56) };
            g4.Controls.Add(chkCross); g4.Controls.Add(chkFullGrid);
            g4.Controls.Add(chkLabel); g4.Controls.Add(chkScaleBar);

            y += 108;
            var g5 = new GroupBox { Text = "五、输出方式（分幅后如何出图）", Bounds = new Rectangle(10, y, 560, 148) };
            Controls.Add(g5);
            rbModel = new RadioButton { Text = "叠加模型空间（图框画在地形图上）", AutoSize = true, Location = new Point(15, 26) };
            rbLayout = new RadioButton { Text = "每幅单独布局（推荐：单独打印/批量发布）", AutoSize = true, Location = new Point(15, 52), Checked = true };
            rbSplit = new RadioButton { Text = "仅拆分为独立DWG（每幅一个文件）", AutoSize = true, Location = new Point(15, 78) };
            g5.Controls.Add(rbModel); g5.Controls.Add(rbLayout); g5.Controls.Add(rbSplit);
            g5.Controls.Add(new Label { Text = "输出目录:", AutoSize = true, Location = new Point(300, 26) });
            txtDir = new TextBox
            {
                Bounds = new Rectangle(370, 22, 175, 24),
                Text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "分幅图")
            };
            g5.Controls.Add(txtDir);
            btnDir = new Button { Text = "浏览…", Bounds = new Rectangle(478, 50, 67, 24) };
            btnDir.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog())
                    if (fb.ShowDialog(this) == DialogResult.OK) txtDir.Text = fb.SelectedPath;
            };
            g5.Controls.Add(btnDir);
            chkRebuild = new CheckBox { Text = "重建时删除旧“幅-”布局", AutoSize = true, Location = new Point(300, 78), Checked = true };
            g5.Controls.Add(chkRebuild);

            chkSaveDwg = new CheckBox
            {
                Text = "每幅另存为独立DWG（按图幅名命名，便于修改/分发）",
                AutoSize = true,
                Location = new Point(15, 106),
                Checked = true
            };
            chkSaveDwg.CheckedChanged += (s, e) => txtDir.Enabled = chkSaveDwg.Checked || rbSplit.Checked;
            g5.Controls.Add(chkSaveDwg);
            rbSplit.CheckedChanged += (s, e) =>
            {
                if (rbSplit.Checked) { chkSaveDwg.Checked = true; chkSaveDwg.Enabled = false; }
                else chkSaveDwg.Enabled = true;
            };
            y += 156;

            lblPreview = new Label { AutoSize = false, Bounds = new Rectangle(15, y, 550, 40), ForeColor = Color.Navy };
            Controls.Add(lblPreview);
            btnOK = new Button { Text = "确 定", Bounds = new Rectangle(380, y + 48, 85, 30) };
            btnOK.Click += OnOk;
            btnCancel = new Button { Text = "取 消", Bounds = new Rectangle(475, y + 48, 85, 30), DialogResult = DialogResult.Cancel };
            Controls.Add(btnOK); Controls.Add(btnCancel);
            AcceptButton = btnOK; CancelButton = btnCancel;
        }

        TextBox MkText(GroupBox g, string caption, int x, int y, int w, string val)
        {
            g.Controls.Add(new Label { Text = caption, AutoSize = true, Location = new Point(x, y + 3) });
            var t = new TextBox { Bounds = new Rectangle(x + 75, y, w, 24), Text = val };
            g.Controls.Add(t);
            return t;
        }

        void OnPickRange(object sender, EventArgs e)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Hide();
            try
            {
                using (doc.LockDocument())
                {
                    var pr1 = ed.GetPoint("\n指定分幅范围的第一角点: ");
                    if (pr1.Status != PromptStatus.OK) return;
                    var pr2 = ed.GetCorner(new PromptCornerOptions("\n指定对角点: ", pr1.Value));
                    if (pr2.Status != PromptStatus.OK) return;
                    _pickMin = new Point2d(Math.Min(pr1.Value.X, pr2.Value.X), Math.Min(pr1.Value.Y, pr2.Value.Y));
                    _pickMax = new Point2d(Math.Max(pr1.Value.X, pr2.Value.X), Math.Max(pr1.Value.Y, pr2.Value.Y));
                    _hasPick = true;
                    rbPick.Checked = true;
                }
            }
            finally
            {
                Show(); Activate(); UpdatePreview();
            }
        }

        SheetOptions Collect()
        {
            string s = (cmbScale.Text ?? "").Trim();
            int den = 0;
            int i = s.LastIndexOf(':');
            if (i >= 0) int.TryParse(s.Substring(i + 1).Trim(), out den);
            else int.TryParse(s, out den);
            if (den <= 0) throw new SysException("比例尺应为 1:2000 形式（分母为正整数）");

            Point2d rmin, rmax;
            if (rbPick.Checked && _hasPick) { rmin = _pickMin; rmax = _pickMax; }
            else
            {
                rmin = new Point2d(_ext.MinPoint.X, _ext.MinPoint.Y);
                rmax = new Point2d(_ext.MaxPoint.X, _ext.MaxPoint.Y);
            }
            return new SheetOptions
            {
                ScaleDen = den,
                PaperW = (double)numW.Value * 10.0,
                PaperH = (double)numH.Value * 10.0,
                Margin = (double)numMargin.Value,
                GridPaper = (double)numGrid.Value * 10.0,
                CrossLen = 10.0,
                FullGridLine = chkFullGrid.Checked,
                DrawCross = chkCross.Checked,
                LabelCoord = chkLabel.Checked,
                DrawScaleBar = chkScaleBar.Checked,
                AlignSheet = chkAlign.Checked,
                RangeMin = rmin,
                RangeMax = rmax,
                UnitName = txtUnit.Text.Trim(),
                SheetTitle = txtTitle.Text.Trim(),
                SurveyDate = txtDate.Text.Trim(),
                CoordSys = txtCoordSys.Text.Trim(),
                Datum = txtDatum.Text.Trim(),
                Surveyor = txtSurveyor.Text.Trim(),
                Plotter = txtPlotter.Text.Trim(),
                Checker = txtChecker.Text.Trim(),
                UseCoordNumber = rbNumCoord.Checked,
                Prefix = txtPrefix.Text.Trim(),
                Mode = rbLayout.Checked ? OutputMode.LayoutPerSheet
                     : (rbModel.Checked ? OutputMode.ModelOverlay : OutputMode.SplitDwg),
                OutDir = txtDir.Text.Trim(),
                RebuildLayouts = chkRebuild.Checked,
                SaveEachDwg = chkSaveDwg.Checked
            };
        }

        void UpdatePreview()
        {
            if (!_uiReady) return;
            lblRange.Text = rbPick.Checked
                ? (_hasPick
                    ? string.Format("框选: ({0:0.0}, {1:0.0}) ~ ({2:0.0}, {3:0.0})",
                                    _pickMin.X, _pickMin.Y, _pickMax.X, _pickMax.Y)
                    : "尚未拾取范围（暂用全图范围）")
                : string.Format("全图: ({0:0.0}, {1:0.0}) ~ ({2:0.0}, {3:0.0})",
                                _ext.MinPoint.X, _ext.MinPoint.Y, _ext.MaxPoint.X, _ext.MaxPoint.Y);
            try
            {
                var plan = SheetPlan.Create(Collect());
                lblPreview.Text = string.Format(
                    "预计分幅: {0} 行 × {1} 列, 共 {2} 幅 | 单幅实地 {3:0.#}m × {4:0.#}m | 图上 {5:0.#}cm×{6:0.#}cm",
                    plan.Rows, plan.Cols, plan.Total, plan.W, plan.H,
                    (double)numW.Value, (double)numH.Value);
            }
            catch (SysException ex) { lblPreview.Text = "参数无效: " + ex.Message; }
        }

        void OnOk(object sender, EventArgs e)
        {
            try
            {
                Options = Collect();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (SysException ex)
            {
                MessageBox.Show(this, ex.Message, "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}