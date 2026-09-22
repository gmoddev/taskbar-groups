// Milestone 1: disposable storage fixtures; all UI is confined to a private desktop.
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

internal static class StorageProbe
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
        string Exe = Path.Combine(Root, "StorageProbe.exe");
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
        string Broken = Path.Combine(Install, "config", "Broken");
        Directory.CreateDirectory(Broken);
        File.WriteAllText(Path.Combine(Broken, "ObjectData.xml"), "<Category>");
        string BrokenImage = Path.Combine(Install, "config", "BrokenImage");
        WriteLegacy(BrokenImage, "BrokenImage");
        File.WriteAllText(Path.Combine(BrokenImage, "GroupImage.png"), "bad image");

        string OriginalInstall = Digest(Install);
        Directory.CreateDirectory(Path.Combine(Root, "Unrelated"));
        Directory.SetCurrentDirectory(Path.Combine(Root, "Unrelated"));
        var OriginalAcl = Directory.GetAccessControl(Install);
        var RestrictedAcl = Directory.GetAccessControl(Install);
        var Identity = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        var Denial = new System.Security.AccessControl.FileSystemAccessRule(Identity,
            System.Security.AccessControl.FileSystemRights.Write | System.Security.AccessControl.FileSystemRights.Delete | System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Deny);
        RestrictedAcl.AddAccessRule(Denial);
        try
        {
            if (Path.GetFullPath(Install) != Path.Combine(Root, "Install")) throw new Exception("Unexpected ACL target");
            Directory.SetAccessControl(Install, RestrictedAcl);
            bool WriteBlocked = false;
            try { File.WriteAllText(Path.Combine(Install, "ShouldNotWrite.txt"), "bad"); }
            catch (UnauthorizedAccessException) { WriteBlocked = true; }
            Check("fixture installation ACL actually blocks writes", WriteBlocked);
            Initialize(Profile);
            string Config = GetPath("ConfigDirectory");
            string Imported = (string)Call("GetGroupDirectory", "Legacy_Group");
            Check("storage root is per-user and separate from installation", GetPath("DataDirectory") == Path.Combine(Profile, "TaskbarGroups"));
            Check("valid legacy group and cache copied byte-for-byte", Digest(Legacy) == Digest(Imported));
            Check("corrupt legacy XML and image isolated", !Directory.Exists(Path.Combine(Config, "Broken")) && !Directory.Exists(Path.Combine(Config, "BrokenImage")));
            Check("migration warnings logged without dialogs", File.ReadAllText(Path.Combine(GetPath("LogDirectory"), "Storage.log")).Contains("Could not import"));
            VerifyLink((string)Call("GetShortcutPath", "Legacy_Group"), "Legacy_Group");

            using (Bitmap Picture = new Category(Imported).LoadIconImage()) Check("migrated icon loads from unrelated working directory", Picture.Width > 0);
            Category NewGroup = Group("New_Group");
            using (Bitmap Picture = SystemIcons.Application.ToBitmap()) NewGroup.CreateConfig(Picture);
            Check("new configuration saved under user root", File.Exists(Path.Combine(Config, NewGroup.Id, "Current.xml")) && !Directory.Exists(Path.Combine(Environment.CurrentDirectory, "config")));
            VerifyLink(Path.Combine(GetPath("ShortcutDirectory"), NewGroup.Id + ".lnk"), NewGroup.Id);
            Check("new shortcut working directory is a directory", new ProgramShortcut().WorkingDirectory == Install);

            string Modified = Path.Combine(Imported, "Icons", "Sentinel.txt");
            File.WriteAllText(Modified, "newer user cache");
            Initialize(Profile);
            Check("retry preserves newer user data", File.ReadAllText(Modified) == "newer user cache");
            // Simulate deletion by moving only the precise test-owned published group outside config.
            if (Path.GetDirectoryName(Imported) != Config) throw new Exception("Unexpected removal fixture");
            Directory.Move(Imported, Path.Combine(Root, "RemovedGroup"));
            File.Delete((string)Call("GetShortcutPath", "Legacy_Group"));
            Initialize(Profile);
            Check("completed import does not resurrect a deleted group", !Directory.Exists(Imported));
            Directory.Move(Path.Combine(Root, "RemovedGroup"), Imported);
            // A deleted generated link is deliberately not recreated by a completed import.
            Check("original install and legacy files untouched", Digest(Install) == OriginalInstall);
            Smoke("", "manager with imported groups");
            Smoke("Legacy_Group", "legacy-name popup");
            Check("startup and popup never write to installation", Digest(Install) == OriginalInstall);
        }
        finally { RestorePermissions(Install, OriginalAcl); }

        string RetryProfile = Path.Combine(Root, "RetryProfile");
        string RetryConfig = Path.Combine(RetryProfile, "TaskbarGroups", "config", "Legacy_Group");
        // Existing target plus missing receipt is the interruption state after directory publication.
        WriteLegacy(RetryConfig, "Legacy_Group");
        File.WriteAllText(Path.Combine(RetryConfig, "Icons", "Sentinel.txt"), "existing destination wins");
        Initialize(RetryProfile);
        Check("existing destination wins over older legacy source", File.ReadAllText(Path.Combine(RetryConfig, "Icons", "Sentinel.txt")) == "existing destination wins");
        Check("interrupted publication resumes missing shortcut and receipt", File.Exists((string)Call("GetShortcutPath", "Legacy_Group")) && Directory.GetFiles(GetPath("MigrationDirectory"), "*.done").Length == 1);
        string RetryDigest = Digest(RetryConfig);
        Initialize(RetryProfile);
        Check("migration retry is idempotent", Digest(RetryConfig) == RetryDigest);

        string BlockedProfile = Path.Combine(Root, "BlockedProfile");
        Directory.CreateDirectory(BlockedProfile);
        string Blocker = Path.Combine(BlockedProfile, "TaskbarGroups");
        File.WriteAllText(Blocker, "blocks directory creation");
        Type Entry = typeof(Category).Assembly.GetType("client.client", true);
        Entry.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { new[] { ExePath }, ExePath, BlockedProfile });
        Check("unavailable storage returns failure without opening UI", Environment.ExitCode == 1 && System.Windows.Forms.Application.OpenForms.Count == 0);
        Check("unavailable storage produces actionable fallback log", File.ReadAllText(Path.Combine(BlockedProfile, "TaskbarGroups-Startup.log")).Contains("Check write access"));
        Environment.ExitCode = 0;
        File.Delete(Blocker);
        Initialize(BlockedProfile);
        Check("startup can recover after storage is repaired", Directory.Exists(GetPath("ConfigDirectory")));

        string ReadOnlyState = GetPath("DataDirectory");
        var OriginalStateAcl = Directory.GetAccessControl(ReadOnlyState);
        var RestrictedStateAcl = Directory.GetAccessControl(ReadOnlyState);
        RestrictedStateAcl.AddAccessRule(Denial);
        try
        {
            if (ReadOnlyState != Path.Combine(BlockedProfile, "TaskbarGroups")) throw new Exception("Unexpected ACL target");
            Directory.SetAccessControl(ReadOnlyState, RestrictedStateAcl);
            Entry.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { new[] { ExePath }, ExePath, BlockedProfile });
            Check("existing read-only state fails quietly before opening UI", Environment.ExitCode == 1 && System.Windows.Forms.Application.OpenForms.Count == 0);
        }
        finally { RestorePermissions(ReadOnlyState, OriginalStateAcl); Environment.ExitCode = 0; }
        Initialize(BlockedProfile);
        Check("read-only state recovers after permissions are restored", Directory.Exists(GetPath("ConfigDirectory")));

        bool TraversalRejected = false;
        try { Call("GetGroupDirectory", "..\\Outside"); } catch (IOException) { TraversalRejected = true; }
        Check("group path cannot escape user configuration root", TraversalRejected);
        bool ForeignPathRejected = false;
        try { Call("ResolveGroupDirectory", Legacy); } catch (IOException) { ForeignPathRejected = true; }
        Check("runtime reads do not fall back to legacy install directories", ForeignPathRejected);

        Entry.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
            new object[] { new[] { ExePath, "Missing_Group" }, ExePath, BlockedProfile });
        Check("missing or unimported group startup exits quietly", Environment.ExitCode == 1 && System.Windows.Forms.Application.OpenForms.Count == 0);
        Environment.ExitCode = 0;
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
            string Exe = Path.Combine(Root, "StorageProbe.exe");
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
