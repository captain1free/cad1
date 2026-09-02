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
// ★ 强行指定 Exception 为系统异常，解决与 CAD.Runtime.Exception 的冲突
using Exception = System.Exception;

namespace SheetFramePlugin
{
    public class Commands
    {
        [CommandMethod("SHEETFRAME", "SF", CommandFlags.Modal)]
        public void SheetFrame()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            Extents3d ext;
            try { ext = GetModelExtents(db); }
            catch (Exception ex)
            {
                ext = new Extents3d(Point3d.Origin, new Point3d(1000, 1000, 0));
                ed.WriteMessage("\n提示: " + ex.Message + "（可在窗体中改用【框选范围】）");
            }

            using (var form = new SheetFrameForm(ext))
            {
                if (AcadApp.ShowModalDialog(form) != System.Windows.Forms.DialogResult.OK) return;
                SheetOptions opt = form.Options;
                try
                {
                    using (doc.LockDocument())
                    {
                        var plan = SheetPlan.Create(opt);
                        var numbers = new List<string>();
                        for (int r = 0; r < plan.Rows; r++)
                            for (int c = 0; c < plan.Cols; c++)
                                numbers.Add(SheetNamer.Number(opt, plan, r, c));

                        // ★ 空幅检测（编号仍按完整网格位置生成，相邻编号关系正确）
                        var buckets = DwgSplitter.BucketBySheet(db, plan, DwgSplitter.GetPad(opt));
                        var skipped = new List<string>();
                        for (int k = 0; k < plan.Total; k++)
                            if (buckets[k] == null || buckets[k].Count == 0) skipped.Add(numbers[k]);

                        if (skipped.Count > 0)
                            ed.WriteMessage("\n[空幅过滤] {0}/{1} 幅无内容已跳过: {2}",
                                skipped.Count, plan.Total, string.Join("  ", skipped.ToArray()));

                        int valid = plan.Total - skipped.Count;
                        if (valid == 0) { ed.WriteMessage("\n范围内无任何内容，未生成图框。"); return; }

                        if (opt.Mode == OutputMode.ModelOverlay)
                        {
                            using (var tr = db.TransactionManager.StartTransaction())
                            {
                                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                                var drawer = new FrameDrawer(db, tr, opt);
                                int idx = 0;
                                for (int r = 0; r < plan.Rows; r++)
                                    for (int c = 0; c < plan.Cols; c++, idx++)
                                        if (buckets[idx].Count > 0)               // ★跳过空幅
                                            drawer.DrawSheet(ms, plan.Corner(r, c), plan.Corner(r, c),
                                                opt.ScaleDen / 1000.0, 1.0, plan, r, c, numbers[idx]);
                                tr.Commit();
                            }
                            ed.WriteMessage("\n已绘制 {0}/{1} 幅图框（叠加方式，空白幅已跳过）。", valid, plan.Total);
                        }
                        else if (opt.Mode == OutputMode.LayoutPerSheet)
                        {
                            if (opt.RebuildLayouts) LayoutSheetBuilder.RemoveOld(doc);
                            var used = LayoutSheetBuilder.ExistingNames(db);
                            string first = null;
                            using (var tr = db.TransactionManager.StartTransaction())
                            {
                                int idx = 0;
                                for (int r = 0; r < plan.Rows; r++)
                                    for (int c = 0; c < plan.Cols; c++, idx++)
                                    {
                                        if (buckets[idx].Count == 0) continue;    // ★跳过空幅
                                        string nm = LayoutSheetBuilder.Build(db, tr, opt, plan, r, c, numbers[idx], used);
                                        if (first == null) first = nm;
                                    }
                                tr.Commit();
                            }
                            if (first != null) LayoutManager.Current.CurrentLayout = first;
                            ed.WriteMessage("\n已创建 {0}/{1} 个布局（图框1:1、视口锁定1:{2}，空白幅已跳过）。",
                                valid, plan.Total, opt.ScaleDen);
                        }
                        else ed.WriteMessage("\n[拆分模式] 每幅保存为独立DWG（空白幅自动剔除）。");

                        // ===== 独立DWG导出（任意模式可叠加）=====
                        bool needExport = opt.SaveEachDwg || opt.Mode == OutputMode.SplitDwg;
                        if (needExport && string.IsNullOrWhiteSpace(opt.OutDir))
                        {
                            using (var fb = new System.Windows.Forms.FolderBrowserDialog
                            { Description = "请指定分幅DWG的保存位置：" })
                            {
                                if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                                    opt.OutDir = fb.SelectedPath;
                                else { needExport = false; ed.WriteMessage("\n未指定保存目录，已跳过独立DWG导出。"); }
                            }
                        }
                        if (needExport)
                        {
                            ed.WriteMessage("\n开始导出 {0} 幅独立DWG（已剔除空白幅）……", valid);
                            var result = DwgSplitter.ExportSheets(db, opt, plan, numbers, buckets,
                                (n, name) => ed.WriteMessage("\n  导出 [{0}/{1}] {2}", n, valid, name));
                            foreach (string err in result.Errors) ed.WriteMessage("\n  失败: " + err);
                            ed.WriteMessage("\n完成：成功 {0}/{1} 幅（空白跳过 {2} 幅）→ {3}",
                                result.Files.Count, valid, skipped.Count, opt.OutDir);
                        }
                    }
                }
                catch (Exception ex) { ed.WriteMessage("\n失败: " + ex.Message); }
            }
        }

        static Extents3d GetModelExtents(Database db)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            // 1. 先用一个小事务快速拿到所有的 ObjectId (仅收集ID，不占内存)
            var ids = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms) ids.Add(id);
                tr.Commit();
            }

            // 2. 分批次打开图元，解决巨量图元撑爆事务内存导致闪崩的问题
            int batch = 50000;
            for (int i = 0; i < ids.Count; i += batch)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    int end = Math.Min(i + batch, ids.Count);
                    for (int j = i; j < end; j++)
                    {
                        Entity ent;
                        try { ent = tr.GetObject(ids[j], OpenMode.ForRead) as Entity; }
                        catch { continue; }
                        if (ent == null || ent.IsErased || ent is Viewport) continue;
                        try
                        {
                            Extents3d e = ent.GeometricExtents;
                            minX = Math.Min(minX, e.MinPoint.X); minY = Math.Min(minY, e.MinPoint.Y);
                            maxX = Math.Max(maxX, e.MaxPoint.X); maxY = Math.Max(maxY, e.MaxPoint.Y);
                            any = true;
                        }
                        catch { }
                    }
                    tr.Commit(); // 提交事务，及时释放本批次读取的内存
                }
            }

            if (!any) throw new Exception("模型空间中未找到可用实体。");
            if (minX >= maxX) maxX = minX + 1;
            if (minY >= maxY) maxY = minY + 1;
            return new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
        }
    }
}