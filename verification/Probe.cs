// Baseline verification: runs only against a disposable copy on a private desktop.
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

internal static class Probe
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
    private static string Root;
    private static IntPtr Desktop;
    private static string DesktopName;

    private static void Check(string Name, bool Result)
    {
        Console.WriteLine("[Verification:Baseline] " + (Result ? "PASS " : "FAIL ") + Name);
        if (!Result) Failures++;
    }

    private static void Smoke(string Arguments, string Name)
    {
        StartupInfo Startup = new StartupInfo();
        Startup.Size = Marshal.SizeOf(typeof(StartupInfo));
        Startup.Desktop = DesktopName;
        ProcessInfo Info;
        string Exe = Path.Combine(Root, "TaskbarGroups.exe");
        if (!CreateProcess(Exe, new StringBuilder("\"" + Exe + "\" " + Arguments), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Root, ref Startup, out Info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(Info.Thread);
        using (Process Child = Process.GetProcessById((int)Info.Id))
        {
            IntPtr Found = IntPtr.Zero;
            bool Dialog = false;
            Stopwatch Elapsed = Stopwatch.StartNew();
            try
            {
                while (Elapsed.ElapsedMilliseconds < 30000 && !Child.HasExited && Found == IntPtr.Zero)
                {
                    EnumDesktopWindows(Desktop, delegate(IntPtr Window, IntPtr Parameter)
                    {
                        uint Id;
                        GetWindowThreadProcessId(Window, out Id);
                        if (Id != Info.Id || !IsWindowVisible(Window)) return true;
                        StringBuilder Class = new StringBuilder(256);
                        GetClassName(Window, Class, Class.Capacity);
                        if (Class.ToString() == "#32770") Dialog = true;
                        if (Class.ToString().StartsWith("WindowsForms10.Window")) Found = Window;
                        return true;
                    }, IntPtr.Zero);
                    Thread.Sleep(100);
                }
                Check(Name + " displayed a WinForms window without a native error dialog", Found != IntPtr.Zero && !Dialog);
                if (Found != IntPtr.Zero) PostMessage(Found, 0x0010, IntPtr.Zero, IntPtr.Zero);
                bool Exited = Child.WaitForExit(5000);
                uint Code;
                Check(Name + " closed cleanly", Exited && GetExitCodeProcess(Info.Process, out Code) && Code == 0);
            }
            finally { if (!Child.HasExited) { Child.Kill(); Child.WaitForExit(); } CloseHandle(Info.Process); }
        }
    }

    private static Category Group(string Name)
    {
        return new Category { Name = Name, Width = 1, ShortcutList = new List<ProgramShortcut> {
            new ProgramShortcut { FilePath = Path.Combine(Root, "MissingTarget.exe"), name = "Missing target", WorkingDirectory = Root }
        }};
    }

    [STAThread]
    private static int Main(string[] Arguments)
    {
        SetErrorMode(0x0001 | 0x0002 | 0x8000);
        if (Arguments.Length == 2 && Arguments[0] == "--Target")
        {
            File.WriteAllText(Arguments[1], Environment.CurrentDirectory);
            return 0;
        }
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
            string Exe = Path.Combine(Root, "Probe.exe");
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
                Type Paths = typeof(Category).Assembly.GetType("client.Classes.MainPath", true);
                Paths.GetField("path").SetValue(null, Root);
                Paths.GetField("exeString").SetValue(null, Path.Combine(Root, "TaskbarGroups.exe"));
                Directory.CreateDirectory(Path.Combine(Root, "config"));
                Directory.CreateDirectory(Path.Combine(Root, "Shortcuts"));

                // Produce a disposable group without invoking the editor or pinning anything.
                Category Healthy = Group("Smoke_Group");
                using (Bitmap Picture = SystemIcons.Application.ToBitmap()) Healthy.CreateConfig(Picture);
                Category Loaded = new Category(Path.Combine(Root, "config", Healthy.Name));
                Check("group XML round-trip and image generation", Loaded.Name == Healthy.Name && Loaded.ShortcutList.Count == 1 && File.Exists(Path.Combine(Root, "config", Healthy.Name, "GroupIcon.ico")));
                Check("generated shell link exists", File.Exists(Path.Combine(Root, "Shortcuts", "Smoke Group.lnk")));
                Smoke("", "manager");
                Smoke("Smoke_Group", "group popup with a missing target");

                using (client.frmMain Popup = new client.frmMain("Smoke_Group", 0, 0))
                {
                    string Marker = Path.Combine(Root, "LaunchMarker.txt");
                    string Working = Path.Combine(Root, "TargetWorkingDirectory");
                    Directory.CreateDirectory(Working);
                    Popup.OpenFile("--Target \"" + Marker + "\"", Path.Combine(Root, "Probe.exe"), Working);
                    for (int I = 0; I < 50 && !File.Exists(Marker); I++) Thread.Sleep(100);
                    Check("OpenFile executable, quoted argument, and working directory", File.Exists(Marker) && File.ReadAllText(Marker) == Working);
                }

                // Expected baseline defects: PASS means the issue was reproduced, not fixed.
                string Cache = Path.Combine(Root, "config", Healthy.Name, "Icons");
                File.WriteAllText(Path.Combine(Cache, "Sentinel.txt"), "old cache");
                Healthy.cacheIcons();
                Check("KNOWN KI-04: nonempty shortcut list produces an empty replacement cache", Directory.GetFiles(Cache).Length == 0);

                string Broken = Path.Combine(Root, "BrokenXml");
                Directory.CreateDirectory(Broken);
                File.WriteAllText(Path.Combine(Broken, "ObjectData.xml"), "<Category>");
                bool InvalidXml = false;
                try { new Category(Broken); } catch (InvalidOperationException) { InvalidXml = true; }
                Check("KNOWN KI-08: malformed XML escapes as InvalidOperationException", InvalidXml);

                bool MissingIcon = false;
                try { using (client.frmMain Missing = new client.frmMain("MissingGroup", 0, 0)) { } }
                catch (IOException) { MissingIcon = true; }
                Check("KNOWN KI-08: missing group throws before graceful exit", MissingIcon);

                Category WriteFailure = Group("WriteFailure");
                string FailureDir = Path.Combine(Root, "config", WriteFailure.Name);
                Directory.CreateDirectory(FailureDir);
                string ConfigFile = Path.Combine(FailureDir, "ObjectData.xml");
                using (StreamWriter Previous = new StreamWriter(ConfigFile)) new XmlSerializer(typeof(Category)).Serialize(Previous, WriteFailure);
                string PreviousConfig = File.ReadAllText(ConfigFile);
                WriteFailure.ShortcutList[0].Arguments = "\uD800";
                bool Failed = false;
                try { using (Bitmap Picture = SystemIcons.Application.ToBitmap()) WriteFailure.CreateConfig(Picture); }
                catch (InvalidOperationException) { Failed = true; }
                Check("KNOWN KI-05: failed serialization overwrites existing configuration", Failed && File.ReadAllText(ConfigFile) != PreviousConfig);

                string Other = Path.Combine(Root, "OtherWorkingDirectory");
                Directory.CreateDirectory(Other);
                Directory.SetCurrentDirectory(Other);
                bool RelativeFailure = false;
                try { using (Bitmap Picture = Healthy.LoadIconImage()) { } }
                catch (IOException) { RelativeFailure = true; }
                finally { Directory.SetCurrentDirectory(Root); }
                Check("KNOWN KI-01: image loading depends on current directory", RelativeFailure);
                Check("KNOWN KI-03: default working directory is an executable filename", new ProgramShortcut().WorkingDirectory == Path.Combine(Root, "TaskbarGroups.exe"));
            }
            catch (Exception Error) { Console.WriteLine("[Verification:Error] " + Error); Failures++; }
            Console.WriteLine("[Verification:Result] Unexpected failures: " + Failures);
        }
        // The OS releases the private desktop at process exit. Never switch the input desktop.
        return Failures == 0 ? 0 : 1;
    }
}
