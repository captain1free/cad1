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

namespace SheetFramePlugin
{
    public static class LayoutSheetBuilder
    {
        public static string Build(Database db, Transaction tr, SheetOptions opt,
            SheetPlan plan, int row, int col, string number, HashSet<string> used)
        {
            string name = "幅-" + number;
            if (used.Contains(name))
            { int k = 2; while (used.Contains(name + " (" + k + ")")) k++; name = name + " (" + k + ")"; }
            used.Add(name);

            LayoutManager lm = LayoutManager.Current;
            ObjectId layId = lm.CreateLayout(name);
            lm.CurrentLayout = name;

            var layout = (Layout)tr.GetObject(layId, OpenMode.ForWrite);
            var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);

            var del = new List<ObjectId>();
            foreach (ObjectId id in btr)
            { var e = tr.GetObject(id, OpenMode.ForRead); if (e is Viewport) del.Add(id); }
            foreach (ObjectId id in del) tr.GetObject(id, OpenMode.ForWrite).Erase();

            double mL = 45, mB = 100;   // ★左加宽容纳竖排单位名(3cm)，底加深容纳9cm整饰带
            Point2d innerSW = new Point2d(mL + opt.Margin, mB + opt.Margin);
            Point2d sheetSW = plan.Corner(row, col);

            var drawer = new FrameDrawer(db, tr, opt);
            drawer.DrawSheet(btr, innerSW, sheetSW, 1.0, 1000.0 / opt.ScaleDen, plan, row, col, number);

            ObjectId vpLayer = EnsureLayer(db, tr, "TK-视口", 1);
            var vp = new Viewport { LayerId = vpLayer };
            vp.Width = opt.PaperW; vp.Height = opt.PaperH;
            vp.CenterPoint = new Point3d(innerSW.X + opt.PaperW / 2.0, innerSW.Y + opt.PaperH / 2.0, 0);
            btr.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
            vp.On = true;
            vp.ViewDirection = new Vector3d(0, 0, 1);
            vp.TwistAngle = 0;
            vp.ViewCenter = new Point2d(sheetSW.X + plan.W / 2.0, sheetSW.Y + plan.H / 2.0);
            vp.ViewHeight = plan.H;
            vp.Locked = true;

            TryConfigPlot(layout, opt);
            return name;
        }

        static void TryConfigPlot(Layout layout, SheetOptions o)
        {
            try
            {
                PlotSettings ps = new PlotSettings(layout.ModelType);
                ps.CopyFrom(layout);
                PlotSettingsValidator psv = PlotSettingsValidator.Current;
                psv.SetPlotType(ps, PlotType.Layout);
                psv.SetPlotRotation(ps, o.PaperW >= o.PaperH ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                try { psv.SetPlotConfigurationName(ps, "DWG To PDF.pc3", null); } catch { }
                layout.CopyFrom(ps);
            }
            catch { }
        }

        static ObjectId EnsureLayer(Database db, Transaction tr, string name, short ci)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, ci) };
            try { rec.IsPlottable = (name != "TK-视口"); } catch { }
            lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
            return rec.ObjectId;
        }

        public static HashSet<string> ExistingNames(Database db)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in lt)
                {
                    var l = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    set.Add(l.LayoutName);
                }
                tr.Commit();
            }
            return set;
        }

        public static void RemoveOld(Document doc)
        {
            Database db = doc.Database;
            var names = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in lt)
                {
                    var l = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (l.LayoutName.StartsWith("幅-", StringComparison.Ordinal)) names.Add(l.LayoutName);
                }
                tr.Commit();
            }
            if (names.Count == 0) return;
            try { LayoutManager.Current.CurrentLayout = "Model"; } catch { }
            foreach (var n in names) { try { LayoutManager.Current.DeleteLayout(n); } catch { } }
        }
    }
}
