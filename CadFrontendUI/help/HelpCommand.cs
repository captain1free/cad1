#if AUTOCAD
using Autodesk.AutoCAD.Runtime;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application;
#elif ZWCAD
using ZwSoft.ZwCAD.Runtime;
using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif

using CadFrontendUI.Help;
using System;

namespace CadFrontendUI.Commands
{
    public class HelpCommand
    {
        // 声明静态实例，防止用户疯狂点击弹出无数个窗口
        private static HelpDocForm _helpForm = null;

        [CommandMethod("Z_SHOW_HELP")]
        public void ShowHelpWindow()
        {
            try
            {
                // 如果窗体未实例化或已被关闭，则重新创建
                if (_helpForm == null || _helpForm.IsDisposed)
                {
                    _helpForm = new HelpDocForm();

                    // 【关键法则】使用 CADApp.ShowModelessDialog 显示无模式窗体
                    // 这样用户在看帮助文档的同时，依然可以在 CAD 里面画图！
                    CADApp.ShowModelessDialog(_helpForm);
                }
                else
                {
                    // 如果窗体已经存在，就把它激活到最前面
                    _helpForm.Activate();
                }
            }
            catch (System.Exception ex)
            {
                // 命令行兜底报错
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n打开帮助文档失败: {ex.Message}");
            }
        }
    }
}