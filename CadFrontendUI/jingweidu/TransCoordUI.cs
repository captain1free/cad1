#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application;
#elif ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif

using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;
using SysDrawing = System.Drawing;

namespace ElevationPointsImporter
{
    // ==========================================================
    // 1. 【极客防线】C++ 底层测绘金库 P/Invoke 接口 (增加转换模式)
    // ==========================================================
    internal static class NativeTransCoordEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        [DllImport(DllName, EntryPoint = "RunCoordinateTransform", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunCoordinateTransform(
            [In] long[] handles,
            int entityCount,
            int convMode, // 【核心新增】：0:XY->XY, 1:XY->BL, 2:BL->XY, 3:BL->BL
            int srcEllip, int dstEllip,
            double L0, double falseEasting, double falseNorthing,
            double dx, double dy, double dz,
            double rx, double ry, double rz, double ppm,
            [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    // ==========================================================
    // 2. 专业测绘级 UI 面板 (带智能文本导入导出)
    // ==========================================================
    public class CoordinateTransformForm : WinForms.Form
    {
        private WinForms.ComboBox cmbMode, cmbSrcEllip, cmbDstEllip;
        private WinForms.TextBox txtL0, txtFE, txtFN;
        private WinForms.TextBox txtDx, txtDy, txtDz, txtRx, txtRy, txtRz, txtPpm;
        private WinForms.ProgressBar progressBar;
        private WinForms.Button btnTransform, btnImport, btnExport;

        public int ConvMode => cmbMode.SelectedIndex;
        public int SrcEllip => cmbSrcEllip.SelectedIndex;
        public int DstEllip => cmbDstEllip.SelectedIndex;
        public double L0 => double.Parse(txtL0.Text);
        public double FE => double.Parse(txtFE.Text);
        public double FN => double.Parse(txtFN.Text);
        public double Dx => double.Parse(txtDx.Text);
        public double Dy => double.Parse(txtDy.Text);
        public double Dz => double.Parse(txtDz.Text);
        public double Rx => double.Parse(txtRx.Text);
        public double Ry => double.Parse(txtRy.Text);
        public double Rz => double.Parse(txtRz.Text);
        public double Ppm => double.Parse(txtPpm.Text);

        public CoordinateTransformForm()
        {
            this.Text = "专业测绘坐标极速转换 (支持经纬度/平面互转)";
            this.Size = new SysDrawing.Size(600, 560); // 增加尺寸以容纳新功能
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            int margin = 15;
            int y = margin;
            int grpW = 550;

            // --- 转换模式组 ---
            var grpMode = new WinForms.GroupBox { Text = "转换模式", Left = margin, Top = y, Width = grpW, Height = 65 };
            cmbMode = new WinForms.ComboBox { Left = 15, Top = 25, Width = 300, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            cmbMode.Items.AddRange(new string[] {
                "0: 平面坐标 (XY)  ->  平面坐标 (XY)",
                "1: 平面坐标 (XY)  ->  经纬度 (BL°)",
                "2: 经纬度 (BL°)   ->  平面坐标 (XY)",
                "3: 经纬度 (BL°)   ->  经纬度 (BL°)"
            });
            cmbMode.SelectedIndex = 0;
            grpMode.Controls.Add(cmbMode);
            this.Controls.Add(grpMode); y += 75;

            // --- 椭球基准组 ---
            var grpEllip = new WinForms.GroupBox { Text = "椭球基准", Left = margin, Top = y, Width = grpW, Height = 65 };
            grpEllip.Controls.Add(new WinForms.Label { Text = "源椭球:", Left = 15, Top = 25, Width = 55 });
            cmbSrcEllip = new WinForms.ComboBox { Left = 75, Top = 22, Width = 140, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            grpEllip.Controls.Add(cmbSrcEllip);
            grpEllip.Controls.Add(new WinForms.Label { Text = "目标椭球:", Left = 230, Top = 25, Width = 65 });
            cmbDstEllip = new WinForms.ComboBox { Left = 300, Top = 22, Width = 140, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            grpEllip.Controls.Add(cmbDstEllip);
            string[] ellips = { "WGS-84", "CGCS2000", "北京54 (BJ54)", "西安80 (Xian80)" };
            cmbSrcEllip.Items.AddRange(ellips); cmbDstEllip.Items.AddRange(ellips);
            cmbSrcEllip.SelectedIndex = 0; cmbDstEllip.SelectedIndex = 1;
            this.Controls.Add(grpEllip); y += 75;

            // --- 高斯投影参数组 ---
            var grpProj = new WinForms.GroupBox { Text = "高斯投影参数", Left = margin, Top = y, Width = grpW, Height = 95 };
            grpProj.Controls.Add(new WinForms.Label { Text = "中央子午线 L0 (°):", Left = 15, Top = 25, Width = 110 });
            txtL0 = new WinForms.TextBox { Text = "114.000000", Left = 130, Top = 22, Width = 100 };
            grpProj.Controls.Add(new WinForms.Label { Text = "东偏移 FE (m):", Left = 15, Top = 58, Width = 90 });
            txtFE = new WinForms.TextBox { Text = "500000.000", Left = 110, Top = 55, Width = 120 };
            grpProj.Controls.Add(new WinForms.Label { Text = "北偏移 FN (m):", Left = 250, Top = 58, Width = 90 });
            txtFN = new WinForms.TextBox { Text = "0.000", Left = 340, Top = 55, Width = 100 };
            grpProj.Controls.Add(txtL0); grpProj.Controls.Add(txtFE); grpProj.Controls.Add(txtFN);
            this.Controls.Add(grpProj); y += 105;

            // --- 七参数组 ---
            var grp7P = new WinForms.GroupBox { Text = "Bursa-Wolf 7参数模型", Left = margin, Top = y, Width = grpW, Height = 140 };
            int pX = 15, pY = 25, wL = 60, wT = 85;

            grp7P.Controls.Add(new WinForms.Label { Text = "ΔX (m):", Left = pX, Top = pY + 3, Width = wL });
            txtDx = new WinForms.TextBox { Text = "0.000", Left = pX + wL, Top = pY, Width = wT };
            grp7P.Controls.Add(new WinForms.Label { Text = "ΔY (m):", Left = pX + 160, Top = pY + 3, Width = wL });
            txtDy = new WinForms.TextBox { Text = "0.000", Left = pX + 160 + wL, Top = pY, Width = wT };
            grp7P.Controls.Add(new WinForms.Label { Text = "ΔZ (m):", Left = pX + 320, Top = pY + 3, Width = wL });
            txtDz = new WinForms.TextBox { Text = "0.000", Left = pX + 320 + wL, Top = pY, Width = wT };

            pY += 35;
            grp7P.Controls.Add(new WinForms.Label { Text = "Rx (\"):", Left = pX, Top = pY + 3, Width = wL });
            txtRx = new WinForms.TextBox { Text = "0.0000", Left = pX + wL, Top = pY, Width = wT };
            grp7P.Controls.Add(new WinForms.Label { Text = "Ry (\"):", Left = pX + 160, Top = pY + 3, Width = wL });
            txtRy = new WinForms.TextBox { Text = "0.0000", Left = pX + 160 + wL, Top = pY, Width = wT };
            grp7P.Controls.Add(new WinForms.Label { Text = "Rz (\"):", Left = pX + 320, Top = pY + 3, Width = wL });
            txtRz = new WinForms.TextBox { Text = "0.0000", Left = pX + 320 + wL, Top = pY, Width = wT };

            pY += 35;
            grp7P.Controls.Add(new WinForms.Label { Text = "尺度 k (ppm):", Left = pX, Top = pY + 3, Width = 90 });
            txtPpm = new WinForms.TextBox { Text = "0.0000", Left = pX + 90, Top = pY, Width = wT };

            // 导入/保存按钮
            btnImport = new WinForms.Button { Text = "导入文本", Left = 350, Top = pY - 5, Width = 80, Height = 28 };
            btnExport = new WinForms.Button { Text = "保存文本", Left = 440, Top = pY - 5, Width = 80, Height = 28 };
            btnImport.Click += BtnImport_Click;
            btnExport.Click += BtnExport_Click;

            grp7P.Controls.Add(txtDx); grp7P.Controls.Add(txtDy); grp7P.Controls.Add(txtDz);
            grp7P.Controls.Add(txtRx); grp7P.Controls.Add(txtRy); grp7P.Controls.Add(txtRz);
            grp7P.Controls.Add(txtPpm); grp7P.Controls.Add(btnImport); grp7P.Controls.Add(btnExport);
            this.Controls.Add(grp7P); y += 150;

            // --- 进度条与按钮 ---
            progressBar = new WinForms.ProgressBar { Left = margin, Top = y, Width = grpW, Height = 15, Minimum = 0, Maximum = 100 };
            this.Controls.Add(progressBar); y += 30;

            btnTransform = new WinForms.Button { Text = "执行极速转换", Left = 400, Top = y, Width = 165, Height = 35 };
            btnTransform.Click += BtnTransform_Click;
            this.Controls.Add(btnTransform);
            this.AcceptButton = btnTransform;
        }

        // ==================== 智能正则参数解析黑科技 ====================
        private void BtnImport_Click(object sender, EventArgs e)
        {
            using (var ofd = new WinForms.OpenFileDialog { Filter = "文本文件 (*.txt;*.dat)|*.txt;*.dat|所有文件|*.*" })
            {
                if (ofd.ShowDialog() == WinForms.DialogResult.OK)
                {
                    try
                    {
                        string txt = File.ReadAllText(ofd.FileName);
                        txtDx.Text = ExtractRegex(txt, "Dx") ?? txtDx.Text;
                        txtDy.Text = ExtractRegex(txt, "Dy") ?? txtDy.Text;
                        txtDz.Text = ExtractRegex(txt, "Dz") ?? txtDz.Text;
                        txtRx.Text = ExtractRegex(txt, "Rx") ?? txtRx.Text;
                        txtRy.Text = ExtractRegex(txt, "Ry") ?? txtRy.Text;
                        txtRz.Text = ExtractRegex(txt, "Rz") ?? txtRz.Text;

                        // 智能判断 K 是尺度因子(接近1) 还是 PPM
                        string kStr = ExtractRegex(txt, "K") ?? ExtractRegex(txt, "Scale");
                        if (kStr != null && double.TryParse(kStr, out double k))
                        {
                            if (k > 0.5 && k < 1.5) txtPpm.Text = ((k - 1.0) * 1000000.0).ToString("F6"); // 是 0.9999 这种格式
                            else txtPpm.Text = k.ToString("F6"); // 已经是 PPM
                        }
                        WinForms.MessageBox.Show("参数导入并自动换算成功！", "成功", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                    }
                    catch { WinForms.MessageBox.Show("读取文件失败。", "错误"); }
                }
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (var sfd = new WinForms.SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "7Params.txt" })
            {
                if (sfd.ShowDialog() == WinForms.DialogResult.OK)
                {
                    double.TryParse(txtPpm.Text, out double ppm);
                    double k = 1.0 + ppm / 1000000.0; // 还原为专业尺度因子
                    string content = $"Dx = {txtDx.Text},\r\nDy = {txtDy.Text},\r\nDz = {txtDz.Text},\r\n" +
                                     $"Rx = {txtRx.Text},\r\nRy = {txtRy.Text},\r\nRz = {txtRz.Text},\r\n" +
                                     $"K = {k:F11}";
                    File.WriteAllText(sfd.FileName, content);
                    WinForms.MessageBox.Show("参数已成功导出！", "成功");
                }
            }
        }

        private string ExtractRegex(string source, string key)
        {
            var match = Regex.Match(source, $@"{key}\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public void SetProgress(int percent)
        {
            if (this.IsHandleCreated) this.Invoke((WinForms.MethodInvoker)delegate {
                progressBar.Value = Math.Min(100, Math.Max(0, percent));
                WinForms.Application.DoEvents();
            });
        }

        private void BtnTransform_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(txtL0.Text, out _) || !double.TryParse(txtDx.Text, out _))
            {
                WinForms.MessageBox.Show("参数格式有误，请输入正确的浮点数！", "错误", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
                return;
            }
            this.DialogResult = WinForms.DialogResult.OK;
        }
    }

    // ==========================================================
    // 3. CAD 命令入口调度器
    // ==========================================================
    public class CoordinateToolCommand
    {
        private NativeTransCoordEngine.ProgressCallbackDelegate _progressCallback;
        private CoordinateTransformForm _uiForm;

        public CoordinateToolCommand()
        {
            _progressCallback = new NativeTransCoordEngine.ProgressCallbackDelegate(OnProgressUpdate);
        }

        private void OnProgressUpdate(int percent)
        {
            if (_uiForm != null && _uiForm.Visible) _uiForm.SetProgress(percent);
        }

        [CommandMethod("TRANSCOORD_FAST", CommandFlags.Modal)]
        public void BatchTransformCoordinates()
        {
            Document doc = CADApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                PromptSelectionOptions selOpts = new PromptSelectionOptions();
                selOpts.MessageForAdding = "\n请框选需要进行坐标系转换的对象: ";
                PromptSelectionResult selRes = ed.GetSelection(selOpts);

                if (selRes.Status != PromptStatus.OK || selRes.Value.Count == 0) return;

                long[] handles = selRes.Value.GetObjectIds().Select(id => id.Handle.Value).ToArray();

                _uiForm = new CoordinateTransformForm();
                if (CADApp.ShowModalDialog(_uiForm) != WinForms.DialogResult.OK)
                {
                    _uiForm.Dispose();
                    return;
                }

                // 提取表单严谨参数
                int mode = _uiForm.ConvMode;
                int src = _uiForm.SrcEllip; int dst = _uiForm.DstEllip;
                double l0 = _uiForm.L0; double fe = _uiForm.FE; double fn = _uiForm.FN;
                double dx = _uiForm.Dx; double dy = _uiForm.Dy; double dz = _uiForm.Dz;
                double rx = _uiForm.Rx; double ry = _uiForm.Ry; double rz = _uiForm.Rz;
                double ppm = _uiForm.Ppm;

                _uiForm.Enabled = false;
                ed.WriteMessage($"\n[前端调度] 将 {handles.Length} 个图元移交 C++ 金库极速转换...\n");

                // 跨界打击
                int successCount = NativeTransCoordEngine.RunCoordinateTransform(
                    handles, handles.Length,
                    mode, src, dst, l0, fe, fn,
                    dx, dy, dz, rx, ry, rz, ppm,
                    _progressCallback);

                if (successCount == -999) ed.WriteMessage("\n[安全拦截] 核心算力未授权！");
                else if (successCount >= 0) ed.WriteMessage($"\n[C++ 引擎] 坐标转换完成！成功处理 {successCount} 个图元。");

                _uiForm.Dispose();
                ed.UpdateScreen();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[前端异常]: {ex.Message}");
            }
        }
    }
}