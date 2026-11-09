using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace NcNamerAutoService
{
    internal static class ToolboxUpdateService
    {
        // 发版时同步改成 Release tag 对应的版本号（例如 v3.4.0 -> 3.4.0）。
        internal const string CurrentVersion = "3.3.0";

        private const string RepoOwner = "zhaoshuaishuai956";
        private const string RepoName = "IKUN_Cimatron_tools";
        private const string ApiUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";

        private static int _started;
        private static readonly object LogGate = new object();

        internal static void StartCheck()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;

            Thread thread = new Thread(delegate() { CheckAndPrompt(); });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private static void CheckAndPrompt()
        {
            DateTime started = DateTime.UtcNow;
            try
            {
                Log("check start, current=" + CurrentVersion);

                string tag;
                string downloadUrl;
                if (!TryGetLatestRelease(out tag, out downloadUrl)) return;

                Log("latest release=" + tag + ", current=" + CurrentVersion);
                if (CompareVersions(tag, CurrentVersion) <= 0)
                {
                    Log("no update");
                    return;
                }
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Log("newer release has no payload asset; skipped");
                    return;
                }

                // 启动提示会显示 6 秒；更新确认框排在其后，避免两个置顶窗口叠在一起。
                int elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                int wait = 6500 - elapsed;
                if (wait > 0) Thread.Sleep(wait);

                ShowUpdatePrompt(tag, downloadUrl);
            }
            catch (Exception ex)
            {
                Log("check failed: " + ex.Message);
            }
        }

        private static bool TryGetLatestRelease(out string tag, out string downloadUrl)
        {
            tag = null;
            downloadUrl = null;

            string cimatronVersion = GetCurrentCimatronVersion();
            if (string.IsNullOrEmpty(cimatronVersion))
            {
                Log("cannot determine Cimatron version");
                return false;
            }
            string major = cimatronVersion.Split('.')[0];
            string assetName = "IKUN_Cimatron_Plugins_" + major + ".zip";

            try
            {
                // .NET 4.0 的枚举里可能没有 Tls12，直接写数值 3072。
                ServicePointManager.SecurityProtocol =
                    ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ApiUrl);
                request.UserAgent = "IKUN-Cimatron-Tools/" + CurrentVersion;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;

                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    Match tagMatch = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    if (!tagMatch.Success || string.IsNullOrEmpty(tagMatch.Groups[1].Value))
                    {
                        Log("latest release has no tag_name");
                        return false;
                    }
                    tag = tagMatch.Groups[1].Value.Trim();

                    // 只接受已经构建好的、与本机 Cimatron 大版本对应的原子更新资源。
                    // 没有该资源就不提示，绝不用源码包或 Setup.zip 走到现场编译。
                    string assetPattern = "\"name\"\\s*:\\s*\"" + Regex.Escape(assetName) +
                        "\"[\\s\\S]{0,6000}?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"";
                    Match assetMatch = Regex.Match(json, assetPattern);
                    if (!assetMatch.Success || string.IsNullOrEmpty(assetMatch.Groups[1].Value))
                    {
                        Log("latest release has no asset " + assetName);
                        return true;
                    }

                    downloadUrl = assetMatch.Groups[1].Value.Replace("\\/", "/");
                    if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("asset url is not https: " + downloadUrl);
                        downloadUrl = null;
                        return true;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log("latest release request failed: " + ex.Message);
                return false;
            }
        }

        private static string GetCurrentCimatronVersion()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                DirectoryInfo program = new FileInfo(exePath).Directory;
                if (program != null && program.Parent != null) return program.Parent.Name;
            }
            catch { }
            return "";
        }

        private static void ShowUpdatePrompt(string tag, string downloadUrl)
        {
            string updater = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "NcNamer", "IKUNUpdater.exe");
            if (!File.Exists(updater))
            {
                Log("updater missing: " + updater);
                MessageBox.Show(
                    "发现新版本 " + tag + "，但更新器缺失。\n\n请先重新运行一次安装.cmd，再重启 Cimatron。",
                    "IKUN工具箱更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult choice;
            using (Form form = new Form())
            {
                form.Text = "IKUN工具箱更新";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.TopMost = true;
                form.ShowInTaskbar = true;
                form.ClientSize = new Size(520, 245);
                form.BackColor = Color.White;

                Label title = new Label();
                title.Text = "发现新版本 " + tag;
                title.Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold);
                title.ForeColor = Color.FromArgb(31, 78, 121);
                title.SetBounds(22, 20, 470, 36);
                form.Controls.Add(title);

                Label info = new Label();
                info.Text = "当前版本：" + CurrentVersion + "\r\n\r\n" +
                    "更新会自动关闭 Cimatron，下载对应版本的插件资源并原子替换，\r\n" +
                    "然后重新启动。未保存的模型会丢失，请先保存再继续。";
                info.Font = new Font("Microsoft YaHei", 9.5F);
                info.ForeColor = Color.DimGray;
                info.SetBounds(24, 64, 470, 90);
                form.Controls.Add(info);

                Button update = new Button();
                update.Text = "立即更新";
                update.Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold);
                update.SetBounds(300, 190, 100, 32);
                update.DialogResult = DialogResult.OK;
                form.Controls.Add(update);

                Button skip = new Button();
                skip.Text = "本次跳过";
                skip.Font = new Font("Microsoft YaHei", 9F);
                skip.SetBounds(410, 190, 88, 32);
                skip.DialogResult = DialogResult.Cancel;
                form.Controls.Add(skip);

                form.AcceptButton = update;
                form.CancelButton = skip;

                choice = form.ShowDialog();
            }

            if (choice != DialogResult.OK)
            {
                Log("update skipped this session: " + tag);
                return;
            }

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string cimatronVersion = GetCimatronVersion(exePath);
                string args = "--pid " + Process.GetCurrentProcess().Id +
                    " --url \"" + downloadUrl + "\"" +
                    " --tag \"" + tag + "\"" +
                    " --exe \"" + exePath + "\"" +
                    " --version \"" + cimatronVersion + "\"";

                ProcessStartInfo start = new ProcessStartInfo(updater);
                start.Arguments = args;
                start.UseShellExecute = true;
                start.Verb = "runas";
                start.WorkingDirectory = Path.GetDirectoryName(updater);
                Process.Start(start);

                Log("updater launched, exiting Cimatron pid=" + Process.GetCurrentProcess().Id);
                try { Process.GetCurrentProcess().Kill(); }
                catch { Environment.Exit(0); }
            }
            catch (Exception ex)
            {
                Log("updater launch failed: " + ex.Message);
                MessageBox.Show(
                    "更新器启动失败，Cimatron 未关闭。\n\n" + ex.Message,
                    "IKUN工具箱更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetCimatronVersion(string exePath)
        {
            try
            {
                DirectoryInfo program = new FileInfo(exePath).Directory;
                if (program != null && program.Parent != null) return program.Parent.Name;
            }
            catch { }
            return "";
        }

        private static int CompareVersions(string left, string right)
        {
            int[] a = ParseVersion(left);
            int[] b = ParseVersion(right);
            if (a == null || b == null) return 0;
            for (int i = 0; i < 3; i++)
            {
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            }
            return 0;
        }

        private static int[] ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            string[] parts = text.Split(new char[] { '.', '-', '+' });
            int[] result = new int[3];
            int count = Math.Min(parts.Length, 3);
            for (int i = 0; i < count; i++)
            {
                int value;
                if (!int.TryParse(parts[i], out value)) return null;
                result[i] = value;
            }
            return result;
        }

        private static void Log(string message)
        {
            try
            {
                lock (LogGate)
                {
                    NcAutoExportService.AppendLog("AutoUpdate: " + message);
                }
            }
            catch { }
        }
    }
}
