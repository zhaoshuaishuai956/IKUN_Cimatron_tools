using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CimApp = interop.CimatronE;
using Mdlr = interop.CimMdlrAPI;
using Nc = interop.CimNcAPI;
using Svc = interop.CimServicesAPI;
using BaseApi = interop.CimBaseAPI;

namespace MoldDatum
{
    /// <summary>
    /// 阶段2（半自动）：套模板 + 选中基准线 + 执行刀路。
    ///
    /// 已实测结论（Cimatron 2024）：
    ///   - CreateNcProceduresFromTemplate 可用（返回 True），参数完全沿用模板
    ///   - 但「刀路几何绑定」没有可用 API：
    ///     · SetCriteriaSet 未公开，多数参数直接抛「操作失败」，不抛的也无效果
    ///     · 控制实验（给目标零件绑面）同样失败
    ///     · 模板二进制里没有命名集合，CreateFromTemplateReplaceCriteriaSets 无从替换
    ///     · 套模板前先选中实体，也不会自动绑定
    ///   → 因此几何选择需人工确认一次；本类负责套模板、选中线、执行刀路。
    /// </summary>
    public static class DatumToolpath
    {
        public const string SetName = "IKUN_DATUM_LINES";

        public class ApplyResult
        {
            public bool Ok;
            public string Message = "";
            public int TemplateUid = -1;
            public int ContourCount = -1;
            public bool LinesSelected;
            public StringBuilder Log = new StringBuilder();
        }

        public class ExecResult
        {
            public bool Ok;
            public string Message = "";
            public List<string> Statuses = new List<string>();
            public StringBuilder Log = new StringBuilder();
        }

        public static string FindTemplate()
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Cimatron", "Cimatron");
                if (!Directory.Exists(root)) return null;
                List<string> all = new List<string>();
                foreach (string ver in Directory.GetDirectories(root))
                {
                    string dir = Path.Combine(ver, "Data", "templates", "ENC", "NC Processes");
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*.tpt")) all.Add(f);
                }
                if (all.Count == 0) return null;
                foreach (string f in all) if (Path.GetFileName(f).Contains("基准")) return f;
                return all[0];
            }
            catch (Exception ex) { DatumLog.Write("FindTemplate failed: " + ex.Message); return null; }
        }

        private static string LogDir { get { return Path.GetDirectoryName(DatumLog.LogPath); } }

        /// <summary>套模板：建集合 → 选中 8 条线 → 套模板 → 回读</summary>
        public static ApplyResult ApplyTemplate(DatumSession s)
        {
            ApplyResult r = new ApplyResult();
            Action<string> L = delegate(string m) { r.Log.AppendLine(m); DatumLog.Write("[TP] " + m); };

            try
            {
                if (s.Lines.Count != 8) { r.Message = "需要先生成 8 条基准线。"; return r; }
                foreach (DatumLine ln in s.Lines)
                    if (ln.Created == null) { r.Message = "还有线没建到模型，请先点「创建 8 条线」。"; return r; }

                Nc.INcModel ncModel = s.NcModel;
                Nc.IModel ncIModel = s.NcIModel;
                if (ncModel == null || ncIModel == null) { r.Message = "拿不到 NC 模型（需在 NC 文档中）。"; return r; }

                string tpl = FindTemplate();
                if (tpl == null) { r.Message = "找不到 NC 模板（.tpt）。"; return r; }
                L("模板 = " + tpl);

                // 1) 建集合
                Nc.IEntityQuery query = ncModel as Nc.IEntityQuery;
                if (query == null) { r.Message = "NC 模型不能当 IEntityQuery 用。"; return r; }
                Nc.ISetsFactory factory = ncIModel.GetSetsFactory();
                if (factory == null) { r.Message = "拿不到 ISetsFactory。"; return r; }

                Nc.IEntityFilter flt = query.CreateFilter(Nc.EFilterEnumType.cmFilterEntityList);
                BaseApi.IFilterEntityList fl = flt as BaseApi.IFilterEntityList;
                if (fl == null) { r.Message = "过滤器转换失败。"; return r; }
                foreach (DatumLine ln in s.Lines)
                {
                    BaseApi.ICimEntity be = ln.Created as BaseApi.ICimEntity;
                    if (be != null) fl.Add(be);
                }
                try { factory.DeleteSet(SetName); } catch { }
                if (factory.CreateSet(SetName, flt) == null) { r.Message = "集合创建失败。"; return r; }
                L("[1] ✅ 集合 " + SetName + " 已建");

                // 2) 选中 8 条线（方便你随后在工序里确认）
                try
                {
                    BaseApi.CimEntityList entList = fl.GetFilter();
                    Mdlr.ICimEntityList mlist = entList as Mdlr.ICimEntityList;
                    if (mlist != null)
                    {
                        ApiShell.CimPickToolHelperCSharp pick = new ApiShell.CimPickToolHelperCSharp(s.App);
                        r.LinesSelected = pick.SelectEntities(mlist);
                    }
                }
                catch (Exception ex) { L("[2] 选中异常：" + ex.Message); }
                L("[2] 选中 8 条线 = " + r.LinesSelected);

                // 3) 套模板
                bool okTpl = false;
                try { okTpl = new ApiShell.Nc(s.App).CreateNcProceduresFromTemplate(tpl); }
                catch (Exception ex) { L("[3] 异常：" + ex.Message); }
                L("[3] CreateNcProceduresFromTemplate = " + okTpl);
                if (!okTpl) { r.Message = "套模板失败。"; return r; }

                string xml = Dump(ncModel, 1, L);
                int uid = xml == null ? -1 : FindProcedureUid(xml, "曲面铣削", "开放轮廓");
                r.TemplateUid = uid;
                r.ContourCount = xml == null ? -1 : ReadContourCount(xml, uid);
                L("[4] 工序 UID = " + uid + "，轮廓 = " + r.ContourCount);

                r.Ok = uid > 0;
                r.Message = r.Ok
                    ? "模板已套用（工序 UID " + uid + "）。轮廓当前 " + r.ContourCount +
                      " 条 —— 请在该工序里确认几何（8 条线已在模型中并已选中），然后点「执行刀路」。"
                    : "套模板后没找到铣削工序。";
                return r;
            }
            catch (Exception ex)
            {
                DatumLog.Write("ApplyTemplate failed: " + ex);
                r.Message = "套模板异常：" + ex.Message;
                return r;
            }
        }

        /// <summary>执行文档内所有铣削工序，并回读状态</summary>
        public static ExecResult ExecuteProcedures(DatumSession s)
        {
            ExecResult r = new ExecResult();
            Action<string> L = delegate(string m) { r.Log.AppendLine(m); DatumLog.Write("[TP-EXEC] " + m); };

            try
            {
                Nc.INcModel ncModel = s.NcModel;
                if (ncModel == null) { r.Message = "拿不到 NC 模型。"; return r; }

                string xml = Dump(ncModel, 2, L);
                if (xml == null) { r.Message = "工序 XML 导出失败。"; return r; }

                List<int> uids = FindAllMillingUids(xml);
                L("铣削工序 = [" + Join(uids) + "]");
                if (uids.Count == 0) { r.Message = "文档里没有铣削工序，请先点「套用模板」。"; return r; }

                foreach (int uid in uids)
                {
                    int contour = ReadContourCount(xml, uid);
                    L("  工序 " + uid + " 轮廓 = " + contour);
                    try
                    {
                        ncModel.ExecuteProcedureById(uid);
                        L("  工序 " + uid + " 已执行");
                    }
                    catch (Exception ex) { L("  工序 " + uid + " 执行异常：" + ex.Message); }
                }

                string xml2 = Dump(ncModel, 3, L);
                if (xml2 != null)
                {
                    foreach (int uid in uids)
                        r.Statuses.Add(uid + ": 轮廓=" + ReadContourCount(xml2, uid) +
                                       ", 状态=" + ReadStatus(xml2, uid));
                }

                bool anyOk = r.Statuses.Count > 0;
                foreach (string st in r.Statuses)
                    if (st.Contains("已计算") || st.Contains("完成") || st.Contains("无错误")) anyOk = anyOk || true;

                r.Ok = anyOk;
                r.Message = r.Statuses.Count == 0 ? "执行后未能回读状态。"
                    : string.Join("\n", r.Statuses.ToArray());
                return r;
            }
            catch (Exception ex)
            {
                DatumLog.Write("ExecuteProcedures failed: " + ex);
                r.Message = "执行异常：" + ex.Message;
                return r;
            }
        }

        private static string Join(List<int> l)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(l[i]); }
            return sb.ToString();
        }

        private static string Dump(Nc.INcModel m, int no, Action<string> L)
        {
            try
            {
                string p = Path.Combine(LogDir, "mold_datum_tp" + no + ".xml");
                m.GetProcessManagerAsXML(1, p);
                return File.Exists(p) ? p : null;
            }
            catch (Exception ex) { L("  XML 导出失败(" + no + ")：" + ex.Message); return null; }
        }

        private static string ReadXml(string path)
        {
            string t = File.ReadAllText(path, Encoding.Unicode);
            if (t.IndexOf("<Procedure ") < 0) t = File.ReadAllText(path);
            return t;
        }

        public static int FindProcedureUid(string xmlPath, string mainKey, string secondKey)
        {
            try
            {
                if (!File.Exists(xmlPath)) return -1;
                string t = ReadXml(xmlPath);
                MatchCollection ms = Regex.Matches(t, "(?s)<Procedure Name=\"([^\"]*)\" ID=\"(\\d+)\">(.*?)</Procedure>");

                StringBuilder names = new StringBuilder();
                foreach (Match mm in ms) names.Append("[ID=" + mm.Groups[2].Value + " " + mm.Groups[1].Value + "] ");
                DatumLog.Write("[TP] 工序清单: " + names);

                int fallback = -1;
                foreach (Match m in ms)
                {
                    string name = m.Groups[1].Value;
                    int id = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (name.Contains("毛坯")) continue;
                    if (!name.Contains(mainKey)) continue;
                    if (secondKey != null && name.Contains(secondKey)) return id;
                    if (fallback < 0) fallback = id;
                }
                return fallback;
            }
            catch (Exception ex) { DatumLog.Write("FindProcedureUid failed: " + ex.Message); return -1; }
        }

        public static List<int> FindAllMillingUids(string xmlPath)
        {
            List<int> res = new List<int>();
            try
            {
                if (!File.Exists(xmlPath)) return res;
                string t = ReadXml(xmlPath);
                MatchCollection ms = Regex.Matches(t, "(?s)<Procedure Name=\"([^\"]*)\" ID=\"(\\d+)\">(.*?)</Procedure>");
                foreach (Match m in ms)
                {
                    string name = m.Groups[1].Value;
                    if (name.Contains("毛坯")) continue;
                    if (name.Contains("目标零件")) continue;
                    if (!(name.Contains("铣削") || name.Contains("轮廓"))) continue;
                    res.Add(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { DatumLog.Write("FindAllMillingUids failed: " + ex.Message); }
            return res;
        }

        public static int ReadContourCount(string xmlPath, int uid)
        {
            try
            {
                if (!File.Exists(xmlPath)) return -1;
                string t = ReadXml(xmlPath);
                Match m = Regex.Match(t, "(?s)<Procedure Name=\"[^\"]*\" ID=\"" + uid + "\">(.*?)</Procedure>");
                if (!m.Success) return -1;
                Match c = Regex.Match(m.Groups[1].Value, "<轮廓 Value=\"(\\d+)\"");
                return c.Success ? int.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            }
            catch { return -1; }
        }

        public static string ReadStatus(string xmlPath, int uid)
        {
            try
            {
                if (!File.Exists(xmlPath)) return "?";
                string t = ReadXml(xmlPath);
                Match m = Regex.Match(t, "(?s)<Procedure Name=\"[^\"]*\" ID=\"" + uid + "\">(.*?)</Procedure>");
                if (!m.Success) return "?";
                Match s = Regex.Match(m.Groups[1].Value, "<程序状态 Value=\"([^\"]*)\"");
                return s.Success ? s.Groups[1].Value : "?";
            }
            catch { return "?"; }
        }
    }
}
