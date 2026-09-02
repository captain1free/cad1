#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CADApp = Autodesk.AutoCAD.ApplicationServices.Application;
#elif ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using CADApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace ElevationPointsExporter
{
    /// <summary>
    /// 【极客防线】C++ 底层金库 P/Invoke 接口 (极速几何导出引擎)
    /// </summary>
    internal static class NativeExporterEngine
    {
#if AUTOCAD
        private const string DllName = "CadMultiPlatformProj.arx";
#elif ZWCAD
        private const string DllName = "CadMultiPlatformProj.zrx";
#endif
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ProgressCallbackDelegate(int progressPercent);

        [DllImport(DllName, EntryPoint = "RunElevPointsExporter", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int RunElevPointsExporter(
            string filePath,
            [In] long[] handles,
            int entityCount,
            [MarshalAs(UnmanagedType.FunctionPtr)] ProgressCallbackDelegate callback);
    }

    public class ElevationPointExporterCmd
    {
        private NativeExporterEngine.ProgressCallbackDelegate _progressCallback;

        public ElevationPointExporterCmd()
        {
            _progressCallback = new NativeExporterEngine.ProgressCallbackDelegate(OnProgressUpdate);
        }

        private void OnProgressUpdate(int percent)
        {
            if (percent % 10 == 0)
                CADApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"...{percent}%");

            // 强制释放消息队列，防止UI线程假死（跨平台防线法则）
            WinForms.Application.DoEvents();
        }

        [CommandMethod("EXPORT_ELEVPOINTS", CommandFlags.Modal)]
        public void ExportElevationPoints()
        {
            Document doc = CADApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptKeywordOptions options = new PromptKeywordOptions("\n选择纯几何高程提取方式 [按图层(L)/多边形区域(S)/选择对象所在的图层(O)]: ", "L S O");
                PromptResult result = ed.GetKeywords(options);

                if (result.Status != PromptStatus.OK) return;

                PromptSelectionResult selRes = null;
                string exportMethod = result.StringResult;

                // 【架构师改造点】：精准锁定，仅抓取点图元(POINT)和块参照(INSERT)
                string allowedEntities = "POINT,INSERT,TEXT";
                SelectionFilter entityFilter = new SelectionFilter(new TypedValue[] {
                    new TypedValue((int)DxfCode.Start, allowedEntities)
                });

                if (exportMethod == "L") // 按图层导出
                {
                    PromptResult layerRes = ed.GetString("\n输入要导出的图层名: ");
                    if (layerRes.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(layerRes.StringResult)) return;

                    SelectionFilter layerFilter = new SelectionFilter(new TypedValue[] {
                        new TypedValue((int)DxfCode.LayerName, layerRes.StringResult),
                        new TypedValue((int)DxfCode.Start, allowedEntities)
                    });
                    selRes = ed.SelectAll(layerFilter);
                }
                else if (exportMethod == "S") // 多边形橡皮筋区域导出
                {
                    Point3dCollection polygonPts = GetPolygonPointsWithRubberBand(ed, db);
                    if (polygonPts == null || polygonPts.Count < 3)
                    {
                        ed.WriteMessage("\n无效的多边形区域。");
                        return;
                    }
                    // 利用 CAD 空间索引抓取多边形内的实体
                    selRes = ed.SelectCrossingPolygon(polygonPts, entityFilter);
                }
                else if (exportMethod == "O") // 选择对象导出其图层
                {
                    PromptEntityOptions entOpts = new PromptEntityOptions("\n选择一个基准对象: ");
                    PromptEntityResult entRes = ed.GetEntity(entOpts);
                    if (entRes.Status != PromptStatus.OK) return;

                    string targetLayer = "";
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = (Entity)tr.GetObject(entRes.ObjectId, OpenMode.ForRead);
                        targetLayer = ent.Layer;
                    }

                    SelectionFilter layerFilter = new SelectionFilter(new TypedValue[] {
                        new TypedValue((int)DxfCode.LayerName, targetLayer),
                        new TypedValue((int)DxfCode.Start, allowedEntities)
                    });
                    selRes = ed.SelectAll(layerFilter);
                }

                if (selRes == null || selRes.Status != PromptStatus.OK || selRes.Value.Count == 0)
                {
                    ed.WriteMessage("\n未在指定范围内找到任何有效的高程点(POINT)或块(INSERT)对象。");
                    return;
                }

                // 扁平化数据封送：提取稳定的 64位 Handle 交给 C++ 寻址
                long[] handles = selRes.Value.GetObjectIds().Select(id => id.Handle.Value).ToArray();

                using (var sfd = new WinForms.SaveFileDialog { Filter = "DAT文件 (*.dat)|*.dat|所有文件 (*.*)|*.*", FileName = "ExportedGeoElevPoints.dat" })
                {
                    if (sfd.ShowDialog() == WinForms.DialogResult.OK)
                    {
                        ed.WriteMessage($"\n[前端调度] 锁定 {handles.Length} 个几何高程实体，交由 C++ 金库极速抽取...\n");
                        int exportedCount = NativeExporterEngine.RunElevPointsExporter(sfd.FileName, handles, handles.Length, _progressCallback);

                        if (exportedCount == -999) ed.WriteMessage("\n[安全拦截] 核心算力未授权！");
                        else if (exportedCount >= 0) ed.WriteMessage($"\n[C++ 引擎] 极速导出完毕！共物理直读 {exportedCount} 个空间坐标。");
                        else ed.WriteMessage("\n[异常] 写入文件失败，请检查文件是否被占用。");
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[前端异常]: {ex.Message}");
            }
        }

        // ==========================================================
        // 交互增强：带橡皮筋跟随的动态多边形采集器
        // ==========================================================
        private Point3dCollection GetPolygonPointsWithRubberBand(Editor ed, Database db)
        {
            Point3dCollection pts = new Point3dCollection();
            ObjectId tempPolyId = ObjectId.Null;

            try
            {
                PromptPointResult ptRes = ed.GetPoint("\n指定第一个顶点: ");
                if (ptRes.Status != PromptStatus.OK) return null;
                pts.Add(ptRes.Value);

                while (true)
                {
                    PromptPointOptions ptOpts = new PromptPointOptions($"\n指定第 {pts.Count + 1} 个顶点 [完成(F)]: ");
                    ptOpts.Keywords.Add("F");
                    ptOpts.UseBasePoint = true;
                    ptOpts.BasePoint = pts[pts.Count - 1];

                    PromptPointResult nextRes = ed.GetPoint(ptOpts);

                    if (nextRes.Status == PromptStatus.Keyword && nextRes.StringResult == "F")
                        break;

                    if (nextRes.Status == PromptStatus.OK)
                    {
                        pts.Add(nextRes.Value);
                        UpdateTempPolygon(db, pts, ref tempPolyId);
                    }
                    else if (nextRes.Status == PromptStatus.None)
                    {
                        break;
                    }
                    else
                    {
                        ClearTempPolygon(db, ref tempPolyId);
                        return null;
                    }
                }
            }
            finally
            {
                ClearTempPolygon(db, ref tempPolyId);
            }

            return pts;
        }

        private void UpdateTempPolygon(Database db, Point3dCollection points, ref ObjectId tempPolyId)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                if (tempPolyId != ObjectId.Null && tempPolyId.IsValid)
                {
                    Entity oldEnt = tr.GetObject(tempPolyId, OpenMode.ForWrite) as Entity;
                    if (oldEnt != null && !oldEnt.IsErased) oldEnt.Erase();
                }

                if (points.Count >= 2)
                {
                    Polyline poly = new Polyline();
                    for (int i = 0; i < points.Count; i++)
                    {
                        poly.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);
                    }
                    if (points.Count >= 3) poly.Closed = true;

                    poly.ColorIndex = 1; // 红色
                    btr.AppendEntity(poly);
                    tr.AddNewlyCreatedDBObject(poly, true);
                    tempPolyId = poly.ObjectId;
                }
                tr.Commit();
            }
            CADApp.DocumentManager.MdiActiveDocument.Editor.UpdateScreen();
        }

        private void ClearTempPolygon(Database db, ref ObjectId tempPolyId)
        {
            if (tempPolyId == ObjectId.Null || !tempPolyId.IsValid) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity oldEnt = tr.GetObject(tempPolyId, OpenMode.ForWrite) as Entity;
                if (oldEnt != null && !oldEnt.IsErased) oldEnt.Erase();
                tr.Commit();
            }
            tempPolyId = ObjectId.Null;
            CADApp.DocumentManager.MdiActiveDocument.Editor.UpdateScreen();
        }
    }
}