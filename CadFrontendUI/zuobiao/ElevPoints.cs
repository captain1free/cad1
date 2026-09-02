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

namespace CadFrontendUI.Commands
{
    internal static class NativeEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        // 【核心新增】定义供 C++ 调用的委托函数签名
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        // P/Invoke：暴露 callback 参数
        [DllImport(DllName, EntryPoint = "RunElevPointsImporter", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunElevPointsImporter(
            string filePath, double textHeight, double minDist, int decimalPlaces, int pointType,
            [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    public class ElevPointsForm : WinForms.Form
    {
        private WinForms.TextBox txtPath;
        private WinForms.TextBox txtHeight;
        private WinForms.TextBox txtMinDist;
        private WinForms.ComboBox cmbDecimals;
        private WinForms.RadioButton rdoText;
        private WinForms.RadioButton rdoCass;

        // 【核心新增】进度条控件
        private WinForms.ProgressBar progressBar;

        private WinForms.Button btnOk;
        private WinForms.Button btnCancel;

        // 必须保持一个对委托的强引用，防止被 C# 的垃圾回收器(GC)意外回收！
        private NativeEngine.ProgressCallbackDelegate _progressCallback;

        public ElevPointsForm()
        {
            InitializeComponent();
            _progressCallback = new NativeEngine.ProgressCallbackDelegate(OnProgressUpdate);
        }

        private void InitializeComponent()
        {
            this.Text = "坐标展点";
            this.Size = new SysDrawing.Size(460, 430); // 增加高度以容纳进度条
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            int x = 20, y = 20, labelW = 80, inputW = 200;

            // 1. 数据文件
            this.Controls.Add(new WinForms.Label { Text = "数据文件:", Left = x, Top = y + 3, Width = labelW });
            txtPath = new WinForms.TextBox { Left = x + labelW, Top = y, Width = inputW };
            var btnBrowse = new WinForms.Button { Text = "...", Left = x + labelW + inputW + 10, Top = y - 1, Width = 40, Height = 23 };
            btnBrowse.Click += (s, e) =>
            {
                using (var ofd = new WinForms.OpenFileDialog { Filter = "数据文件 (*.dat;*.xyz)|*.dat;*.xyz" })
                {
                    if (ofd.ShowDialog() == WinForms.DialogResult.OK) txtPath.Text = ofd.FileName;
                }
            };
            this.Controls.Add(txtPath); this.Controls.Add(btnBrowse);
            y += 40;

            this.Controls.Add(new WinForms.Label { Text = "文字高度:", Left = x, Top = y + 3, Width = labelW });
            txtHeight = new WinForms.TextBox { Left = x + labelW, Top = y, Width = inputW, Text = "2.0" };
            this.Controls.Add(txtHeight);
            y += 40;

            this.Controls.Add(new WinForms.Label { Text = "最小间距:", Left = x, Top = y + 3, Width = labelW });
            txtMinDist = new WinForms.TextBox { Left = x + labelW, Top = y, Width = inputW, Text = "1.0" };
            this.Controls.Add(txtMinDist);
            y += 40;

            this.Controls.Add(new WinForms.Label { Text = "小数位数:", Left = x, Top = y + 3, Width = labelW });
            cmbDecimals = new WinForms.ComboBox { Left = x + labelW, Top = y, Width = inputW, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            for (int i = 0; i <= 6; i++) cmbDecimals.Items.Add(i.ToString());
            cmbDecimals.SelectedIndex = 3;
            this.Controls.Add(cmbDecimals);
            y += 40;

            this.Controls.Add(new WinForms.Label { Text = "点位格式:", Left = x, Top = y + 3, Width = labelW });
            rdoText = new WinForms.RadioButton { Text = "展纯文字高程 (AcDbText)", Left = x + labelW, Top = y, Width = inputW + 50 };
            rdoText.Checked = true;
            this.Controls.Add(rdoText);
            y += 25;
            rdoCass = new WinForms.RadioButton { Text = "CASS高程点 (块参照 GC200)", Left = x + labelW, Top = y, Width = inputW + 50 };
            this.Controls.Add(rdoCass);
            y += 40;

            // 【核心新增】进度条
            progressBar = new WinForms.ProgressBar { Left = x, Top = y, Width = 400, Height = 20, Minimum = 0, Maximum = 100 };
            this.Controls.Add(progressBar);
            y += 35;

            btnOk = new WinForms.Button { Text = "确 定", Left = 130, Top = y, Width = 90, Height = 30 };
            btnCancel = new WinForms.Button { Text = "取 消", Left = 240, Top = y, Width = 90, Height = 30, DialogResult = WinForms.DialogResult.Cancel };
            btnOk.Click += BtnOk_Click;

            this.Controls.Add(btnOk); this.Controls.Add(btnCancel);
            this.AcceptButton = btnOk; this.CancelButton = btnCancel;
        }

        // C++ 发来贺电时的回调函数
        private void OnProgressUpdate(int percent)
        {
            if (this.IsHandleCreated)
            {
                // 将进度条更新压入 UI 线程，避免跨线程报错并让 UI 实时刷新
                this.Invoke((WinForms.MethodInvoker)delegate {
                    progressBar.Value = Math.Min(100, Math.Max(0, percent));
                    // 强制界面重绘，给用户丝滑体验
                    WinForms.Application.DoEvents();
                });
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPath.Text)) { WinForms.MessageBox.Show("请选择数据文件！", "提示"); return; }
            if (!double.TryParse(txtHeight.Text, out double h) || h <= 0) { WinForms.MessageBox.Show("文字高度必须为正数！", "提示"); return; }
            if (!double.TryParse(txtMinDist.Text, out double md) || md < 0) { WinForms.MessageBox.Show("最小间距不能为负数！", "提示"); return; }

            int dec = cmbDecimals.SelectedIndex;
            int pointType = rdoCass.Checked ? 1 : 0;

            btnOk.Enabled = false; // 防连点
            progressBar.Value = 0;

            try
            {
                // 将用户的选择以及 回调委托 压入 C++ 金库
                int result = NativeEngine.RunElevPointsImporter(txtPath.Text, h, md, dec, pointType, _progressCallback);

                if (result == -999)
                {
                    CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[安全守卫] 未授权的调用！");
                }
                else if (result > 0)
                {
                    // === 【优化点】：获取当前文档，输出日志后发送范围缩放命令 ===
                    var doc = CADApp.DocumentManager.MdiActiveDocument;
                    doc.Editor.WriteMessage($"\n[引擎日志] 极速导入完成！成功处理 {result} 个点位数据。");
                    this.DialogResult = WinForms.DialogResult.OK; // 处理完自动关闭

                    // 静默发送“缩放至范围 (Zoom Extents)”命令，完美用户体验
                    doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
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
    }

    public class ElevPointsCommandEntry
    {
        [CommandMethod("ELEVPOINTS")]
        public void ShowElevPointsUI()
        {
            try
            {
                using (var form = new ElevPointsForm())
                {
                    CADApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[界面异常] 无法加载高程点导入面板: {ex.Message}\n");
            }
        }
    }
}