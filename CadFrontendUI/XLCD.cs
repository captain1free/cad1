// ==========================================
// 跨平台宏定义头部 (彻底抹平底层的命名空间差异)
// ==========================================
#if ZWCAD
    using ZwSoft.ZwCAD.Runtime;
    using ZwSoft.ZwCAD.Windows;
    using ZwSoft.ZwCAD.ApplicationServices;
    using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application; // 统一别名
#elif AUTOCAD
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application; // 统一别名
#else
#error "【编译配置错误】：请在 VS 顶部工具栏下拉框中，选择 AutoCAD_Release 或 ZWCAD_Release！"
#endif

using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections.Generic;

namespace CadFrontendUI.Menus
{
    public class DynamicToolbarMenuWithTextAndIcons
    {
        private static PaletteSet xlcdPaletteSet;

        // 自定义渲染器，保持深色主题风格
        private class CustomToolStripRenderer : ToolStripProfessionalRenderer
        {
            public CustomToolStripRenderer() : base(new CustomColorTable()) { }
        }

        private class CustomColorTable : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin => Color.FromArgb(45, 45, 48);
            public override Color MenuStripGradientEnd => Color.FromArgb(45, 45, 48);
            public override Color ToolStripGradientBegin => Color.FromArgb(45, 45, 48);
            public override Color ToolStripGradientEnd => Color.FromArgb(45, 45, 48);
            public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 48);
            public override Color ToolStripBorder => Color.FromArgb(60, 60, 60);
            public override Color MenuItemBorder => Color.FromArgb(60, 60, 60);
            public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(60, 60, 60);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(60, 60, 60);
        }

        public class CommandConfig
        {
            public string Title { get; set; }
            public string Macro { get; set; }
            public List<CommandConfig> SubCommands { get; set; } = new List<CommandConfig>();
        }

        [CommandMethod("XLCD")]
        public void CreateDynamicToolbarWithTextAndIcons()
        {
            // TODO: 此处后续可以集成 C++ 层的授权检查
            // if (!LicenseManager.CheckOrRegister()) return;

            if (xlcdPaletteSet != null && xlcdPaletteSet.Visible)
            {
                xlcdPaletteSet.Visible = true;
                return;
            }

            List<CommandConfig> textCommands = LoadTextCommandsFromEmbeddedResource();

            if (textCommands == null || textCommands.Count == 0)
            {
                MessageBox.Show("未在配置文件中找到有效菜单命令。");
                return;
            }

            xlcdPaletteSet = new PaletteSet("功能菜单")
            {
                Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton,
                MinimumSize = new Size(300, 30)
            };

            Panel panel = new Panel() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 48) };

            ToolStrip toolStrip = new ToolStrip()
            {
                Dock = DockStyle.Top,
                LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular),
                RenderMode = ToolStripRenderMode.Professional,
                Renderer = new CustomToolStripRenderer()
            };

            foreach (var textCommand in textCommands)
            {
                ToolStripDropDownButton dropDownButton = new ToolStripDropDownButton()
                {
                    Text = textCommand.Title,
                    DisplayStyle = ToolStripItemDisplayStyle.Text,
                    Padding = new Padding(6, 4, 6, 4),
                    Margin = new Padding(1, 0, 1, 0)
                };

                if (textCommand.SubCommands != null && textCommand.SubCommands.Count > 0)
                {
                    foreach (var subCommand in textCommand.SubCommands)
                    {
                        ToolStripMenuItem subButton = new ToolStripMenuItem()
                        {
                            Text = subCommand.Title,
                            Padding = new Padding(8, 4, 8, 4),
                            ForeColor = Color.FromArgb(220, 220, 220)
                        };
                        subButton.Click += (sender, e) =>
                        {
                            // 使用别名 CADApp 统一处理文档管理，自动适配 AutoCAD 和 ZWCAD
                            CADApp.DocumentManager.MdiActiveDocument.SendStringToExecute(subCommand.Macro + "\n", true, false, false);
                        };
                        dropDownButton.DropDownItems.Add(subButton);
                    }
                }
                toolStrip.Items.Add(dropDownButton);
            }

            // 添加辅助按钮
            ToolStripButton ZDCDButton = new ToolStripButton() { Text = "折叠菜单", DisplayStyle = ToolStripItemDisplayStyle.Text };
            ZDCDButton.Click += (sender, e) => {
                CADApp.DocumentManager.MdiActiveDocument.SendStringToExecute("ZDCD ", true, false, false);
            };
            toolStrip.Items.Add(ZDCDButton);

            ToolStripButton closeButton = new ToolStripButton() { Text = "关闭", DisplayStyle = ToolStripItemDisplayStyle.Text };
            closeButton.Click += (sender, e) => {
                if (xlcdPaletteSet != null) { xlcdPaletteSet.Visible = false; xlcdPaletteSet.Dispose(); xlcdPaletteSet = null; }
            };
            toolStrip.Items.Add(closeButton);

            panel.Controls.Add(toolStrip);
            xlcdPaletteSet.Add("菜单", panel);

            // 自动计算初始大小
            int menuCount = textCommands.Count + 2;
            xlcdPaletteSet.Size = new Size(Math.Min(800, menuCount * 80), 30);
            xlcdPaletteSet.Dock = DockSides.Top;
            xlcdPaletteSet.Visible = true;
        }

        private List<CommandConfig> LoadTextCommandsFromEmbeddedResource()
        {
            List<CommandConfig> commands = new List<CommandConfig>();
            CommandConfig currentParent = null;
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                // 动态获取程序集名称，确保资源加载路径正确 (配合 .csproj 里的 EmbeddedResource)
                string assemblyName = assembly.GetName().Name;
                string resourceName = $"{assemblyName}.commandICO.txt";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string content = reader.ReadToEnd();
                            string[] lines = content.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

                            foreach (string line in lines)
                            {
                                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("'") || line.StartsWith("#")) continue;

                                // 检测父菜单 [主菜单]
                                if (!line.StartsWith(" ") && line.StartsWith("["))
                                {
                                    string title = line.Trim().Trim('[', ']');
                                    currentParent = new CommandConfig() { Title = title };
                                    commands.Add(currentParent);
                                }
                                // 检测子命令 [子命令,宏]宏内容
                                else if (line.StartsWith(" "))
                                {
                                    Match match = Regex.Match(line.Trim(), @"^\[(.*?)(?:,(.*?))?\](.*?)$");
                                    if (match.Success && currentParent != null)
                                    {
                                        currentParent.SubCommands.Add(new CommandConfig()
                                        {
                                            Title = match.Groups[1].Value.Trim(),
                                            Macro = match.Groups[3].Value.Trim()
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("加载菜单资源失败：" + ex.Message);
            }
            return commands;
        }
    }
}