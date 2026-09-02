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

namespace ElevationPointsImporter
{
    /// <summary>
    /// 【极客防线】底层 C++ 引擎跨端调用接口 (按高程设色专用 - 真彩升级版)
    /// </summary>
    internal static class NativeEngineColor
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        // P/Invoke：暴露给 C++ 的标准 C 接口，传入真彩色 RGB 数组 (int[])
        [DllImport(DllName, EntryPoint = "RunElevPointsColorImporter", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunElevPointsColorImporter(
            string filePath,
            double textHeight,
            double minDist,
            int decimalPlaces,
            [In] double[] minZ,
            [In] double[] maxZ,
            [In] int[] colors, // 核心修改：short[] -> int[] 以支持 RGB
            int rangeCount,
            [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    // ==========================================================
    // 1. 数据结构 (真彩色升级)
    // ==========================================================
    public class ColorElevationRange
    {
        public double MinElevation { get; set; }
        public double MaxElevation { get; set; }
        public string ColorName { get; set; }
        public int ColorRGB { get; set; } // 核心修改：使用 24 位 RGB 整数
    }

    // ==========================================================
    // 2. 命令入口
    // ==========================================================
    public class ElevPointsColorImporterEntry
    {
        [CommandMethod("ELEVPOINTSCOLORDAT", CommandFlags.Modal)]
        public void ImportColorDat() => ExecuteImport("选择DAT数据文件", "DAT文件 (*.dat)|*.dat|文本文件 (*.xyz)|*.xyz");

        [CommandMethod("EPCOLORDAT", CommandFlags.Modal)]
        public void AliasDat() => ImportColorDat();

        private void ExecuteImport(string title, string filter)
        {
            var ed = CADApp.DocumentManager.MdiActiveDocument.Editor;
            try
            {
                PromptOpenFileOptions fileOpts = new PromptOpenFileOptions(title) { Filter = filter, DialogName = title };
                PromptFileNameResult fileResult = ed.GetFileNameForOpen(fileOpts);
                if (fileResult.Status != PromptStatus.OK) return;

                using (var form = new ElevationColorForm(fileResult.StringResult))
                {
                    CADApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[界面异常] 无法加载高程点导入面板: {ex.Message}\n");
            }
        }
    }

    // ==========================================================
    // 3. 纯前端 UI 窗体
    // ==========================================================
    public partial class ElevationColorForm : WinForms.Form
    {
        private string _targetFilePath;
        private WinForms.DataGridView dataGridView;
        private WinForms.Button btnAddRange, btnRemoveRange, btnOK, btnCancel;
        private WinForms.Button btnSaveRange, btnLoadRange;
        private WinForms.TextBox txtSpacing, txtHeight;
        private WinForms.ComboBox cmbDecimals;
        private WinForms.ProgressBar progressBar;

        // 【安全守卫】强引用委托，防止 GC 回收导致 C++ 回调崩溃
        private NativeEngineColor.ProgressCallbackDelegate _progressCallback;

        public ElevationColorForm(string filePath)
        {
            _targetFilePath = filePath;
            InitializeComponent();
            _progressCallback = new NativeEngineColor.ProgressCallbackDelegate(OnProgressUpdate);
        }

        private void InitializeComponent()
        {
            this.Size = new SysDrawing.Size(600, 520);
            this.Text = "高程点分段设色导入 (C++ 极速真彩版)";
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            int x = 15;
            var grpSettings = new WinForms.GroupBox { Text = "通用设置", Location = new SysDrawing.Point(10, 10), Size = new SysDrawing.Size(560, 80) };
            this.Controls.Add(grpSettings);

            grpSettings.Controls.Add(new WinForms.Label { Text = "文字高度:", Location = new SysDrawing.Point(x, 25), Size = new SysDrawing.Size(60, 20) });
            txtHeight = new WinForms.TextBox { Text = "0.5", Location = new SysDrawing.Point(x + 65, 22), Size = new SysDrawing.Size(60, 20) };
            grpSettings.Controls.Add(txtHeight);

            grpSettings.Controls.Add(new WinForms.Label { Text = "小数位:", Location = new SysDrawing.Point(x + 150, 25), Size = new SysDrawing.Size(50, 20) });
            cmbDecimals = new WinForms.ComboBox { Location = new SysDrawing.Point(x + 200, 22), Size = new SysDrawing.Size(60, 20), DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            for (int i = 0; i <= 6; i++) cmbDecimals.Items.Add(i.ToString());
            cmbDecimals.SelectedIndex = 3;
            grpSettings.Controls.Add(cmbDecimals);

            grpSettings.Controls.Add(new WinForms.Label { Text = "抽希间距(m):", Location = new SysDrawing.Point(x + 290, 25), Size = new SysDrawing.Size(80, 20) });
            txtSpacing = new WinForms.TextBox { Text = "0", Location = new SysDrawing.Point(x + 370, 22), Size = new SysDrawing.Size(60, 20) };
            grpSettings.Controls.Add(txtSpacing);

            this.Controls.Add(new WinForms.Label { Text = "高程分段设色 (真彩支持):", Location = new SysDrawing.Point(10, 100), Size = new SysDrawing.Size(200, 20) });
            dataGridView = new WinForms.DataGridView { Location = new SysDrawing.Point(10, 125), Size = new SysDrawing.Size(560, 250), AutoGenerateColumns = false, AllowUserToAddRows = false, SelectionMode = WinForms.DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
            dataGridView.Columns.Add(new WinForms.DataGridViewTextBoxColumn { HeaderText = "最小高程", Width = 120 });
            dataGridView.Columns.Add(new WinForms.DataGridViewTextBoxColumn { HeaderText = "最大高程", Width = 120 });
            dataGridView.Columns.Add(new WinForms.DataGridViewTextBoxColumn { HeaderText = "颜色代码", Width = 120 });
            dataGridView.Columns.Add(new WinForms.DataGridViewButtonColumn { HeaderText = "选择", Text = "...", UseColumnTextForButtonValue = true, Width = 80, Name = "SelectColor" });
            this.Controls.Add(dataGridView);
            dataGridView.CellClick += DataGridView_CellClick;

            progressBar = new WinForms.ProgressBar { Location = new SysDrawing.Point(10, 390), Size = new SysDrawing.Size(560, 15), Minimum = 0, Maximum = 100 };
            this.Controls.Add(progressBar);

            btnAddRange = new WinForms.Button { Text = "添加分段", Location = new SysDrawing.Point(10, 430), Size = new SysDrawing.Size(80, 30) };
            btnAddRange.Click += (s, e) => { var r = new WinForms.DataGridViewRow(); r.CreateCells(dataGridView); r.Cells[0].Value = "0"; r.Cells[1].Value = "0"; r.Cells[2].Value = "#FFFFFF"; dataGridView.Rows.Add(r); };
            this.Controls.Add(btnAddRange);

            btnRemoveRange = new WinForms.Button { Text = "删除选中", Location = new SysDrawing.Point(100, 430), Size = new SysDrawing.Size(80, 30) };
            btnRemoveRange.Click += (s, e) => { if (dataGridView.SelectedRows.Count > 0) dataGridView.Rows.Remove(dataGridView.SelectedRows[0]); };
            this.Controls.Add(btnRemoveRange);

            btnSaveRange = new WinForms.Button { Text = "保存颜色分段", Location = new SysDrawing.Point(190, 430), Size = new SysDrawing.Size(90, 30) };
            btnSaveRange.Click += BtnSaveRange_Click;
            this.Controls.Add(btnSaveRange);

            btnLoadRange = new WinForms.Button { Text = "加载颜色分段", Location = new SysDrawing.Point(285, 430), Size = new SysDrawing.Size(90, 30) };
            btnLoadRange.Click += BtnLoadRange_Click;
            this.Controls.Add(btnLoadRange);

            btnOK = new WinForms.Button { Text = "极速导入", Location = new SysDrawing.Point(390, 430), Size = new SysDrawing.Size(80, 30) };
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new WinForms.Button { Text = "取消", Location = new SysDrawing.Point(490, 430), Size = new SysDrawing.Size(80, 30), DialogResult = WinForms.DialogResult.Cancel };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOK; this.CancelButton = btnCancel;
        }

        private void OnProgressUpdate(int percent)
        {
            if (this.IsHandleCreated)
            {
                this.Invoke((WinForms.MethodInvoker)delegate {
                    progressBar.Value = Math.Min(100, Math.Max(0, percent));
                    WinForms.Application.DoEvents(); // 强制释放消息队列，防止假死
                });
            }
        }

        private void DataGridView_CellClick(object sender, WinForms.DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 3 && e.RowIndex >= 0)
            {
                using (var cd = new WinForms.ColorDialog())
                {
                    cd.FullOpen = true; // 允许用户自定义任意真彩色
                    if (cd.ShowDialog() == WinForms.DialogResult.OK)
                    {
                        dataGridView.Rows[e.RowIndex].Cells[2].Value = SysDrawing.ColorTranslator.ToHtml(cd.Color);
                        dataGridView.Rows[e.RowIndex].Cells[2].Style.BackColor = cd.Color;
                    }
                }
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(txtHeight.Text, out double h) || h <= 0) { Msg("文字高度无效"); return; }
            if (!double.TryParse(txtSpacing.Text, out double s) || s < 0) { Msg("抽希间距无效"); return; }

            var ranges = new List<ColorElevationRange>();
            foreach (WinForms.DataGridViewRow row in dataGridView.Rows)
            {
                try
                {
                    double min = Convert.ToDouble(row.Cells[0].Value ?? "0");
                    double max = Convert.ToDouble(row.Cells[1].Value ?? "0");
                    if (min > max) (min, max) = (max, min);

                    // 提取颜色的核心逻辑修正：直接获取底色并编码为 int
                    SysDrawing.Color cellColor = row.Cells[2].Style.BackColor;
                    string cName = row.Cells[2].Value?.ToString() ?? "";

                    // 防御性：如果没有直接通过面板选取，尝试从输入的十六进制解析
                    if (cellColor.IsEmpty || cellColor.A == 0)
                    {
                        if (cName.StartsWith("#")) { try { cellColor = SysDrawing.ColorTranslator.FromHtml(cName); } catch { cellColor = SysDrawing.Color.White; } }
                        else { cellColor = SysDrawing.Color.White; } // 默认兜底为白色
                    }

                    // 将 R, G, B 压入一个 32位整型中传给 C++
                    int rgb = (cellColor.R << 16) | (cellColor.G << 8) | cellColor.B;

                    ranges.Add(new ColorElevationRange { MinElevation = min, MaxElevation = max, ColorRGB = rgb });
                }
                catch { Msg($"第 {row.Index + 1} 行数据无效"); return; }
            }

            // 扁平化数据封送，C++ 最喜欢的极简数组结构
            double[] minZArray = ranges.Select(r => r.MinElevation).ToArray();
            double[] maxZArray = ranges.Select(r => r.MaxElevation).ToArray();
            int[] colorsArray = ranges.Select(r => r.ColorRGB).ToArray();

            btnOK.Enabled = false;
            progressBar.Value = 0;

            try
            {
                int dec = cmbDecimals.SelectedIndex;
                int result = NativeEngineColor.RunElevPointsColorImporter(_targetFilePath, h, s, dec, minZArray, maxZArray, colorsArray, ranges.Count, _progressCallback);

                if (result == -999) CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[Security] 未授权的引擎调用！");
                else if (result >= 0)
                {
                    var doc = CADApp.DocumentManager.MdiActiveDocument;
                    doc.Editor.WriteMessage($"\n[Engine] 极速设色渲染完成！共处理 {result} 个点位，全面启用真彩色渲染！");
                    this.DialogResult = WinForms.DialogResult.OK;

                    // =====================================================================
                    // 👉 【终极体验修复】：静默发送“缩放至范围 (Zoom Extents)”命令
                    // true, false, false 的参数组合保证了命令在后台队列安全、无回显地执行
                    // =====================================================================
                    doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
                }
                else Msg("读取文件失败，请检查文件是否被占用。");
            }
            catch (System.Exception ex) { CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[前端异常]: {ex.Message}"); }
            finally { btnOK.Enabled = true; }
        }

        private void BtnSaveRange_Click(object sender, EventArgs e)
        {
            using (var sfd = new WinForms.SaveFileDialog
            {
                Filter = "颜色分段配置 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = "颜色分段配置.txt"
            })
            {
                if (sfd.ShowDialog() != WinForms.DialogResult.OK) return;

                var lines = new List<string>
                {
                    "TextHeight=" + txtHeight.Text,
                    "DecimalPlaces=" + cmbDecimals.SelectedIndex,
                    "Spacing=" + txtSpacing.Text
                };
                foreach (WinForms.DataGridViewRow row in dataGridView.Rows)
                {
                    string min = row.Cells[0].Value?.ToString() ?? "";
                    string max = row.Cells[1].Value?.ToString() ?? "";
                    string hex = row.Cells[2].Value?.ToString() ?? "";
                    lines.Add("Range=" + min + "," + max + "," + hex);
                }

                try
                {
                    System.IO.File.WriteAllLines(sfd.FileName, lines);
                    Msg("颜色分段配置已保存！");
                }
                catch (System.Exception ex)
                {
                    Msg("保存失败：" + ex.Message);
                }
            }
        }

        private void BtnLoadRange_Click(object sender, EventArgs e)
        {
            using (var ofd = new WinForms.OpenFileDialog
            {
                Filter = "颜色分段配置 (*.txt)|*.txt|所有文件 (*.*)|*.*"
            })
            {
                if (ofd.ShowDialog() != WinForms.DialogResult.OK) return;

                try
                {
                    var lines = System.IO.File.ReadAllLines(ofd.FileName);
                    dataGridView.Rows.Clear();

                    foreach (var line in lines)
                    {
                        string s = line.Trim();
                        if (string.IsNullOrEmpty(s) || s.StartsWith("#")) continue;

                        if (s.StartsWith("TextHeight="))
                        {
                            txtHeight.Text = s.Substring("TextHeight=".Length);
                        }
                        else if (s.StartsWith("DecimalPlaces="))
                        {
                            int idx;
                            if (int.TryParse(s.Substring("DecimalPlaces=".Length), out idx) && idx >= 0 && idx < cmbDecimals.Items.Count)
                                cmbDecimals.SelectedIndex = idx;
                        }
                        else if (s.StartsWith("Spacing="))
                        {
                            txtSpacing.Text = s.Substring("Spacing=".Length);
                        }
                        else if (s.StartsWith("Range="))
                        {
                            string[] parts = s.Substring("Range=".Length).Split(',');
                            if (parts.Length >= 3)
                            {
                                var r = new WinForms.DataGridViewRow();
                                r.CreateCells(dataGridView);
                                r.Cells[0].Value = parts[0];
                                r.Cells[1].Value = parts[1];
                                r.Cells[2].Value = parts[2];
                                if (parts[2].StartsWith("#"))
                                {
                                    try { r.Cells[2].Style.BackColor = SysDrawing.ColorTranslator.FromHtml(parts[2]); } catch { }
                                }
                                dataGridView.Rows.Add(r);
                            }
                        }
                    }

                    Msg("颜色分段配置已加载！");
                }
                catch (System.Exception ex)
                {
                    Msg("加载失败：" + ex.Message);
                }
            }
        }

        private void Msg(string txt) => WinForms.MessageBox.Show(txt, "提示", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
    }
}