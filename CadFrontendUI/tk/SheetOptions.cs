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
    public enum OutputMode { ModelOverlay, LayoutPerSheet, SplitDwg }

    public class SheetOptions
    {
        public int ScaleDen = 2000;
        public double PaperW = 500, PaperH = 500;
        public double Margin = 6.0;   // ★修复②：图廓带宽默认 12→6mm
        public double GridPaper = 100;
        public double CrossLen = 10;
        public bool FullGridLine = false, DrawCross = true, LabelCoord = true, DrawScaleBar = false;
        public bool AlignSheet = true;
        public Point2d RangeMin, RangeMax;

        public string UnitName = "测绘单位全称", SheetTitle = "", SurveyDate = "";
        public string CoordSys = "2000国家大地坐标系", Datum = "1985国家高程基准";
        public string Surveyor = "", Plotter = "", Checker = "";
        public bool UseCoordNumber = true;
        public string Prefix = "";

        public OutputMode Mode = OutputMode.LayoutPerSheet;
        public string OutDir = "";
        public bool RebuildLayouts = true;
        public bool SaveEachDwg = true;
    }

    public class SheetPlan
    {
        public Point2d Origin; public int Rows, Cols;
        public double W, H;
        public int Total => Rows * Cols;
        public Point2d Corner(int row, int col) => new Point2d(Origin.X + col * W, Origin.Y + row * H);

        public static SheetPlan Create(SheetOptions o)
        {
            double w = o.PaperW * o.ScaleDen / 1000.0, h = o.PaperH * o.ScaleDen / 1000.0;
            double x0 = o.RangeMin.X, y0 = o.RangeMin.Y;
            if (o.AlignSheet) { x0 = Math.Floor(x0 / w) * w; y0 = Math.Floor(y0 / h) * h; }
            int cols = Math.Max(1, (int)Math.Ceiling((o.RangeMax.X - x0) / w - 1e-9));
            int rows = Math.Max(1, (int)Math.Ceiling((o.RangeMax.Y - y0) / h - 1e-9));
            return new SheetPlan { Origin = new Point2d(x0, y0), Rows = rows, Cols = cols, W = w, H = h };
        }
    }

    public static class SheetNamer
    {
        public static string Number(SheetOptions o, SheetPlan plan, int row, int col)
        {
            Point2d sw = plan.Corner(row, col);
            if (o.UseCoordNumber)
                return string.Format("{0:0.0}-{1:0.0}", sw.Y / 1000.0, sw.X / 1000.0);
            int n = (plan.Rows - 1 - row) * plan.Cols + col + 1;
            return o.Prefix + n.ToString("00");
        }
    }
}
