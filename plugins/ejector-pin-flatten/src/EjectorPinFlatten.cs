using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CimUIInfrastructure.Commands;
using CimUIInfrastructure.PlugIn;
using Base = interop.CimBaseAPI;
using CimApp = interop.CimatronE;
using Mdlr = interop.CimMdlrAPI;

namespace EjectorPinFlatten
{
    public class EjectorPinFlattenPlugin : ICimApiCommandPlugin
    {
        public ApiCommand AppendCommand()
        {
            FlattenEngine.Log("EjectorPinFlatten plugin loaded (pid=" + Process.GetCurrentProcess().Id + ")");
            FlattenEngine.EnsureNativeIcon();

            return new ApiCommand
            {
                Name = "EjectorPinFlattenPlugin",
                ToolbarName = "IKUN思美创工具箱",
                MenuPath = "IKUN思美创工具箱\n模具",
                Caption = "顶针顶面拉平",
                ToolTip = "按每根顶针各自的最高点把随形顶面拉平",
                Description = "IKUN思美创工具箱 · 顶针顶面拉平：框选顶针顶面，按每根顶针自己的最高点替换为平面",
                Application = ApiApplications.Part,
                ExecuteCommand = new EjectorPinFlattenCommand(),
                IconSize = ApiIconSize.Large,
                License = "1"
            };
        }
    }

    public class EjectorPinFlattenCommand : ICimWpfCommand
    {
        private static readonly List<PinFlattenDialog> OpenDialogs = new List<PinFlattenDialog>();

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
                if (app == null)
                {
                    FlattenEngine.Info(null, "无法连接 Cimatron 应用。");
                    return true;
                }

                CimApp.ICimDocument doc = app.GetActiveDoc();
                if (doc == null)
                {
                    FlattenEngine.Info(GetOwner(app), "没有打开的文档。");
                    return true;
                }
                if (doc.Type != CimApp.DocumentEnumType.cmPart)
                {
                    FlattenEngine.Info(GetOwner(app), "本命令只支持 Part 文档。\n请先打开包含顶针的 Part 文件。");
                    return true;
                }

                Mdlr.IModelContainer container = doc as Mdlr.IModelContainer;
                if (container == null)
                {
                    FlattenEngine.Info(GetOwner(app), "当前文档不是可编辑的零件模型。");
                    return true;
                }

                Mdlr.IModel model = container.Model;
                Mdlr.IMdlrModel mdlr = model as Mdlr.IMdlrModel;
                if (model == null || mdlr == null)
                {
                    FlattenEngine.Info(GetOwner(app), "无法获取零件模型。");
                    return true;
                }

                FlattenSession session = new FlattenSession();
                session.App = app;
                session.Doc = doc;
                session.Model = model;
                session.Modeler = mdlr;
                session.Axis = HeightDirection.WorldZPlus;
                session.LoadInitial();

                if (session.Auto)
                {
                    FlattenEngine.Log("Command start. auto mode. pins=" + session.AutoPins.Count +
                        ", error=" + (session.AutoError ?? ""));
                }
                else
                {
                    FlattenEngine.Log("Command start. selected faces=" + session.Faces.Count +
                        ", ignored=" + session.IgnoredNonFace);
                }

                IWin32Window owner = GetOwner(app);
                PinFlattenDialog dlg = new PinFlattenDialog(session, owner);
                dlg.FormClosed += delegate { OpenDialogs.Remove(dlg); };
                OpenDialogs.Add(dlg);
                if (owner != null) dlg.Show(owner);
                else dlg.Show();
                return true;
            }
            catch (Exception ex)
            {
                FlattenEngine.Log("OnCommand failed: " + ex);
                FlattenEngine.Info(null, "执行失败：" + ex.Message + "\n\n详情见日志：" + FlattenEngine.LogPath);
                return true;
            }
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
            catch (Exception ex)
            {
                FlattenEngine.Log("CimApplicationProvider failed: " + ex.Message);
            }

            try
            {
                interop.CimAppAccess.AppAccess access = new interop.CimAppAccess.AppAccess();
                return access.GetApplication() as CimApp.IApplication;
            }
            catch (Exception ex)
            {
                FlattenEngine.Log("AppAccess failed: " + ex.Message);
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
    }

    public enum HeightDirection
    {
        WorldXPlus = 0,
        WorldXMinus = 1,
        WorldYPlus = 2,
        WorldYMinus = 3,
        WorldZPlus = 4,
        WorldZMinus = 5,
        UcsXPlus = 6,
        UcsXMinus = 7,
        UcsYPlus = 8,
        UcsYMinus = 9,
        UcsZPlus = 10,
        UcsZMinus = 11
    }

    internal static class DirectionUtil
    {
        public static bool IsPlus(HeightDirection direction)
        {
            return ((int)direction % 2) == 0;
        }

        public static bool IsUcs(HeightDirection direction)
        {
            return (int)direction >= (int)HeightDirection.UcsXPlus;
        }

        public static int AxisIndex(HeightDirection direction)
        {
            return ((int)direction % 6) / 2;
        }

        public static string ToText(HeightDirection direction)
        {
            string reference = IsUcs(direction) ? "当前 UCS" : "世界";
            char axis = "XYZ"[AxisIndex(direction)];
            return reference + " " + (IsPlus(direction) ? "+" : "-") + axis;
        }
    }

    public sealed class DirectionFrame
    {
        public readonly HeightDirection Direction;
        public readonly int Axis;
        public readonly bool Plus;

        public DirectionFrame(HeightDirection direction)
        {
            Direction = direction;
            Axis = DirectionUtil.AxisIndex(direction);
            Plus = DirectionUtil.IsPlus(direction);
        }

        public Box3 ToLocalBox(Box3 ucsBox)
        {
            Box3 box = new Box3();
            if (Axis == 0)
            {
                box.MinX = ucsBox.MinY; box.MaxX = ucsBox.MaxY;
                box.MinY = ucsBox.MinZ; box.MaxY = ucsBox.MaxZ;
                box.MinZ = ucsBox.MinX; box.MaxZ = ucsBox.MaxX;
            }
            else if (Axis == 1)
            {
                box.MinX = ucsBox.MinX; box.MaxX = ucsBox.MaxX;
                box.MinY = ucsBox.MinZ; box.MaxY = ucsBox.MaxZ;
                box.MinZ = ucsBox.MinY; box.MaxZ = ucsBox.MaxY;
            }
            else
            {
                box.MinX = ucsBox.MinX; box.MaxX = ucsBox.MaxX;
                box.MinY = ucsBox.MinY; box.MaxY = ucsBox.MaxY;
                box.MinZ = ucsBox.MinZ; box.MaxZ = ucsBox.MaxZ;
            }
            return box;
        }

        public double[] LocalToUcs(double u, double v, double w)
        {
            if (Axis == 0) return new double[] { w, u, v };
            if (Axis == 1) return new double[] { u, w, v };
            return new double[] { u, v, w };
        }
    }

    public class Box3
    {
        public double MinX;
        public double MinY;
        public double MinZ;
        public double MaxX;
        public double MaxY;
        public double MaxZ;

        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public double Depth { get { return MaxZ - MinZ; } }
    }

    public class PinGroup
    {
        public List<Mdlr.ICimEntity> Faces = new List<Mdlr.ICimEntity>();
        public Box3 Box;
        public double TargetZ;
        public int Index;
        public bool DirectionMismatch;
    }

    public class FlattenPlan
    {
        public List<PinGroup> Groups = new List<PinGroup>();
        public HeightDirection Direction;
        public DirectionFrame Frame;
    }

    public class AutoPin
    {
        public int Index;
        public string Label;
        public Mdlr.ICimEntity Body;
        public readonly List<Mdlr.ICimEntity> TopFaces = new List<Mdlr.ICimEntity>();
        public HeightDirection Direction;
        public double Target;
        public int Axis;
        public bool Plus;
        public Box3 Box;
        public string Note;
        public string Error;
        public bool Skipped;
    }

    internal sealed class FlattenSession
    {
        public CimApp.IApplication App;
        public CimApp.ICimDocument Doc;
        public Mdlr.IModel Model;
        public Mdlr.IMdlrModel Modeler;
        public HeightDirection Axis = HeightDirection.WorldZPlus;
        public readonly List<Mdlr.ICimEntity> Faces = new List<Mdlr.ICimEntity>();
        public int IgnoredNonFace;
        public FlattenPlan Plan;
        public string Error;
        public bool Auto;
        public readonly List<AutoPin> AutoPins = new List<AutoPin>();
        public string AutoError;

        public void LoadInitial()
        {
            // 自动识别是默认模式：即使打开命令时还没选面，用户随后在对话框里框选
            // 顶面/顶针体再执行，也会走自动识别，不会落回默认的世界 +Z。
            Auto = true;
            ReloadAuto();
        }

        public bool CurrentSelectionCanAuto()
        {
            try
            {
                Mdlr.ISelection selection = Doc as Mdlr.ISelection;
                if (selection == null || selection.Selection == null) return false;
                foreach (object raw in selection.Selection)
                {
                    Mdlr.ICimEntity entity = raw as Mdlr.ICimEntity;
                    if (entity == null) continue;
                    if (entity.Type == Mdlr.EntityEnumType.cmBody ||
                        entity.Type == Mdlr.EntityEnumType.cmFace) return true;
                }
            }
            catch { }
            return false;
        }

        private bool SelectionHasSolidsOrFaces()
        {
            return CurrentSelectionCanAuto();
        }

        public void ReloadAuto()
        {
            AutoPins.Clear();
            AutoError = null;
            Plan = null;
            Error = null;

            try
            {
                Mdlr.ISelection selection = Doc as Mdlr.ISelection;
                if (selection == null)
                {
                    AutoError = "无法读取当前选择";
                    return;
                }

                List<AutoPin> pins;
                string analyzeError;
                if (FlattenEngine.AnalyzeSelection(Model, Modeler, selection.Selection, out pins, out analyzeError))
                {
                    AutoPins.AddRange(pins);
                }
                else
                {
                    AutoError = analyzeError;
                }
            }
            catch (Exception ex)
            {
                AutoError = ex.Message;
                FlattenEngine.Log("ReloadAuto failed: " + ex);
            }
        }

        public void ReloadSelection()
        {
            Faces.Clear();
            IgnoredNonFace = 0;
            Plan = null;
            Error = null;

            try
            {
                Mdlr.ISelection selection = Doc as Mdlr.ISelection;
                if (selection == null)
                {
                    Error = "无法读取 Cimatron 的当前选择。";
                    return;
                }

                int ignored;
                List<Mdlr.ICimEntity> faces = FlattenEngine.FilterFaces(selection.Selection, out ignored);
                Faces.AddRange(faces);
                IgnoredNonFace = ignored;
            }
            catch (Exception ex)
            {
                Error = "读取选择失败：" + ex.Message;
                FlattenEngine.Log("ReloadSelection failed: " + ex);
            }
        }

        public void Recalculate()
        {
            Plan = null;
            Error = null;

            if (Faces.Count == 0)
            {
                Error = "未选中任何面";
                return;
            }

            try
            {
                Plan = FlattenEngine.BuildPlan(Model, Modeler, Faces, Axis);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                FlattenEngine.Log("BuildPlan failed: " + ex);
            }
        }
    }

    public class FlattenResult
    {
        public int SuccessCount;
        public int FailCount;
        public int SkippedCount;
        public List<string> Lines = new List<string>();

        public void Add(string line)
        {
            Lines.Add(line);
            FlattenEngine.Log(line);
        }
    }

    public static class FlattenEngine
    {
        private const double FlatTolerance = 0.001;

        public static string LogPath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NcNamer"),
                    "pin_flatten_log.txt");
            }
        }

        public static void Log(string message)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    new UTF8Encoding(true));
            }
            catch { }
        }

        public static void Info(IWin32Window owner, string text)
        {
            try
            {
                if (owner != null)
                    MessageBox.Show(owner, text, "IKUN思美创工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(text, "IKUN思美创工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        public static void Error(IWin32Window owner, string text)
        {
            try
            {
                if (owner != null)
                    MessageBox.Show(owner, text, "IKUN思美创工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    MessageBox.Show(text, "IKUN思美创工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        public static void ShowResult(IWin32Window owner, FlattenResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("顶针顶面拉平完成");
            sb.AppendLine();
            sb.AppendLine("成功：" + result.SuccessCount + " 根");
            if (result.SkippedCount > 0) sb.AppendLine("跳过：" + result.SkippedCount + " 根");
            if (result.FailCount > 0) sb.AppendLine("失败：" + result.FailCount + " 根");
            sb.AppendLine();
            int max = result.Lines.Count > 14 ? 14 : result.Lines.Count;
            for (int i = 0; i < max; i++) sb.AppendLine(result.Lines[i]);
            if (result.Lines.Count > max) sb.AppendLine("...");
            sb.AppendLine();
            sb.Append("日志：" + LogPath);
            Info(owner, sb.ToString());
        }

        public static List<Mdlr.ICimEntity> FilterFaces(Mdlr.ICimEntityList selection, out int ignored)
        {
            List<Mdlr.ICimEntity> result = new List<Mdlr.ICimEntity>();
            ignored = 0;
            if (selection == null) return result;
            foreach (object raw in selection)
            {
                Mdlr.ICimEntity ent = raw as Mdlr.ICimEntity;
                if (ent == null)
                {
                    ignored++;
                    continue;
                }
                if (ent.Type == Mdlr.EntityEnumType.cmFace)
                    result.Add(ent);
                else
                    ignored++;
            }
            return result;
        }

        public static FlattenPlan BuildPlan(Mdlr.IModel model, Mdlr.IMdlrModel mdlr,
            List<Mdlr.ICimEntity> faces, HeightDirection direction)
        {
            Mdlr.IUcs ucs = ResolveUcs(model, mdlr, direction);
            if (ucs == null) throw new InvalidOperationException("无法取得坐标系统（世界 UCS / 当前 UCS）");

            FlattenPlan plan = new FlattenPlan();
            plan.Direction = direction;
            plan.Frame = new DirectionFrame(direction);
            plan.Groups = GroupFaces(mdlr, faces, ucs, plan.Frame);

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                PinGroup group = plan.Groups[i];
                group.Index = i + 1;
                group.Box = plan.Frame.ToLocalBox(GetBox(mdlr, group.Faces, ucs));
                group.TargetZ = plan.Frame.Plus ? group.Box.MaxZ : group.Box.MinZ;
                double crossSpan = Math.Max(group.Box.Width, group.Box.Height);
                group.DirectionMismatch = crossSpan > 1e-9 && group.Box.Depth > crossSpan * 0.95;
            }
            return plan;
        }

        public static FlattenResult Run(Mdlr.IModel model, Mdlr.IMdlrModel mdlr,
            List<Mdlr.ICimEntity> faces, HeightDirection direction)
        {
            return Run(model, mdlr, faces, direction, true);
        }

        public static FlattenResult Run(Mdlr.IModel model, Mdlr.IMdlrModel mdlr,
            List<Mdlr.ICimEntity> faces, HeightDirection direction, bool hideHelper)
        {
            FlattenResult result = new FlattenResult();
            FlattenPlan plan = BuildPlan(model, mdlr, faces, direction);

            result.Add("方向：" + DirectionUtil.ToText(direction));
            result.Add("选中面 " + faces.Count + " 个，识别顶针 " + plan.Groups.Count + " 根");

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                PinGroup group = plan.Groups[i];
                try
                {
                    if (group.DirectionMismatch)
                    {
                        result.FailCount++;
                        result.Add("顶针 " + group.Index + "：所选方向与顶面不匹配，请选择与顶针轴线一致的方向（当前 " +
                            DirectionUtil.ToText(direction) + "）");
                        continue;
                    }
                    if (group.Box.Depth <= FlatTolerance)
                    {
                        result.SkippedCount++;
                        result.Add("顶针 " + group.Index + "：顶面已经水平，跳过（坐标=" + Format(group.TargetZ) + "）");
                        continue;
                    }

                    double offset = Math.Min(0.01, group.Box.Depth * 0.1);
                    if (offset < 0.0001) offset = 0.0001;
                    double retryHeight = DirectionUtil.IsPlus(direction)
                        ? group.TargetZ - offset
                        : group.TargetZ + offset;
                    double[] heights = new double[] { group.TargetZ, retryHeight };
                    bool done = false;
                    string lastError = null;

                    for (int attempt = 0; attempt < heights.Length && !done; attempt++)
                    {
                        double height = heights[attempt];
                        Mdlr.IMdProcedure helperProc = null;
                        Mdlr.ICimEntity helperFace = null;
                        Mdlr.IMdProcedure replaceProc = null;

                        try
                        {
                            string label = "IKUN_顶针拉平面_" + group.Index +
                                (attempt > 0 ? "_r" + attempt : "");
                            CreateHelperFace(model, mdlr, direction, plan.Frame, group, height, label,
                                out helperProc, out helperFace);
                            if (helperFace == null)
                                throw new InvalidOperationException("未能取得拉平辅助面实体");

                            replaceProc = ReplaceFaces(mdlr, group.Faces, helperFace);

                            if (hideHelper) HideEntity(helperFace);
                            result.SuccessCount++;
                            result.Add("顶针 " + group.Index + "：拉平到 " + DirectionUtil.ToText(direction) +
                                " = " + Format(height) + (attempt > 0 ? "（避让偏移 " + Format(offset) + "）" : ""));
                            done = true;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex.Message;
                            Log("Pin " + group.Index + " attempt " + (attempt + 1) + " failed: " + ex);
                            if (replaceProc != null) TryDeleteProcedure(mdlr, replaceProc);
                            if (helperFace != null) HideEntity(helperFace);
                        }
                    }

                    if (!done)
                    {
                        result.FailCount++;
                        result.Add("顶针 " + group.Index + "：失败 - " + (lastError == null ? "未知原因" : lastError));
                    }
                }
                catch (Exception ex)
                {
                    result.FailCount++;
                    result.Add("顶针 " + group.Index + "：失败 - " + ex.Message);
                    Log("Pin " + group.Index + " failed: " + ex);
                }
            }

            return result;
        }

        private static List<PinGroup> GroupFaces(Mdlr.IMdlrModel mdlr,
            List<Mdlr.ICimEntity> faces, Mdlr.IUcs ucs, DirectionFrame frame)
        {
            int n = faces.Count;
            Box3[] boxes = new Box3[n];
            for (int i = 0; i < n; i++)
            {
                try { boxes[i] = frame.ToLocalBox(GetBox(mdlr, MakeFaceList(faces[i]), ucs)); }
                catch (Exception ex)
                {
                    Log("Face box failed for ID " + faces[i].ID + ": " + ex.Message);
                    boxes[i] = new Box3();
                }
            }

            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            double maxSpan = 0;
            for (int i = 0; i < n; i++)
            {
                maxSpan = Math.Max(maxSpan, Math.Max(boxes[i].Width, boxes[i].Height));
            }
            double tolerance = Math.Max(0.01, maxSpan * 0.001);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (Overlap3D(boxes[i], boxes[j], tolerance)) Union(parent, i, j);
                }
            }

            Dictionary<int, PinGroup> map = new Dictionary<int, PinGroup>();
            List<int> order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                PinGroup group;
                if (!map.TryGetValue(root, out group))
                {
                    group = new PinGroup();
                    map[root] = group;
                    order.Add(root);
                }
                group.Faces.Add(faces[i]);
            }

            List<PinGroup> result = new List<PinGroup>();
            for (int i = 0; i < order.Count; i++) result.Add(map[order[i]]);
            return result;
        }

        private sealed class FaceBox
        {
            public Mdlr.ICimEntity Entity;
            public Box3 Box;
        }

        public static bool AnalyzeSelection(Mdlr.IModel model, Mdlr.IMdlrModel mdlr,
            Mdlr.ICimEntityList selection, out List<AutoPin> pins, out string error)
        {
            pins = new List<AutoPin>();
            error = null;
            if (selection == null)
            {
                error = "没有选择";
                return false;
            }

            List<Mdlr.ICimEntity> bodies = new List<Mdlr.ICimEntity>();
            List<Mdlr.ICimEntity> selectedFaces = new List<Mdlr.ICimEntity>();
            foreach (object raw in selection)
            {
                Mdlr.ICimEntity entity = raw as Mdlr.ICimEntity;
                if (entity == null) continue;
                if (entity.Type == Mdlr.EntityEnumType.cmBody) bodies.Add(entity);
                else if (entity.Type == Mdlr.EntityEnumType.cmFace) selectedFaces.Add(entity);
            }

            if (bodies.Count == 0 && selectedFaces.Count == 0)
            {
                error = "请先框选顶针体（或在面过滤器下框选整个顶针）";
                return false;
            }

            Mdlr.IUcs worldUcs = GetWorldUcs(model);
            if (worldUcs == null) Log("Auto: world UCS unavailable; using Geometry.Box");

            if (bodies.Count > 0)
            {
                List<FaceBox> allFaces = QueryAllFaceBoxes(mdlr, worldUcs);
                for (int i = 0; i < bodies.Count; i++)
                {
                    AutoPin pin = new AutoPin();
                    pin.Index = pins.Count + 1;
                    pin.Body = bodies[i];
                    pin.Label = "体 " + bodies[i].ID;
                    pin.Box = GetBodyBox(bodies[i]);
                    if (pin.Box == null)
                    {
                        pin.Error = "无法取得实体包围盒";
                        pins.Add(pin);
                        continue;
                    }

                    double tol = Math.Max(0.01,
                        Math.Max(pin.Box.Width, Math.Max(pin.Box.Height, pin.Box.Depth)) * 0.01);
                    List<FaceBox> bodyFaces = new List<FaceBox>();
                    for (int j = 0; j < allFaces.Count; j++)
                    {
                        if (InsideBox(allFaces[j].Box, pin.Box, tol)) bodyFaces.Add(allFaces[j]);
                    }

                    AnalyzePin(pin, bodyFaces);
                    pins.Add(pin);
                }
            }
            else
            {
                List<FaceBox> selectedBoxes = new List<FaceBox>();
                for (int i = 0; i < selectedFaces.Count; i++)
                {
                    Box3 box = GetFaceBox(mdlr, selectedFaces[i], worldUcs);
                    if (box == null) continue;
                    FaceBox fb = new FaceBox();
                    fb.Entity = selectedFaces[i];
                    fb.Box = box;
                    selectedBoxes.Add(fb);
                }

                List<List<FaceBox>> groups = GroupFaceBoxes(selectedBoxes);

                List<Mdlr.ICimEntity> allBodies = new List<Mdlr.ICimEntity>();
                List<Box3> allBodyBoxes = new List<Box3>();
                try
                {
                    Mdlr.ICimEntityList bodyList = QueryAllBodies(mdlr);
                    foreach (object raw in bodyList)
                    {
                        Mdlr.ICimEntity body = raw as Mdlr.ICimEntity;
                        if (body == null) continue;
                        Box3 bodyBox = GetBodyBox(body);
                        if (bodyBox == null) continue;
                        allBodies.Add(body);
                        allBodyBoxes.Add(bodyBox);
                    }
                }
                catch (Exception ex)
                {
                    Log("Auto: query bodies failed: " + ex.Message);
                }

                List<FaceBox> allFaces = QueryAllFaceBoxes(mdlr, worldUcs);

                for (int i = 0; i < groups.Count; i++)
                {
                    AutoPin pin = new AutoPin();
                    pin.Index = i + 1;
                    Box3 groupBox = null;
                    for (int j = 0; j < groups[i].Count; j++)
                    {
                        groupBox = UnionBox(groupBox, groups[i][j].Box);
                    }

                    Mdlr.ICimEntity owner = null;
                    Box3 ownerBox = null;
                    double bestDim = double.MaxValue;
                    for (int j = 0; j < allBodies.Count; j++)
                    {
                        Box3 bodyBox = allBodyBoxes[j];
                        double tol = Math.Max(0.01, MaxDim(bodyBox) * 0.01);
                        if (!InsideBox(groupBox, bodyBox, tol)) continue;
                        double dim = MaxDim(bodyBox);
                        if (dim < bestDim)
                        {
                            bestDim = dim;
                            owner = allBodies[j];
                            ownerBox = bodyBox;
                        }
                    }

                    pin.Body = owner;
                    pin.Label = owner != null ? "体 " + owner.ID : "面组 " + (i + 1);
                    pin.Box = ownerBox != null ? ownerBox : groupBox;

                    bool analyzed = false;
                    if (ownerBox != null)
                    {
                        double tol = Math.Max(0.01, MaxDim(ownerBox) * 0.01);
                        List<FaceBox> bodyFaces = new List<FaceBox>();
                        for (int j = 0; j < allFaces.Count; j++)
                        {
                            if (InsideBox(allFaces[j].Box, ownerBox, tol)) bodyFaces.Add(allFaces[j]);
                        }
                        if (bodyFaces.Count > 0)
                        {
                            AnalyzePin(pin, bodyFaces);
                            analyzed = true;
                        }
                    }

                    if (!analyzed) AnalyzePin(pin, groups[i]);
                    pins.Add(pin);
                }
            }

            if (pins.Count == 0)
            {
                error = "未识别到顶针";
                return false;
            }
            return true;
        }

        private static List<FaceBox> QueryAllFaceBoxes(Mdlr.IMdlrModel mdlr, Mdlr.IUcs ucs)
        {
            List<FaceBox> result = new List<FaceBox>();
            Mdlr.ICimEntityList faces = QueryAllFaces(mdlr);
            foreach (object raw in faces)
            {
                Mdlr.ICimEntity face = raw as Mdlr.ICimEntity;
                if (face == null) continue;
                Box3 box = GetFaceBox(mdlr, face, ucs);
                if (box == null) continue;
                FaceBox fb = new FaceBox();
                fb.Entity = face;
                fb.Box = box;
                result.Add(fb);
            }
            return result;
        }

        private static Box3 GetFaceBox(Mdlr.IMdlrModel mdlr, Mdlr.ICimEntity face, Mdlr.IUcs ucs)
        {
            try
            {
                if (ucs != null) return GetBox(mdlr, MakeFaceList(face), ucs);
                return ParseBox(ToDoubleArray(face.Geometry.Box));
            }
            catch (Exception ex)
            {
                Log("Auto face box failed " + face.ID + ": " + ex.Message);
                return null;
            }
        }

        private static Box3 GetBodyBox(Mdlr.ICimEntity body)
        {
            try { return ParseBox(ToDoubleArray(body.Geometry.Box)); }
            catch (Exception ex)
            {
                Log("Auto body box failed " + body.ID + ": " + ex.Message);
                return null;
            }
        }

        private static bool InsideBox(Box3 inner, Box3 outer, double tol)
        {
            return inner.MinX >= outer.MinX - tol && inner.MaxX <= outer.MaxX + tol
                && inner.MinY >= outer.MinY - tol && inner.MaxY <= outer.MaxY + tol
                && inner.MinZ >= outer.MinZ - tol && inner.MaxZ <= outer.MaxZ + tol;
        }

        private static List<List<FaceBox>> GroupFaceBoxes(List<FaceBox> boxes)
        {
            List<List<FaceBox>> result = new List<List<FaceBox>>();
            int n = boxes.Count;
            if (n == 0) return result;

            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double tol = 0.001 * Math.Max(MaxDim(boxes[i].Box), MaxDim(boxes[j].Box));
                    if (BoxesTouch3D(boxes[i].Box, boxes[j].Box, tol)) Union(parent, i, j);
                }
            }

            Dictionary<int, List<FaceBox>> map = new Dictionary<int, List<FaceBox>>();
            List<int> order = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                List<FaceBox> group;
                if (!map.TryGetValue(root, out group))
                {
                    group = new List<FaceBox>();
                    map[root] = group;
                    order.Add(root);
                }
                group.Add(boxes[i]);
            }

            for (int i = 0; i < order.Count; i++) result.Add(map[order[i]]);
            return result;
        }

        private static bool BoxesTouch3D(Box3 a, Box3 b, double tol)
        {
            return a.MaxX >= b.MinX - tol && b.MaxX >= a.MinX - tol
                && a.MaxY >= b.MinY - tol && b.MaxY >= a.MinY - tol
                && a.MaxZ >= b.MinZ - tol && b.MaxZ >= a.MinZ - tol;
        }

        private static Box3 UnionBox(Box3 a, Box3 b)
        {
            if (a == null) return b;
            if (b == null) return a;
            Box3 box = new Box3();
            box.MinX = Math.Min(a.MinX, b.MinX);
            box.MinY = Math.Min(a.MinY, b.MinY);
            box.MinZ = Math.Min(a.MinZ, b.MinZ);
            box.MaxX = Math.Max(a.MaxX, b.MaxX);
            box.MaxY = Math.Max(a.MaxY, b.MaxY);
            box.MaxZ = Math.Max(a.MaxZ, b.MaxZ);
            return box;
        }

        private static double MaxDim(Box3 box)
        {
            return Math.Max(box.Width, Math.Max(box.Height, box.Depth));
        }

        private static void AnalyzePin(AutoPin pin, List<FaceBox> faces)
        {
            if (pin.Box == null)
            {
                pin.Error = "无法取得包围盒";
                return;
            }
            if (faces == null || faces.Count == 0)
            {
                pin.Error = "没有可用的实体面";
                return;
            }

            Box3 box = pin.Box;
            double ex = box.Width;
            double ey = box.Height;
            double ez = box.Depth;
            int axis;
            double length;
            if (ex >= ey && ex >= ez) { axis = 0; length = ex; }
            else if (ey >= ez) { axis = 1; length = ey; }
            else { axis = 2; length = ez; }

            if (length <= 1e-6)
            {
                pin.Error = "实体尺寸无效";
                return;
            }

            double bodyMin = AxisMin(box, axis);
            double bodyMax = AxisMax(box, axis);
            double crossArea = axis == 0 ? box.Height * box.Depth :
                (axis == 1 ? box.Width * box.Depth : box.Width * box.Height);
            double flatTol = Math.Max(0.001, length * 0.01);
            double posTol = Math.Max(0.01, length * 0.001);
            double minArea = Math.Max(0.001, crossArea * 0.05);

            bool flatMin = false;
            bool flatMax = false;
            for (int i = 0; i < faces.Count; i++)
            {
                Box3 fb = faces[i].Box;
                double aMin = AxisMin(fb, axis);
                double aMax = AxisMax(fb, axis);
                double thickness = aMax - aMin;
                double area = ProjectedArea(fb, axis);
                if (thickness > flatTol || area < minArea) continue;
                if (Math.Abs(aMin - bodyMin) <= posTol && Math.Abs(aMax - bodyMin) <= posTol) flatMin = true;
                if (Math.Abs(aMax - bodyMax) <= posTol && Math.Abs(aMin - bodyMax) <= posTol) flatMax = true;
            }

            pin.Axis = axis;
            if (flatMin && flatMax)
            {
                pin.Skipped = true;
                pin.Note = "两端都是平面，跳过";
                return;
            }
            if (!flatMin && !flatMax)
            {
                pin.Error = "未找到平面端，无法判断顶面方向";
                return;
            }

            bool plus = flatMin;
            pin.Plus = plus;
            pin.Direction = WorldDirection(axis, plus);
            pin.Target = plus ? bodyMax : bodyMin;

            double topDepth = Math.Max(flatTol * 2, length * 0.2);
            List<FaceBox> candidates = new List<FaceBox>();
            for (int i = 0; i < faces.Count; i++)
            {
                Box3 fb = faces[i].Box;
                double aMin = AxisMin(fb, axis);
                double aMax = AxisMax(fb, axis);
                if (plus)
                {
                    if (aMin >= bodyMax - topDepth) candidates.Add(faces[i]);
                }
                else
                {
                    if (aMax <= bodyMin + topDepth) candidates.Add(faces[i]);
                }
            }

            if (candidates.Count == 0)
            {
                for (int i = 0; i < faces.Count; i++)
                {
                    Box3 fb = faces[i].Box;
                    double aMin = AxisMin(fb, axis);
                    double aMax = AxisMax(fb, axis);
                    if (plus)
                    {
                        if (aMax >= bodyMax - posTol) candidates.Add(faces[i]);
                    }
                    else
                    {
                        if (aMin <= bodyMin + posTol) candidates.Add(faces[i]);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                pin.Error = "未找到顶面";
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                pin.TopFaces.Add(candidates[i].Entity);
            }

            pin.Note = "自动识别，轴 " + "XYZ"[axis] + (plus ? "+" : "-");
        }

        private static double AxisMin(Box3 box, int axis)
        {
            if (axis == 0) return box.MinX;
            if (axis == 1) return box.MinY;
            return box.MinZ;
        }

        private static double AxisMax(Box3 box, int axis)
        {
            if (axis == 0) return box.MaxX;
            if (axis == 1) return box.MaxY;
            return box.MaxZ;
        }

        private static double ProjectedArea(Box3 box, int axis)
        {
            if (axis == 0) return Math.Max(0, box.Height) * Math.Max(0, box.Depth);
            if (axis == 1) return Math.Max(0, box.Width) * Math.Max(0, box.Depth);
            return Math.Max(0, box.Width) * Math.Max(0, box.Height);
        }

        private static HeightDirection WorldDirection(int axis, bool plus)
        {
            if (axis == 0) return plus ? HeightDirection.WorldXPlus : HeightDirection.WorldXMinus;
            if (axis == 1) return plus ? HeightDirection.WorldYPlus : HeightDirection.WorldYMinus;
            return plus ? HeightDirection.WorldZPlus : HeightDirection.WorldZMinus;
        }

        private static bool Overlap3D(Box3 a, Box3 b, double tolerance)
        {
            if (a.MaxX < b.MinX - tolerance || b.MaxX < a.MinX - tolerance) return false;
            if (a.MaxY < b.MinY - tolerance || b.MaxY < a.MinY - tolerance) return false;

            double zOverlap = Math.Min(a.MaxZ, b.MaxZ) - Math.Max(a.MinZ, b.MinZ);
            return zOverlap >= -tolerance;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

        private static Mdlr.IUcs GetWorldUcs(Mdlr.IModel model)
        {
            try
            {
                Mdlr.ICimEntity entity = model.GetEntityById(2, model);
                return entity as Mdlr.IUcs;
            }
            catch (Exception ex)
            {
                Log("GetWorldUcs failed: " + ex.Message);
                return null;
            }
        }

        private static Mdlr.IUcs ResolveUcs(Mdlr.IModel model, Mdlr.IMdlrModel mdlr, HeightDirection direction)
        {
            if (DirectionUtil.IsUcs(direction))
            {
                try
                {
                    Mdlr.IUcs active = mdlr.ActivateUcs;
                    if (active != null) return active;
                }
                catch (Exception ex) { Log("ActivateUcs failed: " + ex.Message); }
                return null;
            }

            try
            {
                Mdlr.ICimEntity ent = model.GetEntityById(2, model);
                Mdlr.IUcs ucs = ent as Mdlr.IUcs;
                if (ucs != null) return ucs;
            }
            catch (Exception ex) { Log("World UCS lookup failed: " + ex.Message); }

            try { return mdlr.ActivateUcs; }
            catch { return null; }
        }

        private static Box3 GetBox(Mdlr.IMdlrModel mdlr, List<Mdlr.ICimEntity> faces, Mdlr.IUcs ucs)
        {
            return GetBox(mdlr, MakeFaceList(faces), ucs);
        }

        private static Box3 GetBox(Mdlr.IMdlrModel mdlr, Mdlr.ICimEntityList faces, Mdlr.IUcs ucs)
        {
            try
            {
                Mdlr.IGeomServices geom = mdlr.GetGeomServicesObj();
                double[] arr = ToDoubleArray(geom.GetBoundingBoxByUCS(faces, ucs));
                Box3 box = ParseBox(arr);
                if (box != null) return box;
            }
            catch (Exception ex)
            {
                Log("GetBoundingBoxByUCS failed: " + ex.Message);
            }

            Box3 fallback = new Box3();
            bool hasValue = false;
            foreach (object raw in faces)
            {
                Mdlr.ICimEntity face = raw as Mdlr.ICimEntity;
                if (face == null) continue;
                try
                {
                    double[] arr = ToDoubleArray(face.Geometry.Box);
                    Box3 one = ParseBox(arr);
                    if (one == null) continue;
                    if (!hasValue)
                    {
                        fallback = one;
                        hasValue = true;
                    }
                    else
                    {
                        fallback.MinX = Math.Min(fallback.MinX, one.MinX);
                        fallback.MinY = Math.Min(fallback.MinY, one.MinY);
                        fallback.MinZ = Math.Min(fallback.MinZ, one.MinZ);
                        fallback.MaxX = Math.Max(fallback.MaxX, one.MaxX);
                        fallback.MaxY = Math.Max(fallback.MaxY, one.MaxY);
                        fallback.MaxZ = Math.Max(fallback.MaxZ, one.MaxZ);
                    }
                }
                catch (Exception ex)
                {
                    Log("Face geometry box failed: " + ex.Message);
                }
            }
            if (!hasValue) throw new InvalidOperationException("无法取得面的包围盒");
            return fallback;
        }

        private static Box3 ParseBox(double[] arr)
        {
            if (arr == null || arr.Length < 3) return null;

            Box3 box = new Box3();
            if (arr.Length == 6)
            {
                box.MinX = Math.Min(arr[0], arr[3]);
                box.MaxX = Math.Max(arr[0], arr[3]);
                box.MinY = Math.Min(arr[1], arr[4]);
                box.MaxY = Math.Max(arr[1], arr[4]);
                box.MinZ = Math.Min(arr[2], arr[5]);
                box.MaxZ = Math.Max(arr[2], arr[5]);
                return box;
            }

            bool first = true;
            for (int i = 0; i + 2 < arr.Length; i += 3)
            {
                double x = arr[i];
                double y = arr[i + 1];
                double z = arr[i + 2];
                if (first)
                {
                    box.MinX = box.MaxX = x;
                    box.MinY = box.MaxY = y;
                    box.MinZ = box.MaxZ = z;
                    first = false;
                }
                else
                {
                    box.MinX = Math.Min(box.MinX, x);
                    box.MaxX = Math.Max(box.MaxX, x);
                    box.MinY = Math.Min(box.MinY, y);
                    box.MaxY = Math.Max(box.MaxY, y);
                    box.MinZ = Math.Min(box.MinZ, z);
                    box.MaxZ = Math.Max(box.MaxZ, z);
                }
            }
            return first ? null : box;
        }

        private static double[] ToDoubleArray(object raw)
        {
            if (raw == null) return null;
            double[] direct = raw as double[];
            if (direct != null) return direct;

            Array array = raw as Array;
            if (array != null)
            {
                List<double> values = new List<double>();
                foreach (object item in array) values.Add(Convert.ToDouble(item));
                return values.ToArray();
            }

            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable != null)
            {
                List<double> values = new List<double>();
                foreach (object item in enumerable) values.Add(Convert.ToDouble(item));
                return values.ToArray();
            }

            return null;
        }

        private static void CreateHelperFace(Mdlr.IModel model, Mdlr.IMdlrModel mdlr,
            HeightDirection direction, DirectionFrame frame, PinGroup group, double height, string name,
            out Mdlr.IMdProcedure procedure, out Mdlr.ICimEntity faceEntity)
        {
            procedure = null;
            faceEntity = null;

            double margin = Math.Max(0.05, Math.Max(group.Box.Width, group.Box.Height) * 0.01);
            double u0 = group.Box.MinX - margin;
            double u1 = group.Box.MaxX + margin;
            double v0 = group.Box.MinY - margin;
            double v1 = group.Box.MaxY + margin;

            List<double> local = new List<double>(48);
            for (int j = 0; j < 4; j++)
            {
                double v = v0 + (v1 - v0) * j / 3.0;
                for (int i = 0; i < 4; i++)
                {
                    double u = u0 + (u1 - u0) * i / 3.0;
                    local.Add(u);
                    local.Add(v);
                    local.Add(height);
                }
            }

            List<double> ucsPoints = new List<double>(48);
            for (int i = 0; i + 2 < local.Count; i += 3)
            {
                double[] p = frame.LocalToUcs(local[i], local[i + 1], local[i + 2]);
                ucsPoints.Add(p[0]);
                ucsPoints.Add(p[1]);
                ucsPoints.Add(p[2]);
            }

            double[] controlPoints;
            if (DirectionUtil.IsUcs(direction))
            {
                List<double> modelPoints = new List<double>(48);
                for (int i = 0; i + 2 < ucsPoints.Count; i += 3)
                {
                    double[] p = TranslateActiveToModel(model, ucsPoints[i], ucsPoints[i + 1], ucsPoints[i + 2]);
                    modelPoints.Add(p[0]);
                    modelPoints.Add(p[1]);
                    modelPoints.Add(p[2]);
                }
                controlPoints = modelPoints.ToArray();
            }
            else
            {
                controlPoints = ucsPoints.ToArray();
            }

            Mdlr.IMdProcedure proc = mdlr.CreateProcedure(Mdlr.MdProcedureType.cmGeneralFaceProcedure);
            Mdlr.IMdGeneralFace gf = proc as Mdlr.IMdGeneralFace;
            if (gf == null) throw new InvalidOperationException("无法获取 IMdGeneralFace 接口");

            gf.ControlPtsArray = controlPoints;
            gf.NumControlPtsU = 4;
            gf.NumControlPtsV = 4;
            gf.DegreeU = 3;
            gf.DegreeV = 3;
            gf.KnotsUArray = new double[] { 0, 0, 0, 0, 1, 1, 1, 1 };
            gf.KnotsVArray = new double[] { 0, 0, 0, 0, 1, 1, 1, 1 };
            gf.UStart = 0;
            gf.UEnd = 1;
            gf.VStart = 0;
            gf.VEnd = 1;

            try { proc.Name = name; } catch { }

            HashSet<int> beforeIds = SnapshotFaceIds(mdlr);
            ExecuteProcedure(proc);

            List<Mdlr.ICimEntity> created = FindNewFaces(mdlr, beforeIds);
            if (created != null && created.Count > 0) faceEntity = created[0];

            procedure = proc;
            Log("Helper face created for pin " + group.Index + ": " + name +
                ", face=" + (faceEntity == null ? "null" : faceEntity.ID.ToString()));
        }

        private static double[] TranslateActiveToModel(Mdlr.IModel model, double x, double y, double z)
        {
            object raw = model.Active2ModelTranslate(new double[] { x, y, z });
            double[] p = ToDoubleArray(raw);
            if (p == null || p.Length < 3)
                throw new InvalidOperationException("Active2ModelTranslate 返回无效坐标");
            return new double[] { p[0], p[1], p[2] };
        }

        private static Mdlr.IMdProcedure ReplaceFaces(Mdlr.IMdlrModel mdlr,
            List<Mdlr.ICimEntity> sourceFaces, Mdlr.ICimEntity targetFace)
        {
            Mdlr.IMdProcedure proc = mdlr.CreateProcedure(Mdlr.MdProcedureType.cmReplaceFacesProcedure);

            Mdlr.IMdReplaceFaces rf = proc as Mdlr.IMdReplaceFaces;
            if (rf == null)
            {
                TryDeleteProcedure(mdlr, proc);
                throw new InvalidOperationException("无法获取 IMdReplaceFaces 接口");
            }

            rf.SourceEntities = MakeFaceList(sourceFaces);
            rf.TargetEntities = MakeFaceList(targetFace);

            try
            {
                ExecuteProcedure(proc);
            }
            catch
            {
                TryDeleteProcedure(mdlr, proc);
                throw;
            }
            return proc;
        }

        private static HashSet<int> SnapshotFaceIds(Mdlr.IMdlrModel mdlr)
        {
            HashSet<int> ids = new HashSet<int>();
            try
            {
                Mdlr.ICimEntityList list = QueryAllFaces(mdlr);
                foreach (object raw in list)
                {
                    Mdlr.ICimEntity ent = raw as Mdlr.ICimEntity;
                    if (ent != null) ids.Add(ent.ID);
                }
            }
            catch (Exception ex)
            {
                Log("Snapshot faces failed: " + ex.Message);
            }
            return ids;
        }

        private static List<Mdlr.ICimEntity> FindNewFaces(Mdlr.IMdlrModel mdlr, HashSet<int> beforeIds)
        {
            List<Mdlr.ICimEntity> result = new List<Mdlr.ICimEntity>();
            try
            {
                Mdlr.ICimEntityList list = QueryAllFaces(mdlr);
                foreach (object raw in list)
                {
                    Mdlr.ICimEntity ent = raw as Mdlr.ICimEntity;
                    if (ent == null) continue;
                    if (!beforeIds.Contains(ent.ID)) result.Add(ent);
                }
            }
            catch (Exception ex)
            {
                Log("Find new faces failed: " + ex.Message);
            }
            return result;
        }

        private static Mdlr.ICimEntityList QueryAllBodies(Mdlr.IMdlrModel mdlr)
        {
            Mdlr.IEntityQuery query = (Mdlr.IEntityQuery)mdlr;
            Mdlr.IEntityFilter filter = query.CreateFilter(Mdlr.EFilterEnumType.cmFilterEntityType);
            Base.FilterType typeFilter = (Base.FilterType)filter;
            typeFilter.Add(Base.EntityEnumType.cmBody);
            query.SetFilter((Mdlr.IEntityFilter)typeFilter);
            return query.Select();
        }

        private static Mdlr.ICimEntityList QueryAllFaces(Mdlr.IMdlrModel mdlr)
        {
            Mdlr.IEntityQuery query = (Mdlr.IEntityQuery)mdlr;
            Mdlr.IEntityFilter filter = query.CreateFilter(Mdlr.EFilterEnumType.cmFilterEntityType);
            Base.FilterType typeFilter = (Base.FilterType)filter;
            typeFilter.Add(Base.EntityEnumType.cmFace);
            query.SetFilter((Mdlr.IEntityFilter)typeFilter);
            return query.Select();
        }

        private static Mdlr.ICimEntityList MakeFaceList(Mdlr.ICimEntity face)
        {
            Base.CimEntityList list = new Base.CimEntityList();
            list.Add((Base.ICimEntity)face);
            return (Mdlr.ICimEntityList)list;
        }

        private static Mdlr.ICimEntityList MakeFaceList(List<Mdlr.ICimEntity> faces)
        {
            Base.CimEntityList list = new Base.CimEntityList();
            for (int i = 0; i < faces.Count; i++)
                list.Add((Base.ICimEntity)faces[i]);
            return (Mdlr.ICimEntityList)list;
        }

        private static void ExecuteProcedure(Mdlr.IMdProcedure proc)
        {
            int attempts = 5;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    proc.Execute();
                    return;
                }
                catch (Exception ex)
                {
                    if (i < attempts - 1 && IsBusy(ex.Message))
                    {
                        Log("Cimatron busy, retry " + (i + 1) + ": " + ex.Message);
                        Thread.Sleep(300);
                        continue;
                    }
                    throw;
                }
            }
        }

        private static bool IsBusy(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.IndexOf("非空闲", StringComparison.Ordinal) >= 0
                || message.IndexOf("not idle", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("busy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void HideEntity(Mdlr.ICimEntity entity)
        {
            try { if (entity != null) entity.Show = 0; }
            catch (Exception ex) { Log("Hide helper failed: " + ex.Message); }
        }

        private static void TryDeleteProcedure(Mdlr.IMdlrModel mdlr, Mdlr.IMdProcedure proc)
        {
            try { if (proc != null) mdlr.DeleteProcedure(proc); }
            catch (Exception ex) { Log("Delete procedure failed: " + ex.Message); }
        }

        private static string Format(double value)
        {
            return value.ToString("0.####");
        }

        public static void EnsureNativeIcon()
        {
            try
            {
                string programDir = Path.GetDirectoryName(typeof(EjectorPinFlattenPlugin).Assembly.Location) ?? "";
                string sourceIco = Path.Combine(programDir, "EjectorPin_icon64.ico");
                if (!File.Exists(sourceIco))
                {
                    string alt = Path.Combine(programDir, "IKUNtools.ico");
                    if (File.Exists(alt)) sourceIco = alt;
                }
                if (!File.Exists(sourceIco))
                {
                    Log("Icon source missing: " + sourceIco);
                    return;
                }

                int commandId = CalculateApiCommandId(typeof(EjectorPinFlattenPlugin).FullName);
                string fileName = commandId + "_IKUNtools.ico";

                List<string> dirs = new List<string>();
                try
                {
                    string versionDir = Path.GetDirectoryName(programDir.TrimEnd('\\'));
                    string version = Path.GetFileName(versionDir);
                    if (!string.IsNullOrEmpty(version))
                    {
                        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                        dirs.Add(Path.Combine(common, "Cimatron", "Cimatron", version, "Data", "Images", "Commands"));
                    }
                }
                catch { }

                try
                {
                    string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    foreach (string root in new string[]
                    {
                        Path.Combine(common, "Cimatron", "Cimatron"),
                        Path.Combine(common, "Cimatron")
                    })
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (string ver in Directory.GetDirectories(root))
                        {
                            string dir = Path.Combine(ver, "Data", "Images", "Commands");
                            if (Directory.Exists(dir) && !dirs.Contains(dir)) dirs.Add(dir);
                        }
                    }
                }
                catch { }

                foreach (string dir in dirs)
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        string target = Path.Combine(dir, fileName);
                        if (File.Exists(target) && SameContent(sourceIco, target)) continue;
                        File.Copy(sourceIco, target, true);
                        Log("Icon installed: " + target);
                    }
                    catch (Exception ex)
                    {
                        Log("Icon write failed " + dir + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Icon ensure failed: " + ex.Message);
            }
        }

        private static int CalculateApiCommandId(string command)
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
    }

    internal class PinFlattenDialog : Form
    {
        private readonly FlattenSession _session;
        private readonly IWin32Window _owner;
        private ComboBox _directionCombo;
        private CheckBox _autoCheck;
        private ListView _groupList;
        private Label _infoLabel;
        private Label _hintLabel;
        private Button _okButton;
        private bool _loading;

        public HeightDirection Direction
        {
            get { return (HeightDirection)_directionCombo.SelectedIndex; }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_NOACTIVATE：对话框不抢焦点，保证 Cimatron 里的框选不丢失
                cp.ExStyle |= 0x08000000;
                return cp;
            }
        }

        public PinFlattenDialog(FlattenSession session, IWin32Window owner)
        {
            _session = session;
            _owner = owner;

            Text = "顶针顶面拉平";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 430);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei", 9F);

            Label title = new Label();
            title.Text = "顶针顶面拉平";
            title.Font = new Font("Microsoft YaHei", 15F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(31, 78, 121);
            title.SetBounds(18, 14, 520, 32);
            Controls.Add(title);

            _infoLabel = new Label();
            _infoLabel.SetBounds(18, 52, 520, 20);
            _infoLabel.ForeColor = Color.DimGray;
            Controls.Add(_infoLabel);

            Label dirLabel = new Label();
            dirLabel.Text = "向上方向";
            dirLabel.Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold);
            dirLabel.SetBounds(18, 84, 80, 20);
            Controls.Add(dirLabel);

            _directionCombo = new ComboBox();
            _directionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _directionCombo.SetBounds(104, 80, 200, 26);
            _directionCombo.Items.Add("世界 +X");
            _directionCombo.Items.Add("世界 -X");
            _directionCombo.Items.Add("世界 +Y");
            _directionCombo.Items.Add("世界 -Y");
            _directionCombo.Items.Add("世界 +Z");
            _directionCombo.Items.Add("世界 -Z");
            _directionCombo.Items.Add("当前 UCS +X");
            _directionCombo.Items.Add("当前 UCS -X");
            _directionCombo.Items.Add("当前 UCS +Y");
            _directionCombo.Items.Add("当前 UCS -Y");
            _directionCombo.Items.Add("当前 UCS +Z");
            _directionCombo.Items.Add("当前 UCS -Z");
            _directionCombo.SelectedIndexChanged += delegate { OnDirectionChanged(); };
            Controls.Add(_directionCombo);

            _autoCheck = new CheckBox();
            _autoCheck.Text = "自动识别顶针（体或顶面）";
            _autoCheck.SetBounds(312, 80, 230, 24);
            _autoCheck.CheckedChanged += delegate { OnAutoChanged(); };
            Controls.Add(_autoCheck);

            _groupList = new ListView();
            _groupList.View = View.Details;
            _groupList.FullRowSelect = true;
            _groupList.GridLines = true;
            _groupList.SetBounds(18, 118, 520, 230);
            _groupList.Columns.Add("序号", 55, HorizontalAlignment.Left);
            _groupList.Columns.Add("面数", 60, HorizontalAlignment.Left);
            _groupList.Columns.Add("目标坐标", 150, HorizontalAlignment.Left);
            _groupList.Columns.Add("备注", 250, HorizontalAlignment.Left);
            Controls.Add(_groupList);

            _hintLabel = new Label();
            _hintLabel.SetBounds(18, 354, 520, 24);
            _hintLabel.ForeColor = Color.DimGray;
            Controls.Add(_hintLabel);

            Button refresh = new Button();
            refresh.Text = "重新读取";
            refresh.SetBounds(18, 388, 100, 30);
            refresh.Click += delegate { OnRefresh(); };
            Controls.Add(refresh);

            _okButton = new Button();
            _okButton.Text = "执行";
            _okButton.SetBounds(330, 388, 90, 30);
            _okButton.Click += delegate { OnExecute(); };
            Controls.Add(_okButton);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(430, 388, 90, 30);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = _okButton;
            CancelButton = cancel;

            _loading = true;
            _directionCombo.SelectedIndex = (int)_session.Axis;
            _autoCheck.Checked = _session.Auto;
            _directionCombo.Enabled = !_session.Auto;
            _loading = false;
            RefreshList();
        }

        private void OnAutoChanged()
        {
            if (_loading) return;
            _session.Auto = _autoCheck.Checked;
            _directionCombo.Enabled = !_session.Auto;
            if (_session.Auto)
            {
                _session.ReloadAuto();
            }
            else
            {
                _session.Axis = (HeightDirection)_directionCombo.SelectedIndex;
                _session.ReloadSelection();
                _session.Recalculate();
            }
            RefreshList();
        }

        private void OnDirectionChanged()
        {
            if (_loading || _session.Auto) return;
            _session.Axis = (HeightDirection)_directionCombo.SelectedIndex;
            _session.Recalculate();
            RefreshList();
        }

        private void OnRefresh()
        {
            if (_autoCheck.Checked)
            {
                _session.Auto = true;
                _session.ReloadAuto();
            }
            else
            {
                _session.Auto = false;
                _session.Axis = (HeightDirection)_directionCombo.SelectedIndex;
                _session.ReloadSelection();
                _session.Recalculate();
            }
            RefreshList();
        }

        private void OnExecute()
        {
            if (_autoCheck.Checked)
            {
                OnExecuteAuto();
                return;
            }

            try
            {
                _session.Axis = (HeightDirection)_directionCombo.SelectedIndex;
                List<Mdlr.ICimEntity> previous = new List<Mdlr.ICimEntity>(_session.Faces);
                _session.ReloadSelection();
                if (_session.Faces.Count == 0 && previous.Count > 0)
                {
                    _session.Faces.AddRange(previous);
                }
                _session.Recalculate();

                if (_session.Faces.Count == 0)
                {
                    if (_session.CurrentSelectionCanAuto())
                    {
                        SwitchToAutoAndExecute();
                        return;
                    }
                    FlattenEngine.Info(this,
                        "请先在模型窗口中框选顶针顶面，然后再点执行。\n\n" +
                        "提示：只选择顶针的顶面；不要选中型腔面或侧壁。");
                    return;
                }
                if (_session.Plan == null || _session.Plan.Groups.Count == 0)
                {
                    FlattenEngine.Error(this, string.IsNullOrEmpty(_session.Error)
                        ? "没有识别到有效顶针顶面。" : _session.Error);
                    return;
                }

                bool mismatch = false;
                for (int i = 0; i < _session.Plan.Groups.Count; i++)
                {
                    if (_session.Plan.Groups[i].DirectionMismatch) { mismatch = true; break; }
                }
                if (mismatch)
                {
                    if (_session.CurrentSelectionCanAuto())
                    {
                        SwitchToAutoAndExecute();
                        return;
                    }
                    FlattenEngine.Error(this,
                        "当前方向与所选顶面不匹配，直接替换会失败。\n\n" +
                        "请换一个方向，使顶面在该方向上的厚度最小；或重新框选顶针后勾选自动识别。\n\n" +
                        "当前方向：" + DirectionUtil.ToText(_session.Axis));
                    return;
                }

                FlattenResult result = FlattenEngine.Run(_session.Model, _session.Modeler, _session.Faces, _session.Axis);
                Close();
                FlattenEngine.ShowResult(_owner, result);
            }
            catch (Exception ex)
            {
                FlattenEngine.Log("Execute failed: " + ex);
                FlattenEngine.Error(this, "执行失败：" + ex.Message + "\n\n详情见日志：" + FlattenEngine.LogPath);
            }
        }

        private void SwitchToAutoAndExecute()
        {
            _loading = true;
            _autoCheck.Checked = true;
            _loading = false;
            _session.Auto = true;
            _directionCombo.Enabled = false;
            _session.ReloadAuto();

            int ready = 0;
            for (int i = 0; i < _session.AutoPins.Count; i++)
            {
                AutoPin pin = _session.AutoPins[i];
                if (!pin.Skipped && string.IsNullOrEmpty(pin.Error) && pin.TopFaces.Count > 0) ready++;
            }

            RefreshList();
            if (!string.IsNullOrEmpty(_session.AutoError) || ready == 0)
            {
                FlattenEngine.Error(this, string.IsNullOrEmpty(_session.AutoError)
                    ? "当前选择无法自动识别出可拉平的顶针。\n\n" +
                      "请框选整个顶针体，或只框选顶针顶面后再试；也可以取消自动识别，手动选择正确的轴线方向。"
                    : _session.AutoError);
                return;
            }
            OnExecuteAuto();
        }

        private void OnExecuteAuto()
        {
            try
            {
                _session.Auto = true;
                _session.ReloadAuto();
                if (!string.IsNullOrEmpty(_session.AutoError))
                {
                    FlattenEngine.Error(this, _session.AutoError);
                    return;
                }
                if (_session.AutoPins.Count == 0)
                {
                    FlattenEngine.Info(this, "未识别到顶针。请框选顶针体或顶针顶面后重试。");
                    return;
                }

                FlattenResult combined = new FlattenResult();
                for (int i = 0; i < _session.AutoPins.Count; i++)
                {
                    AutoPin pin = _session.AutoPins[i];
                    if (pin.Skipped)
                    {
                        combined.SkippedCount++;
                        combined.Add("顶针 " + pin.Index + "：跳过（" + pin.Note + "）");
                        continue;
                    }
                    if (!string.IsNullOrEmpty(pin.Error) || pin.TopFaces.Count == 0)
                    {
                        combined.FailCount++;
                        combined.Add("顶针 " + pin.Index + "：失败 - " +
                            (string.IsNullOrEmpty(pin.Error) ? "未找到顶面" : pin.Error));
                        continue;
                    }

                    FlattenResult r = FlattenEngine.Run(_session.Model, _session.Modeler, pin.TopFaces, pin.Direction);
                    if (r.SuccessCount > 0)
                    {
                        combined.SuccessCount++;
                        combined.Add("顶针 " + pin.Index + "：成功（" + DirectionUtil.ToText(pin.Direction) +
                            " = " + pin.Target.ToString("0.####") + "）");
                    }
                    else if (r.SkippedCount > 0)
                    {
                        combined.SkippedCount++;
                        combined.Add("顶针 " + pin.Index + "：跳过（替换面未执行）");
                    }
                    else
                    {
                        combined.FailCount++;
                        combined.Add("顶针 " + pin.Index + "：失败（见日志）");
                    }
                }

                Close();
                FlattenEngine.ShowResult(_owner, combined);
            }
            catch (Exception ex)
            {
                FlattenEngine.Log("Execute auto failed: " + ex);
                FlattenEngine.Error(this, "自动识别执行失败：" + ex.Message + "\n\n详情见日志：" + FlattenEngine.LogPath);
            }
        }

        private void RefreshList()
        {
            if (_autoCheck.Checked)
            {
                RefreshAutoList();
                return;
            }

            _groupList.BeginUpdate();
            try
            {
                _groupList.Items.Clear();
                if (_session.Plan != null)
                {
                    for (int i = 0; i < _session.Plan.Groups.Count; i++)
                    {
                        PinGroup group = _session.Plan.Groups[i];
                        ListViewItem item = new ListViewItem((i + 1).ToString());
                        item.SubItems.Add(group.Faces.Count.ToString());
                        item.SubItems.Add(group.TargetZ.ToString("0.####"));
                        item.SubItems.Add(group.DirectionMismatch
                            ? "方向与顶面不匹配" : (group.Faces.Count == 1 ? "单个顶面" : "同根顶针合并"));
                        _groupList.Items.Add(item);
                    }
                }
            }
            finally
            {
                _groupList.EndUpdate();
            }

            int groupCount = _session.Plan == null ? 0 : _session.Plan.Groups.Count;
            bool mismatch = false;
            if (_session.Plan != null)
            {
                for (int i = 0; i < _session.Plan.Groups.Count; i++)
                {
                    if (_session.Plan.Groups[i].DirectionMismatch) { mismatch = true; break; }
                }
            }
            string info = "已选面：" + _session.Faces.Count + "    识别顶针：" + groupCount;
            if (_session.IgnoredNonFace > 0) info += "    忽略非面：" + _session.IgnoredNonFace;
            _infoLabel.Text = info;

            if (_session.Faces.Count == 0)
            {
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = "请在模型中框选顶针顶面，然后点「重新读取」或「执行」。";
            }
            else if (!string.IsNullOrEmpty(_session.Error))
            {
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = _session.Error;
            }
            else if (mismatch)
            {
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = "当前方向与顶面不匹配，请换到顶针轴线方向（顶面最薄的方向），或勾选自动识别。";
            }
            else if (groupCount == 0)
            {
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = "没有识别到有效顶针顶面。";
            }
            else
            {
                _hintLabel.ForeColor = Color.DimGray;
                _hintLabel.Text = "每根顶针拉平到自己的最高点；识别数量不对时请只选顶针顶面。";
            }

            _okButton.Enabled = true;
        }

        private void RefreshAutoList()
        {
            _groupList.BeginUpdate();
            try
            {
                _groupList.Items.Clear();
                for (int i = 0; i < _session.AutoPins.Count; i++)
                {
                    AutoPin pin = _session.AutoPins[i];
                    ListViewItem item = new ListViewItem(pin.Index.ToString());
                    item.SubItems.Add(pin.TopFaces.Count.ToString());
                    item.SubItems.Add(pin.Skipped || !string.IsNullOrEmpty(pin.Error)
                        ? "-" : pin.Target.ToString("0.####"));

                    string note;
                    if (!string.IsNullOrEmpty(pin.Error)) note = "失败：" + pin.Error;
                    else if (pin.Skipped) note = "跳过：" + pin.Note;
                    else note = DirectionUtil.ToText(pin.Direction) + "，顶面 " + pin.TopFaces.Count + " 个";
                    item.SubItems.Add(note);
                    _groupList.Items.Add(item);
                }
            }
            finally
            {
                _groupList.EndUpdate();
            }

            int ready = 0;
            int skipped = 0;
            int failed = 0;
            for (int i = 0; i < _session.AutoPins.Count; i++)
            {
                AutoPin pin = _session.AutoPins[i];
                if (!string.IsNullOrEmpty(pin.Error)) failed++;
                else if (pin.Skipped) skipped++;
                else ready++;
            }

            _infoLabel.Text = "自动模式    识别顶针：" + _session.AutoPins.Count +
                "    可拉平：" + ready + "    跳过：" + skipped + "    失败：" + failed;

            if (!string.IsNullOrEmpty(_session.AutoError))
            {
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = _session.AutoError;
            }
            else if (_session.AutoPins.Count == 0)
            {
                _hintLabel.ForeColor = Color.Firebrick;
                _hintLabel.Text = "请框选顶针体或顶针顶面后点「重新读取」。";
            }
            else
            {
                _hintLabel.ForeColor = Color.DimGray;
                _hintLabel.Text = "自动按圆柱轴线识别顶面方向；框选顶针体或顶面均可，两端都是平面的顶针会跳过。";
            }

            _okButton.Enabled = true;
        }
    }

    internal class WindowOwner : IWin32Window
    {
        private IntPtr _handle;
        public WindowOwner(IntPtr handle) { _handle = handle; }
        public IntPtr Handle { get { return _handle; } }
    }
}
