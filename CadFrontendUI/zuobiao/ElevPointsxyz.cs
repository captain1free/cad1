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
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;
using SysDrawing = System.Drawing;

namespace ElevationPointsImporter
{
    /// <summary>
    /// 【极客防线】底层 C++ 引擎跨端调用接口
    /// </summary>
    internal static class NativeEngineXYZ
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        // 定义供 C++ 调用的回调委托，用于高频进度上报
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        // P/Invoke：暴露给 C++ 的标准 C 接口
        // 注意：此处专为 XYZ 导入优化的引擎入口
        [DllImport(DllName, EntryPoint = "RunElevPointsXYZImporter", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunElevPointsXYZImporter(
            string filePath, double textHeight, double minDist, int decimalPlaces,
            [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    /// <summary>
    /// XYZ高程点导入设置 UI (已重构为纯前端视图)
    /// </summary>
    public class ImportOptionsFormXYZ : WinForms.Form
    {
        private WinForms.TextBox txtPath;
        private WinForms.TextBox txtHeight;
        private WinForms.TextBox txtSpacing;
        private WinForms.ComboBox cmbDecimals;

        // 【核心架构】跨语言进度条控件
        private WinForms.ProgressBar progressBar;

        private WinForms.Button btnOk;
        private WinForms.Button btnCancel;
        private WinForms.Button btnBrowse;

        // 【安全守卫】必须保持强引用，防止 GC 意外回收导致 C++ 调用野指针崩溃！
        private NativeEngineXYZ.ProgressCallbackDelegate _progressCallback;

        public ImportOptionsFormXYZ()
        {
            InitializeComponent();
            _progressCallback = new NativeEngineXYZ.ProgressCallbackDelegate(OnProgressUpdate);
        }

        private void InitializeComponent()
        {
            this.Text = "XYZ高程点极速导入 (C++ 引擎版)";
            this.Size = new SysDrawing.Size(460, 320);
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            int x = 20, y = 20;
            int labelW = 100, inputW = 120;

            // 1. 文件选择
            this.Controls.Add(new WinForms.Label { Text = "数据文件:", Left = x, Top = y + 3, Width = labelW });
            txtPath = new WinForms.TextBox { Left = x + labelW, Top = y, Width = 230, ReadOnly = true };
            btnBrowse = new WinForms.Button { Text = "...", Left = x + labelW + 235, Top = y - 1, Width = 40, Height = 23 };
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new WinForms.OpenFileDialog { Filter = "XYZ数据文件 (*.xyz;*.txt;*.dat;*.csv)|*.xyz;*.txt;*.dat;*.csv|所有文件|*.*" })
                    if (dlg.ShowDialog() == WinForms.DialogResult.OK) txtPath.Text = dlg.FileName;
            };
            this.Controls.Add(txtPath); this.Controls.Add(btnBrowse);
            y += 40;

            // 2. 文字高度
            this.Controls.Add(new WinForms.Label { Text = "文字高度:", Left = x, Top = y + 3, Width = labelW });
            txtHeight = new WinForms.TextBox { Text = "0.5", Left = x + labelW, Top = y, Width = inputW };
            this.Controls.Add(txtHeight);
            y += 40;

            // 3. 抽希间距
            this.Controls.Add(new WinForms.Label { Text = "抽希距离(m):", Left = x, Top = y + 3, Width = labelW });
            txtSpacing = new WinForms.TextBox { Text = "0", Left = x + labelW, Top = y, Width = inputW };
            this.Controls.Add(txtSpacing);
            this.Controls.Add(new WinForms.Label { Text = "(0为不抽希)", Left = x + labelW + inputW + 5, Top = y + 3, Width = 100, ForeColor = SysDrawing.Color.Gray });
            y += 40;

            // 4. 小数位数
            this.Controls.Add(new WinForms.Label { Text = "小数位数:", Left = x, Top = y + 3, Width = labelW });
            cmbDecimals = new WinForms.ComboBox { Left = x + labelW, Top = y, Width = inputW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            for (int i = 0; i <= 6; i++) cmbDecimals.Items.Add(i.ToString());
            cmbDecimals.SelectedIndex = 3;
            this.Controls.Add(cmbDecimals);
            y += 45;

            // 5. 进度条
            progressBar = new WinForms.ProgressBar { Left = x, Top = y, Width = 400, Height = 15, Minimum = 0, Maximum = 100 };
            this.Controls.Add(progressBar);
            y += 35;

            // 6. 按钮
            btnOk = new WinForms.Button { Text = "确 定", Left = 220, Top = y, Width = 90, Height = 30 };
            btnCancel = new WinForms.Button { Text = "取 消", Left = 320, Top = y, Width = 90, Height = 30, DialogResult = WinForms.DialogResult.Cancel };
            btnOk.Click += BtnOk_Click;

            this.Controls.Add(btnOk); this.Controls.Add(btnCancel);
            this.AcceptButton = btnOk; this.CancelButton = btnCancel;
        }

        // C++ 引擎发来的进度通知，需编排到主 UI 线程
        private void OnProgressUpdate(int percent)
        {
            if (this.IsHandleCreated)
            {
                this.Invoke((WinForms.MethodInvoker)delegate {
                    progressBar.Value = Math.Min(100, Math.Max(0, percent));
                    WinForms.Application.DoEvents(); // 触发重绘，防止假死
                });
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPath.Text)) { Msg("请选择文件"); return; }
            if (!double.TryParse(txtHeight.Text, out double h) || h <= 0) { Msg("高度无效"); return; }
            if (!double.TryParse(txtSpacing.Text, out double s) || s < 0) { Msg("间距无效"); return; }

            btnOk.Enabled = false; // 防抖，拦截二次点击
            progressBar.Value = 0;

            try
            {
                int dec = cmbDecimals.SelectedIndex;

                // 将数据压入 C++ 引擎计算
                int result = NativeEngineXYZ.RunElevPointsXYZImporter(txtPath.Text, h, s, dec, _progressCallback);

                if (result == -999)
                {
                    CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[安全守卫] 未授权的调用！");
                }
                else if (result >= 0)
                {
                    CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[引擎日志] 极速导入完成！成功处理 {result} 个点位数据。");
                    this.DialogResult = WinForms.DialogResult.OK; // 成功则自动关闭窗口
                }
                else
                {
                    Msg("读取文件失败，请检查文件是否被占用。");
                }
            }
            catch (System.Exception ex)
            {
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[前端异常] 跨端调用失败: {ex.Message}");
            }
            finally
            {
                btnOk.Enabled = true;
            }
        }

        private void Msg(string txt) => WinForms.MessageBox.Show(txt, "提示", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
    }

    /// <summary>
    /// 命令注册器
    /// </summary>
    public class ElevPointsXYZImporterEntry
    {
        [CommandMethod("ELEVPOINTSXYZ", CommandFlags.Modal)]
        public void ImportElevationPointsXYZ()
        {
            try
            {
                using (var form = new ImportOptionsFormXYZ())
                {
                    CADApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[界面异常] 无法加载高程点导入面板: {ex.Message}\n");
            }
        }

        [CommandMethod("EPXYZ", CommandFlags.Modal)]
        public void ImportElevationPointsXYZAlias()
        {
            ImportElevationPointsXYZ();
        }

        [CommandMethod("HELP_ELEVPOINTSXYZ", CommandFlags.Modal)]
        public void ShowHelp()
        {
            Editor ed = CADApp.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n--- XYZ高程点极速导入说明 ---");
            ed.WriteMessage("\n1. 输入命令 ELEVPOINTSXYZ 或 EPXYZ 弹出极速导入面板。");
            ed.WriteMessage("\n2. 底层采用 C++ 17 零拷贝解析与 O(1) 空间哈希算法。");
            ed.WriteMessage("\n3. 抽希间距设为 0 表示导入所有点。");
        }
    }
}