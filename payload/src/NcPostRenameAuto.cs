using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

class NcPostRename
{
    static readonly string[] AuxExt = { ".origins", ".tools", ".cycles", ".msg", ".log", ".txt", ".cmd", ".bat", ".ini", ".xml", ".xls", ".xlsx", ".pdf" };
    const int FreshXmlTimeoutSeconds = 180;

    // 该程序没有最小伸出要求（Cimatron 用 -1/-2/0 表示）时，文件名刀长段写作 NA
    const string NoLimitLengthText = "NA";

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [STAThread]
    static int Main(string[] args)
    {
        string cwd = Directory.GetCurrentDirectory();
        string outputDir = cwd;
        string leaf = Path.GetFileName(cwd.TrimEnd('\\'));
        if (string.Equals(leaf, "TEMP_POST", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(cwd.TrimEnd('\\'));
            if (parent != null) outputDir = parent.FullName;
        }

        var runLog = new StringBuilder();
        Log(runLog, "CWD=" + cwd);
        Log(runLog, "OutputDir=" + outputDir);

        using (var mutex = new Mutex(false, "NcPostRenameMutex"))
        {
            bool locked = false;
            try
            {
                try { locked = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                catch (AbandonedMutexException) { locked = true; }

                var cands = SelectCandidates(Directory.GetFiles(cwd), 1440);

                // 兜底：Cimatron 某些流程会把带 .BJ 扩展名的原生输出直接写到最终目录，
                // TEMP_POST 里什么都没有。此时改为扫描最终目录里 10 分钟内的新文件。
                if (cands.Count == 0 &&
                    !string.Equals(Norm(cwd), Norm(outputDir), StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = SelectCandidates(Directory.GetFiles(outputDir), 10);
                    if (fallback.Count > 0)
                    {
                        Log(runLog, "No candidate in TEMP_POST; using fresh output from " + outputDir);
                        cands = fallback;
                    }
                }

                if (cands.Count == 0)
                {
                    Log(runLog, "No generated NC candidate found");
                    WriteRunLog(runLog);
                    return 1;
                }

                var metas = new List<PostMeta>();
                foreach (var src in cands)
                {
                    PostMeta meta;
                    string error;
                    if (!TryLoadFreshMeta(src.FullName, runLog, out meta, out error))
                    {
                        Log(runLog, "Fresh metadata failed for " + src.Name + ": " + error);
                        WriteRunLog(runLog);
                        WriteFailure(cwd, src.Name, error);
                        return 3;
                    }
                    metas.Add(meta);
                    Log(runLog, "Meta: File=" + src.Name + "; ProcId=" + meta.ProcId + "; Tool=" + meta.Tool + "; Length=" + meta.Length + "; Xml=" + meta.XmlPath);
                }

                int next = MaxSequence(outputDir) + 1;
                for (int i = 0; i < cands.Count; i++)
                {
                    var src = cands[i];
                    var meta = metas[i];
                    string sourcePath = src.FullName;
                    if (!File.Exists(sourcePath))
                    {
                        string movedSource = Path.Combine(outputDir, src.Name);
                        if (File.Exists(movedSource)) sourcePath = movedSource;
                    }
                    if (!File.Exists(sourcePath))
                    {
                        throw new FileNotFoundException("Generated NC file not found after post completed", src.FullName);
                    }

                    string targetName;
                    string targetPath;
                    do
                    {
                        targetName = next.ToString("D2") + "-" + meta.Tool + "-" + meta.Length + " nc.bj";
                        targetPath = Path.Combine(outputDir, targetName);
                        next++;
                    } while (File.Exists(targetPath));

                    File.Move(sourcePath, targetPath);
                    Log(runLog, "Renamed: " + src.Name + " -> " + targetName);
                }

                foreach (var meta in metas)
                {
                    try { if (File.Exists(meta.XmlPath)) File.Delete(meta.XmlPath); } catch { }
                }
                Cleanup(cwd);
                if (!string.Equals(Norm(cwd), Norm(outputDir), StringComparison.OrdinalIgnoreCase)) Cleanup(outputDir);
                WriteRunLog(runLog);
                OpenFolder(outputDir);
                return 0;
            }
            catch (Exception ex)
            {
                Log(runLog, "FATAL: " + ex);
                WriteRunLog(runLog);
                return 2;
            }
            finally
            {
                if (locked)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }
    }

    class PostMeta
    {
        public string ProcId;
        public string Tool;
        public string Length;
        public string XmlPath;
    }

    static bool TryLoadFreshMeta(string ncPath, StringBuilder log, out PostMeta meta, out string error)
    {
        meta = null;
        error = null;

        // 路线 A: GPP 直出 (NCMETA2) —— E11 等无插件框架版本的快速路径
        string directTool, directLen;
        if (TryExtractDirectMeta(ncPath, out directTool, out directLen))
        {
            Log(log, "NCMETA2 direct meta: Tool=" + directTool + "; Length=" + directLen);
            double direct;
            if (!double.TryParse(directLen, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out direct))
            {
                error = "NCMETA2 length not numeric: " + directLen;
                return false;
            }
            if (string.IsNullOrWhiteSpace(directTool)) directTool = "NO_TOOL";
            if (direct <= 0)
            {
                // 没有最小伸出要求：刀长段记 NA 而不是 0
                Log(log, "NCMETA2 length = " + directLen + " (no clear-length limit); record as " + NoLimitLengthText);
                meta = new PostMeta { ProcId = "", Tool = Sanitize(directTool), Length = NoLimitLengthText };
                return true;
            }
            meta = new PostMeta { ProcId = "", Tool = Sanitize(directTool), Length = ((long)Math.Ceiling(direct)).ToString() };
            return true;
        }

        // 路线 B: 后台插件实时导出 XML
        string procId;
        if (!TryExtractProcId(ncPath, out procId))
        {
            error = "NCMETA PROC line not found in BJ output";
            return false;
        }

        string xmlPath;
        if (!TryRequestFreshXml(procId, log, out xmlPath, out error)) return false;
        if (!TryParseFreshXml(xmlPath, procId, log, out meta, out error)) return false;
        meta.XmlPath = xmlPath;
        return true;
    }

    static bool TryExtractDirectMeta(string path, out string tool, out string lenText)
    {
        tool = null;
        lenText = null;
        string text = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                text = File.ReadAllText(path, Encoding.Default);
                break;
            }
            catch (IOException) { Thread.Sleep(50); }
        }
        if (text == null) return false;
        var match = Regex.Match(text, @"NCMETA2\s+TOOL=(.*?)\s+LEN=(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        tool = match.Groups[1].Value.Trim();
        lenText = match.Groups[2].Value;
        return true;
    }

    static bool TryExtractProcId(string path, out string procId)
    {
        procId = null;
        string text = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                text = File.ReadAllText(path, Encoding.Default);
                break;
            }
            catch (IOException) { Thread.Sleep(50); }
        }
        if (text == null) return false;
        var match = Regex.Match(text, @"NCMETA\s+PROC\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        procId = match.Groups[1].Value;
        return true;
    }

    static bool TryRequestFreshXml(string procId, StringBuilder log, out string xmlPath, out string error)
    {
        xmlPath = null;
        error = null;
        string lastError = null;
        DateTime? errAt = null;
        string dir = NcNamerDir();
        string token = Guid.NewGuid().ToString("N");
        string requestPath = Path.Combine(dir, "export_request_" + token + "_" + procId + ".txt");
        string responsePath = Path.Combine(dir, "export_response_" + token + ".txt");
        DateTime requestStartedUtc = DateTime.UtcNow;
        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(responsePath)) File.Delete(responsePath);
            File.WriteAllText(requestPath, "", new UTF8Encoding(true));
            Log(log, "FreshXmlRequest=" + requestPath);

            DateTime deadline = DateTime.Now.AddSeconds(FreshXmlTimeoutSeconds);
            while (DateTime.Now < deadline)
            {
                if (File.Exists(responsePath))
                {
                    string response;
                    try { response = File.ReadAllText(responsePath, Encoding.UTF8); }
                    catch (IOException) { Thread.Sleep(25); continue; }

                    if (response.StartsWith("ERR|", StringComparison.OrdinalIgnoreCase))
                    {
                        // 可能同时开着多个 Cimatron 实例：非活动实例会报 ERR，活动实例稍后写 OKXML。
                        // 这里不立即放弃，记下原因继续等成功响应，直到超时。
                        lastError = response.Substring(4);
                        if (!errAt.HasValue) errAt = DateTime.Now;
                        Log(log, "Plugin reported error (waiting for another instance): " + lastError);
                        // 只给 20 秒窗口等其他实例/重试的成功响应，超过则放弃，避免长时间空等
                        if ((DateTime.Now - errAt.Value).TotalSeconds > 20)
                        {
                            error = lastError;
                            return false;
                        }
                        Thread.Sleep(50);
                        continue;
                    }
                    if (!response.StartsWith("OKXML|", StringComparison.OrdinalIgnoreCase))
                    {
                        Thread.Sleep(25);
                        continue;
                    }

                    string[] parts = response.Split('|');
                    if (parts.Length < 4 || parts.Skip(1).Any(string.IsNullOrWhiteSpace))
                    {
                        Thread.Sleep(25);
                        continue;
                    }

                    string candidatePath = Decode64(parts[1]);
                    string responseProcId = Decode64(parts[2]);
                    string expectedHash = Decode64(parts[3]);
                    if (!string.Equals(responseProcId, procId, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Response procedure id mismatch: expected " + procId + ", got " + responseProcId;
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                    {
                        error = "Response XML file missing";
                        return false;
                    }
                    if (File.GetLastWriteTimeUtc(candidatePath) < requestStartedUtc.AddSeconds(-2))
                    {
                        error = "Response XML timestamp is older than this request";
                        return false;
                    }
                    string actualHash = Sha256File(candidatePath);
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        Thread.Sleep(25);
                        continue;
                    }

                    xmlPath = candidatePath;
                    Log(log, "FreshXmlReady=" + xmlPath + "; WriteTimeUtc=" + File.GetLastWriteTimeUtc(candidatePath).ToString("o") + "; Sha256=" + actualHash);
                    return true;
                }
                Thread.Sleep(25);
            }
            error = string.IsNullOrEmpty(lastError)
                ? "Fresh XML timeout after " + FreshXmlTimeoutSeconds + " seconds"
                : "Fresh XML failed: " + lastError;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(responsePath)) File.Delete(responsePath); } catch { }
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
        }
    }

    static bool TryParseFreshXml(string xmlPath, string procId, StringBuilder log, out PostMeta meta, out string error)
    {
        meta = null;
        error = null;
        try
        {
            var doc = new XmlDocument();
            doc.Load(xmlPath);
            XmlNode chosen = null;
            var procedures = doc.SelectNodes("//Procedure");
            if (procedures == null)
            {
                error = "No Procedure node in fresh XML";
                return false;
            }
            foreach (XmlNode proc in procedures)
            {
                var id = proc.Attributes["ID"];
                if (id != null && id.Value == procId)
                {
                    chosen = proc;
                    break;
                }
            }
            if (chosen == null)
            {
                error = "Procedure ID " + procId + " not found in fresh XML";
                return false;
            }

            // 各版本 XML 字段名不同：2024 用「程序状态」，2025 用「状态」，英文版另有英文标签
            string[] statusNames = { "程序状态", "状态", "Status", "ProcedureStatus" };
            string[] toolNames = { "刀具_夹持", "刀具夹持", "刀具与夹持", "刀具", "CutterAndHolder", "Cutter_Holder", "CutterName", "Cutter Name" };
            string[] lengthNames = { "最小有效长度", "最小刀长", "最小有效刀长", "MinClearLen", "MinimumClearLength", "Minimum Clear Length" };

            string status = GetField(chosen, new[] { "30009" }, statusNames);
            if (status == null)
            {
                // 该版本未暴露状态字段：只记录，不阻断（刀长仍必须有效，fail-closed 保持）
                Log(log, "Status field not found in this Cimatron version; skip status check");
            }
            else if (!(string.Equals(status, "计算完成", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "Calculated", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)))
            {
                error = "Procedure is not calculated: " + status;
                return false;
            }

            string tool = Sanitize(GetField(chosen, new[] { "3001" }, toolNames));
            if (string.IsNullOrWhiteSpace(tool)) tool = "NO_TOOL";

            string lengthText = GetField(chosen, new[] { "30010" }, lengthNames);
            double length;
            if (lengthText == null)
            {
                error = "Minimum effective length field not found in procedure XML";
                return false;
            }
            if (!double.TryParse(lengthText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out length))
            {
                error = "Minimum effective length not numeric: " + lengthText;
                return false;
            }
            if (length <= 0)
            {
                string strict = Environment.GetEnvironmentVariable("NCNAMER_LEN_ZERO_AS_FAIL");
                if (!string.IsNullOrWhiteSpace(strict) && strict != "0")
                {
                    error = "Minimum clear length has no limit (" + lengthText + "); strict mode keeps the original name";
                    return false;
                }
                // Cimatron 语义: -1 = 刀长足够/忽略夹持, -2 = 不适用, 0 = 无最小伸出要求
                // 这些情况没有具体刀长限制，刀长段记 NA 而不是放弃命名
                Log(log, "Minimum clear length = " + lengthText + " (no clear-length limit); record as " + NoLimitLengthText);
                meta = new PostMeta
                {
                    ProcId = procId,
                    Tool = tool,
                    Length = NoLimitLengthText
                };
                return true;
            }

            meta = new PostMeta
            {
                ProcId = procId,
                Tool = tool,
                Length = ((long)Math.Ceiling(length)).ToString()
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string GetXmlValue(XmlNode node, string name)
    {
        var child = node.SelectSingleNode(".//" + name);
        var attr = child == null ? null : child.Attributes["Value"];
        return attr == null ? null : attr.Value.Trim();
    }

    // 优先按跨版本稳定的字段 ID 查找，失败再按名称候选（兼容 2024/2025/2026 及英文版）
    static string GetField(XmlNode root, string[] ids, string[] names)
    {
        foreach (var id in ids)
        {
            var node = FindNodeById(root, id);
            if (node == null) continue;
            var attr = node.Attributes == null ? null : node.Attributes["Value"];
            if (attr != null && !string.IsNullOrWhiteSpace(attr.Value)) return attr.Value.Trim();
        }
        return GetFirstValue(root, names);
    }

    static XmlNode FindNodeById(XmlNode root, string id)
    {
        var queue = new Queue<XmlNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                var attr = child.Attributes == null ? null : child.Attributes["ID"];
                if (attr != null && attr.Value == id) return child;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    // 按候选名顺序查找，命中即返回（兼容 2024/2025/2026 及英文版的不同字段名）
    static string GetFirstValue(XmlNode root, string[] candidates)
    {
        foreach (var name in candidates)
        {
            var node = FindNodeByName(root, name);
            if (node == null) continue;
            var attr = node.Attributes == null ? null : node.Attributes["Value"];
            if (attr != null && !string.IsNullOrWhiteSpace(attr.Value)) return attr.Value.Trim();
            if (!string.IsNullOrWhiteSpace(node.InnerText) && node.ChildNodes.Count == 0) return node.InnerText.Trim();
        }
        return null;
    }

    static XmlNode FindNodeByName(XmlNode root, string wanted)
    {
        string target = NormName(wanted);
        var queue = new Queue<XmlNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (NormName(child.Name) == target) return child;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    static string NormName(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "")
        {
            if (char.IsWhiteSpace(c) || c == '_' || c == '-') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    static string Decode64(string value)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch { return ""; }
    }

    static string Sha256File(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            byte[] hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    static void WriteFailure(string cwd, string sourceName, string error)
    {
        try
        {
            string path = Path.Combine(cwd, "NC_RENAME_FAILED.txt");
            File.WriteAllText(path, "NC auto rename failed.\r\nSource: " + sourceName + "\r\nReason: " + error + "\r\n", new UTF8Encoding(true));
        }
        catch { }
    }

    static void Log(StringBuilder log, string message)
    {
        log.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
    }

    static string NcNamerDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NcNamer");
    }

    static void WriteRunLog(StringBuilder log)
    {
        try
        {
            Directory.CreateDirectory(NcNamerDir());
            File.WriteAllText(Path.Combine(NcNamerDir(), "post_rename_log.txt"), log.ToString(), new UTF8Encoding(true));
        }
        catch { }
    }

    static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s ?? "")
        {
            if (c == '.') sb.Append('d');
            else if (c < 32 || "\\/:*?\"<>|".IndexOf(c) >= 0) sb.Append('_');
            else sb.Append(c);
        }
        string r = sb.ToString().Trim().Trim('.');
        r = Regex.Replace(r, @"\s+", " ");
        if (r.Length > 80) r = r.Substring(0, 80).Trim();
        return r;
    }

    static List<FileInfo> SelectCandidates(string[] files, double freshMinutes)
    {
        return files
            .Select(f => new FileInfo(f))
            .Where(fi => (DateTime.Now - fi.LastWriteTime).TotalMinutes < freshMinutes)
            .Where(fi => !AuxExt.Contains(fi.Extension.ToLowerInvariant()))
            .Where(fi => !fi.Name.StartsWith("."))
            .Where(fi => !fi.Name.StartsWith("NcPostRename", StringComparison.OrdinalIgnoreCase))
            .Where(fi => !fi.Name.EndsWith(".nc", StringComparison.OrdinalIgnoreCase))
            .Where(fi => !fi.Name.EndsWith(" nc", StringComparison.OrdinalIgnoreCase))
            // 只排除已改名的 v2 文件（空格+nc.bj）；原生 .BJ 输出必须保留为候选
            .Where(fi => !fi.Name.EndsWith(" nc.bj", StringComparison.OrdinalIgnoreCase))
            .Where(fi => !fi.Name.EndsWith(".nc.bj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(fi => fi.CreationTimeUtc)
            .ThenBy(fi => fi.LastWriteTimeUtc)
            .ThenBy(fi => fi.Name)
            .ToList();
    }

    static int MaxSequence(string dir)
    {
        int max = 0;
        try
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (!(name.EndsWith(".nc", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(" nc", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(" nc.bj", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(".nc.bj", StringComparison.OrdinalIgnoreCase))) continue;
                var m = Regex.Match(name, @"^(\d+)[-_]", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    int n;
                    if (int.TryParse(m.Groups[1].Value, out n) && n > max) max = n;
                }
            }
        }
        catch (Exception) { }
        return max;
    }

    static void Cleanup(string cwd)
    {
        string[] patterns = { "*.origins", "*.tools", "*.cycles", "*.msg", "print0.txt", "print00.txt", "NC_RENAME_FAILED.txt" };
        foreach (var p in patterns)
        {
            foreach (var f in Directory.GetFiles(cwd, p))
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    static string Norm(string p)
    {
        if (p == null) return null;
        try
        {
            if (p.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                p = new Uri(p).LocalPath;
            }
        }
        catch { }
        p = p.Trim().TrimEnd('\\', '/');
        return p.Replace('/', '\\');
    }

    static void OpenFolder(string folder)
    {
        try
        {
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                object shell = Activator.CreateInstance(shellType);
                object windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
                int count = (int)windows.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, windows, null);
                for (int i = 0; i < count; i++)
                {
                    object w = null;
                    try
                    {
                        w = windows.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, windows, new object[] { i });
                        string path = null;
                        try
                        {
                            object doc = w.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, w, null);
                            object fol = doc.GetType().InvokeMember("Folder", BindingFlags.GetProperty, null, doc, null);
                            object self = fol.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, fol, null);
                            path = (string)self.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, self, null);
                        }
                        catch { }
                        if (path == null)
                        {
                            try { path = (string)w.GetType().InvokeMember("LocationURL", BindingFlags.GetProperty, null, w, null); } catch { }
                        }
                        if (path != null && string.Equals(Norm(path), Norm(folder), StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                object hwndObj = w.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, w, null);
                                IntPtr hwnd = new IntPtr(Convert.ToInt64(hwndObj));
                                ShowWindow(hwnd, 9);
                                SetForegroundWindow(hwnd);
                            }
                            catch { }
                            return;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception) { }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
