#if ZWCAD
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using AcadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else // 默认 AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Exception = System.Exception;
using SysException = System.Exception;

namespace SheetFramePlugin
{
    public class ExportResult
    {
        public List<string> Files = new List<string>();
        public List<string> Errors = new List<string>();
        public List<string> Skipped = new List<string>();    // 空幅清单
    }

    public static class DwgSplitter
    {
        const string FrameLayerPrefix = "TK-";
        public const string EngineVersion = "v7.0 (C++ Native Core)";

        // =========================================================================
        // 🔴 接入 C++ 核心引擎 (P/Invoke)
        // ZWCAD 加载 .zrx，AutoCAD 加载 .arx
        // =========================================================================
#if ZWCAD
        private const string EngineDll = "CadMultiPlatformProj.zrx";
#else
        private const string EngineDll = "CadMultiPlatformProj.arx";
#endif
        [DllImport(EngineDll, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SplitAndExportSheet(double minX, double minY, double maxX, double maxY, string outputPath);

        public static double GetPad(SheetOptions opt)
            => (opt.Margin + 20) * opt.ScaleDen / 1000.0;

        public static ExportResult ExportSheets(Database srcDb, SheetOptions opt, SheetPlan plan,
            List<string> numbers, List<ObjectId>[] buckets, Action<int, string> progress = null)
        {
            var res = new ExportResult();
            if (buckets == null || buckets.Length != plan.Total)
                buckets = BucketBySheet(srcDb, plan, GetPad(opt));
            string dir = opt.OutDir;
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "分幅图");
            Directory.CreateDirectory(dir);

            WriteMsg("\n[导出引擎" + EngineVersion + "] C++ 底层克隆与精准裁剪 已启用");

            var frameIds = new List<ObjectId>[plan.Total];
            try
            {
                // ===== 阶段A：仅为非空幅在源图画临时图框 =====
                try
                {
                    using (var tr = srcDb.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(srcDb.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                        var drawer = new FrameDrawer(srcDb, tr, opt);
                        for (int i = 0; i < plan.Total; i++)
                        {
                            if (buckets[i] == null || buckets[i].Count == 0)
                            { res.Skipped.Add(numbers[i]); frameIds[i] = null; continue; }   // 跳过空幅

                            int r = i / plan.Cols, c = i % plan.Cols;
                            frameIds[i] = drawer.DrawSheet(ms, plan.Corner(r, c), plan.Corner(r, c),
                                opt.ScaleDen / 1000.0, 1.0, plan, r, c, numbers[i]);
                        }
                        tr.Commit();
                    }
                }
                catch (System.Exception ex) { throw new System.Exception("源图绘制临时图框失败: " + ex.Message); }

                // ===== 阶段B：下沉至 C++ 引擎逐幅导出 =====
                int done = 0;
                for (int i = 0; i < plan.Total; i++)
                {
                    if (frameIds[i] == null) continue;
                    int r = i / plan.Cols, c = i % plan.Cols;
                    Point2d sw = plan.Corner(r, c);
                    string file = Path.Combine(dir, SafeName(numbers[i]) + ".dwg");
                    try
                    {
                        // 🚀 直接调用 C++ 接口，彻底解决 C# 内存爆破和假死问题
                        bool ok = SplitAndExportSheet(sw.X, sw.Y, sw.X + plan.W, sw.Y + plan.H, file);

                        if (ok) res.Files.Add(file);
                        else res.Errors.Add(numbers[i] + " → C++ 引擎导出失败(可能图幅为空或被完全剔除)");
                    }
                    catch (System.Exception ex)
                    {
                        res.Errors.Add(numbers[i] + " → P/Invoke C++引擎调用失败: " + ex.Message + " (请检查ARX/ZRX是否已加载或路径正确)");
                    }

                    done++;
                    if (progress != null) progress(done, numbers[i]);
                }
            }
            finally { CleanupTempFrames(srcDb, frameIds); }
            return res;
        }

        static void CleanupTempFrames(Database db, List<ObjectId>[] frameIds)
        {
            if (frameIds == null) return;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (List<ObjectId> list in frameIds)
                    {
                        if (list == null) continue;
                        foreach (ObjectId id in list)
                        {
                            if (id.IsNull) continue;
                            try
                            {
                                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                if (ent != null && !ent.IsErased) ent.Erase();
                            }
                            catch { }
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        // 保留 C# 侧的分桶逻辑：用于在 UI 和 Commands 层面快速预判哪些图幅完全是空的
        public static List<ObjectId>[] BucketBySheet(Database db, SheetPlan plan, double pad)
        {
            var buckets = new List<ObjectId>[plan.Total];
            for (int k = 0; k < buckets.Length; k++) buckets[k] = new List<ObjectId>();

            var ids = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms) ids.Add(id);
                tr.Commit();
            }

            int batch = 50000;
            for (int i = 0; i < ids.Count; i += batch)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    int end = Math.Min(i + batch, ids.Count);
                    for (int j = i; j < end; j++)
                    {
                        Entity ent = null;
                        try { ent = tr.GetObject(ids[j], OpenMode.ForRead) as Entity; }
                        catch { continue; }
                        if (ent == null || ent.IsErased || ent is Viewport) continue;
                        try
                        {
                            if (ent.Layer != null &&
                                ent.Layer.StartsWith(FrameLayerPrefix, StringComparison.OrdinalIgnoreCase))
                                continue;
                            Extents3d e = ent.GeometricExtents;
                            int c0 = Math.Max(0, (int)Math.Floor((e.MinPoint.X - pad - plan.Origin.X) / plan.W));
                            int c1 = Math.Min(plan.Cols - 1, (int)Math.Floor((e.MaxPoint.X + pad - plan.Origin.X) / plan.W));
                            int r0 = Math.Max(0, (int)Math.Floor((e.MinPoint.Y - pad - plan.Origin.Y) / plan.H));
                            int r1 = Math.Min(plan.Rows - 1, (int)Math.Floor((e.MaxPoint.Y + pad - plan.Origin.Y) / plan.H));
                            for (int r = r0; r <= r1; r++)
                                for (int c = c0; c <= c1; c++)
                                    buckets[r * plan.Cols + c].Add(ids[j]);
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }
            return buckets;
        }

        static void WriteMsg(string s)
        {
            try
            {
                AcadApp.DocumentManager
                    .MdiActiveDocument.Editor.WriteMessage(s);
            }
            catch { }
        }

        static string SafeName(string s)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s;
        }
    }
}