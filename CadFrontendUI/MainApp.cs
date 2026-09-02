// ==========================================
// ==========================================
// 跨平台宏定义头部
// ==========================================
#if ZWCAD
    using ZwSoft.ZwCAD.Runtime;
    using ZwSoft.ZwCAD.ApplicationServices;
    using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#elif AUTOCAD
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
#error "【编译配置错误】：请选择 AutoCAD_Release 或 ZWCAD_Release！"
#endif

[assembly: ExtensionApplication(typeof(CadFrontendUI.MainApp))]

namespace CadFrontendUI
{
    public class MainApp : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = CADApp.DocumentManager.MdiActiveDocument;

            // 【关键点火开关】：在插件加载的瞬间，立刻呼叫授权管理器！
            // 注意：请确保 ZWCAD_Plugin 命名空间与你 LicenseManager 所在的命名空间一致
            bool isAuthorized = ZWCAD_Plugin.LicenseManager.CheckOrRegister();

            if (doc != null)
            {
                doc.Editor.WriteMessage("\n=====================================\n");
                if (isAuthorized)
                {
                    doc.Editor.WriteMessage("  🚀 CadFrontendUI 界面模块已授权并成功加载！\n");
                    doc.Editor.WriteMessage("  👉 请输入命令: ZDCD (折叠菜单) 或 XLCD (下拉菜单)\n");
                }
                else
                {
                    doc.Editor.WriteMessage("  ❌ 授权失败！核心算法引擎已被底层锁定。\n");
                    doc.Editor.WriteMessage("  👉 请联系管理员获取授权码。\n");
                }
                doc.Editor.WriteMessage("=====================================\n");
            }
        }

        public void Terminate()
        {
        }
    }
}