// Stable identity and atomic-generation probes; all UI is confined to a private desktop.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using client.Classes;

internal static class UiProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public short Show, ReservedSize;
        public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
    private delegate bool WindowCallback(IntPtr Window, IntPtr Parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string Name, IntPtr Device, IntPtr Mode, uint Flags, uint Access, IntPtr Security);
    [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint ThreadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr Object, int Index, StringBuilder Value, int Size, out int Needed);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(IntPtr Desktop, WindowCallback Callback, IntPtr Parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr Window, out uint Id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr Window, StringBuilder Name, int Count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr Window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr Window, uint Message, IntPtr WParam, IntPtr LParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string Application, StringBuilder Command, IntPtr ProcessSecurity, IntPtr ThreadSecurity, bool Inherit, uint Flags, IntPtr Environment, string Directory, ref StartupInfo Startup, out ProcessInfo Info);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr Handle);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr Handle, out uint Code);
    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint Mode);
    private static int Failures;
    private static Type Paths = typeof(Category).Assembly.GetType("client.Classes.MainPath", true);
    private static string ExePath { get { return Path.Combine(Root, "Install", "TaskbarGroups.exe"); } }
    private static string Profile { get { return Path.Combine(Root, "UserProfile"); } }
    private static string Root;
    private static IntPtr Desktop;
    private static string DesktopName;

    private static void Check(string Name, bool Result)
    {
        Console.WriteLine("[Verification:UI] " + (Result ? "PASS " : "FAIL ") + Name);
        if (!Result) Failures++;
    }


    private static Type Icons = typeof(Category).Assembly.GetType("client.Classes.IconService");
    private static object Call(Type Type, string Method, params object[] Args)
    {
        try {
            foreach (MethodInfo Info in Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                if (Info.Name == Method && Info.GetParameters().Length == Args.Length) return Info.Invoke(null, Args);
            throw new MissingMethodException(Method);
        } catch (TargetInvocationException Error) { throw Error.InnerException; }
    }
    private static object Field(object Instance, string Name)
    { return Instance.GetType().GetField(Name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(Instance); }
    private static void Event(object Instance, string Name, params object[] Args)
    {
        try { Instance.GetType().GetMethod(Name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Instance, Args); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }
    private static Bitmap Icon(ProgramShortcut Item) { return (Bitmap)Call(Icons, "GetIcon", Item); }
    private static bool Extracted(ProgramShortcut Item)
    {
        object[] Args = new object[] { Item, false };
        using (Bitmap Picture = (Bitmap)Call(Icons, "GetIcon", Args)) return (bool)Args[1];
    }
    private static string Pixels(Image Picture)
    {
        using (Bitmap Bitmap = new Bitmap(Picture))
        {
            StringBuilder Result = new StringBuilder();
            Result.Append(Bitmap.Width).Append(":").Append(Bitmap.Height).Append(":");
            for (int Y = 0; Y < Bitmap.Height; Y++)
                for (int X = 0; X < Bitmap.Width; X++)
                {
                    Color Pixel = Bitmap.GetPixel(X, Y);
                    Result.Append(Pixel.A).Append(",").Append((Pixel.R * Pixel.A + 127) / 255).Append(",")
                        .Append((Pixel.G * Pixel.A + 127) / 255).Append(",").Append((Pixel.B * Pixel.A + 127) / 255).Append(",");
                }
            return Result.ToString();
        }
    }
    private static void Pump(Func<bool> Condition)
    {
        Stopwatch Clock = Stopwatch.StartNew();
        while (!Condition() && Clock.ElapsedMilliseconds < 3000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
    }
    private static void Placement(string Name, Rectangle Bounds, Rectangle Work, Point Anchor, Size Size, Point Expected)
    {
        Point Actual = (Point)Call(typeof(Category).Assembly.GetType("client.Classes.PopupPlacement"),
            "GetLocation", Bounds, Work, Anchor, Size);
        Check(Name, Actual == Expected);
    }

    private static void VerifyPlacement(string GroupId)
    {
        Rectangle Full = new Rectangle(0, 0, 1920, 1080);
        Size PopupSize = new Size(220, 100);
        Placement("bottom reserved edge", Full, new Rectangle(0, 0, 1920, 1040), new Point(960, 1060), PopupSize, new Point(850, 930));
        Placement("top reserved edge", Full, new Rectangle(0, 40, 1920, 1040), new Point(960, 10), PopupSize, new Point(850, 50));
        Placement("left reserved edge", Full, new Rectangle(60, 0, 1860, 1080), new Point(20, 500), PopupSize, new Point(70, 450));
        Placement("right reserved edge", Full, new Rectangle(0, 0, 1860, 1080), new Point(1900, 500), PopupSize, new Point(1630, 450));
        Placement("negative-X monitor", new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1040),
            new Point(-1000, 1060), PopupSize, new Point(-1110, 930));
        Placement("negative-Y monitor", new Rectangle(0, -1200, 1920, 1200), new Rectangle(0, -1200, 1920, 1160),
            new Point(960, -10), PopupSize, new Point(850, -150));
        Rectangle Secondary = new Rectangle(1920, 200, 1280, 720);
        Placement("taskbar-less secondary uses its own bounds", Secondary, Secondary, new Point(2560, 900), PopupSize, new Point(2450, 780));
        Rectangle Negative = new Rectangle(-1280, -720, 1280, 720);
        Placement("auto-hide geometry on negative monitor", Negative, Negative, new Point(-640, -5), PopupSize, new Point(-750, -125));
        Placement("top corner opens below anchor", Full, Full, new Point(0, 0), PopupSize, new Point(10, 20));
        Placement("bottom-right corner remains visible", Full, Full, new Point(1919, 1079), PopupSize, new Point(1690, 959));
        Placement("off-monitor anchor clamps to chosen display", Full, Full, new Point(-10000, 500), PopupSize, new Point(10, 380));
        Placement("maximum anchor arithmetic does not wrap", Full, Full, new Point(int.MaxValue, int.MaxValue), PopupSize, new Point(1690, 970));
        Placement("minimum anchor arithmetic does not wrap", Full, Full, new Point(int.MinValue, int.MinValue), PopupSize, new Point(10, 10));
        Placement("empty working area falls back to monitor", Full, Rectangle.Empty, new Point(960, 500), PopupSize, new Point(850, 380));
        Placement("disjoint working area falls back to monitor", Full, new Rectangle(5000, 5000, 100, 100), new Point(960, 500), PopupSize, new Point(850, 380));
        Placement("partial working area is intersected with monitor", Full, new Rectangle(-100, -100, 2020, 1140),
            new Point(960, 1060), PopupSize, new Point(850, 930));
        Rectangle Small = new Rectangle(100, 100, 200, 80);
        Placement("oversized popup retains visible leading edge", Small, Small, new Point(250, 150), PopupSize, new Point(100, 100));
        Rectangle Exact = new Rectangle(0, 0, 220, 100);
        Placement("exact-fit popup loses padding instead of going off-screen", Exact, Exact, new Point(100, 50), PopupSize, Point.Empty);
        Rectangle Tight = new Rectangle(0, 0, 225, 105);
        Placement("tight working area reduces padding", Tight, Tight, new Point(-100, -100), PopupSize, new Point(2, 2));
        Placement("multiple reserved edges avoid the corner", Full, new Rectangle(60, 40, 1800, 1000),
            new Point(15, 5), PopupSize, new Point(70, 50));
        bool Visible = true, ErrorVisible = true;
        foreach (var Display in System.Windows.Forms.Screen.AllScreens)
        {
            Point Anchor = new Point(Display.Bounds.Right - 4, Display.Bounds.Bottom - 4);
            using (client.frmMain Popup = new client.frmMain(GroupId, Anchor.X, Anchor.Y))
            {
                Popup.Show(); System.Windows.Forms.Application.DoEvents();
                Visible &= Display.WorkingArea.Contains(Popup.Bounds) && Popup.StartPosition == System.Windows.Forms.FormStartPosition.Manual;
                LaunchResult Result = Popup.LaunchItem(new ProgramShortcut { FilePath = Path.Combine(Root, "missing-placement.exe") });
                ErrorVisible &= !Result.Success && Display.WorkingArea.Contains(Popup.Bounds);
                Popup.Close();
            }
        }
        Check("real popup fits each available private-desktop display", Visible);
        Check("launch error expansion is repositioned within working area", ErrorVisible);
        Point Offscreen = new Point(-30000, -30000);
        var Nearest = System.Windows.Forms.Screen.FromPoint(Offscreen);
        using (client.frmMain Popup = new client.frmMain(GroupId, Offscreen.X, Offscreen.Y))
        {
            Popup.Show(); System.Windows.Forms.Application.DoEvents();
            Check("off-screen popup anchor uses nearest monitor", Nearest.WorkingArea.Contains(Popup.Bounds));
            Popup.Close();
        }
    }

    private static void VerifyUi()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath));
        File.Copy(Path.Combine(Root, "TaskbarGroups.exe"), ExePath);
        Call(Paths, "Initialize", ExePath, Profile);
        string Target = Path.Combine(Root, "TaskbarGroups.exe");
        ProgramShortcut Member = new ProgramShortcut { FilePath = Target, WorkingDirectory = Root };
        Category Group = new Category { Name = "Saturated", Width = 2, ColorString = "#FF0000",
            ShortcutList = new List<ProgramShortcut> { Member } };
        using (Bitmap Picture = SystemIcons.Application.ToBitmap()) Group.CreateConfig(Picture);
        byte[] Ico = File.ReadAllBytes(Path.Combine(Group.ResourceDirectory, "GroupIcon.ico"));
        Check("new group icon contains six frames", BitConverter.ToUInt16(Ico, 4) == 6);
        Check("group icon includes 16 and 256 pixel entries", Ico[6] == 16 && Ico[6 + 5 * 16] == 0);
        VerifyPlacement(Group.Id);
        using (client.frmMain Popup = new client.frmMain(Group.Id, 200, 200))
        {
            Popup.Show(); System.Windows.Forms.Application.DoEvents();
            Color Hover = Popup.HoverColor;
            Check("saturated red popup loads with clamped hover color", Hover.R == 255 && Hover.G == 50 && Hover.B == 50);
            Popup.Close();
        }
        Check("saturated bright color clamps darkening", ImageFunctions.HoverColor(Color.Yellow).B == 0);
        string Revision = Group.Revision;
        Group.ColorString = "bad-color";
        bool Rejected = false;
        try { using (Bitmap Picture = SystemIcons.Application.ToBitmap()) Group.CreateConfig(Picture); }
        catch (ArgumentException) { Rejected = true; }
        Check("invalid color save leaves committed group unchanged", Rejected && new Category(Group.Id).Revision == Revision);
        Group = new Category(Group.Id); Member = Group.ShortcutList[0];
        string ExpectedIcon;
        using (Bitmap Picture = Icon(Member)) ExpectedIcon = Pixels(Picture);
        Check("executable extraction succeeds without placeholder", Extracted(Member));
        using (Image Picture = Group.loadImageCache(Member))
        {
            string Actual = Pixels(Picture);
            string[] Left = ExpectedIcon.Split(','), Right = Actual.Split(',');
            int Difference = 0;
            if (Left.Length == Right.Length)
                for (int Index = 1; Index < Left.Length - 1; Index++)
                    Difference = Math.Max(Difference, Math.Abs(int.Parse(Left[Index]) - int.Parse(Right[Index])));
            Console.WriteLine("[Verification:Pixels] Maximum cached alpha/premultiplied channel difference: " + Difference);
            Check("executable cache preserves rendered pixels within rounding tolerance", Left.Length == Right.Length && Left[0] == Right[0] && Difference <= 1);
        }
        string Cache = Directory.GetFiles(Group.CacheDirectory, "*.png")[0];
        File.WriteAllText(Cache, "corrupt");
        using (Image Picture = Group.loadImageCache(Member)) Check("corrupt cache regenerates an icon", Picture.Width > 0);
        using (Image Picture = Image.FromFile(Cache)) Check("replacement cache is valid PNG", Picture.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Png.Guid);
        using (FileStream LockTest = new FileStream(Cache, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check("returned cache image does not retain file lock", LockTest.Length > 0);
        File.WriteAllText(Cache, "corrupt");
        File.SetAttributes(Cache, FileAttributes.ReadOnly);
        try
        {
            using (Image Picture = Group.loadImageCache(Member)) Check("read-only corrupt cache does not suppress extraction", Pixels(Picture) == ExpectedIcon);
            Check("failed cache replacement leaves original bytes", File.ReadAllText(Cache) == "corrupt");
        }
        finally { File.SetAttributes(Cache, FileAttributes.Normal); }
        string Saved = Group.CacheDirectory + "-saved";
        Directory.Move(Group.CacheDirectory, Saved);
        File.WriteAllText(Group.CacheDirectory, "not a directory");
        try
        {
            using (Image Picture = Group.loadImageCache(Member)) Check("unavailable cache folder still renders icon", Pixels(Picture) == ExpectedIcon);
            using (client.frmMain Popup = new client.frmMain(Group.Id, 200, 200))
            { Popup.Show(); System.Windows.Forms.Application.DoEvents(); Check("popup loads with unavailable cache folder", Popup.ControlList.Count == 1); Popup.Close(); }
        }
        finally { File.Delete(Group.CacheDirectory); Directory.Move(Saved, Group.CacheDirectory); }
        ProgramShortcut Missing = new ProgramShortcut { FilePath = Path.Combine(Root, "missing.exe"), Id = Guid.NewGuid().ToString("N") };
        using (Bitmap Picture = Icon(Missing)) Check("missing icon uses placeholder", Picture.Width > 0);
        using (Bitmap Picture = Icon(null)) Check("null icon item uses placeholder", Picture.Width > 0);
        using (Bitmap Picture = Icon(new ProgramShortcut { FilePath = "https://example.com/" })) Check("URI icon does not launch browser", Picture.Width > 0);
        using (Bitmap Picture = Icon(new ProgramShortcut { FilePath = Root })) Check("folder icon renders", Picture.Width > 0);
        Check("folder extraction succeeds without placeholder", Extracted(new ProgramShortcut { FilePath = Root }));
        string Link = Path.Combine(Root, "custom, icon.lnk");
        Call(typeof(Category).Assembly.GetType("client.Classes.ShellLink"), "InstallShortcut",
            Target, "Tests", "Tests", Root, Path.Combine(Group.ResourceDirectory, "GroupIcon.ico"), Link, "");
        using (Bitmap Picture = Icon(new ProgramShortcut { FilePath = Link })) Check("link custom resource icon renders", Picture.Width > 0);
        Check("custom link icon extraction succeeds without placeholder", Extracted(new ProgramShortcut { FilePath = Link }));
        string BadLink = Path.Combine(Root, "bad.lnk"); File.WriteAllText(BadLink, "invalid");
        using (Bitmap Picture = Icon(new ProgramShortcut { FilePath = BadLink })) Check("invalid link icon uses placeholder", Picture.Width > 0);
        Check("malformed link is reported as extraction failure", !Extracted(new ProgramShortcut { FilePath = BadLink }));
        FieldInfo Package = Icons.GetField("PackageIconOverride", BindingFlags.Static | BindingFlags.NonPublic);
        string Seen = null;
        Package.SetValue(null, new Func<string, Bitmap>(delegate(string Identity) { Seen = Identity; return new Bitmap(21, 21); }));
        try
        {
            using (Bitmap Picture = Icon(new ProgramShortcut { FilePath = @"shell:AppsFolder\Test.Family_123!App", isWindowsApp = true }))
                Check("packaged icon uses raw identity route", Picture.Width == 21 && Seen == "Test.Family_123!App");
            Package.SetValue(null, new Func<string, Bitmap>(delegate(string Identity) { throw new InvalidOperationException("missing package"); }));
            using (Bitmap Picture = Icon(new ProgramShortcut { FilePath = "Missing!App", isWindowsApp = true }))
                Check("package lookup failure uses placeholder", Picture.Width > 0);
        }
        finally { Package.SetValue(null, null); }
        using (Bitmap Picture = new Bitmap(32, 32))
        using (System.Drawing.Icon Converted = ImageFunctions.IconFromImage(Picture))
            using (Bitmap Decoded = Converted.ToBitmap())
                Check("image-to-icon result remains usable after stream disposal", Decoded.Width == 32);
        string Legacy = Directory.CreateDirectory(Path.Combine(Root, "SaturatedLegacy")).FullName;
        Group.Name = "SaturatedLegacy";
        using (StreamWriter Writer = new StreamWriter(Path.Combine(Legacy, "ObjectData.xml")))
            new XmlSerializer(typeof(Category)).Serialize(Writer, Group);
        File.Copy(Path.Combine(Group.ResourceDirectory, "GroupImage.png"), Path.Combine(Legacy, "GroupImage.png"));
        File.Copy(Path.Combine(Group.ResourceDirectory, "GroupIcon.ico"), Path.Combine(Legacy, "GroupIcon.ico"));
        Category ValidLegacy = (Category)Call(typeof(Category).Assembly.GetType("client.Classes.LegacyMigration"), "Validate", Legacy, Group.Name);
        Check("saturated legacy group passes migration validation", ValidLegacy.ColorString == "#FF0000");
        Group = new Category(Group.Id);
        string CopiedTarget = Path.Combine(Root, "Icon change.exe");
        File.Copy(Target, CopiedTarget);
        ProgramShortcut Changed = new ProgramShortcut { FilePath = CopiedTarget, Id = Guid.NewGuid().ToString("N") };
        using (Image Picture = Group.loadImageCache(Changed)) { }
        int BeforeChange = Directory.GetFiles(Group.CacheDirectory, "*.png").Length;
        File.SetLastWriteTimeUtc(CopiedTarget, File.GetLastWriteTimeUtc(CopiedTarget).AddMinutes(1));
        using (Image Picture = Group.loadImageCache(Changed)) { }
        Check("target timestamp change invalidates cached icon", Directory.GetFiles(Group.CacheDirectory, "*.png").Length == BeforeChange + 1);
        Type ManagerType = typeof(client.Forms.frmClient);
        Check("release JSON version parsed", (string)Call(ManagerType, "ParseVersion", "{\"tag_name\":\"v-test\"}") == "v-test");
        int InvalidResponses = 0;
        foreach (string Body in new[] { "bad-json", "{}", "{\"tag_name\":10}", "{\"tag_name\":\"\"}" })
        {
            try { Call(ManagerType, "ParseVersion", Body); }
            catch (InvalidDataException) { InvalidResponses++; }
        }
        Check("malformed missing and wrong-type release tags fail as validation errors", InvalidResponses == 4);
        FieldInfo Lookup = ManagerType.GetField("VersionLookupOverride", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo Timeout = ManagerType.GetField("VersionTimeoutMilliseconds", BindingFlags.Static | BindingFlags.NonPublic);
        var Pending = new System.Threading.Tasks.TaskCompletionSource<string>();
        bool Started = false; CancellationToken Token = new CancellationToken();
        Lookup.SetValue(null, new Func<CancellationToken, System.Threading.Tasks.Task<string>>(delegate(CancellationToken Value) { Started = true; Token = Value; return Pending.Task; }));
        Timeout.SetValue(null, 100);
        try
        {
            using (client.Forms.frmClient Manager = new client.Forms.frmClient())
            {
                Check("manager constructor never starts or waits for release lookup", !Started);
                Manager.Show(); Pump(delegate { return Started; });
                Check("lookup starts after manager is shown", Started && Manager.Visible);
                var Version = (System.Windows.Forms.Control)Field(Manager, "githubVersion");
                Pump(delegate { return Version.Text == "Unavailable"; });
                Check("hung release lookup times out without blocking UI", Version.Text == "Unavailable" && Token.IsCancellationRequested);
                Pending.SetException(new InvalidOperationException("late lookup fault"));
                System.Windows.Forms.Application.DoEvents();
                using (client.Forms.frmGroup Editor = new client.Forms.frmGroup(Manager, Group))
                {
                    Editor.Show(); System.Windows.Forms.Application.DoEvents();
                    var Panel = (System.Windows.Forms.Control)Field(Editor, "pnlShortcuts");
                    var OldControl = Panel.Controls[0];
                    Event(Editor, "addShortcut", Missing.FilePath, false);
                    Check("invalid editor import rejected without modifying membership", Editor.Category.ShortcutList.Count == 1);
                    Event(Editor, "addShortcut", Target, false);
                    Check("valid item after invalid import still adds", Editor.Category.ShortcutList.Count == 2);
                    Editor.Swap(Editor.Category.ShortcutList, 0, 1);
                    Check("reorder disposes replaced shortcut controls", OldControl.IsDisposed && Editor.Category.ShortcutList.Count == 2);
                    Editor.DeleteShortcut(Editor.Category.ShortcutList[0]);
                    Check("delete rebuilds positions without indexing removed controls", Editor.Category.ShortcutList.Count == 1 && Panel.Controls.Count == 1);
                    Image Original = ((System.Windows.Forms.Control)Field(Editor, "cmdAddGroupIcon")).BackgroundImage;
                    string Broken = Path.Combine(Root, "broken.png"); File.WriteAllText(Broken, "invalid");
                    Event(Editor, "handleIcon", Broken, ".png");
                    Check("bad editor image retains previous image and shows inline error", ReferenceEquals(Original, ((System.Windows.Forms.Control)Field(Editor, "cmdAddGroupIcon")).BackgroundImage) &&
                        ((System.Windows.Forms.Control)Field(Editor, "lblErrorIcon")).Visible);
                    string Good = Path.Combine(Root, "good.png");
                    using (Bitmap Picture = new Bitmap(18, 18)) Picture.Save(Good);
                    Event(Editor, "handleIcon", Good, ".png");
                    using (FileStream Opened = new FileStream(Good, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        Check("editor image detaches from source file", Opened.Length > 0);
                    Event(Editor, "pnlDragDropImg", Editor, new System.Windows.Forms.DragEventArgs(new System.Windows.Forms.DataObject(), 0, 0, 0, System.Windows.Forms.DragDropEffects.Copy, System.Windows.Forms.DragDropEffects.None));
                    Check("empty image drop is ignored", !Editor.IsDisposed);
                    Editor.Close();
                }
                Manager.Close();
            }
            var Late = new System.Threading.Tasks.TaskCompletionSource<string>();
            Lookup.SetValue(null, new Func<CancellationToken, System.Threading.Tasks.Task<string>>(delegate(CancellationToken Value) { Token = Value; return Late.Task; }));
            Timeout.SetValue(null, 5000);
            using (client.Forms.frmClient Manager = new client.Forms.frmClient())
            {
                Manager.Show(); System.Windows.Forms.Application.DoEvents(); Manager.Close();
                Check("closing manager cancels outstanding lookup", Token.IsCancellationRequested);
                Late.SetResult("late-version"); System.Windows.Forms.Application.DoEvents();
                Check("late lookup completion does not reopen manager", Manager.IsDisposed);
            }
            Lookup.SetValue(null, new Func<CancellationToken, System.Threading.Tasks.Task<string>>(delegate(CancellationToken Value) { return System.Threading.Tasks.Task.FromResult("test-version"); }));
            using (client.Forms.frmClient Manager = new client.Forms.frmClient())
            {
                Manager.Show(); System.Windows.Forms.Application.DoEvents();
                Check("successful async release lookup updates label", ((System.Windows.Forms.Control)Field(Manager, "githubVersion")).Text == "test-version");
                Manager.Close();
            }
        }
        finally { Lookup.SetValue(null, null); Timeout.SetValue(null, 5000); }
        bool Dialog = false;
        EnumDesktopWindows(Desktop, delegate(IntPtr Window, IntPtr Parameter) {
            StringBuilder Class = new StringBuilder(256); GetClassName(Window, Class, 256);
            if (IsWindowVisible(Window) && Class.ToString() == "#32770") Dialog = true;
            return true;
        }, IntPtr.Zero);
        Check("no native error dialog on private desktop", !Dialog);
        Check("cache temporary files cleaned", Directory.GetFiles(Group.CacheDirectory, "*.tmp").Length == 0);
    }

    [STAThread]
    private static int Main(string[] Arguments)
    {
        SetErrorMode(0x0001 | 0x0002 | 0x8000);
        Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if (Arguments.Length == 0)
        {
            DesktopName = "TaskbarGroupsProbe_" + Guid.NewGuid().ToString("N");
            Desktop = CreateDesktop(DesktopName, IntPtr.Zero, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
            if (Desktop == IntPtr.Zero) return 2;
            StringBuilder Station = new StringBuilder(256);
            int Needed;
            if (!GetUserObjectInformation(GetProcessWindowStation(), 2, Station, 512, out Needed)) return 5;
            StartupInfo Startup = new StartupInfo();
            Startup.Size = Marshal.SizeOf(typeof(StartupInfo));
            Startup.Desktop = Station.ToString() + "\\" + DesktopName;
            ProcessInfo Info;
            string Exe = Path.Combine(Root, "UiProbe.exe");
            if (!CreateProcess(Exe, new StringBuilder("\"" + Exe + "\" --Isolated " + DesktopName), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Root, ref Startup, out Info)) return 3;
            CloseHandle(Info.Thread);
            using (Process Child = Process.GetProcessById((int)Info.Id))
            {
                if (!Child.WaitForExit(80000)) { Child.Kill(); Child.WaitForExit(); CloseHandle(Info.Process); return 4; }
                uint Code;
                bool Result = GetExitCodeProcess(Info.Process, out Code);
                CloseHandle(Info.Process);
                return Result ? (int)Code : 6;
            }
        }
        using (StreamWriter Log = new StreamWriter(Path.Combine(Root, "Verification.txt"), false))
        {
            Log.AutoFlush = true;
            Console.SetOut(Log);
            try
            {
                SetErrorMode(0x0001 | 0x0002 | 0x8000);
                DesktopName = Arguments[1];
                Desktop = GetThreadDesktop(GetCurrentThreadId());
                StringBuilder ActualDesktop = new StringBuilder(256);
                int Needed;
                if (!GetUserObjectInformation(Desktop, 2, ActualDesktop, 512, out Needed) || ActualDesktop.ToString() != DesktopName)
                    throw new Exception("Unexpected desktop: " + ActualDesktop + "; expected " + DesktopName);
                Console.WriteLine("[Verification:Desktop] " + ActualDesktop);
                StringBuilder Station = new StringBuilder(256);
                GetUserObjectInformation(GetProcessWindowStation(), 2, Station, 512, out Needed);
                DesktopName = Station.ToString() + "\\" + DesktopName;
                Directory.SetCurrentDirectory(Root);
                VerifyUi();
            }
            catch (Exception Error) { Console.WriteLine("[Verification:Error] " + Error); Failures++; }
            Console.WriteLine("[Verification:Result] Unexpected failures: " + Failures);
        }
        // The OS releases the private desktop at process exit. Never switch the input desktop.
        return Failures == 0 ? 0 : 1;
    }
}
