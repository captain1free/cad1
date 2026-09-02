// ==========================================
// 跨平台宏定义头部 (抹平命名空间差异)
// ==========================================
#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Windows;
using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application; // 统一别名
#elif AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application; // 统一别名
#else
#error "【编译配置错误】：请在 VS 顶部工具栏下拉框中，选择 AutoCAD_Release 或 ZWCAD_Release！"
#endif

using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ZWCAD_Plugin; // 确保引入 LicenseManager 所在命名空间

namespace CadFrontendUI.Menus
{
    public class CADCollapsibleMenuWithAutoCollapse
    {
        public class CommandConfig
        {
            public string Title { get; set; }
            public string Macro { get; set; }
            public List<CommandConfig> SubCommands { get; set; } = new List<CommandConfig>();
        }

        private static PaletteSet paletteSet;
        private static FlowLayoutPanel currentExpandedMenu;

        // 【新增极客彩蛋】：提供命令行全局调用方式
        [CommandMethod("CAD_AUTH")]
        public void ShowCadAuthCommand()
        {
            LicenseManager.ShowAuthStatus();
        }

        [CommandMethod("ZDCD")]
        public void CreateCollapsibleMenuWithTriangles()
        {
            // 你之前注释掉的静默验证逻辑。如果希望启动即强制校验，可以解开。
            // 建议：前端 UI 可以继续让面板弹出来，核心计算命令在 C++ 层面仍会被拦截，体验会更优雅。
            // if (!LicenseManager.CheckOrRegister()) return; 

            if (paletteSet != null && paletteSet.Visible)
            {
                paletteSet.Visible = true;
                return;
            }

            paletteSet = new PaletteSet("功能菜单")
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton,
                MinimumSize = new System.Drawing.Size(170, 200)
            };

            List<CommandConfig> commands = LoadCommandsFromEmbeddedResource();

            if (commands == null || commands.Count == 0)
            {
                MessageBox.Show("未在配置文件中找到有效命令。");
                return;
            }

            FlowLayoutPanel mainPanel = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(5),
                Margin = new Padding(0),
                BackColor = Color.FromArgb(45, 45, 48)
            };

            foreach (CommandConfig CommandA in commands)
            {
                Button mainButton = new Button()
                {
                    Text = "▶ " + CommandA.Title,
                    Width = 160,
                    Height = 26,
                    Margin = new Padding(2),
                    BackColor = Color.FromArgb(45, 45, 48),
                    ForeColor = Color.FromArgb(220, 220, 220),
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular)
                };

                FlowLayoutPanel subMenuPanel = new FlowLayoutPanel()
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    Visible = false,
                    FlowDirection = FlowDirection.TopDown,
                    Padding = new Padding(10, 0, 0, 0),
                    Margin = new Padding(0),
                    BackColor = Color.FromArgb(45, 45, 48)
                };

                if (CommandA.SubCommands != null && CommandA.SubCommands.Count > 0)
                {
                    foreach (var subCommand in CommandA.SubCommands)
                    {
                        Button subButton = new Button()
                        {
                            Text = subCommand.Title,
                            Width = 140,
                            Height = 26,
                            Margin = new Padding(2),
                            BackColor = Color.FromArgb(45, 45, 48),
                            ForeColor = Color.FromArgb(200, 200, 200),
                            FlatStyle = FlatStyle.Flat,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular)
                        };

                        subButton.Click += (sender, e) =>
                        {
                            CADApp.DocumentManager.MdiActiveDocument.SendStringToExecute(subCommand.Macro + "\n", true, false, false);
                        };
                        subMenuPanel.Controls.Add(subButton);
                    }
                }

                mainButton.Click += (sender, e) =>
                {
                    if (currentExpandedMenu != null && currentExpandedMenu != subMenuPanel && mainPanel.Controls.Contains(currentExpandedMenu))
                    {
                        currentExpandedMenu.Visible = false;
                        foreach (Control control in mainPanel.Controls)
                        {
                            if (control is Button btn && mainPanel.Controls.GetChildIndex(control) == mainPanel.Controls.GetChildIndex(currentExpandedMenu) - 1)
                            {
                                btn.Text = "▶ " + btn.Text.Substring(2);
                                break;
                            }
                        }
                    }
                    subMenuPanel.Visible = !subMenuPanel.Visible;
                    mainButton.Text = subMenuPanel.Visible ? "▼ " + CommandA.Title : "▶ " + CommandA.Title;
                    currentExpandedMenu = subMenuPanel.Visible ? subMenuPanel : null;
                };

                mainPanel.Controls.Add(mainButton);
                mainPanel.Controls.Add(subMenuPanel);
            }

            Panel separator = new Panel() { Width = 160, Height = 1, Margin = new Padding(0, 5, 0, 5), BackColor = SystemColors.ControlDark };
            mainPanel.Controls.Add(separator);

            Button XLCDButton = new Button() { Text = "下拉菜单", Width = 160, Height = 26, Margin = new Padding(2), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(220, 220, 220), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular) };
            XLCDButton.Click += (sender, e) => { CADApp.DocumentManager.MdiActiveDocument.SendStringToExecute("xlcd ", true, false, false); };
            mainPanel.Controls.Add(XLCDButton);

            // ==========================================
            // 【新增】：底层驱动的授权状态 UI 组件
            // ==========================================
            bool isAuth = false;
            try { isAuth = NativeSecurity.IsAuthorized(); } catch { /* 容错：防止 C++ 加载失败时导致菜单崩溃 */ }

            Button authBtn = new Button()
            {
                Text = isAuth ? "★ 软件授权 (已激活)" : "☆ 软件授权 (未激活)",
                Width = 160,
                Height = 26,
                Margin = new Padding(2),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = isAuth ? Color.FromArgb(100, 255, 100) : Color.FromArgb(255, 100, 100), // 已激活绿色，未激活红色
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Bold)
            };

            authBtn.Click += (sender, e) =>
            {
                // 调用状态反馈或注册表单
                LicenseManager.ShowAuthStatus();

                // 关闭弹窗后，重嗅探 C++ 内存，实时刷新按钮状态
                bool newAuth = false;
                try { newAuth = NativeSecurity.IsAuthorized(); } catch { }
                authBtn.Text = newAuth ? "★ 软件授权 (已激活)" : "☆ 软件授权 (未激活)";
                authBtn.ForeColor = newAuth ? Color.FromArgb(100, 255, 100) : Color.FromArgb(255, 100, 100);
            };
            mainPanel.Controls.Add(authBtn);

            Button closeBtn = new Button() { Text = "关闭", Width = 160, Height = 26, Margin = new Padding(2, 4, 2, 4), BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.FromArgb(220, 220, 220), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular) };
            closeBtn.Click += (sender, e) => { if (paletteSet != null) { paletteSet.Visible = false; paletteSet.Dispose(); paletteSet = null; currentExpandedMenu = null; } };
            mainPanel.Controls.Add(closeBtn);

            paletteSet.Add("功能菜单", mainPanel);
            paletteSet.Visible = true;
            paletteSet.Size = new System.Drawing.Size(170, 300);
            paletteSet.Dock = DockSides.Left;
        }

        private List<CommandConfig> LoadCommandsFromEmbeddedResource()
        {
            List<CommandConfig> commands = new List<CommandConfig>();
            CommandConfig currentParent = null;
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string assemblyName = assembly.GetName().Name;
                string resourceName = $"{assemblyName}.commandICO.txt";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string[] lines = reader.ReadToEnd().Split(new string[] { "\r\n", "\n" }, System.StringSplitOptions.None);
                            foreach (string line in lines)
                            {
                                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                                if (line.StartsWith(" "))
                                {
                                    Match match = Regex.Match(line.Trim(), @"^\[(.*?)(?:,(.*?))?\](.*?)$");
                                    if (match.Success && currentParent != null)
                                    {
                                        currentParent.SubCommands.Add(new CommandConfig() { Title = match.Groups[1].Value.Trim(), Macro = match.Groups[3].Value.Trim() });
                                    }
                                }
                                else
                                {
                                    Match match = Regex.Match(line.Trim(), @"^\[(.*)\]");
                                    if (match.Success)
                                    {
                                        CommandConfig parentCommand = new CommandConfig() { Title = match.Groups[1].Value.Trim(), Macro = string.Empty };
                                        commands.Add(parentCommand);
                                        currentParent = parentCommand;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex) { MessageBox.Show("读取嵌入资源时出错：" + ex.Message); }
            return commands;
        }
    }
}