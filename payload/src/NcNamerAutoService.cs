using System;
using System.Collections.Generic;
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
            var cmd = new ApiCommand
            {
                Name = "NcNamerAutoService2024",
                ToolbarName = "",
                MenuPath = "NC\nNcNamerAutoService",
                Caption = "NcNamer Auto Service",
                ToolTip = "Automatic NC naming support service",
                Description = "Background service used automatically by BJ post-processing",
                Application = ApiApplications.Nc | ApiApplications.NcPart,
                ExecuteCommand = new NcAutoServiceCommand(),
                License = "1"
            };
            return cmd;
        }
    }

    public class NcAutoServiceCommand : ICimWpfCommand
    {
        public string GetAccelerator() { return ""; }
        public void SetAccelerator(string accelerator) { }
        public bool OnCommandDblClk() { return false; }

        public bool OnCommand()
        {
            NcAutoExportService.Start();
            NcAutoExportService.AppendLog("Manual service check; no user action is required");
            return true;
        }

        public CimWpfUICommandStates OnCommandUI()
        {
            return new CimWpfUICommandStates { UiState = CommandUIState.Enabled };
        }
    }

    internal static class NcAutoExportService
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> RecentTokens = new HashSet<string>();
        private static FileSystemWatcher _watcher;
        private static SynchronizationContext _uiContext;

        internal static void Start()
        {
            lock (Gate)
            {
                if (_watcher != null) return;
                string dir = NcNamerDir();
                Directory.CreateDirectory(dir);
                _uiContext = SynchronizationContext.Current;
                _watcher = new FileSystemWatcher(dir, "export_request_*.txt");
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                _watcher.Created += OnRequest;
                _watcher.Changed += OnRequest;
                _watcher.EnableRaisingEvents = true;
                AppendLog("NcNamer auto service started");
            }
        }

        private static void OnRequest(object sender, FileSystemEventArgs e)
        {
            string name = Path.GetFileNameWithoutExtension(e.FullPath);
            string stem = name.Replace("export_request_", "");
            int procUid = 0;
            int split = stem.LastIndexOf('_');
            string token = stem;
            if (split > 0)
            {
                token = stem.Substring(0, split);
                int.TryParse(stem.Substring(split + 1), out procUid);
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

            Action work = delegate { HandleRequest(e.FullPath, token, procUid); };
            if (_uiContext != null) _uiContext.Post(delegate { work(); }, null);
            else work();
        }

        private static void HandleRequest(string requestPath, string token, int procUid)
        {
            string responsePath = Path.Combine(NcNamerDir(), "export_response_" + token + ".txt");
            try
            {
                AppendLog("Request " + token + " procUid=" + procUid);
                if (procUid <= 0) throw new InvalidOperationException("Missing procedure id");

                string xmlPath = Path.Combine(NcNamerDir(), "nc_item_" + token + "_" + procUid + ".xml");
                try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }

                ExportSingleItem(procUid, xmlPath);
                if (!File.Exists(xmlPath) || new FileInfo(xmlPath).Length == 0)
                {
                    throw new InvalidOperationException("XML export returned no file");
                }

                string hash = Sha256File(xmlPath);
                string response = string.Join("|", new[]
                {
                    "OKXML",
                    Encode64(xmlPath),
                    Encode64(procUid.ToString()),
                    Encode64(hash)
                });
                File.WriteAllText(responsePath, response, new UTF8Encoding(true));
                AppendLog("Response " + token + " xml=" + xmlPath + " sha256=" + hash);
            }
            catch (Exception ex)
            {
                AppendLog("Request failed " + token + ": " + ex);
                try { File.WriteAllText(responsePath, "ERR|" + ex.Message, new UTF8Encoding(true)); } catch { }
            }
            finally
            {
                try { File.Delete(requestPath); } catch { }
            }
        }

        private static void ExportSingleItem(int procUid, string xmlPath)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
                    ExportOnce(procUid, xmlPath);
                    if (File.Exists(xmlPath) && new FileInfo(xmlPath).Length > 0) return;
                    last = new InvalidOperationException("XML export returned no file");
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                AppendLog("Export attempt " + attempt + " failed for procUid=" + procUid + ": " + (last == null ? "empty file" : last.Message));
                Thread.Sleep(400);
            }
            throw new InvalidOperationException("XML export failed after 8 attempts for procUid=" + procUid + ": " + (last == null ? "unknown" : last.Message));
        }

        private static void ExportOnce(int procUid, string xmlPath)
        {
            var provider = new interop.CimServicesAPI.CimApplicationProviderClass();
            object appObject = provider.GetApplication();
            if (appObject == null) throw new InvalidOperationException("No Cimatron application");
            var app = (interop.CimatronE.IApplication)appObject;
            var doc = app.GetActiveDoc();
            if (doc == null) throw new InvalidOperationException("No active Cimatron document");
            var container = (interop.CimNcAPI.IModelContainer)doc;
            var nc = (interop.CimNcAPI.INcModel)container.Model;
            nc.GetItemAsXML(procUid, xmlPath);
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
