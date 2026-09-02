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
using GI = ZwSoft.ZwCAD.GraphicsInterface;
using AcadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#else // 默认 AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Colors;
using GI = Autodesk.AutoCAD.GraphicsInterface;
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
    public class FrameDrawer
    {
        // ★字高=图纸mm规范值（1:1000基准），M()自动按比例尺缩放实地坐标：
        //   拆分模式 mm2d=ScaleDen/1000 → 1:1000实地3m/1:2000实地6m，打印1:N后图上均=3mm
        //   布局模式 mm2d=1.0           → 字高直接=图纸mm
        const double HTitle = 3.0;     // 图名            3mm
        const double HMid   = 2.0;     // 编号/单位名     2mm
        const double HSong  = 2.0;     // 比例尺注记      2mm
        const double HCoord = 1.5;     // 坐标注记        1.5mm
        const double HSmall = 1.5;     // 底部整饰注记    1.5mm
        readonly Database _db; readonly Transaction _tr; readonly SheetOptions _o;
        ObjectId _lyFrame, _lyGrid, _lyText, _stDeng, _stThin, _stSong;
        BlockTableRecord _target;
        Point2d _base, _sw; double _mm2d, _m2d;
        readonly List<ObjectId> _ids = new List<ObjectId>();

        public FrameDrawer(Database db, Transaction tr, SheetOptions opt)
        {
            _db = db; _tr = tr; _o = opt;
            _lyFrame = EnsureLayer("TK-图框边线", 7);
            _lyGrid  = EnsureLayer("TK-坐标格网", 4);
            _lyText  = EnsureLayer("TK-图廓注记", 2);
            _stDeng = EnsureTextStyle("图式-等线体", "DengXian", "黑体");
            _stThin = EnsureTextStyle("图式-细等线体", "DengXian Light", "微软雅黑 Light");
            _stSong = EnsureTextStyle("图式-宋体", "宋体", "SimSun");
        }

        double M(double mm) => mm * _mm2d;
        double X(double xm) => _base.X + (xm - _sw.X) * _m2d;
        double Y(double ym) => _base.Y + (ym - _sw.Y) * _m2d;

        public List<ObjectId> DrawSheet(BlockTableRecord target, Point2d drawBase, Point2d sheetSW,
            double mmToDraw, double modelToDraw, SheetPlan plan, int row, int col, string number)
        {
            _target = target; _base = drawBase; _sw = sheetSW;
            _mm2d = mmToDraw; _m2d = modelToDraw; _ids.Clear();

            Point2d ne = new Point2d(sheetSW.X + plan.W, sheetSW.Y + plan.H);
            double gap = M(_o.Margin);
            Point2d iSW = new Point2d(X(sheetSW.X), Y(sheetSW.Y));
            Point2d iNE = new Point2d(X(ne.X), Y(ne.Y));
            Point2d oSW = new Point2d(iSW.X - gap, iSW.Y - gap);
            Point2d oNE = new Point2d(iNE.X + gap, iNE.Y + gap);

            AddRect(oSW, oNE, _lyFrame, LineWeight.LineWeight100);
            AddRect(iSW, iNE, _lyFrame, LineWeight.LineWeight015);
            DrawGrid(sheetSW, ne, iSW, iNE, gap);
            DrawTop(oSW, oNE, number);
            DrawUnitVertical(oSW, oNE);          // ★bug2新增：测绘单位左侧竖排
            DrawBottom(oSW, oNE);                // ★bug2重写
            if (_o.DrawScaleBar) DrawScaleBar(iSW);
            return new List<ObjectId>(_ids);
        }

        // ★bug3：图廓间坐标注记——角部错位避让、内框刻度向内侧绘制、角部连线
        void DrawGrid(Point2d sw, Point2d ne, Point2d iSW, Point2d iNE, double gap)
        {
            double step = _o.GridPaper * _o.ScaleDen / 1000.0;
            if (step <= 1e-9) return;

            // half 为内部十字格网的单边半长，边框刻度线也使用此长度向内绘制
            double half = M(_o.CrossLen) / 2.0, hTxt = M(HCoord), tol = step * 1e-6;
            double tick = half;

            // 1. 遍历X方向（处理上下边框及竖向格网/十字）
            for (double xm = Math.Ceiling(sw.X / step) * step; xm <= ne.X + tol; xm += step)
            {
                double x = X(xm);
                bool edge = xm - sw.X < tol || ne.X - xm < tol;

                if (_o.FullGridLine && !edge)
                    AddLine(new Point2d(x, Y(sw.Y)), new Point2d(x, Y(ne.Y)), _lyGrid, LineWeight.ByLayer);
                else if (_o.DrawCross && !edge)
                {
                    for (double ym = Math.Ceiling(sw.Y / step) * step; ym <= ne.Y + tol; ym += step)
                    {
                        if (ym - sw.Y < tol || ne.Y - ym < tol) continue;
                        double y = Y(ym);
                        AddLine(new Point2d(x - half, y), new Point2d(x + half, y), _lyGrid, LineWeight.ByLayer);
                        AddLine(new Point2d(x, y - half), new Point2d(x, y + half), _lyGrid, LineWeight.ByLayer);
                    }
                }

                if (edge)
                {
                    // 【关键修正】：四个角部，格网线向外侧延伸，连接内框和外框，封闭四个角
                    AddLine(new Point2d(x, iSW.Y - gap), new Point2d(x, iSW.Y), _lyGrid, LineWeight.ByLayer);
                    AddLine(new Point2d(x, iNE.Y), new Point2d(x, iNE.Y + gap), _lyGrid, LineWeight.ByLayer);
                }
                else
                {
                    // 【关键修正】：非角部的边界点，仅向内侧绘制 tick 长度的刻度
                    AddLine(new Point2d(x, iSW.Y), new Point2d(x, iSW.Y + tick), _lyGrid, LineWeight.ByLayer);
                    AddLine(new Point2d(x, iNE.Y - tick), new Point2d(x, iNE.Y), _lyGrid, LineWeight.ByLayer);
                }

                if (_o.LabelCoord)
                {
                    string s = FmtKm(xm, step);
                    double dx = 0;
                    if (xm - sw.X < tol) dx = M(4.0);
                    else if (ne.X - xm < tol) dx = -M(4.0);
                    AddText(s, new Point2d(x + dx, (iSW.Y - gap + iSW.Y) / 2), hTxt, _stThin, _lyText,
                        TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, 0);
                    AddText(s, new Point2d(x + dx, (iNE.Y + iNE.Y + gap) / 2), hTxt, _stThin, _lyText,
                        TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, 0);
                }
            }

            // 2. 遍历Y方向（处理左右边框及横向格网/十字）
            for (double ym = Math.Ceiling(sw.Y / step) * step; ym <= ne.Y + tol; ym += step)
            {
                double y = Y(ym);
                bool edge = ym - sw.Y < tol || ne.Y - ym < tol;

                if (_o.FullGridLine && !edge)
                    AddLine(new Point2d(X(sw.X), y), new Point2d(X(ne.X), y), _lyGrid, LineWeight.ByLayer);

                if (edge)
                {
                    // 【关键修正】：四个角部，格网线向外侧延伸，连接内框和外框，封闭四个角
                    AddLine(new Point2d(iSW.X - gap, y), new Point2d(iSW.X, y), _lyGrid, LineWeight.ByLayer);
                    AddLine(new Point2d(iNE.X, y), new Point2d(iNE.X + gap, y), _lyGrid, LineWeight.ByLayer);
                }
                else
                {
                    // 【关键修正】：非角部的边界点，仅向内侧绘制 tick 长度的刻度
                    AddLine(new Point2d(iSW.X, y), new Point2d(iSW.X + tick, y), _lyGrid, LineWeight.ByLayer);
                    AddLine(new Point2d(iNE.X - tick, y), new Point2d(iNE.X, y), _lyGrid, LineWeight.ByLayer);
                }

                if (_o.LabelCoord)
                {
                    string s = FmtKm(ym, step);
                    double dy = 0;
                    if (ym - sw.Y < tol) dy = M(4.0);
                    else if (ne.Y - ym < tol) dy = -M(4.0);
                    AddText(s, new Point2d((iSW.X - gap + iSW.X) / 2, y + dy), hTxt, _stThin, _lyText,
                        TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, 90);
                    AddText(s, new Point2d((iNE.X + iNE.X + gap) / 2, y + dy), hTxt, _stThin, _lyText,
                        TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, 90);
                }
            }
        }

        static string FmtKm(double coordMeter, double stepMeter)
        {
            double km = stepMeter / 1000.0;
            int dec = km >= 0.1 ? 1 : 2;
            return (coordMeter / 1000.0).ToString("F" + dec);
        }

        void DrawTop(Point2d oSW, Point2d oNE, string number)
        {
            double baseY = oNE.Y + M(5.0), cx = (oSW.X + oNE.X) / 2;
            AddText("编号: " + number, new Point2d(oSW.X, baseY), M(HMid), _stDeng, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);
            if (!string.IsNullOrWhiteSpace(_o.SheetTitle))
                AddText(_o.SheetTitle, new Point2d(cx, baseY), M(HTitle), _stDeng, _lyText,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextBase, 0);
        }

        // ★bug2重写：左组(3cm)/中组(9cm)/右组(5cm，右对齐右外图廓)
        void DrawBottom(Point2d oSW, Point2d oNE)
        {
            double rowGap = M(HSmall * 1.5);                         // ★1.5倍行距（随字高联动：1.5mm×1.5=2.25mm）

            // 左组：距底框0.3cm起，距左外图廓3mm处开始左对齐（细等线13K）
            double yL = oSW.Y - M(3.0);
            double xL = oSW.X + M(3.0);

            AddText(_o.SurveyDate + "测量", new Point2d(xL, yL), M(HSmall), _stThin, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);
            AddText("坐标系: " + _o.CoordSys, new Point2d(xL, yL - rowGap), M(HSmall), _stThin, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);
            AddText("深度(高程)基准: " + _o.Datum, new Point2d(xL, yL - 2 * rowGap), M(HSmall), _stThin, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);

            // 中组：距底框0.9cm，水平居中（宋体20K）
            AddText("1:" + _o.ScaleDen, new Point2d((oSW.X + oNE.X) / 2, oSW.Y - M(9.0)), M(HSong), _stSong, _lyText,
                TextHorizontalMode.TextCenter, TextVerticalMode.TextBase, 0);

            // 右组：距底框5mm起，左端距右外图廓50mm处开始左对齐（细等线13K）
            double yR = oSW.Y - M(5.0);
            double xR = oNE.X - M(50.0); // 距右图廓50.0mm

            // 👇【关键改动】：请检查以下三行的 Point2d 中必须是 xR，且必须是 TextLeft
            AddText("测 量: " + _o.Surveyor, new Point2d(xR, yR), M(HSmall), _stThin, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);
            AddText("绘 图: " + _o.Plotter, new Point2d(xR, yR - rowGap), M(HSmall), _stThin, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);
            AddText("审 核: " + _o.Checker, new Point2d(xR, yR - 2 * rowGap), M(HSmall), _stThin, _lyText,
                TextHorizontalMode.TextLeft, TextVerticalMode.TextBase, 0);
        }

        /// <summary>
        /// 绘制测绘单位全称（竖向排列，如对联方式）
        /// 位于外图廓左下角，距左外图廓3mmm，距底外图廓3mm
        /// 字体高度使用 HMid（4mm），行距为字体高度的1.5倍
        /// </summary>
        void DrawUnitVertical(Point2d oSW, Point2d oNE)
        {
            if (string.IsNullOrWhiteSpace(_o.UnitName)) return;

            string name = _o.UnitName.Trim();
            double charHeight = M(HMid);                    // 字体高度 4mm
            double lineSpacing = charHeight * 1.5;          // 行距 = 6mm

            // X坐标：外图廓左侧3mm处
            double x = oSW.X - M(3.0);

            // Y坐标：保证最下方的字距离底外图廓3mm。
            // 👇【关键改动】：改为 + M(3.0)，并加上半个字高的偏移量(charHeight / 2)保证边缘不压线
            double startY = oSW.Y + M(3.0) + (charHeight / 2.0);

            // 逐字绘制，从下到上排列（如对联方式，从底部开始）
            // 逐字绘制，按正常阅读顺序从上往下排列，最后一个字在最底部
            for (int i = 0; i < name.Length; i++)
            {
                string ch = name[i].ToString();

                // 【关键改动】：反转 Y 坐标的分配。
                // 让第一个字(i=0)在最高点，最后一个字(i=name.Length-1)在最低点(startY)
                double y = startY + (name.Length - 1 - i) * lineSpacing;

                AddText(ch, new Point2d(x, y), charHeight, _stDeng, _lyText,
                    TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid, 0);
            }
        }

        void DrawScaleBar(Point2d iSW)
        {
            double unit = M(10), len = unit * 6, t = M(1.5);
            double x0 = iSW.X + M(6), y0 = iSW.Y + M(10);
            AddLine(new Point2d(x0, y0), new Point2d(x0 + len, y0), _lyGrid, LineWeight.ByLayer);
            for (int i = 0; i <= 6; i++)
            {
                double x = x0 + i * unit;
                AddLine(new Point2d(x, y0 - t), new Point2d(x, y0 + t), _lyGrid, LineWeight.ByLayer);
                if (i % 2 == 0)
                {
                    double v = i * 10.0 * _o.ScaleDen / 1000.0;   // ★每格=图上10mm的实地长度（原 GridPaper×2 算法有误）
                    string lab = i == 0 ? "0" : (v >= 1000 ? (v / 1000.0).ToString("0.#") + "km" : v.ToString("0.#") + "m");
                    AddText(lab, new Point2d(x, y0 + t + M(0.5)), M(2.5), _stThin, _lyText,
                        TextHorizontalMode.TextCenter, TextVerticalMode.TextBase, 0);
                }
            }
        }

        ObjectId Append(Entity ent)
        {
            ObjectId id = _target.AppendEntity(ent);
            _tr.AddNewlyCreatedDBObject(ent, true);
            _ids.Add(id);
            return id;
        }

        void AddLine(Point2d a, Point2d b, ObjectId layer, LineWeight lw)
        {
            Append(new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0))
            { LayerId = layer, LineWeight = lw });
        }

        void AddRect(Point2d p1, Point2d p2, ObjectId layer, LineWeight lw)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);
            pl.Closed = true;
            pl.LayerId = layer; pl.LineWeight = lw;
            Append(pl);
        }

        void AddText(string s, Point2d at, double h, ObjectId style, ObjectId layer,
                     TextHorizontalMode hj, TextVerticalMode vj, double rotDeg)
        {
            if (string.IsNullOrEmpty(s)) return;
            var t = new DBText
            {
                TextString = s, Height = h, TextStyleId = style, LayerId = layer,
                Position = new Point3d(at.X, at.Y, 0)
            };
            if (hj != TextHorizontalMode.TextLeft) t.HorizontalMode = hj;
            if (vj != TextVerticalMode.TextBase) t.VerticalMode = vj;
            if (hj != TextHorizontalMode.TextLeft || vj != TextVerticalMode.TextBase)
                t.AlignmentPoint = new Point3d(at.X, at.Y, 0);
            if (rotDeg != 0) t.Rotation = rotDeg * Math.PI / 180.0;
            Append(t);
        }

        ObjectId EnsureLayer(string name, short ci)
        {
            var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, ci) };
            try { rec.IsPlottable = true; } catch { }
            lt.Add(rec); _tr.AddNewlyCreatedDBObject(rec, true);
            return rec.ObjectId;
        }

        ObjectId EnsureTextStyle(string name, string font, string fallback)
        {
            var ts = (TextStyleTable)_tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead);
            if (ts.Has(name)) return ts[name];
            ts.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = name };
            try { rec.Font = new GI.FontDescriptor(font, false, false, 134, 2); }
            catch
            {
                try { rec.Font = new GI.FontDescriptor(fallback, false, false, 134, 2); }
                catch { rec.FileName = "txt.shx"; }
            }
            ts.Add(rec); _tr.AddNewlyCreatedDBObject(rec, true);
            return rec.ObjectId;
        }
    }
}
