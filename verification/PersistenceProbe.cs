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

internal static class PersistenceProbe
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
        Console.WriteLine("[Verification:Storage] " + (Result ? "PASS " : "FAIL ") + Name);
        if (!Result) Failures++;
    }

    private static void Smoke(string Arguments, string Name)
    {
        StartupInfo Startup = new StartupInfo();
        Startup.Size = Marshal.SizeOf(typeof(StartupInfo));
        Startup.Desktop = DesktopName;
        ProcessInfo Info;
        string Exe = Path.Combine(Root, "PersistenceProbe.exe");
        Arguments = "--Launch " + Arguments;
        if (!CreateProcess(Exe, new StringBuilder("\"" + Exe + "\" " + Arguments), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Path.Combine(Root, "Unrelated"), ref Startup, out Info))
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


    private static object Call(string Method, params object[] Arguments)
    {
        try { return Paths.GetMethod(Method).Invoke(null, Arguments); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }

    private static string GetPath(string Name) { return (string)Paths.GetProperty(Name).GetValue(null, null); }

    private static void Initialize(string UserRoot) { Call("Initialize", ExePath, UserRoot); }

    private static void WriteLegacy(string Folder, string Name)
    {
        Directory.CreateDirectory(Folder);
        Category Item = Group(Name);
        using (StreamWriter Writer = new StreamWriter(Path.Combine(Folder, "ObjectData.xml")))
            new XmlSerializer(typeof(Category)).Serialize(Writer, Item);
        using (Bitmap Picture = SystemIcons.Application.ToBitmap()) Picture.Save(Path.Combine(Folder, "GroupImage.png"), System.Drawing.Imaging.ImageFormat.Png);
        using (FileStream Writer = File.Create(Path.Combine(Folder, "GroupIcon.ico"))) SystemIcons.Application.Save(Writer);
        Directory.CreateDirectory(Path.Combine(Folder, "Icons"));
        File.WriteAllText(Path.Combine(Folder, "Icons", "Sentinel.txt"), "retained cache");
    }

    private static string Digest(string Folder)
    {
        string[] Files = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories);
        Array.Sort(Files, StringComparer.OrdinalIgnoreCase);
        StringBuilder Result = new StringBuilder();
        using (System.Security.Cryptography.SHA256 Hash = System.Security.Cryptography.SHA256.Create())
            foreach (string FilePath in Files)
                Result.Append(FilePath.Substring(Folder.Length)).Append(Convert.ToBase64String(Hash.ComputeHash(File.ReadAllBytes(FilePath))));
        return Result.ToString();
    }

    private static void RestorePermissions(string Folder, System.Security.AccessControl.DirectorySecurity Original)
    {
        var Restored = new System.Security.AccessControl.DirectorySecurity();
        Restored.SetSecurityDescriptorSddlForm(Original.GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access),
            System.Security.AccessControl.AccessControlSections.Access);
        Directory.SetAccessControl(Folder, Restored);
    }

    private static void VerifyLink(string Link, string ExpectedGroup)
    {
        Type ShellType = Type.GetTypeFromProgID("WScript.Shell", true);
        object Shell = Activator.CreateInstance(ShellType);
        object Shortcut = null;
        try
        {
            Shortcut = ShellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, Shell, new object[] { Link });
            Type ShortcutType = Shortcut.GetType();
            string Target = (string)ShortcutType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, Shortcut, null);
            string Arguments = (string)ShortcutType.InvokeMember("Arguments", BindingFlags.GetProperty, null, Shortcut, null);
            string Working = (string)ShortcutType.InvokeMember("WorkingDirectory", BindingFlags.GetProperty, null, Shortcut, null);
            string Icon = (string)ShortcutType.InvokeMember("IconLocation", BindingFlags.GetProperty, null, Shortcut, null);
            Check("shell link target, legacy argument, working directory and per-user icon: " + ExpectedGroup,
                string.Equals(Target, ExePath, StringComparison.OrdinalIgnoreCase) && Arguments == ExpectedGroup &&
                string.Equals(Working, Path.GetDirectoryName(ExePath), StringComparison.OrdinalIgnoreCase) &&
                Icon.StartsWith(GetPath("ConfigDirectory"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Shortcut != null) Marshal.FinalReleaseComObject(Shortcut);
            Marshal.FinalReleaseComObject(Shell);
        }
    }

    private static Type Store = typeof(Category).Assembly.GetType("client.Classes.GroupStore", true);
    private static FieldInfo Fault = Store.GetField("Checkpoint", BindingFlags.Static | BindingFlags.NonPublic);

    private static object StoreCall(string Method, params object[] Arguments)
    {
        try { return Store.GetMethod(Method).Invoke(null, Arguments); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }

    private static void Save(Category Item)
    {
        using (Bitmap Picture = SystemIcons.Application.ToBitmap()) Item.CreateConfig(Picture);
    }

    private static void Crash(string Id, string Stage)
    {
        StartupInfo Startup = new StartupInfo();
        Startup.Size = Marshal.SizeOf(typeof(StartupInfo));
        Startup.Desktop = DesktopName;
        ProcessInfo Info;
        string Exe = Path.Combine(Root, "PersistenceProbe.exe");
        if (!CreateProcess(Exe, new StringBuilder("\"" + Exe + "\" --Crash " + Id + " " + Stage),
            IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Root, ref Startup, out Info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(Info.Thread);
        try
        {
            using (Process Child = Process.GetProcessById((int)Info.Id))
            {
                if (!Child.WaitForExit(10000)) { Child.Kill(); Child.WaitForExit(); throw new Exception("Crash probe timed out."); }
                uint Code;
                Check("terminated at " + Stage, GetExitCodeProcess(Info.Process, out Code) && Code == 73);
            }
        }
        finally { CloseHandle(Info.Process); }
    }

    private static void VerifyStorage()
    {
        string Install = Path.GetDirectoryName(ExePath);
        Directory.CreateDirectory(Install);
        foreach (string FilePath in Directory.GetFiles(Root))
        {
            string Extension = Path.GetExtension(FilePath).ToLowerInvariant();
            if (Extension == ".exe" || Extension == ".dll" || Extension == ".winmd" || Extension == ".config")
                File.Copy(FilePath, Path.Combine(Install, Path.GetFileName(FilePath)));
        }
        string Legacy = Path.Combine(Install, "config", "Legacy_Group");
        WriteLegacy(Legacy, "Legacy_Group");
        string InstallDigest = Digest(Install);
        Directory.CreateDirectory(Path.Combine(Root, "Unrelated"));
        Directory.SetCurrentDirectory(Path.Combine(Root, "Unrelated"));
        Initialize(Profile);
        Category Old = new Category("Legacy_Group");
        string StableId = Old.Id;
        string ItemId = Old.ShortcutList[0].Id;
        Check("legacy identities deterministic before first save", new Category("Legacy_Group").Id == StableId);
        Old.Name = "Browsers / work: 日本語";
        Save(Old);
        Category Renamed = new Category("Legacy_Group");
        Check("rename keeps legacy alias, group and item IDs and AppUserModelID", Renamed.Id == StableId &&
            Renamed.ShortcutList[0].Id == ItemId && Renamed.AppIdKey == "Legacy_Group" && Renamed.Name == Old.Name);
        Check("GUID resolves migrated group", new Category(StableId).Name == Old.Name);
        Check("display names never become storage paths", Renamed.StoreKey == "Legacy_Group");
        VerifyLink((string)StoreCall("GetLink", Renamed), StableId);
        VerifyLink((string)Call("GetShortcutPath", "Legacy_Group"), "Legacy_Group");
        Check("legacy source remains byte-for-byte unchanged", Digest(Install) == InstallDigest);

        string LegacyPointer = Path.Combine(GetPath("ConfigDirectory"), Old.StoreKey, "Current.xml");
        string ValidLegacyPointer = File.ReadAllText(LegacyPointer);
        File.WriteAllText(LegacyPointer, "<broken");
        Check("first upgraded generation can recover original legacy data", new Category("Legacy_Group").Recovered);
        File.WriteAllText(LegacyPointer, ValidLegacyPointer);

        Category Members = new Category(StableId);
        for (int Index = 0; Index < 2; Index++)
        {
            string TargetFolder = Path.Combine(Root, "Target" + Index);
            Directory.CreateDirectory(TargetFolder);
            string Target = Path.Combine(TargetFolder, "SameName.exe");
            File.Copy(ExePath, Target);
            Members.ShortcutList.Add(new ProgramShortcut { FilePath = Target, name = "Target " + Index });
        }
        Save(Members);
        Check("new members receive IDs while existing member keeps identity", Members.ShortcutList[0].Id == ItemId &&
            Members.ShortcutList[1].Id != null && Members.ShortcutList[1].Id != Members.ShortcutList[2].Id);
        using (Image First = Members.loadImageCache(Members.ShortcutList[1])) { }
        using (Image Second = Members.loadImageCache(Members.ShortcutList[2])) { }
        Check("same-basename targets have separate caches without editor controls", Directory.GetFiles(Members.CacheDirectory, "*.png").Length == 2);
        Members.ShortcutList.Reverse();
        Save(Members);
        Check("member ordering and identities survive reload", new Category(StableId).ShortcutList[2].Id == ItemId);

        Category NewGroup = Group("../CON / 新しい");
        Save(NewGroup);
        Check("new groups use GUID directories with arbitrary display names", NewGroup.StoreKey == NewGroup.Id &&
            File.Exists(Path.Combine(GetPath("ConfigDirectory"), NewGroup.Id, "Current.xml")));
        string Name = NewGroup.Name;
        string Revision = NewGroup.Revision;
        string GenerationDigest = Digest(NewGroup.ResourceDirectory);
        Category Editing = (Category)StoreCall("Copy", NewGroup);
        Editing.ShortcutList[0].Arguments = "unsaved";
        Check("editor snapshot does not mutate manager data", NewGroup.ShortcutList[0].Arguments == "");

        foreach (string Stage in new[] { "ConfigurationFlushed", "ImagesWritten", "ResourcesFlushed", "Validated", "PointerFlushed", "Committed" })
        {
            Crash(NewGroup.Id, Stage);
            Category Reloaded = new Category(NewGroup.Id);
            bool Committed = Stage == "Committed";
            Check("restart sees complete " + (Committed ? "new" : "old") + " generation after " + Stage,
                Reloaded.Name == (Committed ? "Crash edit" : Name) &&
                (Committed ? Reloaded.Revision != Revision : Reloaded.Revision == Revision) &&
                File.Exists(Path.Combine(Reloaded.ResourceDirectory, "GroupIcon.ico")));
        }
        Check("old generation retained byte-for-byte", Digest(NewGroup.ResourceDirectory) == GenerationDigest);
        Category Current = new Category(NewGroup.Id);
        StoreCall("RepairLink", Current);
        VerifyLink((string)StoreCall("GetLink", Current), Current.Id);

        Category Stale = new Category(Current.Id);
        Current.Name = "Winner";
        Save(Current);
        bool Conflict = false;
        try { Stale.Name = "Loser"; Save(Stale); } catch (IOException) { Conflict = true; }
        Check("stale editor cannot overwrite newer save", Conflict && new Category(Current.Id).Name == "Winner");
        bool DeleteConflict = false;
        try { StoreCall("Delete", Stale); } catch (IOException) { DeleteConflict = true; }
        Check("stale delete cannot remove newer save", DeleteConflict && new Category(Current.Id).Name == "Winner");

        using (FileStream Busy = new FileStream(Path.Combine(GetPath("DataDirectory"), "Groups.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool Locked = false;
            try { Save(new Category(Current.Id)); } catch (IOException) { Locked = true; }
            Check("busy writer lock fails without altering data", Locked && new Category(Current.Id).Revision == Current.Revision);
        }

        string ReadOnlyPointer = Path.Combine(GetPath("ConfigDirectory"), Current.StoreKey, "Current.xml");
        File.SetAttributes(ReadOnlyPointer, FileAttributes.ReadOnly);
        try
        {
            bool Denied = false;
            try { Save(new Category(Current.Id)); } catch (UnauthorizedAccessException) { Denied = true; } catch (IOException) { Denied = true; }
            Check("failed pointer replacement retains committed generation", Denied && new Category(Current.Id).Revision == Current.Revision);
        }
        finally { File.SetAttributes(ReadOnlyPointer, FileAttributes.Normal); }

        Category Bad = new Category(Current.Id);
        Bad.ShortcutList[0].Arguments = "\uD800";
        bool InvalidXml = false;
        try { Save(Bad); } catch (InvalidOperationException) { InvalidXml = true; }
        Check("serialization failure preserves committed data", InvalidXml && new Category(Current.Id).Revision == Current.Revision);

        string CurrentPath = Path.Combine(GetPath("ConfigDirectory"), Current.StoreKey, "Current.xml");
        string SavedPointer = File.ReadAllText(CurrentPath);
        File.WriteAllText(CurrentPath, "<broken");
        Category Recovered = new Category(Current.Id);
        Check("damaged pointer recovers previous full generation", Recovered.Recovered && Recovered.Name == "Crash edit");
        Recovered.Name = "Recovered edit";
        Save(Recovered);
        Check("recovered group can be saved without destroying good fallback", new Category(Current.Id).Name == "Recovered edit" &&
            File.Exists(Path.Combine(GetPath("ConfigDirectory"), Current.StoreKey, "Previous.xml")));

        string Unsupported = File.ReadAllText(CurrentPath).Replace("<SchemaVersion>1</SchemaVersion>", "<SchemaVersion>99</SchemaVersion>");
        string Supported = File.ReadAllText(CurrentPath);
        File.WriteAllText(CurrentPath, Unsupported);
        bool FutureRejected = false;
        try { new Category(Current.Id); } catch (NotSupportedException) { FutureRejected = true; }
        Check("future schema rejected without rollback", FutureRejected && File.ReadAllText(CurrentPath) == Unsupported);
        File.WriteAllText(CurrentPath, Supported);

        Category Delete = new Category(Current.Id);
        StoreCall("Delete", Delete);
        bool Missing = false;
        try { new Category(Delete.Id); } catch (IOException) { Missing = true; }
        Initialize(Profile);
        Check("delete is durable and retains recoverable resources", Missing && File.Exists(Path.Combine(Delete.ResourceDirectory, "ObjectData.xml")));

        string Corrupt = Path.Combine(GetPath("ConfigDirectory"), "Corrupt");
        Directory.CreateDirectory(Corrupt);
        File.WriteAllText(Path.Combine(Corrupt, "ObjectData.xml"), "<broken>");
        List<Category> Groups = new List<Category>((IEnumerable<Category>)StoreCall("LoadAll"));
        Check("corrupt group isolated from healthy group", Groups.Count == 1 && Groups[0].Id == StableId);

        Category Empty = Group("Never committed");
        Fault.SetValue(null, new Action<string>(delegate(string Stage) { if (Stage == "PointerFlushed") throw new IOException("Injected"); }));
        try { Save(Empty); } catch (IOException) { }
        finally { Fault.SetValue(null, null); }
        Check("uncommitted first save invisible", new List<Category>((IEnumerable<Category>)StoreCall("LoadAll")).Count == 1);

        Smoke("", "manager with healthy and corrupt groups");
        Smoke("Legacy_Group", "renamed legacy popup");
        Smoke(StableId, "GUID popup");
        Check("install untouched after saves and startup", Digest(Install) == InstallDigest);
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
        if (Arguments.Length == 3 && Arguments[0] == "--Crash")
        {
            Initialize(Profile);
            Category Item = new Category(Arguments[1]);
            Item.Name = "Crash edit";
            Fault.SetValue(null, new Action<string>(delegate(string Stage) { if (Stage == Arguments[2]) Environment.Exit(73); }));
            Save(Item);
            return 74;
        }
        if (Arguments.Length > 0 && Arguments[0] == "--Launch")
        {
            try
            {
                string[] AppArguments = Arguments.Length > 1 ? new[] { ExePath, Arguments[1] } : new[] { ExePath };
                typeof(Category).Assembly.GetType("client.client", true).GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { AppArguments, ExePath, Profile });
                return Environment.ExitCode;
            }
            catch (Exception Error) { File.WriteAllText(Path.Combine(Root, "LaunchError.txt"), Error.ToString()); return 10; }
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
            string Exe = Path.Combine(Root, "PersistenceProbe.exe");
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
                VerifyStorage();
            }
            catch (Exception Error) { Console.WriteLine("[Verification:Error] " + Error); Failures++; }
            Console.WriteLine("[Verification:Result] Unexpected failures: " + Failures);
        }
        // The OS releases the private desktop at process exit. Never switch the input desktop.
        return Failures == 0 ? 0 : 1;
    }
}
