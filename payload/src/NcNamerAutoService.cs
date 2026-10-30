using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CimUIInfrastructure.Commands;
using CimUIInfrastructure.PlugIn;

namespace NcNamerAutoService
{
    public class NcDumpPlugin : ICimApiCommandPlugin
    {
        public ApiCommand AppendCommand()
        {
            NcAutoExportService.Start();
            NcAutoExportService.EnsureNativeIcon();
            var cmd = new ApiCommand
            {
                Name = "NcNamerAutoService2024",
                // 必须给固定名字。留空时 Cimatron 每次启动都会新建一个匿名工具栏组
                // （CommandsLayout.xml 里的 Group -1xxxxxxxxx），按钮数量随启动次数增长。
                ToolbarName = "NcNamer",
                MenuPath = "NC\nNcNamerAutoService",
                Caption = "NcNamer Auto Service",
                ToolTip = "Automatic NC naming support service",
                Description = "Background service used automatically by BJ post-processing",
                Application = ApiApplications.Nc | ApiApplications.NcPart,
                ExecuteCommand = new NcAutoServiceCommand(),
                IconSize = ApiIconSize.Large,
                License = "1"
            };
            return cmd;
        }

        // 图标走 Cimatron 的原生机制：按命令 ID 在 DataDir\Images\Commands 下找图片。
        // 反编译 CimStreepBar 得到的规则：
        //   CommandsRepositoryBase.CreateCommand():
        //       string path = GetIconById(id.Id) ?? "99999_Missing.ico";
        //       IconSource = new WpfImageIdentifier(
        //           Path.Combine(DataDir, "Images\\Commands", path), 0);
        //   GetIconById(id):
        //       扫描该目录所有文件，取文件名里第一个 '_' 之前的数字做 key
        //
        // 所以命令 50468 的图标文件就是 50468_任何名字.ico；找不到就退回
        // 99999_Missing.ico —— 那个黄底红叉的占位图。
        //
        // ApiCommand.IconSource 那条 overlay 分支要求 ico 与同名 png 同目录且目录可写，
        // 在 Program Files 下会静默失败，因此不再使用它，直接投放原生图标文件。
    }

    public class NcAutoServiceCommand : ICimWpfCommand
    {
        public string GetAccelerator() { return ""; }
        public void SetAccelerator(string accelerator) { }
        public bool OnCommandDblClk() { return false; }

        public bool OnCommand()
        {
            NcAutoExportService.Start();
            NcAutoExportService.AppendLog("Manual service check; no user action is required (pid=" + NcAutoExportService.MyPid + ")");
            return true;
        }

        public CimWpfUICommandStates OnCommandUI()
        {
            return new CimWpfUICommandStates { UiState = CommandUIState.Enabled };
        }
    }

    internal static class NcAutoExportService
    {
        private const string RequestPrefix = "export_request_";
        private const string BusyPrefix = "Cimatron is busy: ";
        private const int MaxAttempts = 120;
        private const int RetryDelayMs = 500;
        private const int BroadcastErrorDelayMs = 8000;

        private static readonly object Gate = new object();
        private static readonly HashSet<string> RecentTokens = new HashSet<string>();
        private static FileSystemWatcher _watcher;
        private static SynchronizationContext _uiContext;
        private static int _myPid;

        internal static int MyPid { get { return _myPid; } }

        private static bool _nativeIconDone;

        // 把图标投放到 Cimatron 期望的 DataDir\Images\Commands\{命令ID}_*.ico
        internal static void EnsureNativeIcon()
        {
            if (_nativeIconDone) return;
            _nativeIconDone = true;

            try
            {
                string programDir = Path.GetDirectoryName(typeof(NcDumpPlugin).Assembly.Location) ?? "";
                string sourceIco = Path.Combine(programDir, "NcNamer_icon64.ico");
                if (!File.Exists(sourceIco))
                {
                    AppendLog("Icon: source missing " + sourceIco);
                    return;
                }

                int commandId = CalculateApiCommandId(typeof(NcDumpPlugin).FullName);
                string fileName = commandId + "_NcNamer.ico";

                foreach (string dir in CandidateCommandImageDirs(programDir))
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        string target = Path.Combine(dir, fileName);
                        if (File.Exists(target) && SameContent(sourceIco, target)) continue;
                        File.Copy(sourceIco, target, true);
                        AppendLog("Icon: installed " + target);
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Icon: write failed " + dir + " :: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Icon: failed " + ex.Message);
            }
        }

        // DataDir 候选位置：先按自身版本号精确匹配，再兜底扫描所有已装版本
        private static List<string> CandidateCommandImageDirs(string programDir)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // programDir 形如 ...\Cimatron\Cimatron\2024.0\Program
                // 配置目录固定在 %ProgramData%\Cimatron\Cimatron\<版本>\Data
                string versionDir = Path.GetDirectoryName(programDir.TrimEnd('\\'));
                string version = Path.GetFileName(versionDir);
                if (!string.IsNullOrEmpty(version))
                {
                    string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    string dataDir = Path.Combine(common, "Cimatron", "Cimatron", version, "Data", "Images", "Commands");
                    if (seen.Add(dataDir)) result.Add(dataDir);
                }
            }
            catch { }

            try
            {
                string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                foreach (string root in new[]
                {
                    Path.Combine(common, "Cimatron", "Cimatron"),
                    Path.Combine(common, "Cimatron")
                })
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string ver in Directory.GetDirectories(root))
                    {
                        string dataDir = Path.Combine(ver, "Data", "Images", "Commands");
                        if (Directory.Exists(dataDir) && seen.Add(dataDir)) result.Add(dataDir);
                    }
                }
            }
            catch { }

            return result;
        }

        // 复刻 Cimatron 的 CalculateApiCommandId（CimStreepBar.StreepAPICommands）
        internal static int CalculateApiCommandId(string command)
        {
            string text = (command ?? string.Empty).ToLowerInvariant();
            int sum = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int v = text[i] - 65 + 1;
                if (v >= 0) sum += v;
                else if (text[i] >= '0' && text[i] <= '9') sum += text[i] - 48;
            }
            return 49152 + (sum % 4096);
        }

        private static bool SameContent(string a, string b)
        {
            try
            {
                byte[] x = File.ReadAllBytes(a);
                byte[] y = File.ReadAllBytes(b);
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                return true;
            }
            catch { return false; }
        }

        internal static void Start()
        {
            lock (Gate)
            {
                if (_watcher != null) return;
                string dir = NcNamerDir();
                Directory.CreateDirectory(dir);
                _myPid = Process.GetCurrentProcess().Id;
                _uiContext = SynchronizationContext.Current;
                _watcher = new FileSystemWatcher(dir, RequestPrefix + "*.txt");
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                _watcher.Created += OnRequest;
                _watcher.Changed += OnRequest;
                // 有些写入方式（先写临时文件再改名）只会产生 Renamed 事件，
                // 订阅它以免漏掉请求。
                _watcher.Renamed += OnRenamed;
                _watcher.EnableRaisingEvents = true;
                AppendLog("NcNamer auto service started (pid=" + _myPid + ")");
            }
        }

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            // 只处理"改成请求文件名"的情况，忽略改走的事件
            if (e.Name == null || !e.Name.StartsWith(RequestPrefix, StringComparison.OrdinalIgnoreCase)) return;
            if (!e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return;
            OnRequest(sender, new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetDirectoryName(e.FullPath), e.Name));
        }

        private static void OnRequest(object sender, FileSystemEventArgs e)
        {
            string name = Path.GetFileNameWithoutExtension(e.FullPath);
            string stem = name.StartsWith(RequestPrefix, StringComparison.OrdinalIgnoreCase)
                ? name.Substring(RequestPrefix.Length)
                : name;
            int procUid = 0;
            int split = stem.LastIndexOf('_');
            string token = stem;
            if (split > 0)
            {
                token = stem.Substring(0, split);
                int.TryParse(stem.Substring(split + 1), out procUid);
            }

            // 请求文件里记录了发起方所属的 Cimatron 进程 PID。
            // 只有该进程才应答，多开时其他实例保持静默，避免抢答并覆盖正确结果。
            int ownerPid = ReadOwnerPid(e.FullPath);
            if (ownerPid > 0 && ownerPid != _myPid)
            {
                AppendLog("Request " + token + " belongs to pid " + ownerPid + "; this instance (pid " + _myPid + ") stays silent");
                return;
            }

            lock (Gate)
            {
                if (RecentTokens.Contains(token))
                {
                    AppendLog("Duplicate request ignored: " + token);
                    return;
                }
                RecentTokens.Add(token);
                if (RecentTokens.Count > 1000) RecentTokens.Clear();
            }

            Action work = delegate { AttemptExport(e.FullPath, token, procUid, ownerPid, 1); };
            if (_uiContext != null) _uiContext.Post(delegate { work(); }, null);
            else work();
        }

        private static int ReadOwnerPid(string requestPath)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    string text = File.ReadAllText(requestPath, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // 文件刚创建、内容尚未落盘，稍等再看
                        Thread.Sleep(25);
                        continue;
                    }
                    int idx = text.IndexOf("owner=", StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) return 0;
                    int start = idx + "owner=".Length;
                    int end = start;
                    while (end < text.Length && char.IsDigit(text[end])) end++;
                    int pid;
                    if (end > start && int.TryParse(text.Substring(start, end - start), out pid)) return pid;
                    return 0;
                }
                catch (IOException) { Thread.Sleep(25); }
                catch (Exception) { return 0; }
            }
            return 0;
        }

        private static void AttemptExport(string requestPath, string token, int procUid, int ownerPid, int attempt)
        {
            // 文件名带实例 PID：多开时即使两个实例都应答，也不会互相覆盖
            string responsePath = Path.Combine(NcNamerDir(), "export_response_" + token + "_" + _myPid + ".txt");
            try
            {
                if (procUid <= 0) throw new InvalidOperationException("Missing procedure id");

                string xmlPath = Path.Combine(NcNamerDir(), "nc_item_" + token + "_" + _myPid + "_" + procUid + ".xml");
                string error;
                if (TryExportFromAnyDocument(procUid, xmlPath, out error))
                {
                    string hash = Sha256File(xmlPath);
                    string response = string.Join("|", new[]
                    {
                        "OKXML",
                        Encode64(xmlPath),
                        Encode64(procUid.ToString()),
                        Encode64(hash)
                    });
                    if (!HasOkResponse(responsePath))
                    {
                        File.WriteAllText(responsePath, response, new UTF8Encoding(true));
                        AppendLog("Response " + token + " procUid=" + procUid + " xml=" + xmlPath + " sha256=" + hash + " (attempt " + attempt + ", pid " + _myPid + ")");
                    }
                    else
                    {
                        AppendLog("Response already present for " + token + "; keeping it (pid " + _myPid + ")");
                    }
                    CleanupRequest(requestPath);
                    return;
                }

                if (IsBusy(error) && attempt < MaxAttempts)
                {
                    AppendLog("Export attempt " + attempt + " waiting for procUid=" + procUid + ": " + error);
                    ScheduleRetry(delegate { AttemptExport(requestPath, token, procUid, ownerPid, attempt + 1); }, RetryDelayMs);
                    return;
                }

                AppendLog("Request failed " + token + " procUid=" + procUid + ": " + error);
                if (ownerPid > 0)
                {
                    // 指定实例：只有本实例能回答，直接写回失败原因
                    WriteErrorIfEmpty(responsePath, error);
                    CleanupRequest(requestPath);
                }
                else
                {
                    // 广播模式：给真正持有该程序的实例留出应答时间，
                    // 避免本实例的错误结果抢在正确结果之前落盘。
                    ScheduleRetry(delegate
                    {
                        WriteErrorIfEmpty(responsePath, error);
                        CleanupRequest(requestPath);
                    }, BroadcastErrorDelayMs);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Request failed " + token + ": " + ex);
                WriteErrorIfEmpty(responsePath, ex.Message);
                CleanupRequest(requestPath);
            }
        }

        private static void ScheduleRetry(Action work, int delayMs)
        {
            Timer timer = null;
            timer = new Timer(delegate
            {
                try { timer.Dispose(); } catch { }
                if (_uiContext != null) _uiContext.Post(delegate { work(); }, null);
                else work();
            }, null, delayMs, Timeout.Infinite);
        }

        private static void CleanupRequest(string requestPath)
        {
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
        }

        private static bool HasOkResponse(string responsePath)
        {
            try
            {
                if (!File.Exists(responsePath)) return false;
                string text = File.ReadAllText(responsePath, Encoding.UTF8);
                return text.StartsWith("OKXML|", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void WriteErrorIfEmpty(string responsePath, string error)
        {
            try
            {
                if (File.Exists(responsePath))
                {
                    string existing = File.ReadAllText(responsePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(existing)) return;
                }
                File.WriteAllText(responsePath, "ERR|" + error, new UTF8Encoding(true));
                AppendLog("Error response written for pid " + _myPid + ": " + error);
            }
            catch { }
        }

        // 只在本实例内查找，并遍历所有已打开的文档，
        // 而不是只看活动文档——被后处理的程序可能位于非活动文档中。
        private static bool TryExportFromAnyDocument(int procUid, string xmlPath, out string error)
        {
            error = null;
            string firstError = null;
            bool sawBusy = false;

            List<KeyValuePair<string, interop.CimNcAPI.INcModel>> targets = CandidateModels();
            if (targets.Count == 0)
            {
                error = "No open Cimatron NC document in this instance (pid " + _myPid + ")";
                return false;
            }

            foreach (var target in targets)
            {
                try
                {
                    try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
                    target.Value.GetItemAsXML(procUid, xmlPath);
                    if (File.Exists(xmlPath) && new FileInfo(xmlPath).Length > 0) return true;
                    if (firstError == null) firstError = "XML export returned no file in " + target.Key;
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    if (firstError == null) firstError = message;
                    if (IsBusy(message)) sawBusy = true;
                    AppendLog("Export failed in " + target.Key + " for procUid=" + procUid + ": " + message);
                }
            }

            string summary = firstError == null
                ? "NC item " + procUid + " not found in any open document of this instance (pid " + _myPid + ")"
                : firstError;
            error = sawBusy ? BusyPrefix + summary : summary;
            return false;
        }

        private static bool IsBusy(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            if (message.StartsWith(BusyPrefix, StringComparison.Ordinal)) return true;
            return message.IndexOf("非空闲", StringComparison.Ordinal) >= 0
                || message.IndexOf("not idle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<KeyValuePair<string, interop.CimNcAPI.INcModel>> CandidateModels()
        {
            var list = new List<KeyValuePair<string, interop.CimNcAPI.INcModel>>();
            interop.CimatronE.IApplication app = GetApplication();
            if (app == null) return list;

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                object active = app.GetActiveDoc();
                AddModels(list, active, "active document");
                string activePath = TryGetDocumentPath(active);
                if (!string.IsNullOrEmpty(activePath)) seenPaths.Add(activePath);
            }
            catch (Exception ex)
            {
                AppendLog("GetActiveDoc failed: " + ex.Message);
            }

            try
            {
                interop.CimatronE.IPdm pdm = app.GetPdm();
                if (pdm != null)
                {
                    foreach (object entry in ToObjectList(pdm.GetOpenDocuments()))
                    {
                        string path = entry as string;
                        if (string.IsNullOrEmpty(path)) continue;
                        if (!seenPaths.Add(path)) continue;
                        try
                        {
                            AddModels(list, pdm.GetDocumentByPath(path), Path.GetFileName(path));
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Open document skipped (" + Path.GetFileName(path) + "): " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("GetOpenDocuments failed: " + ex.Message);
            }

            return list;
        }

        private static string TryGetDocumentPath(object doc)
        {
            try
            {
                if (doc == null) return null;
                var property = doc.GetType().GetProperty("Path");
                if (property == null) return null;
                return property.GetValue(doc, null) as string;
            }
            catch { return null; }
        }

        private static void AddModels(List<KeyValuePair<string, interop.CimNcAPI.INcModel>> list, object doc, string label)
        {
            if (doc == null) return;
            interop.CimNcAPI.IModelContainer container = doc as interop.CimNcAPI.IModelContainer;
            if (container == null) return;

            try
            {
                var model = container.Model as interop.CimNcAPI.INcModel;
                if (model != null) list.Add(new KeyValuePair<string, interop.CimNcAPI.INcModel>(label, model));
            }
            catch (Exception ex)
            {
                AppendLog("Model access failed (" + label + "): " + ex.Message);
            }

            try
            {
                foreach (object entry in ToObjectList(container.GetModels()))
                {
                    var model = entry as interop.CimNcAPI.INcModel;
                    if (model != null) list.Add(new KeyValuePair<string, interop.CimNcAPI.INcModel>(label + " (multi-model)", model));
                }
            }
            catch (Exception ex)
            {
                AppendLog("GetModels failed (" + label + "): " + ex.Message);
            }
        }

        private static List<object> ToObjectList(object raw)
        {
            var result = new List<object>();
            if (raw == null) return result;

            var array = raw as Array;
            if (array != null)
            {
                foreach (object item in array) result.Add(item);
                return result;
            }

            var enumerable = raw as IEnumerable;
            if (enumerable != null)
            {
                foreach (object item in enumerable) result.Add(item);
                return result;
            }

            try
            {
                Type type = raw.GetType();
                var countProperty = type.GetProperty("Count");
                var itemProperty = type.GetProperty("Item");
                if (countProperty != null && itemProperty != null)
                {
                    int count = Convert.ToInt32(countProperty.GetValue(raw, null));
                    for (int i = 0; i < count; i++)
                    {
                        try { result.Add(itemProperty.GetValue(raw, new object[] { i })); } catch { }
                    }
                }
            }
            catch { }

            return result;
        }

        private static interop.CimatronE.IApplication GetApplication()
        {
            try
            {
                var provider = new interop.CimServicesAPI.CimApplicationProviderClass();
                object appObject = provider.GetApplication();
                return appObject as interop.CimatronE.IApplication;
            }
            catch (Exception ex)
            {
                AppendLog("GetApplication failed: " + ex.Message);
                return null;
            }
        }

        private static string Sha256File(string path)
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

        private static string Encode64(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string NcNamerDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NcNamer");
        }

        internal static void AppendLog(string message)
        {
            try
            {
                Directory.CreateDirectory(NcNamerDir());
                File.AppendAllText(Path.Combine(NcNamerDir(), "auto_service_log.txt"), DateTime.Now.ToString("o") + " " + message + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
