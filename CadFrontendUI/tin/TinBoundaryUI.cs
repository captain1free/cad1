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
using System.Drawing;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace ZwCadTinAnalysis
{
    // ==========================================================
    // 1. 【极客防线】C++ 底层金库 P/Invoke 接口
    // ==========================================================
    internal static class NativeTinBoundaryEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        [DllImport(DllName, EntryPoint = "RunTinBoundaryGeneration", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunTinBoundaryGeneration(
            string filePath,
            double maxEdgeLength,
            ProgressCallbackDelegate callback);
    }

    // ==========================================================
    // 2. WinForms 窗体交互层 (纯代码驱动，摒弃 Designer)
    // ==========================================================
    public partial class TinBoundaryUI : WinForms.Form
    {
        // --- 核心控件声明 ---
        private WinForms.Label lblFilePath;
        private WinForms.TextBox txtFilePath;
        private WinForms.Button btnBrowse;
        private WinForms.Label lblMaxEdge;
        private WinForms.TextBox txtMaxEdge;
        private WinForms.Label lblUnit;
        private WinForms.ProgressBar progressBar;
        private WinForms.Button btnGenerate;

        public TinBoundaryUI()
        {
            // 1. 核心组装：初始化纯代码 UI 界面
            InitializeComponent();

            // 2. 事件绑定
            btnBrowse.Click += BtnBrowse_Click;
            btnGenerate.Click += BtnGenerate_Click;
        }

        /// <summary>
        /// 手写 UI 引擎：精确控制像素级坐标与样式，永不依赖 VS 设计器
        /// </summary>
        private void InitializeComponent()
        {
            this.lblFilePath = new WinForms.Label();
            this.txtFilePath = new WinForms.TextBox();
            this.btnBrowse = new WinForms.Button();
            this.lblMaxEdge = new WinForms.Label();
            this.txtMaxEdge = new WinForms.TextBox();
            this.lblUnit = new WinForms.Label();
            this.progressBar = new WinForms.ProgressBar();
            this.btnGenerate = new WinForms.Button();

            this.SuspendLayout();

            // --- 窗体全局设置 ---
            this.Text = "地形边界提取";
            this.ClientSize = new Size(480, 230);
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.Font = new Font("微软雅黑", 9F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));

            // --- 数据文件路径区域 ---
            this.lblFilePath.AutoSize = true;
            this.lblFilePath.Location = new Point(20, 25);
            this.lblFilePath.Text = "离散点云数据文件 (*.txt, *.csv):";

            this.txtFilePath.Location = new Point(20, 50);
            this.txtFilePath.Size = new Size(350, 23);
            this.txtFilePath.ReadOnly = true;
            this.txtFilePath.BackColor = Color.White;

            this.btnBrowse.Location = new Point(380, 48);
            this.btnBrowse.Size = new Size(80, 27);
            this.btnBrowse.Text = "浏览(&B)...";
            this.btnBrowse.UseVisualStyleBackColor = true;

            // --- 边长参数设置区域 ---
            this.lblMaxEdge.AutoSize = true;
            this.lblMaxEdge.Location = new Point(20, 100);
            this.lblMaxEdge.Text = "边界收缩最大边长阈值:";

            this.txtMaxEdge.Location = new Point(160, 97);
            this.txtMaxEdge.Size = new Size(80, 23);
            this.txtMaxEdge.Text = "50.0"; // 默认初始值

            this.lblUnit.AutoSize = true;
            this.lblUnit.Location = new Point(250, 100);
            this.lblUnit.Text = "米 (自动剔除大于此边长的外部网格)";
            this.lblUnit.ForeColor = Color.DimGray;

            // --- 进度条与执行按钮 ---
            this.progressBar.Location = new Point(20, 140);
            this.progressBar.Size = new Size(440, 10);
            this.progressBar.Style = WinForms.ProgressBarStyle.Continuous;

            this.btnGenerate.Location = new Point(160, 170);
            this.btnGenerate.Size = new Size(160, 38);
            this.btnGenerate.Text = "极速生成边界";
            this.btnGenerate.UseVisualStyleBackColor = true;
            this.btnGenerate.Font = new Font("微软雅黑", 10F, FontStyle.Bold);

            // --- 控件挂载到容器 ---
            this.Controls.Add(this.lblFilePath);
            this.Controls.Add(this.txtFilePath);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.lblMaxEdge);
            this.Controls.Add(this.txtMaxEdge);
            this.Controls.Add(this.lblUnit);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.btnGenerate);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // ==========================================================
        // 3. 业务逻辑与事件流
        // ==========================================================
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (WinForms.OpenFileDialog ofd = new WinForms.OpenFileDialog())
            {
                ofd.Filter = "文本高程数据 (*.txt;*.csv;*.dat)|*.txt;*.csv;*.dat|所有文件 (*.*)|*.*";
                ofd.Title = "选择点云数据";
                if (ofd.ShowDialog() == WinForms.DialogResult.OK)
                {
                    txtFilePath.Text = ofd.FileName;
                }
            }
        }

        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            // 参数前置校验
            if (string.IsNullOrWhiteSpace(txtFilePath.Text) || !double.TryParse(txtMaxEdge.Text, out double maxEdge))
            {
                WinForms.MessageBox.Show("请输入有效的数据文件路径和数字格式的最大边长阈值！", "参数错误", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var doc = CADApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            // 1. 隐藏窗体，切入 CAD 上下文，防止句柄冲突
            this.Hide();
            this.Enabled = false;
            progressBar.Value = 0;

            try
            {
                // 2. 锁定文档 (生命周期护航防线)
                using (doc.LockDocument())
                {
                    // 定义回调，防 UI 假死，接收 C++ 传来的进度
                    NativeTinBoundaryEngine.ProgressCallbackDelegate progressCb = (percent) =>
                    {
                        if (percent >= 0 && percent <= 100)
                        {
                            progressBar.Value = percent;
                            // 强制泵送 Windows 消息，防止高强度运算导致的系统假死
                            WinForms.Application.DoEvents();
                        }
                    };

                    ed.WriteMessage($"\n[引擎启动] 正在通过 Delaunay 拓扑提取边界，最大收缩边长限制: {maxEdge}...\n");

                    // 3. 将沉重的运算交由 C++ 引擎接管
                    int result = NativeTinBoundaryEngine.RunTinBoundaryGeneration(txtFilePath.Text, maxEdge, progressCb);

                    // 4. 解析金库传回的结果状态码
                    if (result == -999) ed.WriteMessage("\n[安全拦截] 核心算力未授权或特征码不匹配！\n");
                    else if (result > 0) ed.WriteMessage($"\n[运算完成] 极速降维打击完毕，成功生成 {result} 条 3D 边界拓扑多段线！\n");
                    else ed.WriteMessage("\n[运算失败] 数据解析异常，或无法构建有效拓扑网。\n");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[系统崩溃] P/Invoke 前后端通信或执行期间发生严重错误: {ex.Message}\n");
            }
            finally
            {
                // 5. 任务结束，释放进度条并恢复窗体交互权
                progressBar.Value = 100;
                this.Enabled = true;
                this.Show();
            }
        }
    }

    // ==========================================================
    // 4. 【架构师补充】CAD 命令注册与唤醒入口
    // ==========================================================
    public class TinBoundaryCommand
    {
        // 注册 CAD 命令行命令：输入 TINBOUNDARY 即可唤醒此 UI
        [CommandMethod("TINBOUNDARY")]
        public void ShowTinBoundaryForm()
        {
            var doc = CADApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                TinBoundaryUI uiForm = new TinBoundaryUI();
                // 极客法则：在跨平台 CAD 开发中，优先使用 Application.ShowModelessDialog 来托管窗体
                // 它能自动处理 CAD 主窗口与子窗口的父子级关系和焦点机制
                CADApp.ShowModelessDialog(uiForm);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[界面加载失败] 无法唤醒 TIN 边界提取模块: {ex.Message}\n");
            }
        }
    }
}