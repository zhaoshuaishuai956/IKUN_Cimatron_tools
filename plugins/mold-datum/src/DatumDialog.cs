using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using CimApp = interop.CimatronE;
using Mdlr = interop.CimMdlrAPI;
using Nc = interop.CimNcAPI;

namespace MoldDatum
{
    /// <summary>
    /// 非模态核对对话框。
    /// 打开时自动识别 4 个侧面（也可点「自动识别」实时重取）。
    /// 严格不改任何加工参数（模板里的 DownStep / 进给 / 转速 / 刀具全部原样）。
    /// </summary>
    public class DatumDialog : Form
    {
        private readonly DatumSession _s;

        private NumericUpDown _nTool, _nOffset, _nGap, _nLen;
        private Label _lRecess, _lStatus, _lDocType, _lPreviewTitle;
        private TextBox _txtPreview;
        private Button _btnAuto, _btnReload, _btnApply, _btnCreate, _btnTpl, _btnExec, _btnClose;
        private bool _suppress;

        public DatumDialog(DatumSession session, IWin32Window owner)
        {
            _s = session;
            BuildUi();
            // 打开即自动识别，不需要用户先在模型里选面
            try { _s.LoadAuto(); } catch (Exception ex) { DatumLog.Write("auto on open failed: " + ex.Message); }
            RefreshFromSession();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        private void BuildUi()
        {
            Text = "IKUN思美创工具箱 · 铁模四边基准";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(620, 660);
            MinimumSize = new Size(560, 600);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int y = 10;

            // ---- 文档信息 ----
            _lDocType = new Label();
            _lDocType.SetBounds(12, y, 590, 18);
            _lDocType.Text = "文档：-";
            _lDocType.ForeColor = Color.DimGray;
            _lDocType.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lDocType);
            y += 24;

            // ---- 参数 ----
            GroupBox gb = new GroupBox();
            gb.Text = "参数（加工参数一律沿用模板，此处不改）";
            gb.SetBounds(10, y, 598, 152);
            gb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(gb);

            _nTool = MakeNum(gb, "刀具直径 D", 20, 22, 0.1m, 200m, 6m);
            _nOffset = MakeNum(gb, "距侧面 d", 20, 52, 0.1m, 200m, 2.5m);
            _nGap = MakeNum(gb, "距角点 s", 20, 82, 0m, 5000m, 10m);
            _nLen = MakeNum(gb, "线长 L", 320, 22, 0.1m, 5000m, 10m);

            Label lCut = new Label();
            lCut.SetBounds(320, 52, 90, 20);
            lCut.Text = "下铣深度 H";
            lCut.TextAlign = ContentAlignment.MiddleLeft;
            gb.Controls.Add(lCut);

            Label lCutVal = new Label();
            lCutVal.SetBounds(414, 52, 160, 20);
            lCutVal.Text = "10（来自模板）";
            lCutVal.ForeColor = Color.DimGray;
            lCutVal.TextAlign = ContentAlignment.MiddleLeft;
            gb.Controls.Add(lCutVal);

            _lRecess = new Label();
            _lRecess.SetBounds(320, 82, 250, 20);
            _lRecess.Text = "推算凹深 = -";
            gb.Controls.Add(_lRecess);

            Label lHint = new Label();
            lHint.SetBounds(20, 112, 560, 32);
            lHint.Text = "线在侧面外侧偏移 d；凹深 = D/2 − d。改 d 或 D 会改变凹深。";
            lHint.ForeColor = Color.DimGray;
            gb.Controls.Add(lHint);

            y += 160;

            // ---- 预览标题 ----
            _lPreviewTitle = new Label();
            _lPreviewTitle.SetBounds(12, y, 590, 18);
            _lPreviewTitle.Text = "识别结果与基准线预览";
            _lPreviewTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lPreviewTitle);
            y += 20;

            // ---- 预览框（随窗口伸缩）----
            _txtPreview = new TextBox();
            _txtPreview.SetBounds(12, y, 596, 300);
            _txtPreview.Multiline = true;
            _txtPreview.ReadOnly = true;
            _txtPreview.ScrollBars = ScrollBars.Both;
            _txtPreview.WordWrap = false;
            _txtPreview.Font = new Font("Consolas", 8.5f);
            _txtPreview.BackColor = Color.White;
            _txtPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_txtPreview);

            // ---- 底部（自下而上排，避免被裁）----
            int bottom = ClientSize.Height;

            _btnClose = MakeBtn("关闭", 508, bottom - 36, 100);
            _btnExec = MakeBtn("执行刀路", 402, bottom - 36, 100);
            _btnTpl = MakeBtn("套用模板", 296, bottom - 36, 100);

            _btnCreate = MakeBtn("创建线条", 402, bottom - 70, 100);
            _btnApply = MakeBtn("应用参数", 296, bottom - 70, 100);
            _btnReload = MakeBtn("读取选择", 190, bottom - 70, 100);
            _btnAuto = MakeBtn("自动识别", 84, bottom - 70, 100);

            _lStatus = new Label();
            _lStatus.SetBounds(12, bottom - 112, 596, 38);
            _lStatus.Text = "";
            _lStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_lStatus);

            // 事件
            _btnAuto.Click += delegate { OnAutoDetect(); };
            _btnReload.Click += delegate { OnReload(); };
            _btnApply.Click += delegate { OnApplyParams(); };
            _btnCreate.Click += delegate { OnCreateLines(); };
            _btnTpl.Click += delegate { OnApplyTemplate(); };
            _btnExec.Click += delegate { OnExecuteToolpath(); };
            _btnClose.Click += delegate { Close(); };

            _nTool.ValueChanged += delegate { OnParamChanged(); };
            _nOffset.ValueChanged += delegate { OnParamChanged(); };
            _nGap.ValueChanged += delegate { OnParamChanged(); };
            _nLen.ValueChanged += delegate { OnParamChanged(); };

            AcceptButton = null;
            KeyPreview = true;
            KeyDown += delegate(object o, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private NumericUpDown MakeNum(Control parent, string label, int x, int y, decimal min, decimal max, decimal val)
        {
            Label l = new Label();
            l.SetBounds(x, y, 90, 20);
            l.Text = label;
            l.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(l);

            NumericUpDown n = new NumericUpDown();
            n.SetBounds(x + 96, y, 96, 22);
            n.DecimalPlaces = 2;
            n.Increment = 0.1m;
            n.Minimum = min;
            n.Maximum = max;
            n.Value = val;
            parent.Controls.Add(n);
            return n;
        }

        private Button MakeBtn(string text, int x, int y, int w)
        {
            Button b = new Button();
            b.SetBounds(x, y, w, 28);
            b.Text = text;
            b.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(b);
            return b;
        }

        // ============ 刷新 ============

        private void RefreshFromSession()
        {
            _suppress = true;
            _nTool.Value = Clamp((decimal)_s.Params.ToolDiameter, _nTool.Minimum, _nTool.Maximum);
            _nOffset.Value = Clamp((decimal)_s.Params.Offset, _nOffset.Minimum, _nOffset.Maximum);
            _nGap.Value = Clamp((decimal)_s.Params.CornerGap, _nGap.Minimum, _nGap.Maximum);
            _nLen.Value = Clamp((decimal)_s.Params.Length, _nLen.Minimum, _nLen.Maximum);
            _suppress = false;

            try
            {
                _lDocType.Text = "文档：" + (_s.IsNcDoc ? "NC 编程文档" : SafeDocType()) +
                    (_s.Doc != null ? "   " + Safe(safeTitle) : "");
            }
            catch { }

            UpdateRecess();
            UpdatePreview();
            UpdateStatus();
        }

        private string safeTitle() { try { return _s.Doc.Title; } catch { return ""; } }
        private string SafeDocType() { try { return _s.Doc.Type.ToString(); } catch { return "?"; } }
        private static string Safe(Func<string> f) { try { return f(); } catch { return ""; } }

        private static decimal Clamp(decimal v, decimal lo, decimal hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private void OnParamChanged()
        {
            if (_suppress) return;
            PullParams();
            UpdateRecess();
            _s.Rebuild();
            UpdatePreview();
            UpdateStatus();
        }

        private void PullParams()
        {
            _s.Params.ToolDiameter = (double)_nTool.Value;
            _s.Params.Offset = (double)_nOffset.Value;
            _s.Params.CornerGap = (double)_nGap.Value;
            _s.Params.Length = (double)_nLen.Value;
        }

        private void UpdateRecess()
        {
            double r = _s.Params.RecessDepth;
            if (_s.Params.ToolDiameter / 2.0 <= _s.Params.Offset)
            {
                _lRecess.Text = "推算凹深 = 无效（d ≥ D/2）";
                _lRecess.ForeColor = Color.Firebrick;
            }
            else
            {
                _lRecess.Text = "推算凹深 = " + r.ToString("0.###", CultureInfo.InvariantCulture) + " mm";
                _lRecess.ForeColor = r > 0 ? Color.ForestGreen : Color.Firebrick;
            }
        }

        private void UpdatePreview()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("【识别到的侧面】");
            if (_s.Faces.Count == 0) sb.AppendLine("  （无）");
            else
            {
                int fi = 0;
                foreach (FaceInfo f in _s.Faces)
                {
                    fi++;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0}. {1}  面数={2}  沿边[{3:0.###},{4:0.###}] 长={5:0.###}  Z顶={6:0.###}{7}",
                        fi, f.Label, f.Faces.Count, f.TMin, f.TMax, (f.TMax - f.TMin), f.MaxZ,
                        string.IsNullOrEmpty(f.Warning) ? "" : "  ⚠" + f.Warning));
                }
            }

            sb.AppendLine();
            sb.AppendLine("【基准线（每面 2 条：一近端、一近另一端）】");
            if (_s.Lines.Count == 0) sb.AppendLine("  （暂无）");
            else
            {
                int i = 0;
                foreach (DatumLine ln in _s.Lines)
                {
                    i++;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,2}. {1}", i, ln.Describe()));
                }
            }

            sb.AppendLine();
            sb.Append("（坐标为模型主 UCS；Z 取该线跨度内的顶面最高点）");
            _txtPreview.Text = sb.ToString();
        }

        private void UpdateStatus()
        {
            _btnTpl.Enabled = AllLinesCreated() && _s.Params.IsValid;
            _btnExec.Enabled = _s.NcModel != null;

            if (!string.IsNullOrEmpty(_s.LoadError))
            {
                _lStatus.ForeColor = Color.Firebrick;
                _lStatus.Text = _s.LoadError;
                _btnCreate.Enabled = false;
                return;
            }

            string warn = "";
            foreach (FaceInfo f in _s.Faces)
                if (!string.IsNullOrEmpty(f.Warning)) warn += "；" + f.Label + "：" + f.Warning;

            if (_s.Lines.Count == 8)
            {
                _lStatus.ForeColor = warn.Length > 0 ? Color.DarkOrange : Color.ForestGreen;
                _lStatus.Text = AllLinesCreated()
                    ? "8 条基准线已写入模型。下一步：套用模板 → 在工序里确认几何 → 执行刀路。"
                    : "已自动识别 4 个侧面并算出 8 条线。点「创建线条」写入模型。";
                if (warn.Length > 0) _lStatus.Text += "\n注意" + warn;
                _btnCreate.Enabled = _s.Params.IsValid;
            }
            else
            {
                _lStatus.ForeColor = Color.DarkOrange;
                _lStatus.Text = "尚未生成 8 条线。点「自动识别」重试，或手动选 4 个侧面后点「读取选择」。";
                _btnCreate.Enabled = false;
            }
        }

        // ============ 动作 ============

        private void OnAutoDetect()
        {
            _btnAuto.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                _s.LoadAuto();
                _s.Rebuild();
                RefreshFromSession();
                DatumLog.Write("OnAutoDetect: faces=" + _s.Faces.Count + " lines=" + _s.Lines.Count +
                               " err=" + (_s.LoadError ?? ""));
            }
            finally
            {
                Cursor = Cursors.Default;
                _btnAuto.Enabled = true;
            }
        }

        private void OnReload()
        {
            _s.LoadSelection();
            _s.Rebuild();
            RefreshFromSession();
            DatumLog.Write("OnReload: lines=" + _s.Lines.Count + " err=" + (_s.LoadError ?? ""));
        }

        private void OnApplyParams()
        {
            PullParams();
            _s.Rebuild();
            UpdateRecess();
            UpdatePreview();
            UpdateStatus();
        }

        private bool AllLinesCreated()
        {
            if (_s.Lines.Count != 8) return false;
            foreach (DatumLine ln in _s.Lines) if (ln.Created == null) return false;
            return true;
        }

        private void OnCreateLines()
        {
            if (!_s.Params.IsValid)
            {
                MessageBox.Show(this, _s.Params.ValidateReason, "参数无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_s.Lines.Count != 8)
            {
                MessageBox.Show(this, "当前没有 8 条基准线，请先点「自动识别」。", "无法创建", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnCreate.Enabled = false;
            try
            {
                _s.EnsureGeo();
                int ok = 0, fail = 0;
                StringBuilder log = new StringBuilder();

                foreach (DatumLine ln in _s.Lines)
                {
                    try
                    {
                        Mdlr.ICimEntity ent = _s.Geo.CreateLine(ln);
                        if (ent != null) { ln.Created = ent; ok++; }
                        else { fail++; log.AppendLine("返回 null：" + ln.Describe()); }
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        log.AppendLine("异常：" + ex.Message + " ← " + ln.Describe());
                        DatumLog.Write("CreateLine failed: " + ex);
                    }
                }

                DatumLog.Write("CreateLines: ok=" + ok + " fail=" + fail);

                if (fail == 0)
                {
                    MessageBox.Show(this,
                        "已创建 " + ok + " 条基准线。\n\n" +
                        "下一步：点「套用模板」生成工序（参数沿用你的模板），\n" +
                        "然后在工序里确认几何，回来点「执行刀路」。\n\n日志：" + DatumLog.LogPath,
                        "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, "创建完成：" + ok + " 成功，" + fail + " 失败。\n\n" + log,
                        "部分失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                DatumLog.Write("OnCreateLines failed: " + ex);
                MessageBox.Show(this, "创建失败：" + ex.Message + "\n\n日志：" + DatumLog.LogPath,
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnCreate.Enabled = true;
                UpdateStatus();
            }
        }

        private void OnApplyTemplate()
        {
            if (!AllLinesCreated())
            {
                MessageBox.Show(this, "请先点「创建线条」，把基准线建到模型里。",
                    "还差一步", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnTpl.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                DatumToolpath.ApplyResult r = DatumToolpath.ApplyTemplate(_s);

                if (r.Ok)
                {
                    _lStatus.ForeColor = Color.ForestGreen;
                    _lStatus.Text = "模板已套用（工序 " + r.TemplateUid + "，轮廓 " + r.ContourCount + " 条）。";
                    MessageBox.Show(this,
                        "模板已套用，加工参数全部沿用你的模板。\n\n" +
                        "工序 UID：" + r.TemplateUid + "\n当前轮廓：" + r.ContourCount + " 条\n" +
                        "8 条基准线已建好" + (r.LinesSelected ? "并在模型中选中" : "") + "。\n\n" +
                        "【需要你做一步】\n" +
                        "Cimatron 未开放几何绑定的 API，请打开该工序 → 轮廓/曲线几何 → 框选这 8 条线 → 确定。\n\n" +
                        "然后回到本对话框点「执行刀路」。\n\n日志：" + DatumLog.LogPath,
                        "模板已套用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _lStatus.ForeColor = Color.Firebrick;
                    _lStatus.Text = r.Message;
                    MessageBox.Show(this, r.Message + "\n\n过程：\n" + Trim(r.Log.ToString(), 2000),
                        "套模板失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                DatumLog.Write("OnApplyTemplate failed: " + ex);
                MessageBox.Show(this, "异常：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                UpdateStatus();
            }
        }

        private void OnExecuteToolpath()
        {
            _btnExec.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                DatumToolpath.ExecResult r = DatumToolpath.ExecuteProcedures(_s);
                _lStatus.ForeColor = r.Ok ? Color.ForestGreen : Color.Firebrick;
                _lStatus.Text = r.Message.Replace("\n", "；");
                MessageBox.Show(this,
                    (r.Ok ? "已执行刀路。\n\n" : "执行完成，但请看状态：\n\n") + r.Message +
                    "\n\n日志：" + DatumLog.LogPath,
                    "执行刀路", MessageBoxButtons.OK,
                    r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                DatumLog.Write("OnExecuteToolpath failed: " + ex);
                MessageBox.Show(this, "异常：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                UpdateStatus();
            }
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "\n...(已截断)";
        }
    }
}
