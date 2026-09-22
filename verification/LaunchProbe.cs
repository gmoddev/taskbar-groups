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

internal static class LaunchProbe
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
        Console.WriteLine("[Verification:Launch] " + (Result ? "PASS " : "FAIL ") + Name);
        if (!Result) Failures++;
    }


    private static Type Service = typeof(Category).Assembly.GetType("client.Classes.LaunchService", true);
    private static object Invoke(Type Type, string Name, params object[] Arguments)
    {
        try { return Type.GetMethod(Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, Arguments); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }
    private static ProgramShortcut Item(string Target, string Arguments = "", string Working = "")
    {
        return new ProgramShortcut { FilePath = Target, Arguments = Arguments, WorkingDirectory = Working };
    }
    private static LaunchPlan Plan(ProgramShortcut Item) { return (LaunchPlan)Invoke(Service, "Build", Item); }
    private static LaunchResult Launch(ProgramShortcut Item) { return (LaunchResult)Invoke(Service, "Launch", Item); }
    private static void Rejected(string Name, ProgramShortcut Item)
    {
        Check(Name, !Launch(Item).Success);
    }
    private static void Link(string PathName, string Target, string Arguments, string Working)
    {
        Invoke(typeof(Category).Assembly.GetType("client.Classes.ShellLink"), "InstallShortcut",
            Target, "TaskbarGroups.Test", "Test", Working, "", PathName, Arguments);
    }
    private static void RawLink(string PathName, string Target, string Arguments, string Working)
    {
        // SetPath resolves existing .lnk files; patch a same-length absent .exe target
        // to construct a real stored link chain without that authoring-time flattening.
        string Placeholder = Path.ChangeExtension(Target, ".exe");
        Link(PathName, Placeholder, Arguments, Working);
        byte[] Bytes = File.ReadAllBytes(PathName);
        foreach (Encoding Codec in new Encoding[] { Encoding.Unicode, Encoding.Default })
        {
            byte[] From = Codec.GetBytes(".exe"), To = Codec.GetBytes(".lnk");
            for (int Index = 0; Index <= Bytes.Length - From.Length; Index++)
            {
                bool Match = true;
                for (int Offset = 0; Offset < From.Length; Offset++)
                    if (Bytes[Index + Offset] != From[Offset]) { Match = false; break; }
                if (Match) Array.Copy(To, 0, Bytes, Index, To.Length);
            }
        }
        File.WriteAllBytes(PathName, Bytes);
    }
    private static void Key(client.frmMain Popup, string Method, System.Windows.Forms.Keys Key)
    {
        typeof(client.frmMain).GetMethod(Method, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(Popup, new object[] { Popup, new System.Windows.Forms.KeyEventArgs(Key) });
    }
    private static void VerifyLaunch()
    {
        Directory.CreateDirectory(Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath));
        File.Copy(Path.Combine(Root, "TaskbarGroups.exe"), ExePath);
        Invoke(Paths, "Initialize", ExePath, Profile);
        string Helper = Path.Combine(Root, "LaunchProbe.exe");
        string Working = Directory.CreateDirectory(Path.Combine(Root, "Working space Ω")).FullName;
        string Args = "--Target \"two words\" \"Ω\"";
        LaunchPlan Direct = Plan(Item(Helper, Args, Working));
        Check("executable preserves exact arguments and Unicode working directory", Direct.Kind == LaunchKind.Executable && Direct.Arguments == Args && Direct.WorkingDirectory == Working);
        Check("empty working directory defaults to executable parent", Plan(Item(Helper)).WorkingDirectory == Root);
        Environment.SetEnvironmentVariable("TASKBARGROUPS_TEST_ROOT", Root);
        Check("environment-expanded absolute target", Plan(Item(@"%TASKBARGROUPS_TEST_ROOT%\LaunchProbe.exe")).Target == Helper);
        ProcessStartInfo Start = (ProcessStartInfo)Invoke(Service, "StartInfo", Direct);
        Check("explicit shell execution, open verb and no shell error dialog", Start.UseShellExecute && !Start.ErrorDialog && Start.Verb == "open" && Start.FileName == Helper && Start.Arguments == Args);
        string Shortcut = Path.Combine(Root, "Shortcut Ω.lnk");
        Link(Shortcut, Helper, "--inside \"a b\"", Working);
        LaunchPlan Resolved = Plan(Item(Shortcut, "--outside"));
        Check("link target arguments and working directory resolved", Resolved.Target == Helper && Resolved.Arguments == "--inside \"a b\" --outside" && Resolved.WorkingDirectory == Working && Resolved.SourceLink == Shortcut);
        Check("explicit working directory overrides link", Plan(Item(Shortcut, "", Root)).WorkingDirectory == Root);
        string Nested = Path.Combine(Root, "Nested.lnk");
        RawLink(Nested, Shortcut, "--nested", "");

        string CycleA = Path.Combine(Root, "CycleA.lnk"), CycleB = Path.Combine(Root, "CycleB.lnk");
        RawLink(CycleA, CycleB, "", "");
        RawLink(CycleB, CycleA, "", "");
        string Broken = Path.Combine(Root, "Broken.lnk");
        File.WriteAllText(Broken, "not a shell link");
        string Web = Path.Combine(Root, "Site.url");
        File.WriteAllText(Web, "[InternetShortcut]\r\nURL=https://example.com/a%20b\r\n");
        Check("internet shortcut reads URL without launching a browser", Plan(Item(Web)).Target == "https://example.com/a%20b");
        Check("directory kind", Plan(Item(Working)).Kind == LaunchKind.Directory);
        Check("registered HTTPS URI kind", Plan(Item("https://example.com/")).Kind == LaunchKind.Uri);
        ProgramShortcut Packaged = Item("Example.Package_123!App", "--argument");
        Packaged.isWindowsApp = true;
        LaunchPlan PackagePlan = Plan(Packaged);
        Check("packaged identity and arguments preserved", PackagePlan.Kind == LaunchKind.PackagedApp && PackagePlan.Arguments == "--argument");
        Packaged.FilePath = @"shell:AppsFolder\Example.Package_123!App";
        Check("packaged prefix normalized", Plan(Packaged).Target == PackagePlan.Target);
        FieldInfo Dispatch = Service.GetField("DispatchOverride", BindingFlags.NonPublic | BindingFlags.Static);
        List<LaunchPlan> Dispatched = new List<LaunchPlan>();
        Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Value) { Dispatched.Add(Value); }));
        try
        {
            Rejected("native nested-link rejection is quiet", Item(Nested));
            Rejected("null item rejected quietly", null);
            Rejected("empty target rejected quietly", Item(""));
            Rejected("missing executable rejected quietly", Item(Path.Combine(Root, "missing.exe")));
            Rejected("relative PATH search rejected", Item("notepad.exe"));
            Rejected("drive-relative path rejected", Item("C:notepad.exe"));
            Rejected("invalid working directory rejected", Item(Helper, "", Path.Combine(Root, "absent")));
            Rejected("directory arguments rejected", Item(Working, "--bad"));
            Rejected("URI arguments rejected", Item("https://example.com/", "--bad"));
            Rejected("file URI rejected", Item("file:///C:/Windows/notepad.exe"));
            Rejected("arbitrary shell namespace rejected", Item("shell:Downloads"));
            Rejected("script URI rejected", Item("javascript:alert(1)"));
            Rejected("unregistered URI rejected", Item("taskbargroupstestmissingprotocol:xyz"));
            Rejected("cyclic shortcut rejected", Item(CycleA));
            Rejected("malformed shortcut rejected", Item(Broken));
            Rejected("null character rejected", Item(Helper, "\0"));
            string Script = Path.Combine(Root, "script.cmd");
            File.WriteAllText(Script, "exit");
            Rejected("script execution is not inferred", Item(Script));
            Packaged.FilePath = "invalid";
            Rejected("invalid packaged identity rejected", Packaged);
            File.WriteAllText(Web, "[InternetShortcut]\r\nURL=https://example.com/\r\nURL=https://example.org/");
            Rejected("ambiguous internet shortcut rejected", Item(Web));
            Check("invalid plans never dispatch", Dispatched.Count == 0);
            Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Value) { throw new System.ComponentModel.Win32Exception(5); }));
            Rejected("dispatch failure returns a quiet result", Item(Helper));
            Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Value) { Dispatched.Add(Value); }));
            Category Group = new Category { Name = "Launch tests", Width = 3, allowOpenAll = true,
                ShortcutList = new List<ProgramShortcut> { Item(Helper, "--one"), Item(Path.Combine(Root, "missing.exe")), Item(Helper, "--three") } };
            using (Bitmap Icon = System.Drawing.SystemIcons.Application.ToBitmap()) Group.CreateConfig(Icon);
            using (client.frmMain Popup = new client.frmMain(Group.Id, 200, 200))
            {
                Popup.Show();
                System.Windows.Forms.Application.DoEvents();
                Popup.ControlList[0].ucShortcut_Click(Popup, EventArgs.Empty);
                Key(Popup, "frmMain_KeyUp", System.Windows.Forms.Keys.D1);
                Check("click and number key preserve same launch contract", Dispatched.Count == 2 && Dispatched[0].Arguments == "--one" && Dispatched[1].Arguments == "--one");
                Key(Popup, "frmMain_KeyDown", System.Windows.Forms.Keys.D0);
                Key(Popup, "frmMain_KeyUp", System.Windows.Forms.Keys.D0);
                Check("out-of-range number key ignored", Dispatched.Count == 2);
                Key(Popup, "frmMain_KeyUp", System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Enter);
                Check("open-all continues after invalid member", Dispatched.Count == 4 && Dispatched[3].Arguments == "--three");
                bool Status = false;
                foreach (System.Windows.Forms.Control Control in Popup.Controls)
                    if (Control is System.Windows.Forms.Label && Control.Text.StartsWith("Could not open item:")) Status = true;
                Check("launch failure appears inline without closing popup", Status && !Popup.IsDisposed);
                Popup.ThisCategory.allowOpenAll = false;
                Key(Popup, "frmMain_KeyUp", System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Enter);
                Check("open-all opt-out respected", Dispatched.Count == 4);
                Popup.ThisCategory.allowOpenAll = true;
                Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Value) {
                    Dispatched.Add(Value);
                    typeof(client.frmMain).GetMethod("frmMain_Deactivate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Popup, new object[] { Popup, EventArgs.Empty });
                }));
                Key(Popup, "frmMain_KeyUp", System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Enter);
                Check("deactivation during batch does not interrupt later members", Dispatched.Count == 6 && !Popup.IsDisposed);
                bool Dialog = false;
                EnumDesktopWindows(Desktop, delegate(IntPtr Window, IntPtr Parameter) {
                    StringBuilder Class = new StringBuilder(256); GetClassName(Window, Class, 256);
                    if (IsWindowVisible(Window) && Class.ToString() == "#32770") Dialog = true;
                    return true;
                }, IntPtr.Zero);
                Check("no native modal error window on private desktop", !Dialog);
                ProgramShortcut Original = Popup.ControlList[0].Psc;
                Packaged.FilePath = "Example.Package_123!App";
                Popup.ControlList[0].Psc = Packaged;
                Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Value) { Dispatched.Add(Value); }));
                Popup.ControlList[0].ucShortcut_Click(Popup, EventArgs.Empty);
                Key(Popup, "frmMain_KeyUp", System.Windows.Forms.Keys.D1);
                Check("packaged click and key preserve arguments through shared dispatcher", Dispatched.Count == 8 &&
                    Dispatched[6].Kind == LaunchKind.PackagedApp && Dispatched[7].Kind == LaunchKind.PackagedApp &&
                    Dispatched[6].Arguments == "--argument" && Dispatched[7].Arguments == "--argument");
                Popup.ControlList[0].Psc = Original;
                Check("URI and directory use shared launch boundary without actual activation",
                    Launch(Item("https://example.com/")).Success && Launch(Item(Working)).Success &&
                    Dispatched[8].Kind == LaunchKind.Uri && Dispatched[9].Kind == LaunchKind.Directory);
                Popup.Close();
            }
        }
        finally { Dispatch.SetValue(null, null); }
        // Only this disposable helper is actually executed. No browser, Explorer or packaged app activation.
        Check("harmless executable dispatch succeeds", Launch(Item(Helper, Args, Working)).Success);
        string Marker = Path.Combine(Root, "TargetResult.txt");
        Stopwatch Timer = Stopwatch.StartNew();
        while (!File.Exists(Marker) && Timer.ElapsedMilliseconds < 5000) Thread.Sleep(25);
        string Result = File.Exists(Marker) ? File.ReadAllText(Marker) : "";
        Check("actual child receives spaced Unicode arguments and working directory", Result.Contains(Working + "\r\ntwo words\r\nΩ\r\n"));
        Check("actual child remains on private desktop", Result.EndsWith(DesktopName.Substring(DesktopName.IndexOf('\\') + 1)));
        File.Delete(Marker);
        string ActualLink = Path.Combine(Root, "Actual helper.lnk");
        Link(ActualLink, Helper, "--Target \"two words\"", Working);
        Check("harmless shell-link dispatch succeeds", Launch(Item(ActualLink, "\"Ω\"")).Success);
        Timer.Restart();
        while (!File.Exists(Marker) && Timer.ElapsedMilliseconds < 5000) Thread.Sleep(25);
        Result = File.Exists(Marker) ? File.ReadAllText(Marker) : "";
        Check("actual link target receives link arguments plus item arguments and link working directory",
            Result.Contains(Working + "\r\ntwo words\r\nΩ\r\n") &&
            Result.EndsWith(DesktopName.Substring(DesktopName.IndexOf('\\') + 1)));
        Check("launch diagnostics use subsystem prefix", File.ReadAllText(Path.Combine(Profile, "TaskbarGroups", "Logs", "Storage.log")).Contains("[TaskbarGroups:Launch]"));
    }

    [STAThread]
    private static int Main(string[] Arguments)
    {
        SetErrorMode(0x0001 | 0x0002 | 0x8000);
        Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if (Arguments.Length == 3 && Arguments[0] == "--Target")
        {
            StringBuilder Name = new StringBuilder(256); int Needed;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, Name, 512, out Needed);
            File.WriteAllText(Path.Combine(Root, "TargetResult.txt"), Environment.CurrentDirectory + "\r\n" + Arguments[1] + "\r\n" + Arguments[2] + "\r\n" + Name);
            return 0;
        }
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
            string Exe = Path.Combine(Root, "LaunchProbe.exe");
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
                VerifyLaunch();
            }
            catch (Exception Error) { Console.WriteLine("[Verification:Error] " + Error); Failures++; }
            Console.WriteLine("[Verification:Result] Unexpected failures: " + Failures);
        }
        // The OS releases the private desktop at process exit. Never switch the input desktop.
        return Failures == 0 ? 0 : 1;
    }
}
