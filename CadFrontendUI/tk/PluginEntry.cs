// =======================================================
// 跨平台万能头文件 (需放置于每个 .cs 文件最顶部)
// =======================================================
#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.Colors;
using AcadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else // 默认 AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Colors;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
// ★ 强行指定 Exception 为系统异常，解决与 CAD.Runtime.Exception 的冲突
using Exception = System.Exception;
using SysException = System.Exception;

[assembly: CommandClass(typeof(SheetFramePlugin.Commands))]

namespace SheetFramePlugin
{
    public class PluginEntry : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc?.Editor?.WriteMessage("\n[水运工程分幅图框] 已加载。命令: SHEETFRAME 或 SF");
        }
        public void Terminate() { }
    }
}
