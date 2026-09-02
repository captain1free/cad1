#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application;
#elif ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;
using SysDrawing = System.Drawing;

namespace ZwCadTinAnalysis
{
    // ==========================================================
    // 1. 【极客防线】C++ 底层金库 P/Invoke 接口
    // ==========================================================
    internal static class NativeTinEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        [DllImport(DllName, EntryPoint = "RunTinDifferenceAnalysis", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunTinDifferenceAnalysis(
            string file1, string file2, double maxEdgeLen, double gridStep, int calcMode,
            [In] double[] diffMin, [In] double[] diffMax, [In] short[] diffColors, int rangeCount,
            [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    // ==========================================================
    // 2. 数据结构定义
    // ==========================================================
    public enum CalcMode { PointToTin = 0, GridToTin = 1 }

    public struct DiffRange
    {
        public double Min; public double Max; public short ColorIndex; public string Label;
        public DiffRange(double min, double max, short color, string label) { Min = min; Max = max; ColorIndex = color; Label = label; }
    }

    // ==========================================================
    // 3. 命令注册入口
    // ==========================================================
    public class TinAnalysisCommand
    {
        [CommandMethod("CREATETIN", CommandFlags.Modal)]
        public void CreateOptimizedMesh()
        {
            try
            {
                using (var form = new TinInputForm())
                {
                    CADApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[界面异常]: {ex.Message}");
            }
        }
    }

    // ==========================================================
    // 4. 纯净的前端 UI 窗体 
    // ==========================================================
    public class TinInputForm : WinForms.Form
    {
        private WinForms.TextBox txtLength, txtGrid, txtPath1, txtPath2;
        private WinForms.DataGridView dgvRanges;
        private WinForms.RadioButton rbModePoint, rbModeGrid;
        private WinForms.ProgressBar progressBar;
        private NativeTinEngine.ProgressCallbackDelegate _progressCallback;

        public TinInputForm()
        {
            this.Text = "TIN 三角网土方差值极速分析 (C++引擎版)";
            this.Size = new SysDrawing.Size(540, 750);
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            _progressCallback = new NativeTinEngine.ProgressCallbackDelegate(OnProgressUpdate);
            InitializeUI();
        }

        private void InitializeUI()
        {
            int margin = 20, fullWidth = 480, y = 20;

            this.Controls.Add(new WinForms.Label { Text = "一期网格最大边长:", Left = margin, Top = y + 3, Width = 150 });
            txtLength = new WinForms.TextBox { Left = margin + 160, Top = y, Width = 100, Text = "50" };
            this.Controls.Add(txtLength); y += 40;

            var grpMode = new WinForms.GroupBox { Text = "计算模式选择", Left = margin, Top = y, Width = fullWidth, Height = 60 };
            rbModePoint = new WinForms.RadioButton { Text = "离散点对比 (读取二期点)", Left = 20, Top = 25, Width = 180, Checked = true };
            rbModeGrid = new WinForms.RadioButton { Text = "网格差值 (二期也建网)", Left = 210, Top = 25, Width = 180 };
            grpMode.Controls.Add(rbModePoint); grpMode.Controls.Add(rbModeGrid);
            this.Controls.Add(grpMode); y += 70;
            rbModePoint.CheckedChanged += (s, e) => { txtGrid.Enabled = rbModeGrid.Checked; };

            this.Controls.Add(new WinForms.Label { Text = "分析网格间距(米):", Left = margin, Top = y + 3, Width = 150 });
            txtGrid = new WinForms.TextBox { Left = margin + 160, Top = y, Width = 100, Text = "5", Enabled = false };
            this.Controls.Add(txtGrid); y += 40;

            AddFileSection("一期基准数据 (TIN):", ref txtPath1, ref y, margin, fullWidth);
            AddFileSection("二期验收数据 (点或TIN):", ref txtPath2, ref y, margin, fullWidth);

            this.Controls.Add(new WinForms.Label { Text = "差值分段设色设置:", Left = margin, Top = y, Width = 300, Font = new SysDrawing.Font(this.Font, SysDrawing.FontStyle.Bold) });
            y += 25;
            dgvRanges = new WinForms.DataGridView { Left = margin, Top = y, Width = fullWidth, Height = 180, AllowUserToAddRows = true, RowHeadersVisible = false };
            dgvRanges.Columns.Add("Label", "描述"); dgvRanges.Columns.Add("Min", "Min"); dgvRanges.Columns.Add("Max", "Max");
            dgvRanges.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "Color", HeaderText = "颜色", ReadOnly = true });
            dgvRanges.Columns.Add(new WinForms.DataGridViewTextBoxColumn { Name = "ColorIdx", HeaderText = "Idx", Visible = false });

            AddGridRow("深挖", -9999, -0.5, SysDrawing.Color.Blue, 5);
            AddGridRow("浅挖", -0.5, -0.05, SysDrawing.Color.Cyan, 4);
            AddGridRow("合格", -0.05, 0.05, SysDrawing.Color.Green, 3);
            AddGridRow("浅填", 0.05, 0.5, SysDrawing.Color.Magenta, 6);
            AddGridRow("深填", 0.5, 9999, SysDrawing.Color.Red, 1);
            dgvRanges.CellClick += DgvRanges_CellClick;
            this.Controls.Add(dgvRanges); y += 190;

            progressBar = new WinForms.ProgressBar { Left = margin, Top = y, Width = fullWidth, Height = 15, Minimum = 0, Maximum = 100 };
            this.Controls.Add(progressBar); y += 30;

            var btnOk = new WinForms.Button { Text = "极速分析", Left = fullWidth - 100 + margin, Top = y, Width = 100, Height = 35 };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk); this.AcceptButton = btnOk;
        }

        private void OnProgressUpdate(int percent)
        {
            if (this.IsHandleCreated) this.Invoke((WinForms.MethodInvoker)delegate { progressBar.Value = Math.Min(100, Math.Max(0, percent)); WinForms.Application.DoEvents(); });
        }

        private void AddFileSection(string title, ref WinForms.TextBox txtBox, ref int y, int margin, int width)
        {
            this.Controls.Add(new WinForms.Label { Text = title, Left = margin, Top = y, Width = 300 }); y += 25;
            var box = new WinForms.TextBox { Left = margin, Top = y + 2, Width = width - 90, ReadOnly = true, BackColor = SysDrawing.Color.White };
            var btn = new WinForms.Button { Text = "浏览...", Left = margin + width - 80, Top = y, Width = 80 };
            this.Controls.Add(box); this.Controls.Add(btn);
            btn.Click += (s, e) => {
                using (var ofd = new WinForms.OpenFileDialog { Filter = "坐标数据 (*.txt;*.dat;*.csv;*.xyz)|*.txt;*.dat;*.csv;*.xyz|所有文件 (*.*)|*.*" })
                    if (ofd.ShowDialog() == WinForms.DialogResult.OK) box.Text = ofd.FileName;
            };
            txtBox = box; y += 40;
        }

        private void AddGridRow(string label, double min, double max, SysDrawing.Color c, short idx)
        {
            int r = dgvRanges.Rows.Add();
            dgvRanges.Rows[r].Cells[0].Value = label; dgvRanges.Rows[r].Cells[1].Value = min; dgvRanges.Rows[r].Cells[2].Value = max;
            dgvRanges.Rows[r].Cells[3].Style.BackColor = c; dgvRanges.Rows[r].Cells[3].Style.SelectionBackColor = c;
            dgvRanges.Rows[r].Cells[4].Value = idx;
        }

        private void DgvRanges_CellClick(object sender, WinForms.DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 3)
            {
                using (var cd = new WinForms.ColorDialog())
                {
                    if (cd.ShowDialog() == WinForms.DialogResult.OK)
                    {
                        dgvRanges.Rows[e.RowIndex].Cells[3].Style.BackColor = cd.Color;
                        dgvRanges.Rows[e.RowIndex].Cells[4].Value = ClosestACI(cd.Color);
                    }
                }
            }
        }

        private short ClosestACI(SysDrawing.Color c)
        {
            if (c.R > 200 && c.G < 50 && c.B < 50) return 1; if (c.R > 200 && c.G > 200 && c.B < 50) return 2;
            if (c.R < 50 && c.G > 200 && c.B < 50) return 3; if (c.R < 50 && c.G > 200 && c.B > 200) return 4;
            if (c.R < 50 && c.G < 50 && c.B > 200) return 5; if (c.R > 200 && c.G < 50 && c.B > 200) return 6;
            return 256;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtPath1.Text)) { WinForms.MessageBox.Show("请选择一期数据！"); return; }
            double.TryParse(txtLength.Text, out double maxEdge);
            double.TryParse(txtGrid.Text, out double gridStep);
            int mode = rbModePoint.Checked ? (int)CalcMode.PointToTin : (int)CalcMode.GridToTin;

            var ranges = new List<DiffRange>();
            foreach (WinForms.DataGridViewRow row in dgvRanges.Rows)
            {
                if (row.IsNewRow) continue;
                try { ranges.Add(new DiffRange(Convert.ToDouble(row.Cells[1].Value), Convert.ToDouble(row.Cells[2].Value), Convert.ToInt16(row.Cells[4].Value), "")); } catch { }
            }

            // 扁平化参数数组供 C++ 调用
            double[] minArr = ranges.Select(r => r.Min).ToArray();
            double[] maxArr = ranges.Select(r => r.Max).ToArray();
            short[] colArr = ranges.Select(r => r.ColorIndex).ToArray();

            this.Enabled = false;
            progressBar.Value = 0;

            try
            {
                int result = NativeTinEngine.RunTinDifferenceAnalysis(txtPath1.Text, txtPath2.Text, maxEdge, gridStep, mode, minArr, maxArr, colArr, ranges.Count, _progressCallback);
                var ed = CADApp.DocumentManager.MdiActiveDocument.Editor;

                if (result == -999) ed.WriteMessage("\n[安全拦截] 核心算力未授权！");
                else if (result >= 0) { ed.WriteMessage($"\n[C++ 引擎] TIN计算与渲染完成！生成标注 {result} 个。"); this.DialogResult = WinForms.DialogResult.OK; }
                else ed.WriteMessage("\n[异常] 底层计算失败，请检查数据文件。");
            }
            finally { this.Enabled = true; }
        }
    }
}