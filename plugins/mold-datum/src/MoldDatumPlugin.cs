using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CimUIInfrastructure.Commands;
using CimUIInfrastructure.PlugIn;
using CimApp = interop.CimatronE;
using Mdlr = interop.CimMdlrAPI;
using Nc = interop.CimNcAPI;
using Svc = interop.CimServicesAPI;
using BaseApi = interop.CimBaseAPI;

namespace MoldDatum
{
    // ======================= 日志 =======================
    public static class DatumLog
    {
        public static readonly string LogPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "NcNamer", "mold_datum_log.txt");

        public static void Write(string s)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + s + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    // ======================= 参数 =======================
    public class DatumParams
    {
        /// <summary>刀具直径，用于推算凹深 D/2-d</summary>
        public double ToolDiameter = 6.0;
        /// <summary>基准线距侧面的距离（线在侧面外侧）</summary>
        public double Offset = 2.5;
        /// <summary>线起点距角点（侧边交点）的距离</summary>
        public double CornerGap = 10.0;
        /// <summary>基准线长度</summary>
        public double Length = 10.0;
        /// <summary>下铣深度（来自模板，仅供参考显示，插件不改）</summary>
        public double CutDepth = 10.0;

        public double RecessDepth { get { return ToolDiameter / 2.0 - Offset; } }
        public bool IsValid { get { return Offset > 0 && Offset < ToolDiameter / 2.0 && Length > 0 && CornerGap >= 0; } }
        public string ValidateReason
        {
            get
            {
                if (Offset <= 0) return "距侧面必须大于 0";
                if (Offset >= ToolDiameter / 2.0)
                    return "距侧面(" + Offset + ") 必须小于刀半径(" + (ToolDiameter / 2.0) + ")，否则切不到侧面";
                if (Length <= 0) return "线长必须大于 0";
                if (CornerGap < 0) return "距角点不能为负";
                return "";
            }
        }

        public DatumParams Clone()
        {
            DatumParams c = new DatumParams();
            c.ToolDiameter = ToolDiameter;
            c.Offset = Offset;
            c.CornerGap = CornerGap;
            c.Length = Length;
            c.CutDepth = CutDepth;
            return c;
        }
    }

    // ======================= 一条基准线 =======================
    public class DatumLine
    {
        public double[] P1 = new double[3];
        public double[] P2 = new double[3];
        public int FaceIndex;      // 1..4
        public string FaceLabel;   // 例如 "+X"
        public double Z;
        /// <summary>该线跨度内顶面的高度落差（0 表示是平面）</summary>
        public double ZSpan;
        public object Created;     // 创建后返回的实体

        public double[] DirNormalized()
        {
            double dx = P2[0] - P1[0], dy = P2[1] - P1[1], dz = P2[2] - P1[2];
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-9) return new double[] { 1, 0, 0 };
            return new double[] { dx / len, dy / len, dz / len };
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "面{0}({1}) ({2:0.###},{3:0.###},{4:0.###})→({5:0.###},{6:0.###},{7:0.###}) Z={8:0.###}{9}",
                FaceIndex, FaceLabel, P1[0], P1[1], P1[2], P2[0], P2[1], P2[2], Z,
                ZSpan > 0.001 ? (" 落差=" + ZSpan.ToString("0.###", CultureInfo.InvariantCulture)) : "");
        }
    }

    // ======================= 侧面信息 =======================
    public class FaceInfo
    {
        public object Raw;
        /// <summary>该「侧面」包含的实际面（同向共面的面会被合并）</summary>
        public List<object> Faces = new List<object>();
        public double[] Min = new double[3];
        public double[] Max = new double[3];
        public double[] Center = new double[3];
        public double[] Normal = new double[3];
        public double[] Tangent = new double[3];
        public double MaxZ;
        public double TMin, TMax;
        public string Label = "?";
        public string Warning = "";
        /// <summary>法向轴：0=X, 1=Y</summary>
        public int Axis;
        /// <summary>该面在法向轴上的坐标</summary>
        public double Plane;
        /// <summary>边界采样点：[0]=沿切线位置（相对面中心），[1]=Z</summary>
        public List<double[]> EdgeSamples = new List<double[]>();

        public double Area()
        {
            double along = TMax - TMin;
            double h = Max[2] - Min[2];
            return Math.Abs(along * h);
        }
    }

    // ======================= 会话 =======================
    public class DatumSession
    {
        public CimApp.IApplication App;
        public CimApp.ICimDocument Doc;
        public DatumParams Params = new DatumParams();
        public List<FaceInfo> Faces = new List<FaceInfo>();
        public List<DatumLine> Lines = new List<DatumLine>();
        public string LoadError = "";
        public int IgnoredNonFace = 0;
        public bool IsNcDoc = false;

        /// <summary>NC 模型（阶段2 用）</summary>
        public Nc.INcModel NcModel;
        public Nc.IModel NcIModel;

        public DatumGeometry Geo;

        public void EnsureGeo()
        {
            if (Geo == null) Geo = new DatumGeometry(App);
        }

        /// <summary>自动识别 4 个侧面并生成 8 条线（无需手动选择）</summary>
        public void LoadAuto()
        {
            Faces.Clear();
            Lines.Clear();
            LoadError = "";
            IgnoredNonFace = 0;

            CaptureBasic();

            EnsureGeo();
            string err;
            List<FaceInfo> infos = Geo.AutoDetectSides(out err);
            if (infos == null || infos.Count != 4)
            {
                LoadError = err == null ? "自动识别失败。" : err;
                return;
            }
            Faces = infos;

            List<DatumLine> lines = Geo.BuildDatumLines(infos, Params, out err);
            if (lines == null || lines.Count != 8)
            {
                LoadError = err;
                Lines = lines == null ? new List<DatumLine>() : lines;
                return;
            }
            Lines = lines;
            DatumLog.Write("LoadAuto ok: 4 面 / 8 线");
        }

        private void CaptureBasic()
        {
            try { if (Doc != null) IsNcDoc = (Doc.Type == CimApp.DocumentEnumType.cmNc); } catch { }
            try
            {
                Nc.IModelContainer cont = Doc as Nc.IModelContainer;
                if (cont != null)
                {
                    Nc.IModel m = cont.Model;
                    NcIModel = m;
                    NcModel = m as Nc.INcModel;
                }
            }
            catch (Exception ex) { DatumLog.Write("capture NcModel failed: " + ex.Message); }
        }

        /// <summary>读取当前选择并重建 8 条线</summary>
        public void LoadSelection()
        {
            Faces.Clear();
            Lines.Clear();
            LoadError = "";
            IgnoredNonFace = 0;

            try
            {
                if (Doc != null)
                    IsNcDoc = (Doc.Type == CimApp.DocumentEnumType.cmNc);
            }
            catch { }

            // 取 NC 模型（阶段2 绑定几何要用），失败不影响几何阶段
            try
            {
                Nc.IModelContainer cont = Doc as Nc.IModelContainer;
                if (cont != null)
                {
                    Nc.IModel m = cont.Model;
                    NcIModel = m;
                    NcModel = m as Nc.INcModel;
                }
            }
            catch (Exception ex) { DatumLog.Write("capture NcModel failed: " + ex.Message); }

            List<object> raw = new List<object>();
            try
            {
                ApiShell.CimPickToolHelperCSharp pick =
                    new ApiShell.CimPickToolHelperCSharp(App);
                BaseApi.ICimEntityList selected = pick.GetSelectedEntities();
                if (selected != null)
                {
                    foreach (object o in selected)
                    {
                        BaseApi.ICimEntity e = o as BaseApi.ICimEntity;
                        if (e == null) { continue; }
                        if (e.Type == BaseApi.EntityEnumType.cmFace) raw.Add(e);
                        else IgnoredNonFace++;
                    }
                }
            }
            catch (Exception ex)
            {
                LoadError = "读取选择失败: " + ex.Message;
                DatumLog.Write("LoadSelection pick failed: " + ex);
                return;
            }

            if (raw.Count == 0)
            {
                LoadError = "没有选中任何面。请在模型上框选/点选铁模的 4 个侧面后再运行。";
                return;
            }
            if (raw.Count != 4)
            {
                LoadError = "选中了 " + raw.Count + " 个面，需要正好 4 个侧面（可忽略其它类型 " + IgnoredNonFace + " 个）。";
                return;
            }

            EnsureGeo();

            // 一次取回 4 个面的信息
            string err;
            List<FaceInfo> infos = Geo.ReadFaces(raw, out err);
            if (infos == null || infos.Count != 4)
            {
                LoadError = err == null ? "无法读取面的几何信息。" : err;
                return;
            }
            Faces = infos;

            // 生成 8 条线
            Lines = Geo.BuildDatumLines(infos, Params, out err);
            if (Lines == null || Lines.Count != 8)
            {
                LoadError = err;
                Lines = Lines == null ? new List<DatumLine>() : Lines;
                return;
            }

            DatumLog.Write("LoadSelection ok: faces=4, lines=8, docNc=" + IsNcDoc);
        }

        /// <summary>仅按当前参数重算线（不改选择）</summary>
        public void Rebuild()
        {
            if (Faces.Count != 4) return;
            EnsureGeo();
            string err;
            List<DatumLine> lines = Geo.BuildDatumLines(Faces, Params, out err);
            if (lines != null) { Lines = lines; LoadError = ""; }
            else LoadError = err;
        }
    }

    // ======================= 几何算法 =======================
    public class DatumGeometry
    {
        private readonly ApiShell.GeomData _gd;
        private readonly ApiShell.Modeler _mdl;

        public DatumGeometry(CimApp.IApplication app)
        {
            _gd = new ApiShell.GeomData(app);
            _mdl = new ApiShell.Modeler(app);
        }

        /// <summary>按两点创建一条模型直线（官方 ApiShell 封装）</summary>
        public Mdlr.ICimEntity CreateLine(DatumLine ln)
        {
            return _mdl.CreateLine2Points(ln.P1, ln.P2);
        }

        public static bool TryParseBox(double[] vals, out double[] min, out double[] max)
        {
            min = new double[3];
            max = new double[3];
            if (vals == null) return false;

            if (vals.Length == 6)
            {
                min[0] = vals[0]; min[1] = vals[1]; min[2] = vals[2];
                max[0] = vals[3]; max[1] = vals[4]; max[2] = vals[5];
                return true;
            }
            if (vals.Length >= 24)
            {
                min[0] = min[1] = min[2] = double.MaxValue;
                max[0] = max[1] = max[2] = double.MinValue;
                for (int i = 0; i + 2 < vals.Length; i += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        double v = vals[i + k];
                        if (v < min[k]) min[k] = v;
                        if (v > max[k]) max[k] = v;
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 自动识别：扫描模型全部面，按「法向轴 + 平面位置」分组，
        /// 取 ±X / ±Y 四个方向各自面积最大的一组作为 4 个侧面。
        /// 同向共面的多块面会被合并（应对侧面被分割的情况）。
        /// </summary>
        public List<FaceInfo> AutoDetectSides(out string error)
        {
            error = "";
            Svc.ICimEntityList all = null;
            try { all = _gd.GetEntitiesListByFilter(BaseApi.EntityEnumType.cmFace); }
            catch (Exception ex) { DatumLog.Write("AutoDetect get faces failed: " + ex.Message); }
            if (all == null || all.Count == 0) { error = "模型里读不到任何面。"; return null; }

            List<FaceInfo> raws = new List<FaceInfo>();
            double[] gmin = new double[] { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] gmax = new double[] { double.MinValue, double.MinValue, double.MinValue };

            foreach (object o in all)
            {
                Svc.ICimEntity e = o as Svc.ICimEntity;
                if (e == null) continue;

                double[] box = null;
                try { box = _gd.GetEntity_Box_Param(e); }
                catch { continue; }
                double[] mn, mx;
                if (!TryParseBox(box, out mn, out mx)) continue;

                for (int k = 0; k < 3; k++)
                {
                    if (mn[k] < gmin[k]) gmin[k] = mn[k];
                    if (mx[k] > gmax[k]) gmax[k] = mx[k];
                }

                double ex = mx[0] - mn[0], ey = mx[1] - mn[1], ez = mx[2] - mn[2];
                double thin = Math.Min(ex, ey), other = Math.Max(ex, ey);

                if (ez <= thin) continue;                       // 顶面/底面
                if (thin > Math.Max(0.15 * other, 1.0)) continue; // 不是竖直平面

                FaceInfo fi = new FaceInfo();
                fi.Raw = o;
                fi.Faces.Add(o);
                fi.Min = mn; fi.Max = mx;
                fi.Center[0] = (mn[0] + mx[0]) / 2.0;
                fi.Center[1] = (mn[1] + mx[1]) / 2.0;
                fi.Center[2] = (mn[2] + mx[2]) / 2.0;
                fi.MaxZ = mx[2];
                fi.Axis = (ex <= ey) ? 0 : 1;
                fi.Plane = fi.Center[fi.Axis];
                raws.Add(fi);
            }

            if (raws.Count == 0) { error = "没有找到竖直的侧面（模型可能不是轴向放置的块体）。"; return null; }

            double[] c = new double[]
            {
                (gmin[0] + gmax[0]) / 2.0,
                (gmin[1] + gmax[1]) / 2.0,
                (gmin[2] + gmax[2]) / 2.0
            };

            // 按方向 + 平面位置分组
            Dictionary<string, FaceInfo> groups = new Dictionary<string, FaceInfo>();
            List<string> order = new List<string>();

            foreach (FaceInfo fi in raws)
            {
                double sign = (fi.Center[fi.Axis] >= c[fi.Axis]) ? 1.0 : -1.0;
                string label = (fi.Axis == 0) ? (sign > 0 ? "+X" : "-X") : (sign > 0 ? "+Y" : "-Y");
                string key = label + "|" + Math.Round(fi.Plane, 1).ToString(CultureInfo.InvariantCulture);

                FaceInfo g;
                if (!groups.TryGetValue(key, out g))
                {
                    g = new FaceInfo();
                    g.Axis = fi.Axis;
                    g.Plane = fi.Plane;
                    g.Label = label;
                    g.Min = (double[])fi.Min.Clone();
                    g.Max = (double[])fi.Max.Clone();
                    groups[key] = g;
                    order.Add(key);
                }
                else
                {
                    for (int k = 0; k < 3; k++)
                    {
                        if (fi.Min[k] < g.Min[k]) g.Min[k] = fi.Min[k];
                        if (fi.Max[k] > g.Max[k]) g.Max[k] = fi.Max[k];
                    }
                }
                g.Faces.Add(fi.Raw);
                if (fi.MaxZ > g.MaxZ) g.MaxZ = fi.MaxZ;
            }

            // 补充派生量（中心/切向/范围）
            List<FaceInfo> cands = new List<FaceInfo>();
            foreach (string key in order)
            {
                FaceInfo g = groups[key];
                g.Center[0] = (g.Min[0] + g.Max[0]) / 2.0;
                g.Center[1] = (g.Min[1] + g.Max[1]) / 2.0;
                g.Center[2] = (g.Min[2] + g.Max[2]) / 2.0;

                double sign = (g.Axis == 0)
                    ? (g.Center[0] >= c[0] ? 1.0 : -1.0)
                    : (g.Center[1] >= c[1] ? 1.0 : -1.0);

                g.Normal = (g.Axis == 0) ? new double[] { sign, 0, 0 } : new double[] { 0, sign, 0 };
                g.Tangent = (g.Axis == 0) ? new double[] { 0, 1, 0 } : new double[] { 1, 0, 0 };

                int tAxis = (g.Axis == 0) ? 1 : 0;
                double uCenter = g.Center[tAxis];
                g.TMin = g.Min[tAxis] - uCenter;
                g.TMax = g.Max[tAxis] - uCenter;

                double thick = (g.Axis == 0) ? (g.Max[0] - g.Min[0]) : (g.Max[1] - g.Min[1]);
                double span = (g.Axis == 0) ? (g.Max[1] - g.Min[1]) : (g.Max[0] - g.Min[0]);
                if (thick > Math.Max(0.2 * span, 0.5))
                    g.Warning = "沿法向厚度 " + thick.ToString("0.###", CultureInfo.InvariantCulture) + "，可能是斜侧壁";

                SampleGroupProfile(g, uCenter);
                cands.Add(g);
            }

            // 每个方向取面积最大的一组
            List<FaceInfo> picked = new List<FaceInfo>();
            string[] labels = new string[] { "+X", "-X", "+Y", "-Y" };
            foreach (string lb in labels)
            {
                FaceInfo best = null;
                foreach (FaceInfo g in cands)
                {
                    if (g.Label != lb) continue;
                    if (best == null || g.Area() > best.Area()) best = g;
                }
                if (best != null) picked.Add(best);
            }

            if (picked.Count < 4)
            {
                StringBuilder sb = new StringBuilder();
                foreach (FaceInfo g in picked) sb.Append(g.Label + " ");
                error = "只识别到 " + picked.Count + " 个方向的侧面（" + sb + "），需要 4 个（±X/±Y）。";
                return null;
            }

            DatumLog.Write("AutoDetect: 候选组=" + cands.Count + "，选中 4 个侧面");
            foreach (FaceInfo g in picked)
                DatumLog.Write(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: 面数={1} 沿边[{2:0.###},{3:0.###}] 长={4:0.###} Z顶={5:0.###}",
                    g.Label, g.Faces.Count, g.TMin, g.TMax, g.TMax - g.TMin, g.MaxZ));

            picked.Sort(delegate(FaceInfo a, FaceInfo b) { return string.CompareOrdinal(a.Label, b.Label); });
            return picked;
        }

        /// <summary>对一组同向共面的面统一采样边界（用于逐线局部 Z）</summary>
        private void SampleGroupProfile(FaceInfo g, double uCenter)
        {
            g.EdgeSamples.Clear();
            int samples = 0;
            foreach (object o in g.Faces)
            {
                Svc.ICimEntity e = o as Svc.ICimEntity;
                if (e == null) continue;
                try
                {
                    Svc.ICimEntityList edges =
                        _gd.GetEntitiesRelatedByFilter(e, BaseApi.EntityEnumType.cmEdge);
                    if (edges == null) continue;
                    foreach (object eo in edges)
                    {
                        Svc.ICimEntity ed = eo as Svc.ICimEntity;
                        if (ed == null) continue;
                        double[] lim = null;
                        try { lim = _gd.GetEdgeLimit(ed); } catch { continue; }
                        if (lim == null || lim.Length < 2) continue;
                        double t0 = lim[0], t1 = lim[1];
                        if (Math.Abs(t1 - t0) < 1e-12) continue;

                        const int N = 16;
                        for (int k = 0; k <= N; k++)
                        {
                            double u = t0 + (t1 - t0) * k / N;
                            double[] p = null;
                            try { p = _gd.GetEdgeEvaluate(ed, u); } catch { continue; }
                            if (p == null || p.Length < 3) continue;
                            double tAlong = p[0] * g.Tangent[0] + p[1] * g.Tangent[1] + p[2] * g.Tangent[2];
                            g.EdgeSamples.Add(new double[] { tAlong - uCenter, p[2] });
                            samples++;
                        }
                    }
                }
                catch (Exception ex) { DatumLog.Write("SampleGroupProfile face failed: " + ex.Message); }
            }
            DatumLog.Write("Face " + g.Label + ": 采样点=" + samples);
        }

        /// <summary>保留：按选中的 4 个面读取（手动模式）</summary>
        public List<FaceInfo> ReadFaces(List<object> rawFaces, out string error)
        {
            error = "";
            List<FaceInfo> result = new List<FaceInfo>();

            // 第一遍：包围盒
            foreach (object o in rawFaces)
            {
                Svc.ICimEntity e = o as Svc.ICimEntity;
                if (e == null) { error = "面对象类型转换失败。"; return null; }

                double[] box = null;
                try { box = _gd.GetEntity_Box_Param(e); }
                catch (Exception ex) { DatumLog.Write("GetEntity_Box_Param failed: " + ex.Message); }

                double[] mn, mx;
                if (!TryParseBox(box, out mn, out mx))
                {
                    error = "无法读取面的包围盒（返回长度 " + (box == null ? "null" : box.Length.ToString()) + "）。";
                    return null;
                }

                FaceInfo fi = new FaceInfo();
                fi.Raw = o;
                fi.Min = mn; fi.Max = mx;
                fi.Center[0] = (mn[0] + mx[0]) / 2.0;
                fi.Center[1] = (mn[1] + mx[1]) / 2.0;
                fi.Center[2] = (mn[2] + mx[2]) / 2.0;
                fi.MaxZ = mx[2];
                result.Add(fi);
            }

            // 整体中心（用于判断外法向）
            double[] c = new double[3];
            foreach (FaceInfo f in result)
            {
                c[0] += f.Center[0]; c[1] += f.Center[1]; c[2] += f.Center[2];
            }
            c[0] /= result.Count; c[1] /= result.Count; c[2] /= result.Count;

            // 第二遍：方向 / 切线 / 沿边范围
            // 说明：不依赖 GetFaceNormal（实测该接口对 U/V 参数敏感，易返回 null）。
            // 平面侧面在法向轴上的包围盒厚度趋近 0，据此判方向最稳，且对带斜度的侧面也成立。
            List<string> labels = new List<string>();
            for (int fi = 0; fi < result.Count; fi++)
            {
                FaceInfo f = result[fi];
                Svc.ICimEntity e = f.Raw as Svc.ICimEntity;

                double ex = f.Max[0] - f.Min[0];
                double ey = f.Max[1] - f.Min[1];
                double ez = f.Max[2] - f.Min[2];

                if (ez <= ex && ez <= ey)
                {
                    error = "第 " + (fi + 1) + " 个面看起来是顶面/底面（包围盒 Z 向厚度 " +
                            ez.ToString("0.###", CultureInfo.InvariantCulture) +
                            " 最小）。请只选 4 个侧面，不要选顶面/底面。";
                    return null;
                }

                int axis = (ex <= ey) ? 0 : 1;   // 0=X, 1=Y；薄的那个水平轴就是法向轴
                double nSign = (axis == 0)
                    ? (f.Center[0] >= c[0] ? 1.0 : -1.0)
                    : (f.Center[1] >= c[1] ? 1.0 : -1.0);

                f.Normal = (axis == 0) ? new double[] { nSign, 0, 0 } : new double[] { 0, nSign, 0 };
                f.Tangent = (axis == 0) ? new double[] { 0, 1, 0 } : new double[] { 1, 0, 0 };
                f.Label = (axis == 0) ? (nSign > 0 ? "+X" : "-X") : (nSign > 0 ? "+Y" : "-Y");
                labels.Add(f.Label);

                // 沿切线范围（相对面中心）
                int tAxis = (axis == 0) ? 1 : 0;
                double uCenter = f.Center[tAxis];
                f.TMin = f.Min[tAxis] - uCenter;
                f.TMax = f.Max[tAxis] - uCenter;

                // 采样该面边界，得到「沿边位置 → 顶面高度」的剖面，供逐线取局部 Z
                SampleBoundaryProfile(e, f, uCenter);

                double thick = (axis == 0) ? ex : ey;
                double span = (axis == 0) ? ey : ex;
                if (thick > Math.Max(0.2 * span, 0.5))
                {
                    f.Warning = "沿法向厚度 " + thick.ToString("0.###", CultureInfo.InvariantCulture) +
                                "（边长 " + span.ToString("0.###", CultureInfo.InvariantCulture) + "），可能是斜侧壁";
                }

                double[] diag = TryGetFaceNormal(e);
                DatumLog.Write(string.Format(CultureInfo.InvariantCulture,
                    "Face{0} label={1} boxX[{2:0.###},{3:0.###}] boxY[{4:0.###},{5:0.###}] boxZ[{6:0.###},{7:0.###}] axis={8} thick={9:0.###} normalApi={10}",
                    fi + 1, f.Label, f.Min[0], f.Max[0], f.Min[1], f.Max[1], f.Min[2], f.Max[2],
                    axis, thick, diag == null ? "null" :
                    ("(" + diag[0].ToString("0.###") + "," + diag[1].ToString("0.###") + "," + diag[2].ToString("0.###") + ")")));
            }

            // 4 个方向必须互不相同
            for (int i = 0; i < labels.Count; i++)
            {
                for (int j = i + 1; j < labels.Count; j++)
                {
                    if (labels[i] == labels[j])
                    {
                        error = "有两个面朝向相同（都是 " + labels[i] + "）。请选 4 个不同方向的侧面。";
                        return null;
                    }
                }
            }

            result.Sort(delegate(FaceInfo a, FaceInfo b) { return string.CompareOrdinal(a.Label, b.Label); });
            return result;
        }

        /// <summary>诊断用：多组 U/V 尝试取法向，仅写日志，不参与计算</summary>
        private double[] TryGetFaceNormal(Svc.ICimEntity face)
        {
            double[][] uvPairs = new double[][]
            {
                new double[] { 1, 1 }, new double[] { 0.5, 0.5 }, new double[] { 0, 0 }
            };
            foreach (double[] uv in uvPairs)
            {
                try
                {
                    double[] pt = _gd.GetFaceEvaluate(face, uv[0], uv[1]);
                    if (pt == null || pt.Length < 3) continue;
                    double[] n = _gd.GetFaceNormal(face, pt);
                    if (n != null && n.Length >= 3) return n;
                }
                catch { }
            }

            try
            {
                double[] limit = _gd.GetFaceLimit(face);
                if (limit != null && limit.Length >= 4)
                {
                    // 兼容 [uMin,vMin,uMax,vMax] 这种顺序
                    double u = (limit[0] + limit[2]) / 2.0;
                    double v = (limit[1] + limit[3]) / 2.0;
                    double[] pt = _gd.GetFaceEvaluate(face, u, v);
                    if (pt != null && pt.Length >= 3)
                    {
                        double[] n = _gd.GetFaceNormal(face, pt);
                        if (n != null && n.Length >= 3) return n;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 采样该面的边界边，记录「沿切线位置(相对面中心) → Z」。
        /// 目的：斜顶面/多级顶面时，每条线的 Z 要按它自己的跨度取局部最高点，
        /// 而不是整个面的包围盒最高点（否则线会飘在低的那一侧上方）。
        /// </summary>
        private void SampleBoundaryProfile(Svc.ICimEntity face, FaceInfo f, double uCenter)
        {
            f.EdgeSamples.Clear();
            int edgeCount = 0, sampleCount = 0;
            try
            {
                Svc.ICimEntityList edges =
                    _gd.GetEntitiesRelatedByFilter(face, BaseApi.EntityEnumType.cmEdge);
                if (edges == null) { DatumLog.Write("Face " + f.Label + ": 无边界边可采样"); return; }

                foreach (object o in edges)
                {
                    Svc.ICimEntity ed = o as Svc.ICimEntity;
                    if (ed == null) continue;
                    edgeCount++;

                    double[] lim = null;
                    try { lim = _gd.GetEdgeLimit(ed); }
                    catch (Exception ex) { DatumLog.Write("GetEdgeLimit failed: " + ex.Message); continue; }
                    if (lim == null || lim.Length < 2) continue;

                    double t0 = lim[0], t1 = lim[1];
                    if (Math.Abs(t1 - t0) < 1e-12) continue;

                    const int N = 16;
                    for (int k = 0; k <= N; k++)
                    {
                        double u = t0 + (t1 - t0) * k / N;
                        double[] p = null;
                        try { p = _gd.GetEdgeEvaluate(ed, u); }
                        catch { continue; }
                        if (p == null || p.Length < 3) continue;

                        double tAlong = p[0] * f.Tangent[0] + p[1] * f.Tangent[1] + p[2] * f.Tangent[2];
                        f.EdgeSamples.Add(new double[] { tAlong - uCenter, p[2] });
                        sampleCount++;
                    }
                }
            }
            catch (Exception ex) { DatumLog.Write("SampleBoundaryProfile failed: " + ex.Message); }

            DatumLog.Write("Face " + f.Label + ": edges=" + edgeCount + ", samples=" + sampleCount);
        }

        /// <summary>取 [t1,t2] 跨度内的边界最高 Z；无采样时回退到整个面的 maxZ</summary>
        private double LocalTopZ(FaceInfo f, double t1, double t2, out double zLow)
        {
            zLow = double.MaxValue;
            if (f.EdgeSamples.Count == 0) { zLow = f.MaxZ; return f.MaxZ; }

            double lo = Math.Min(t1, t2), hi = Math.Max(t1, t2);
            double tol = 1e-6;
            double zmax = double.MinValue;
            bool found = false;

            foreach (double[] s in f.EdgeSamples)
            {
                double t = s[0];
                if (t < lo - tol || t > hi + tol) continue;
                if (s[1] > zmax) { zmax = s[1]; found = true; }
                if (s[1] < zLow) { zLow = s[1]; }
            }

            if (!found) { zLow = f.MaxZ; return f.MaxZ; }
            return zmax;
        }
        /// <summary>
        /// 核心：每个侧面生成 2 条线，共 8 条。
        /// 线在侧面「外侧」偏移 Offset；Z 取该线自身跨度内的局部顶面最高点；
        /// 每条线从角点起 CornerGap 处开始，长 Length。
        /// </summary>
        public List<DatumLine> BuildDatumLines(List<FaceInfo> faces, DatumParams p, out string error)
        {
            error = "";
            List<DatumLine> lines = new List<DatumLine>();

            if (!p.IsValid) { error = p.ValidateReason; return null; }
            if (faces.Count != 4) { error = "需要正好 4 个侧面。"; return null; }

            int idx = 0;
            foreach (FaceInfo f in faces)
            {
                idx++;
                double need = 2.0 * (p.CornerGap + p.Length);
                double have = f.TMax - f.TMin;
                if (have < need)
                {
                    error = "面 " + f.Label + " 沿边长度不足：" + have.ToString("0.##", CultureInfo.InvariantCulture) +
                            " < 需要的 " + need.ToString("0.##", CultureInfo.InvariantCulture) +
                            "（两端各留 " + p.CornerGap + " + 线长 " + p.Length + "）";
                    return null;
                }

                // 基准点：面中心沿外法向偏移 Offset
                double[] basePt = new double[]
                {
                    f.Center[0] + f.Normal[0] * p.Offset,
                    f.Center[1] + f.Normal[1] * p.Offset,
                    f.Center[2]
                };

                // 线 A：从 TMin 端起；线 B：从 TMax 端起（TMin/TMax 均相对面中心）
                lines.Add(MakeLine(f, idx, basePt, f.TMin + p.CornerGap, f.TMin + p.CornerGap + p.Length));
                lines.Add(MakeLine(f, idx, basePt, f.TMax - p.CornerGap - p.Length, f.TMax - p.CornerGap));
            }

            return lines;
        }

        private DatumLine MakeLine(FaceInfo f, int idx, double[] basePt, double tFrom, double tTo)
        {
            DatumLine ln = new DatumLine();
            ln.FaceIndex = idx;
            ln.FaceLabel = f.Label;

            // 按本条线自己的跨度取局部顶面最高点（斜顶面/多级顶面时不会飘高）
            double zLow;
            double zTop = LocalTopZ(f, tFrom, tTo, out zLow);
            ln.Z = zTop;
            ln.ZSpan = zTop - zLow;

            ln.P1 = new double[]
            {
                basePt[0] + f.Tangent[0] * tFrom,
                basePt[1] + f.Tangent[1] * tFrom,
                zTop
            };
            ln.P2 = new double[]
            {
                basePt[0] + f.Tangent[0] * tTo,
                basePt[1] + f.Tangent[1] * tTo,
                zTop
            };
            return ln;
        }
    }

    // ======================= 插件入口 =======================
    public class MoldDatumPlugin : ICimApiCommandPlugin
    {
        public ApiCommand AppendCommand()
        {
            DatumLog.Write("MoldDatumPlugin loaded (pid=" + Process.GetCurrentProcess().Id + ")");
            try { DatumEngine.EnsureIcon(); } catch { }

            return new ApiCommand
            {
                Name = "MoldDatumPlugin",
                ToolbarName = "IKUN思美创工具箱",
                MenuPath = "IKUN思美创工具箱\n模具",
                Caption = "铁模四边基准",
                ToolTip = "选铁模4个侧面，自动生成8条基准线",
                Description = "IKUN思美创工具箱 · 铁模四边基准：选4个侧面，按规则生成8条基准线（外侧偏移、与顶面齐平）",
                Application = ApiApplications.Nc | ApiApplications.NcPart | ApiApplications.Part,
                ExecuteCommand = new MoldDatumCommand(),
                IconSize = ApiIconSize.Large,
                License = "1"
            };
        }
    }

    // ======================= 命令 =======================
    public class MoldDatumCommand : ICimWpfCommand
    {
        private static readonly List<DatumDialog> OpenDialogs = new List<DatumDialog>();

        public string GetAccelerator() { return ""; }
        public void SetAccelerator(string accelerator) { }
        public bool OnCommandDblClk() { return false; }

        public CimWpfUICommandStates OnCommandUI()
        {
            return new CimWpfUICommandStates { UiState = CommandUIState.Enabled };
        }

        public bool OnCommand()
        {
            try
            {
                CimApp.IApplication app = GetApplication();
                if (app == null) { Msg(null, "无法连接 Cimatron 应用。"); return true; }

                CimApp.ICimDocument doc = app.GetActiveDoc();
                if (doc == null) { Msg(null, "没有打开的文档。"); return true; }

                DatumSession s = new DatumSession();
                s.App = app;
                s.Doc = doc;
                s.LoadSelection();

                DatumLog.Write("Command start. docType=" + SafeDocType(doc) +
                    ", error=" + (s.LoadError == null ? "" : s.LoadError) +
                    ", lines=" + s.Lines.Count);

                IWin32Window owner = GetOwner(app);
                DatumDialog dlg = new DatumDialog(s, owner);
                dlg.FormClosed += delegate { OpenDialogs.Remove(dlg); };
                OpenDialogs.Add(dlg);
                if (owner != null) dlg.Show(owner);
                else dlg.Show();
                return true;
            }
            catch (Exception ex)
            {
                DatumLog.Write("OnCommand failed: " + ex);
                Msg(null, "执行失败：" + ex.Message + "\n\n详情见日志：" + DatumLog.LogPath);
                return true;
            }
        }

        private static string SafeDocType(CimApp.ICimDocument doc)
        {
            try { return doc.Type.ToString(); } catch { return "?"; }
        }

        private static CimApp.IApplication GetApplication()
        {
            try
            {
                interop.CimServicesAPI.CimApplicationProviderClass provider =
                    new interop.CimServicesAPI.CimApplicationProviderClass();
                CimApp.IApplication app = provider.GetApplication() as CimApp.IApplication;
                if (app != null) return app;
            }
            catch (Exception ex) { DatumLog.Write("CimApplicationProvider failed: " + ex.Message); }

            try
            {
                interop.CimAppAccess.AppAccess access = new interop.CimAppAccess.AppAccess();
                return access.GetApplication() as CimApp.IApplication;
            }
            catch (Exception ex)
            {
                DatumLog.Write("AppAccess failed: " + ex.Message);
                return null;
            }
        }

        private static IWin32Window GetOwner(CimApp.IApplication app)
        {
            try
            {
                long raw = app.GetMainHWND();
                if (raw == 0) return null;
                return new WindowOwner(new IntPtr(raw));
            }
            catch { return null; }
        }

        private static void Msg(IWin32Window owner, string text)
        {
            if (owner != null) MessageBox.Show(owner, text, "IKUN思美创工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else MessageBox.Show(text, "IKUN思美创工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ======================= 图标投放 =======================
    public static class DatumEngine
    {
        public const string CommandClass = "MoldDatumPlugin.MoldDatumPlugin";

        public static int CommandId()
        {
            int sum = 0;
            foreach (char ch in CommandClass.ToLowerInvariant().ToCharArray())
            {
                int v = (int)ch - 65 + 1;
                if (v >= 0) sum += v;
            }
            return 49152 + (sum % 4096);
        }

        public static void EnsureIcon()
        {
            try
            {
                int id = CommandId();
                string data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string dir = System.IO.Path.Combine(data, "Cimatron", "Cimatron");
                if (!Directory.Exists(dir)) return;

                string dllDir = System.IO.Path.GetDirectoryName(typeof(DatumEngine).Assembly.Location);
                string src = System.IO.Path.Combine(dllDir, "MoldDatum_icon64.ico");
                if (!File.Exists(src)) return;

                foreach (string ver in Directory.GetDirectories(dir))
                {
                    string cmdDir = System.IO.Path.Combine(ver, "Data", "Images", "Commands");
                    if (!Directory.Exists(cmdDir)) continue;
                    string dst = System.IO.Path.Combine(cmdDir, id + "_IKUNtools.ico");
                    if (File.Exists(dst)) continue;
                    File.Copy(src, dst, true);
                    DatumLog.Write("Icon installed " + dst);
                }
            }
            catch (Exception ex) { DatumLog.Write("EnsureIcon failed: " + ex.Message); }
        }
    }

    // ======================= 窗口所有者 =======================
    public class WindowOwner : IWin32Window
    {
        private readonly IntPtr _h;
        public WindowOwner(IntPtr h) { _h = h; }
        public IntPtr Handle { get { return _h; } }
    }
}
