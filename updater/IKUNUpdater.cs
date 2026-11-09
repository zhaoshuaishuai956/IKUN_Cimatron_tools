using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace IKUNUpdater
{
    internal static class Program
    {
        private static readonly object LogGate = new object();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "NcNamer", "updater_log.txt");

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 安装目录里的旧更新器自己复制到临时目录再启动新实例，
            // 这样更新资源里可以原子替换 %USERPROFILE%\NcNamer\IKUNUpdater.exe。
            if (!HasArg(args, "--relocated"))
            {
                try
                {
                    string temp = Path.Combine(
                        Path.GetTempPath(),
                        "IKUNUpdater_" + Process.GetCurrentProcess().Id + "_" +
                        Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Application.ExecutablePath, temp, true);

                    ProcessStartInfo child = new ProcessStartInfo(temp, BuildArgs(args) + " --relocated");
                    child.UseShellExecute = false;
                    child.WorkingDirectory = Path.GetTempPath();
                    Process.Start(child);
                    Log("relaunched from temp: " + temp);
                    return;
                }
                catch (Exception ex)
                {
                    Log("relocate failed: " + ex.Message);
                }
            }

            Application.Run(new UpdaterForm(args));
        }

        internal static void Log(string message)
        {
            try
            {
                lock (LogGate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("o") + " " + message + "\r\n",
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private static bool HasArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string BuildArgs(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < args.Length; i++)
            {
                sb.Append('"').Append(args[i].Replace("\"", "\\\"")).Append("\" ");
            }
            return sb.ToString();
        }
    }

    internal sealed class UpdaterForm : Form
    {
        private sealed class FileMap
        {
            public string Source;
            public string Target;
            public string Staged;
            public string Backup;
            public bool Required;
            public bool Existed;
        }

        private readonly string[] _args;
        private int _pid;
        private string _url;
        private string _tag;
        private string _exe;
        private string _version;

        private Label _status;
        private ProgressBar _progress;
        private Button _close;
        private bool _running;

        private string _zipPath;
        private string _extractDir;

        internal UpdaterForm(string[] args)
        {
            _args = args;
            ParseArgs();

            Text = "IKUN工具箱更新";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            ClientSize = new Size(520, 190);
            BackColor = Color.White;

            Label title = new Label();
            title.Text = "IKUN工具箱更新";
            title.Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(31, 78, 121);
            title.SetBounds(22, 18, 470, 32);
            Controls.Add(title);

            _status = new Label();
            _status.Text = "准备更新...";
            _status.Font = new Font("Microsoft YaHei", 9.5F);
            _status.ForeColor = Color.DimGray;
            _status.SetBounds(24, 62, 470, 46);
            Controls.Add(_status);

            _progress = new ProgressBar();
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _progress.SetBounds(24, 114, 470, 16);
            Controls.Add(_progress);

            _close = new Button();
            _close.Text = "关闭";
            _close.SetBounds(414, 145, 80, 28);
            _close.Enabled = false;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);

            Shown += delegate
            {
                Thread worker = new Thread(RunUpdate);
                worker.IsBackground = true;
                worker.Start();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (_running) e.Cancel = true;
            };
        }

        private void ParseArgs()
        {
            for (int i = 1; i < _args.Length; i++)
            {
                switch (_args[i])
                {
                    case "--pid":
                        int.TryParse(Next(i), out _pid);
                        i++;
                        break;
                    case "--url":
                        _url = Next(i); i++;
                        break;
                    case "--tag":
                        _tag = Next(i); i++;
                        break;
                    case "--exe":
                        _exe = Next(i); i++;
                        break;
                    case "--version":
                        _version = Next(i); i++;
                        break;
                }
            }
        }

        private string Next(int index)
        {
            return index + 1 < _args.Length ? _args[index + 1] : "";
        }

        private void RunUpdate()
        {
            _running = true;
            try
            {
                Program.Log("start tag=" + _tag + " pid=" + _pid + " url=" + _url);

                SetStatus("等待 Cimatron 退出...");
                WaitForCimatron();

                SetStatus("下载更新资源...");
                Download();

                SetStatus("解压并校验...");
                string payloadRoot = Extract();

                SetStatus("原子替换插件文件...");
                ApplyAtomic(payloadRoot);

                SetStatus("更新完成，正在重启 Cimatron...");
                Program.Log("update applied, restarting " + _exe);
                Thread.Sleep(1200);
                StartCimatron();

                _running = false;
                CloseFromWorker();
            }
            catch (Exception ex)
            {
                _running = false;
                Program.Log("failed: " + ex);
                SetStatus("更新失败：\r\n" + ex.Message);
                SetCloseEnabled(true);
            }
        }

        private void WaitForCimatron()
        {
            if (_pid <= 0) return;
            try
            {
                Process process = Process.GetProcessById(_pid);
                if (!process.WaitForExit(60000))
                    throw new Exception("等待 Cimatron 退出超时。请关闭 Cimatron 后重试。");
            }
            catch (ArgumentException)
            {
                // 进程已经退出，正常。
            }
        }

        private void Download()
        {
            Uri uri;
            if (!Uri.TryCreate(_url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new Exception("更新资源地址不是 HTTPS。");

            _zipPath = Path.Combine(Path.GetTempPath(),
                "IKUN_payload_" + Guid.NewGuid().ToString("N") + ".zip");

            ServicePointManager.SecurityProtocol =
                ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = "IKUN-Cimatron-Tools-Updater/1.0";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = true;

            using (WebResponse response = request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (FileStream output = File.Create(_zipPath))
            {
                byte[] buffer = new byte[81920];
                long total = response.ContentLength;
                long received = 0;
                int lastPercent = -1;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    received += read;
                    if (total > 0)
                    {
                        int percent = (int)(received * 100 / total);
                        if (percent != lastPercent && percent % 5 == 0)
                        {
                            lastPercent = percent;
                            SetStatus("下载更新资源... " + percent + "%");
                        }
                    }
                }
            }
            Program.Log("downloaded " + _zipPath);
        }

        private string Extract()
        {
            _extractDir = Path.Combine(Path.GetTempPath(),
                "IKUN_payload_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_extractDir);
            ZipFile.ExtractToDirectory(_zipPath, _extractDir);

            string root = FindPayloadRoot(_extractDir);
            Program.Log("payload root=" + root);

            string manifest = Path.Combine(root, "manifest.txt");
            if (File.Exists(manifest))
            {
                string manifestText = File.ReadAllText(manifest, Encoding.UTF8);
                Program.Log("manifest: " + manifestText.Replace("\r", " ").Replace("\n", " "));

                string manifestVersion = null;
                foreach (string line in manifestText.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    {
                        manifestVersion = line.Substring("version=".Length).Trim();
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(manifestVersion) && !string.IsNullOrEmpty(_tag))
                {
                    string tagVersion = _tag.TrimStart('v', 'V');
                    if (!SameVersion(manifestVersion, tagVersion))
                        throw new Exception("更新包版本 " + manifestVersion +
                            " 与 Release " + _tag + " 不一致，已取消更新。");
                }
            }

            return root;
        }

        private static bool SameVersion(string left, string right)
        {
            string[] a = left.Split('.');
            string[] b = right.Split('.');
            int count = Math.Max(a.Length, b.Length);
            for (int i = 0; i < count; i++)
            {
                int x = 0;
                int y = 0;
                if (i < a.Length && !int.TryParse(a[i], out x)) return false;
                if (i < b.Length && !int.TryParse(b[i], out y)) return false;
                if (x != y) return false;
            }
            return true;
        }

        private static string FindPayloadRoot(string extracted)
        {
            if (File.Exists(Path.Combine(extracted, "NcNamerAutoService.dll")))
                return extracted;

            string[] hits = Directory.GetFiles(extracted, "NcNamerAutoService.dll", SearchOption.AllDirectories);
            if (hits.Length == 0) throw new Exception("更新包中找不到 NcNamerAutoService.dll。");
            return Path.GetDirectoryName(hits[0]);
        }

        private void ApplyAtomic(string payloadRoot)
        {
            string programDir = Path.GetDirectoryName(_exe);
            if (string.IsNullOrEmpty(programDir) || !Directory.Exists(programDir))
                throw new Exception("找不到 Cimatron Program 目录。");

            string version = _version;
            if (string.IsNullOrEmpty(version))
            {
                DirectoryInfo program = new DirectoryInfo(programDir);
                if (program.Parent != null) version = program.Parent.Name;
            }

            string dataImages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Cimatron", "Cimatron", version, "Data", "Images", "Commands");
            string updaterTarget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "NcNamer", "IKUNUpdater.exe");

            List<FileMap> maps = new List<FileMap>();
            AddMap(maps, payloadRoot, programDir, "NcNamerAutoService.dll", true);
            AddMap(maps, payloadRoot, programDir, "EjectorPinFlatten.dll", true);
            AddMap(maps, payloadRoot, programDir, "NcNamer_icon64.ico", false);
            AddMap(maps, payloadRoot, programDir, "EjectorPin_icon64.ico", false);

            if (Directory.Exists(dataImages))
            {
                AddMap(maps, payloadRoot, dataImages, "50468_IKUNtools.ico", false);
                AddMap(maps, payloadRoot, dataImages, "50897_IKUNtools.ico", false);
            }
            else
            {
                Program.Log("data image dir missing, native icons skipped: " + dataImages);
            }

            AddMap(maps, payloadRoot, Path.GetDirectoryName(updaterTarget), "IKUNUpdater.exe", false);

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].Required && !File.Exists(maps[i].Source))
                    throw new Exception("更新包缺少必需文件: " + Path.GetFileName(maps[i].Source));
            }

            string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            List<FileMap> staged = new List<FileMap>();
            foreach (FileMap map in maps)
            {
                if (!File.Exists(map.Source))
                {
                    Program.Log("optional file missing, skipped: " + Path.GetFileName(map.Source));
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(map.Target));
                map.Existed = File.Exists(map.Target);
                map.Staged = map.Target + ".ikun_new_" + stamp;
                map.Backup = map.Target + ".ikun_bak_" + stamp;
                File.Copy(map.Source, map.Staged, true);
                staged.Add(map);
            }

            List<FileMap> applied = new List<FileMap>();
            try
            {
                foreach (FileMap map in staged)
                {
                    if (map.Existed)
                    {
                        ClearReadOnly(map.Target);
                        ReplaceWithRetry(map.Staged, map.Target, map.Backup);
                    }
                    else
                    {
                        File.Move(map.Staged, map.Target);
                    }
                    applied.Add(map);
                }
            }
            catch (Exception ex)
            {
                bool rollbackOk = true;
                for (int i = applied.Count - 1; i >= 0; i--)
                {
                    FileMap map = applied[i];
                    try
                    {
                        if (map.Existed && File.Exists(map.Backup))
                            File.Copy(map.Backup, map.Target, true);
                        else if (!map.Existed && File.Exists(map.Target))
                            File.Delete(map.Target);
                    }
                    catch (Exception rollbackEx)
                    {
                        rollbackOk = false;
                        Program.Log("rollback failed for " + map.Target + ": " + rollbackEx.Message);
                    }
                }

                CleanupStaged(staged);
                throw new Exception(rollbackOk
                    ? "原子替换失败，已回滚: " + ex.Message
                    : "原子替换失败，回滚不完整，请重新运行安装.cmd: " + ex.Message);
            }

            foreach (FileMap map in staged)
            {
                try { if (File.Exists(map.Backup)) File.Delete(map.Backup); }
                catch { }
            }
            Program.Log("atomic replace ok, files=" + applied.Count);
        }

        private static void AddMap(List<FileMap> maps, string payloadRoot, string targetDir, string name, bool required)
        {
            FileMap map = new FileMap();
            map.Source = Path.Combine(payloadRoot, name);
            map.Target = Path.Combine(targetDir, name);
            map.Required = required;
            maps.Add(map);
        }

        private static void ReplaceWithRetry(string staged, string target, string backup)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    File.Replace(staged, target, backup, true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(400);
                }
            }
            throw new IOException("无法替换 " + target + ": " + (last == null ? "未知错误" : last.Message), last);
        }

        private static void ClearReadOnly(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch { }
        }

        private static void CleanupStaged(List<FileMap> staged)
        {
            foreach (FileMap map in staged)
            {
                try { if (File.Exists(map.Staged)) File.Delete(map.Staged); }
                catch { }
            }
        }

        private void StartCimatron()
        {
            if (string.IsNullOrEmpty(_exe) || !File.Exists(_exe))
                throw new Exception("更新已完成，但找不到 Cimatron 可执行文件，请手动启动 Cimatron。");

            // 更新器是提权运行的，用 explorer 以普通用户权限启动 Cimatron。
            ProcessStartInfo start = new ProcessStartInfo("explorer.exe", "\"" + _exe + "\"");
            start.UseShellExecute = true;
            Process.Start(start);
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }
            _status.Text = text;
        }

        private void SetCloseEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetCloseEnabled), enabled);
                return;
            }
            _close.Enabled = enabled;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.MarqueeAnimationSpeed = 0;
        }

        private void CloseFromWorker()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(CloseFromWorker));
                return;
            }
            Close();
        }
    }
}
