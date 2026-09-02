#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application;
#elif ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif

using System;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;
using SysDrawing = System.Drawing;

namespace CadFrontendUI
{
    // ==========================================================
    // 1. P/Invoke 底层防线：对接 C++ 属性图块注入引擎
    // ==========================================================
    internal static class NativeFrameEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [DllImport(DllName, EntryPoint = "InsertAttributeBlockFrame", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool InsertAttributeBlockFrame(
            double insertX, double insertY, double scale,
            string blockName,  // 你的图块名称，比如 "JTS_TITLE_BLOCK"
            string projName, string drawName, string projNo,
            string stage, string discipline, string designer, string scaleStr
        );
    }

    // ==========================================================
    // 2. CAD 命令入口
    // ==========================================================
    public class FrameCommand
    {
        private static StandardFrameUI _form = null;
        [CommandMethod("DRAWFRAME", CommandFlags.Modal)]
        public void ShowUI()
        {
            if (_form == null || _form.IsDisposed) { _form = new StandardFrameUI(); CADApp.ShowModelessDialog(_form); }
            else { _form.WindowState = WinForms.FormWindowState.Normal; _form.Activate(); }
        }
    }

    // ==========================================================
    // 3. 纯 C# 构建的现代工业级图框控制面板
    // ==========================================================
    public class StandardFrameUI : WinForms.Form
    {
        private WinForms.ComboBox cmbPaper;
        private WinForms.TextBox txtScale, txtProjName, txtDrawName, txtProjNo, txtDesigner;
        private WinForms.ComboBox cmbStage, cmbDiscipline;
        private WinForms.Button btnGenerate;

        public StandardFrameUI()
        {
            this.Text = "工程图纸生成系统 (定比例拉框模式)";
            this.Size = new SysDrawing.Size(350, 480);
            this.StartPosition = WinForms.FormStartPosition.CenterScreen;
            this.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            InitializeUI();
        }

        private void InitializeUI()
        {
            int mX = 20, y = 20, wL = 80, wI = 200;

            // 1. 图幅与比例
            var grpBase = new WinForms.GroupBox { Text = "图幅与比例", Left = mX, Top = y, Width = 300, Height = 95 };
            grpBase.Controls.Add(new WinForms.Label { Text = "标准图幅:", Left = 15, Top = 28, Width = wL });
            cmbPaper = new WinForms.ComboBox { Left = 85, Top = 25, Width = wI, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            cmbPaper.Items.AddRange(new string[] { "A0 (1189x841)", "A1 (841x594)", "A2 (594x420)", "A3 (420x297)", "A4 (297x210)" });
            cmbPaper.SelectedIndex = 3; // 默认A3
            grpBase.Controls.Add(cmbPaper);

            grpBase.Controls.Add(new WinForms.Label { Text = "出图比例 1:", Left = 15, Top = 60, Width = wL });
            txtScale = new WinForms.TextBox { Left = 85, Top = 57, Width = wI, Text = "500" };
            grpBase.Controls.Add(txtScale);
            this.Controls.Add(grpBase); y += 105;

            // 2. 图签属性注入
            var grpTitle = new WinForms.GroupBox { Text = "图签属性 (自动注入属性块)", Left = mX, Top = y, Width = 300, Height = 190 };
            string[] labels = { "工程名称:", "图纸名称:", "工程编号:", "设 计 人:" };
            string[] defVals = { "某港区散货码头工程", "平面布置图", "SY-2026-001", "张工" };
            WinForms.TextBox[] txts = { txtProjName = new WinForms.TextBox(), txtDrawName = new WinForms.TextBox(), txtProjNo = new WinForms.TextBox(), txtDesigner = new WinForms.TextBox() };

            for (int i = 0; i < 4; i++)
            {
                grpTitle.Controls.Add(new WinForms.Label { Text = labels[i], Left = 15, Top = 28 + i * 32, Width = wL });
                txts[i].Left = 85; txts[i].Top = 25 + i * 32; txts[i].Width = wI; txts[i].Text = defVals[i];
                grpTitle.Controls.Add(txts[i]);
            }

            // 阶段与专业
            grpTitle.Controls.Add(new WinForms.Label { Text = "阶 段:", Left = 15, Top = 156, Width = 45 });
            cmbStage = new WinForms.ComboBox { Left = 65, Top = 153, Width = 85, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            cmbStage.Items.AddRange(new string[] { "初步设计", "施工图", "竣工图" }); cmbStage.SelectedIndex = 1;
            grpTitle.Controls.Add(cmbStage);

            grpTitle.Controls.Add(new WinForms.Label { Text = "专 业:", Left = 160, Top = 156, Width = 45 });
            cmbDiscipline = new WinForms.ComboBox { Left = 210, Top = 153, Width = 75, DropDownStyle = WinForms.ComboBoxStyle.DropDownList };
            cmbDiscipline.Items.AddRange(new string[] { "水工", "总图", "工艺" }); cmbDiscipline.SelectedIndex = 0;
            grpTitle.Controls.Add(cmbDiscipline);

            this.Controls.Add(grpTitle); y += 205;

            // 3. 动作按钮
            btnGenerate = new WinForms.Button
            {
                Text = "在图纸上框选区域并生成图框",
                Left = mX,
                Top = y,
                Width = 300,
                Height = 45,
                BackColor = SysDrawing.Color.SteelBlue,
                ForeColor = SysDrawing.Color.White,
                FlatStyle = WinForms.FlatStyle.Flat,
                Font = new SysDrawing.Font("微软雅黑", 10F, SysDrawing.FontStyle.Bold)
            };
            btnGenerate.Click += BtnGenerate_Click;
            this.Controls.Add(btnGenerate);
        }

        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            if (!double.TryParse(txtScale.Text, out double scale) || scale <= 0) return;

            // 解析用户选定的图幅物理尺寸 (毫米)
            double paperW = 420.0, paperH = 297.0;
            switch (cmbPaper.SelectedIndex)
            {
                case 0: paperW = 1189.0; paperH = 841.0; break;
                case 1: paperW = 841.0; paperH = 594.0; break;
                case 2: paperW = 594.0; paperH = 420.0; break;
                case 3: paperW = 420.0; paperH = 297.0; break;
                case 4: paperW = 297.0; paperH = 210.0; break;
            }

            Document doc = CADApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            this.Hide(); WinForms.Application.DoEvents(); // 隐藏UI，防假死

            try
            {
                using (doc.LockDocument())
                {
                    // 1. 橡皮筋交互：框选目标区域
                    PromptPointResult ppr1 = ed.GetPoint($"\n>> 请拾取需出图区域的 [第一角点] (固定比例 1:{scale}): ");
                    if (ppr1.Status == PromptStatus.OK)
                    {
                        PromptPointOptions ppo2 = new PromptPointOptions("\n>> 请拉伸橡皮筋框并拾取 [对角点]: ");
                        ppo2.UseBasePoint = true; ppo2.BasePoint = ppr1.Value;
                        PromptPointResult ppr2 = ed.GetCorner(ppo2);

                        if (ppr2.Status == PromptStatus.OK)
                        {
                            // 2. 几何中控：中心锚定算法
                            double centerX = (ppr1.Value.X + ppr2.Value.X) / 2.0;
                            double centerY = (ppr1.Value.Y + ppr2.Value.Y) / 2.0;

                            // 计算图框实际的缩放后尺寸
                            double frameRealWidth = paperW * scale;
                            double frameRealHeight = paperH * scale;

                            // 反推图块插入点 (通常是左下角)
                            double insertX = centerX - frameRealWidth / 2.0;
                            double insertY = centerY - frameRealHeight / 2.0;

                            // 生成比例尺文本
                            string scaleStr = $"1:{scale}";

                            // 3. 将计算结果与 UI 属性瞬间丢给 C++ 引擎
                            bool ok = NativeFrameEngine.InsertAttributeBlockFrame(
                                insertX, insertY, scale, "JTS_TITLE_BLOCK", // 注意这里的块名必须与你的 DWG 模板块名一致
                                txtProjName.Text, txtDrawName.Text, txtProjNo.Text,
                                cmbStage.Text, cmbDiscipline.Text, txtDesigner.Text, scaleStr
                            );

                            if (ok) ed.WriteMessage($"\n>> [成功] 已在框选中心生成 {cmbPaper.Text} 图框，比例 1:{scale}。");
                            else ed.WriteMessage("\n>> [失败] 未在当前图纸中找到图块定义 'JTS_TITLE_BLOCK'。");
                        }
                    }
                }
            }
            finally { this.Show(); } // 闭环恢复UI
        }
    }
}