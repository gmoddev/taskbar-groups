// Organizer snapshot and undo probes; all UI is confined to a private desktop.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using client.Classes;

internal static class OrganizerProbe
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
        string Exe = Path.Combine(Root, "OrganizerProbe.exe");
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

    private static Category FixtureGroup(string Name)
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
        Category Item = FixtureGroup(Name);
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

    private static Type Store = typeof(Category).Assembly.GetType("client.Classes.OrganizerStore", true);
    private static FieldInfo Fault = Store.GetField("Checkpoint", BindingFlags.Static | BindingFlags.NonPublic);
    private static object StoreCall(string Method, params object[] Arguments)
    {
        try { return Store.GetMethod(Method).Invoke(null, Arguments); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }
    private static OrganizerDocument Load() { return (OrganizerDocument)StoreCall("Load"); }
    private static OrganizerDocument Change(string Method, params object[] Arguments) { return (OrganizerDocument)StoreCall(Method, Arguments); }
    private static void Save(Category Item) { using (Bitmap Picture = SystemIcons.Application.ToBitmap()) Item.CreateConfig(Picture); }
    private static string Serialize(object Value)
    {
        using (StringWriter Writer = new StringWriter()) { new XmlSerializer(Value.GetType()).Serialize(Writer, Value); return Writer.ToString(); }
    }
    private static string StateKey(OrganizerDocument Document) { return Serialize(Document.State); }
    private static bool Fails(Action Action)
    {
        try { Action(); return false; }
        catch (Exception Error) { return Error is IOException || Error is UnauthorizedAccessException || Error is InvalidDataException || Error is InvalidOperationException || Error is ArgumentException; }
    }

    private static void Crash(string Source, string Target, string Stage)
    {
        StartupInfo Startup = new StartupInfo();
        Startup.Size = Marshal.SizeOf(typeof(StartupInfo));
        Startup.Desktop = DesktopName;
        ProcessInfo Info;
        string Exe = Path.Combine(Root, "OrganizerProbe.exe");
        if (!CreateProcess(Exe, new StringBuilder("\"" + Exe + "\" --Crash " + Source + " " + Target + " " + Stage),
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
        WriteLegacy(Path.Combine(Install, "config", "Legacy_Group"), "Legacy_Group");
        string InstallDigest = Digest(Install);
        Directory.CreateDirectory(Path.Combine(Root, "Unrelated"));
        Directory.SetCurrentDirectory(Path.Combine(Root, "Unrelated"));
        Initialize(Profile);
        Category Legacy = new Category("Legacy_Group");
        Category Second = FixtureGroup("Second group");
        Second.ShortcutList.Add(new ProgramShortcut { FilePath = Path.Combine(Root, "Two.exe"), Arguments = "--two \"a b\"", WorkingDirectory = Root });
        Second.ShortcutList.Add(new ProgramShortcut { FilePath = Path.Combine(Root, "Three.exe"), Arguments = "--three", WorkingDirectory = Root });
        Save(Second);
        string SecondPointer = Path.Combine(GetPath("ConfigDirectory"), Second.StoreKey, "Current.xml");
        string OriginalSecondPointer = File.ReadAllText(SecondPointer);
        Fault.SetValue(null, new Action<string>(delegate(string Stage) { if (Stage == "ImportedGroup") throw new IOException("Interrupted activation"); }));
        try { StoreCall("Initialize"); } catch (IOException) { }
        finally { Fault.SetValue(null, null); }
        Check("interrupted activation leaves old group authority intact", !(bool)Store.GetProperty("IsActive").GetValue(null, null) &&
            new Category(Second.Id).Revision == Second.Revision && File.ReadAllText(SecondPointer) == OriginalSecondPointer);
        FieldInfo GroupFault = typeof(Category).Assembly.GetType("client.Classes.GroupStore").GetField("Checkpoint", BindingFlags.Static | BindingFlags.NonPublic);
        GroupFault.SetValue(null, new Action<string>(delegate(string Stage)
        {
            if (Stage == "BeforeWriteLock") { GroupFault.SetValue(null, null); Change("Initialize"); }
        }));
        try { Check("activation race rejects stale editor instead of writing ignored pointer", Fails(delegate { Save(Second); }) && File.ReadAllText(SecondPointer) == OriginalSecondPointer); }
        finally { GroupFault.SetValue(null, null); }
        OrganizerDocument Initial = Load();
        Check("activation imports existing groups and ordered members", Initial.State.Groups.Count == 2 &&
            Initial.State.Items.Count == 4 && Initial.State.Layout.Count == 2);
        Check("initialization is idempotent", Change("Initialize").Revision == Initial.Revision);
        Check("legacy argument and AppUserModelID preserved", new Category("Legacy_Group").Id == Legacy.Id && new Category(Legacy.Id).AppIdKey == "Legacy_Group");
        Check("all group readers use organizer revision", new Category(Second.Id).OrganizerRevision == Initial.Revision);
        string Moving = Second.ShortcutList[1].Id;
        string Baseline = StateKey(Initial);
        foreach (string Stage in new[] { "GroupStaged:1", "GroupStaged:2", "SnapshotFlushed", "SnapshotValidated", "PointerFlushed", "Committed" })
        {
            Crash(Moving, Legacy.Id, Stage);
            OrganizerDocument Current = Load();
            bool Committed = Stage == "Committed";
            Check("cross-group move is " + (Committed ? "fully committed" : "fully absent") + " after " + Stage,
                Committed ? Current.State.Groups.Single(Group => Group.Id == Legacy.Id).Members.Contains(Moving) &&
                    !Current.State.Groups.Single(Group => Group.Id == Second.Id).Members.Contains(Moving) : StateKey(Current) == Baseline);
            Check("each member still has exactly one owner after " + Stage,
                Current.State.Groups.Sum(Group => Group.Members.Count) + Current.State.Layout.Count(Entry => !Entry.IsGroup) == Current.State.Items.Count);
        }
        OrganizerDocument Moved = Load();
        Check("stale per-group pointers are not consulted after activation", File.ReadAllText(SecondPointer) == OriginalSecondPointer &&
            new Category(Second.Id).ShortcutList.Count == 2 && new Category("Legacy_Group").ShortcutList.Count == 2);
        Check("move preserves arguments and working directory", new Category("Legacy_Group").ShortcutList.Single(Item => Item.Id == Moving).Arguments == "--two \"a b\"");
        Check("stale organizer revision is rejected", Fails(delegate { Change("Reorder", Initial.Revision, Legacy.Id, 0); }) && Load().Revision == Moved.Revision);
        Crash("Undo", "Unused", "Committed");
        OrganizerDocument Undone = Load();
        Check("undo restores entire pre-move state", StateKey(Undone) == Baseline && new Category(Second.Id).ShortcutList.Count == 3);
        Check("undo uses fresh revision to avoid stale-editor ABA", Undone.Revision != Initial.Revision &&
            Fails(delegate { Change("Reorder", Initial.Revision, Legacy.Id, 0); }));
        // Reinitialize the same profile to exercise durable state rather than process-local history.
        Initialize(Profile);
        Crash("Redo", "Unused", "Committed");
        OrganizerDocument Redone = Load();
        Check("redo survives process restart and restores move", StateKey(Redone) == StateKey(Moved));
        string DragOut = Redone.State.Groups.Single(Group => Group.Id == Second.Id).Members[0];
        OrganizerDocument Detached = Change("MoveOut", Redone.Revision, DragOut, 0);
        OrganizerGroup Dissolved = Detached.State.Groups.Single(Group => Group.Id == Second.Id);
        Check("one-member group dissolves into standalone survivor", Dissolved.Members.Count == 0 && Dissolved.RedirectItemId != null &&
            Detached.State.Layout.Any(Entry => !Entry.IsGroup && Entry.Id == Dissolved.RedirectItemId));
        Check("dissolved group's old launcher still resolves survivor", new Category(Second.Id).ShortcutList.Single().Id == Dissolved.RedirectItemId);
        VerifyLink((string)typeof(Category).Assembly.GetType("client.Classes.GroupStore").GetMethod("GetLink").Invoke(null, new object[] { new Category(Second.Id) }), Second.Id);

        ProgramShortcut AItem = new ProgramShortcut { Id = Guid.NewGuid().ToString("N"), FilePath = Path.Combine(Root, "A.exe"), name = "A", Arguments = "--a" };
        ProgramShortcut BItem = new ProgramShortcut { Id = Guid.NewGuid().ToString("N"), FilePath = Path.Combine(Root, "B.exe"), name = "B" };
        OrganizerDocument AddedA = Change("AddItem", Detached.Revision, AItem, 0);
        OrganizerDocument AddedB = Change("AddItem", AddedA.Revision, BItem, 1);
        Check("adding standalone items preserves requested order", AddedB.State.Layout[0].Id == AItem.Id && AddedB.State.Layout[1].Id == BItem.Id);
        string BeforeInvalid = AddedB.Revision;
        Check("duplicate identity rejected", Fails(delegate { Change("AddItem", BeforeInvalid, AItem, 0); }) && Load().Revision == BeforeInvalid);
        Check("self-drop rejected", Fails(delegate { Change("GroupOnto", BeforeInvalid, AItem.Id, AItem.Id); }) && Load().Revision == BeforeInvalid);
        Check("nested group drop rejected", Fails(delegate { Change("GroupOnto", BeforeInvalid, Legacy.Id, BItem.Id); }) && Load().Revision == BeforeInvalid);
        OrganizerDocument Grouped = Change("GroupOnto", BeforeInvalid, AItem.Id, BItem.Id);
        OrganizerGroup NewGroup = Grouped.State.Groups.Single(Group => Group.Members.Contains(AItem.Id));
        Check("item onto item creates automatic group in target position", NewGroup.Name.StartsWith("Group ") && NewGroup.AutoIcon &&
            NewGroup.Members.SequenceEqual(new[] { BItem.Id, AItem.Id }) && Grouped.State.Layout[0].Id == NewGroup.Id);
        using (Image Picture = new Category(NewGroup.Id).LoadIconImage()) Check("automatic group has generated composite image", Picture.Width == 256);
        OrganizerDocument AddedMember = Change("GroupOnto", Grouped.Revision, DragOut, NewGroup.Id);
        Check("item onto group appends member", AddedMember.State.Groups.Single(Group => Group.Id == NewGroup.Id).Members.Last() == DragOut);
        Check("duplicate membership drop rejected", Fails(delegate { Change("GroupOnto", AddedMember.Revision, DragOut, NewGroup.Id); }));
        OrganizerDocument ReorderedMember = Change("ReorderMember", AddedMember.Revision, DragOut, 0);
        Check("member order persists through popup reader", new Category(NewGroup.Id).ShortcutList[0].Id == DragOut);
        OrganizerDocument Reordered = Change("Reorder", ReorderedMember.Revision, NewGroup.Id, ReorderedMember.State.Layout.Count - 1);
        Check("between-item reorder preserves membership", Reordered.State.Layout.Last().Id == NewGroup.Id &&
            Reordered.State.Groups.Single(Group => Group.Id == NewGroup.Id).Members.SequenceEqual(ReorderedMember.State.Groups.Single(Group => Group.Id == NewGroup.Id).Members));
        string BeforeBadPosition = Reordered.Revision;
        Check("invalid drag-out leaves previous model intact", Fails(delegate { Change("MoveOut", BeforeBadPosition, AItem.Id, -1); }) && Load().Revision == BeforeBadPosition);
        Category Editor = new Category(NewGroup.Id);
        Editor.Name = "Custom / 日本語";
        Save(Editor);
        Check("existing editor saves through global commit", new Category(NewGroup.Id).Name == Editor.Name && Editor.OrganizerRevision == Load().Revision);
        OrganizerDocument AfterEditor = Load();
        Category StaleEditor = new Category(NewGroup.Id);
        OrganizerDocument OrderedAgain = Change("Reorder", AfterEditor.Revision, NewGroup.Id, 0);
        Check("editor cannot overwrite intervening organizer operation", Fails(delegate { Save(StaleEditor); }) && Load().Revision == OrderedAgain.Revision);
        OrganizerDocument UndoOrder = Change("Undo", OrderedAgain.Revision);
        OrganizerDocument Branch = Change("Reorder", UndoOrder.Revision, Legacy.Id, 0);
        Check("new edit after undo clears redo branch", Branch.Redo.Count == 0 && Fails(delegate { Change("Redo", Branch.Revision); }));
        Category Solo = FixtureGroup("Single member");
        Save(Solo);
        OrganizerDocument BeforeEmpty = Load();
        OrganizerDocument Empty = Change("MoveOut", BeforeEmpty.Revision, Solo.ShortcutList.Single().Id, 0);
        Check("zero-member group becomes durable tombstone", Empty.State.Groups.Single(Group => Group.Id == Solo.Id).Deleted && Fails(delegate { new Category(Solo.Id); }));
        OrganizerDocument RestoredSolo = Change("Undo", Empty.Revision);
        Check("undo revives same group identity and member", new Category(Solo.Id).ShortcutList.Single().Id == Solo.ShortcutList.Single().Id);
        Category ToDelete = new Category(NewGroup.Id);
        typeof(Category).Assembly.GetType("client.Classes.GroupStore").GetMethod("Delete").Invoke(null, new object[] { ToDelete });
        Check("editor deletion atomically returns members to organizer", Load().State.Groups.Single(Group => Group.Id == NewGroup.Id).Deleted &&
            ToDelete.ShortcutList.All(Item => Load().State.Layout.Any(Entry => !Entry.IsGroup && Entry.Id == Item.Id)));
        Change("Undo", Load().Revision);
        Check("undo deletion restores exact launcher membership", new Category(NewGroup.Id).ShortcutList.Select(Item => Item.Id).SequenceEqual(ToDelete.ShortcutList.Select(Item => Item.Id)));

        string CurrentPath = Path.Combine(GetPath("DataDirectory"), "Organizer", "Current.xml");
        OrganizerDocument BeforeDenied = Load();
        File.SetAttributes(CurrentPath, FileAttributes.ReadOnly);
        try { Check("failed pointer replace preserves complete state", Fails(delegate { Change("Reorder", BeforeDenied.Revision, NewGroup.Id, 0); }) && Load().Revision == BeforeDenied.Revision); }
        finally { File.SetAttributes(CurrentPath, FileAttributes.Normal); }
        string Supported = File.ReadAllText(CurrentPath);
        File.WriteAllText(CurrentPath, Supported.Replace("<SchemaVersion>1</SchemaVersion>", "<SchemaVersion>99</SchemaVersion>"));
        bool Future = false;
        try { Load(); } catch (NotSupportedException) { Future = true; }
        Check("unsupported organizer schema is refused without rollback", Future);
        File.WriteAllText(CurrentPath, Supported);
        File.WriteAllText(CurrentPath, "<GroupPointer xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" />");
        Check("nil pointer is handled as corrupt storage without null crash", Load().Recovered);
        File.WriteAllText(CurrentPath, Supported);
        string Snapshot = Path.Combine(GetPath("DataDirectory"), "Organizer", "Versions", BeforeDenied.Revision + ".xml");
        byte[] SnapshotBytes = File.ReadAllBytes(Snapshot);
        OrganizerDocument Invalid = Load();
        Invalid.State.Items.Add(Invalid.State.Items[0]);
        File.WriteAllText(Snapshot, Serialize(Invalid), Encoding.Unicode);
        Check("duplicate ownership snapshot recovers whole previous layout", Load().Recovered && Load().Revision != BeforeDenied.Revision);
        File.WriteAllBytes(Snapshot, SnapshotBytes);
        string SnapshotText = File.ReadAllText(Snapshot);
        File.WriteAllText(Snapshot, SnapshotText.Replace("<SchemaVersion>1</SchemaVersion>", "<SchemaVersion>99</SchemaVersion>"));
        Future = false;
        try { Load(); } catch (NotSupportedException) { Future = true; }
        Check("future snapshot schema is refused without rollback", Future);
        File.WriteAllBytes(Snapshot, SnapshotBytes);
        File.WriteAllText(CurrentPath, "<broken");
        OrganizerDocument Recovered = Load();
        Check("corrupt pointer recovers a complete previous snapshot", Recovered.Recovered);
        OrganizerDocument Repaired = Change("Reorder", Recovered.Revision, Legacy.Id, 0);
        Check("editing recovered state retains known-good fallback", !Load().Recovered &&
            File.Exists(Path.Combine(GetPath("DataDirectory"), "Organizer", "Damaged.xml")));
        for (int Index = 0; Index < 52; Index++)
        {
            OrganizerDocument Current = Load();
            Change("Reorder", Current.Revision, Legacy.Id, Index % 2);
        }
        Check("history is bounded to 50 undo entries", Load().Undo.Count == 50);
        string Latest = StateKey(Load());
        Change("Undo", Load().Revision);
        Change("Redo", Load().Revision);
        Check("bounded history still round-trips last state", StateKey(Load()) == Latest);
        Smoke("", "manager reading organizer groups");
        Smoke("Legacy_Group", "legacy popup from organizer");
        Smoke(Second.Id, "dissolved-group redirect popup");
        Check("tests never modify executable-adjacent state", Digest(Install) == InstallDigest);
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
        if (Arguments.Length == 4 && Arguments[0] == "--Crash")
        {
            Initialize(Profile);
            OrganizerDocument Document = Load();
            Fault.SetValue(null, new Action<string>(delegate(string Stage) { if (Stage == Arguments[3]) Environment.Exit(73); }));
            if (Arguments[1] == "Undo" || Arguments[1] == "Redo") Change(Arguments[1], Document.Revision);
            else Change("GroupOnto", Document.Revision, Arguments[1], Arguments[2]);
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
            string Exe = Path.Combine(Root, "OrganizerProbe.exe");
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
