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
using System.Linq;
using System.Threading.Tasks;

internal static class SurfaceProbe
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
        Console.WriteLine("[Verification:Surface] " + (Result ? "PASS " : "FAIL ") + Name);
        if (!Result) Failures++;
    }



    private static object Field(object Instance, string Name)
    { return Instance.GetType().GetField(Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(Instance); }
    private static object Invoke(object Instance, string Name, params object[] Args)
    {
        try { return Instance.GetType().GetMethod(Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(Instance, Args); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }
    private static object Call(Type Type, string Name, params object[] Args)
    {
        try { return Type.GetMethod(Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, Args); }
        catch (TargetInvocationException Error) { throw Error.InnerException; }
    }
    private static OrganizerDocument State(client.Forms.frmOrganizer Form) { return (OrganizerDocument)Field(Form, "Document"); }
    private static System.Windows.Forms.Button Tile(client.Forms.frmOrganizer Form, string Id, bool Member = false)
    {
        var Grid = (System.Windows.Forms.Control)Field(Form, Member ? "MemberGrid" : "LayoutGrid");
        foreach (System.Windows.Forms.Button Button in Grid.Controls)
            if ((string)Field(Button.Tag, "Id") == Id) return Button;
        throw new Exception("Tile not found: " + Id);
    }
    private static object Payload(client.Forms.frmOrganizer Form, string Id, bool Member = false)
    { return Invoke(Form, "CreatePayload", Tile(Form, Id, Member).Tag); }
    private static System.Windows.Forms.DragEventArgs DragEvent(object Payload, System.Windows.Forms.Control Target, int X)
    {
        Point Point = Target.PointToScreen(new Point(X, 30));
        return new System.Windows.Forms.DragEventArgs(new System.Windows.Forms.DataObject(Payload), 0, Point.X, Point.Y,
            System.Windows.Forms.DragDropEffects.Move, System.Windows.Forms.DragDropEffects.None);
    }
    private static void Raise(System.Windows.Forms.Control Target, string Name, object Args)
    {
        typeof(System.Windows.Forms.Control).GetMethod(Name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Target, new object[] { Args });
    }
    private static void Drop(client.Forms.frmOrganizer Form, string Source, string Target, int X, bool SourceMember = false, bool TargetMember = false)
    {
        object Value = Payload(Form, Source, SourceMember);
        var Button = Tile(Form, Target, TargetMember);
        var Args = DragEvent(Value, Button, X);
        Raise(Button, "OnDragEnter", Args);
        if (Args.Effect != System.Windows.Forms.DragDropEffects.Move) throw new Exception("Expected accepted drag.");
        Raise(Button, "OnDragDrop", Args);
        System.Windows.Forms.Application.DoEvents();
    }
    private static void Key(client.Forms.frmOrganizer Form, System.Windows.Forms.Keys Key)
    { Invoke(Form, "OnOrganizerKey", Form, new System.Windows.Forms.KeyEventArgs(Key)); }
    private sealed class FakePinClient : IPinClient
    {
        public PinStatus Status = new PinStatus { Supported = true, Allowed = true };
        public bool RequestResult, Disposed;
        public int Requests;
        public Exception Error;
        public TaskCompletionSource<PinStatus> Pending;
        public TaskCompletionSource<bool> PendingRequest;
        public Task<PinStatus> GetStatusAsync(CancellationToken Cancellation)
        {
            if (Error != null) throw Error;
            if (Pending == null) return Task.FromResult(Status);
            Cancellation.Register(() => Pending.TrySetCanceled());
            return Pending.Task;
        }
        public Task<bool> RequestAsync(IntPtr Window, CancellationToken Cancellation)
        {
            if (Window == IntPtr.Zero) throw new Exception("Missing pin window handle.");
            Requests++;
            if (PendingRequest != null) {
                Cancellation.Register(() => PendingRequest.TrySetCanceled());
                return PendingRequest.Task;
            }
            return Task.FromResult(RequestResult);
        }
        public void Dispose() { Disposed = true; }
    }

    private static void Await(Task Work)
    {
        Stopwatch Timer = Stopwatch.StartNew();
        while (!Work.IsCompleted && Timer.ElapsedMilliseconds < 5000) {
            System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5);
        }
        if (!Work.IsCompleted) throw new TimeoutException("Pin UI test timed out.");
        Work.GetAwaiter().GetResult();
    }

    private static void VerifyPinWindow(string Revision, string GroupId, Type Publishing)
    {
        string Programs = Path.Combine(Root, "Programs");
        string Link = (string)Call(Publishing, "RegisterStartEntry", Revision, GroupId, Programs);
        Type Links = typeof(Category).Assembly.GetType("client.Classes.ShellLink");
        object Data = Call(Links, "ReadShortcut", Link);
        Check("Start-menu registration uses stable ID and ordinary popup arguments",
            Link == Path.Combine(Programs, "TaskbarGroups", GroupId + ".lnk") &&
            (string)Field(Data, "Arguments") == GroupId && (string)Field(Data, "AppId") == "tjackenpacken.taskbarGroup.menu." + GroupId);
        Check("registered shortcut launches real executable, never the pin command", (string)Field(Data, "Target") == ExePath);
        byte[] Bytes = File.ReadAllBytes(Link);
        Call(Publishing, "RegisterStartEntry", Revision, GroupId, Programs);
        Check("registration is idempotent", File.ReadAllBytes(Link).SequenceEqual(Bytes));
        Call(Links, "InstallShortcut", ExePath, "unrelated.app", "Unrelated", Path.GetDirectoryName(ExePath), "", Link, "unrelated");
        byte[] Foreign = File.ReadAllBytes(Link);
        bool Rejected = false;
        try { Call(Publishing, "RegisterStartEntry", Revision, GroupId, Programs); }
        catch (IOException) { Rejected = true; }
        Check("foreign Start-menu entry is not replaced", Rejected && File.ReadAllBytes(Link).SequenceEqual(Foreign));
        File.Delete(Link);
        Call(Publishing, "RegisterStartEntry", Revision, GroupId, Programs);
        Call(Links, "InstallShortcut", ExePath, "tjackenpacken.taskbarGroup.menu." + GroupId, "Old", Path.GetDirectoryName(ExePath), "", Link, GroupId);
        File.SetAttributes(Link, FileAttributes.ReadOnly);
        Rejected = false;
        try { Call(Publishing, "RegisterStartEntry", Revision, GroupId, Programs); }
        catch (UnauthorizedAccessException) { Rejected = true; }
        catch (IOException) { Rejected = true; }
        finally { File.SetAttributes(Link, FileAttributes.Normal); }
        Check("denied owned-entry replacement fails without leaving staging", Rejected &&
            Directory.GetFiles(Path.GetDirectoryName(Link), "*.pending").Length == 0);
        Call(Publishing, "RegisterStartEntry", Revision, GroupId, Programs);
        Check("owned entry repairs exactly from committed generation", File.ReadAllBytes(Link).SequenceEqual(Bytes));

        var Fake = new FakePinClient();
        string SeenIdentity = null;
        using (var Pin = new client.Forms.frmGroupPin(Revision, GroupId, Programs,
            Id => { SeenIdentity = Id; return Fake; }))
        {
            Pin.Show(); System.Windows.Forms.Application.DoEvents();
            Check("opening pin window makes no request or client", SeenIdentity == null && Fake.Requests == 0);
            using (Bitmap Preview = new Bitmap(Pin.Width, Pin.Height)) {
                Pin.DrawToBitmap(Preview, new Rectangle(0, 0, Pin.Width, Pin.Height));
                Preview.Save(Path.Combine(Root, "PinWindow.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Await((Task)Invoke(Pin, "RequestPinAsync"));
            Check("request receives group identity rather than organizer identity", SeenIdentity == "tjackenpacken.taskbarGroup.menu." + GroupId);
            Check("declined request retains group and supports retry", Fake.Requests == 1 && Fake.Disposed &&
                ((System.Windows.Forms.Control)Field(Pin, "Status")).Text.Contains("did not confirm") &&
                ((System.Windows.Forms.Control)Field(Pin, "RequestButton")).Enabled && File.Exists(Link));
            Fake.RequestResult = true;
            Await((Task)Invoke(Pin, "RequestPinAsync"));
            Check("only positive API result reports pinned and disables repeat", Fake.Requests == 2 &&
                ((System.Windows.Forms.Control)Field(Pin, "Status")).Text.Contains("is pinned") &&
                !((System.Windows.Forms.Control)Field(Pin, "RequestButton")).Enabled);
            Pin.Close();
        }
        foreach (string Mode in new[] { "Pinned", "Unsupported", "Denied", "Error" })
        {
            Fake = new FakePinClient();
            Fake.Status.Pinned = Mode == "Pinned";
            Fake.Status.Supported = Mode != "Unsupported";
            Fake.Status.Allowed = Mode != "Denied";
            if (Mode == "Error") Fake.Error = new System.Runtime.InteropServices.COMException("Fixture API failure");
            using (var Pin = new client.Forms.frmGroupPin(Revision, GroupId, Programs, Id => Fake))
            {
                Pin.Show(); System.Windows.Forms.Application.DoEvents();
                Await((Task)Invoke(Pin, "RequestPinAsync"));
                Check("pin state " + Mode + " avoids request and releases client", Fake.Requests == 0 && Fake.Disposed);
                Pin.Close();
            }
        }
        Fake = new FakePinClient { Pending = new TaskCompletionSource<PinStatus>() };
        using (var Pin = new client.Forms.frmGroupPin(Revision, GroupId, Programs, Id => Fake))
        {
            Pin.Show(); System.Windows.Forms.Application.DoEvents();
            Task First = (Task)Invoke(Pin, "RequestPinAsync");
            Await((Task)Invoke(Pin, "RequestPinAsync"));
            Check("outstanding availability check prevents reentrant requests", !First.IsCompleted && Fake.Requests == 0);
            Pin.Close();
            Await(First);
            Check("closing pending window cancels and releases before disposal", Fake.Disposed && Pin.IsDisposed && Fake.Requests == 0);
        }
        Fake = new FakePinClient { PendingRequest = new TaskCompletionSource<bool>() };
        using (var Pin = new client.Forms.frmGroupPin(Revision, GroupId, Programs, Id => Fake))
        {
            Pin.Show(); System.Windows.Forms.Application.DoEvents();
            Task Request = (Task)Invoke(Pin, "RequestPinAsync");
            Pin.Close();
            Await(Request);
            Check("closing during simulated OS confirmation releases client and preserves registration",
                Fake.Requests == 1 && Fake.Disposed && Pin.IsDisposed && File.Exists(Link));
        }
        bool FactoryCalled = false;
        Rejected = false;
        try { using (var Pin = new client.Forms.frmGroupPin(Guid.NewGuid().ToString("N"), GroupId, Programs,
            Id => { FactoryCalled = true; return new FakePinClient(); })) { } }
        catch (IOException) { Rejected = true; }
        Check("stale pin window rejected before client construction", Rejected && !FactoryCalled);
        Check("pin preparation and outcomes leave organizer revision unchanged",
            ((OrganizerDocument)Call(typeof(Category).Assembly.GetType("client.Classes.OrganizerStore"), "Load")).Revision == Revision);
    }

    private static System.Windows.Forms.DragEventArgs FileDrop(System.Windows.Forms.Control Target, string[] Files, int X,
        System.Windows.Forms.DragDropEffects Allowed = System.Windows.Forms.DragDropEffects.Copy | System.Windows.Forms.DragDropEffects.Move)
    {
        var Data = new System.Windows.Forms.DataObject();
        Data.SetData(System.Windows.Forms.DataFormats.FileDrop, Files);
        Point Point = Target.PointToScreen(new Point(X, 30));
        var Args = new System.Windows.Forms.DragEventArgs(Data, 0, Point.X, Point.Y, Allowed, System.Windows.Forms.DragDropEffects.None);
        Raise(Target, "OnDragEnter", Args);
        return Args;
    }

    private static void VerifyExternalDrops(client.Forms.frmOrganizer Form, Type Store)
    {
        string[] Sources = Enumerable.Range(0, 10).Select(Index => Path.Combine(Root, "External " + Index + ".exe")).ToArray();
        foreach (string FileName in Sources) File.Copy(ExePath, FileName);
        byte[] SourceBytes = File.ReadAllBytes(Sources[0]);
        string Link = Path.Combine(Root, "External launch.lnk");
        Call(typeof(Category).Assembly.GetType("client.Classes.ShellLink"), "InstallShortcut",
            Sources[1], "", "External launch", Root, "", Link, "--fixture \"two words\"");
        string TargetId = State(Form).State.Layout.First(Value => !Value.IsGroup).Id;
        int BeforeItems = State(Form).State.Items.Count, BeforeUndo = State(Form).Undo.Count;
        var TileTarget = Tile(Form, TargetId);
        var Args = FileDrop(TileTarget, new[] { Sources[0], Link, Sources[0], Path.Combine(Root, "Unavailable.exe") }, 60);
        Check("external app-center drop previews Copy and grouping", Args.Effect == System.Windows.Forms.DragDropEffects.Copy &&
            ((System.Windows.Forms.Control)Field(Form, "Status")).Text.Contains("group"));
        FieldInfo Dispatch = typeof(Category).Assembly.GetType("client.Classes.LaunchService").GetField("DispatchOverride", BindingFlags.Static | BindingFlags.NonPublic);
        int Launches = 0;
        Dispatch.SetValue(null, new Action<LaunchPlan>(Plan => Launches++));
        try
        {
            Raise(TileTarget, "OnDragDrop", Args);
            OrganizerGroup Group = State(Form).State.Groups.Single(Value => Value.Members.Contains(TargetId));
            string GroupId = Group.Id;
            string ImportedA = State(Form).State.Items.Single(Value => Value.FilePath == Sources[0]).Id;
            string ImportedLink = State(Form).State.Items.Single(Value => Value.FilePath == Link).Id;
            Check("multi-file center drop creates target-first group and filters duplicates", Group.Members.SequenceEqual(new[] { TargetId, ImportedA, ImportedLink }) &&
                State(Form).State.Items.Count == BeforeItems + 2);
            Check("multi-file import uses one history entry", State(Form).Undo.Count == BeforeUndo + 1);
            ProgramShortcut Linked = State(Form).State.Items.Single(Value => Value.Id == ImportedLink);
            LaunchPlan Plan = (LaunchPlan)Call(typeof(Category).Assembly.GetType("client.Classes.LaunchService"), "Build", Linked);
            Check("imported link retains target arguments and working directory", Linked.FilePath == Link &&
                Plan.Target == Sources[1] && Plan.Arguments == "--fixture \"two words\"" && Plan.WorkingDirectory == Root);
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z);
            Check("single undo removes whole import and restores original target", State(Form).State.Items.Count == BeforeItems &&
                State(Form).State.Layout.Any(Value => Value.Id == TargetId && !Value.IsGroup));
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y);
            Check("redo restores the same imported group and member identities", State(Form).State.Groups.Any(Value => Value.Id == GroupId &&
                Value.Members.SequenceEqual(new[] { TargetId, ImportedA, ImportedLink })));
            TileTarget = Tile(Form, GroupId);
            Args = FileDrop(TileTarget, new[] { Sources[2] }, 60);
            Raise(TileTarget, "OnDragDrop", Args);
            Check("external center drop extends existing group", State(Form).State.Groups.Single(Value => Value.Id == GroupId).Members.Count == 4);

            var Members = (System.Windows.Forms.Control)Field(Form, "MemberGrid");
            Args = FileDrop(Members, new[] { Sources[3] }, 20);
            Check("member background accepts external Copy", Args.Effect == System.Windows.Forms.DragDropEffects.Copy);
            Raise(Members, "OnDragDrop", Args);
            TileTarget = Tile(Form, ImportedA, true);
            Args = FileDrop(TileTarget, new[] { Sources[4] }, 2);
            Raise(TileTarget, "OnDragDrop", Args);
            Check("member tile file drop appends to its group", State(Form).State.Groups.Single(Value => Value.Id == GroupId).Members.Count == 6);

            TileTarget = Tile(Form, GroupId);
            int Index = State(Form).State.Layout.FindIndex(Value => Value.Id == GroupId);
            Args = FileDrop(TileTarget, new[] { Sources[5], Sources[6] }, 2);
            Check("external edge preview describes insertion", ((System.Windows.Forms.Control)Field(Form, "Status")).Text.Contains("edge"));
            Raise(TileTarget, "OnDragDrop", Args);
            Check("batch edge drop inserts standalone apps in source order", State(Form).State.Layout[Index].Id ==
                State(Form).State.Items.Single(Value => Value.FilePath == Sources[5]).Id &&
                State(Form).State.Layout[Index + 1].Id == State(Form).State.Items.Single(Value => Value.FilePath == Sources[6]).Id &&
                State(Form).State.Layout[Index + 2].Id == GroupId);
            string Revision = State(Form).Revision;
            TileTarget = Tile(Form, GroupId);
            Args = FileDrop(TileTarget, new[] { Sources[0] }, 60);
            Raise(TileTarget, "OnDragDrop", Args);
            Check("duplicate-only drop creates no revision or undo entry", State(Form).Revision == Revision);
            TileTarget = Tile(Form, GroupId);
            Args = FileDrop(TileTarget, new[] { Sources[7] }, 60, System.Windows.Forms.DragDropEffects.Move);
            Check("move-only external source is rejected", Args.Effect == System.Windows.Forms.DragDropEffects.None);
            Raise(TileTarget, "OnDragDrop", Args);
            Check("forced move-only drop leaves layout untouched", State(Form).Revision == Revision);

            FieldInfo Checkpoint = Store.GetField("Checkpoint", BindingFlags.Static | BindingFlags.NonPublic);
            Checkpoint.SetValue(null, new Action<string>(Step => { if (Step == "SnapshotFlushed") throw new IOException("Fixture import failure"); }));
            try
            {
                TileTarget = Tile(Form, GroupId);
                Args = FileDrop(TileTarget, new[] { Sources[7], Sources[8] }, 60);
                Raise(TileTarget, "OnDragDrop", Args);
            }
            finally { Checkpoint.SetValue(null, null); }
            Check("failed batch commit leaves all items and membership unchanged", State(Form).Revision == Revision &&
                !State(Form).State.Items.Any(Value => Value.FilePath == Sources[7] || Value.FilePath == Sources[8]));
            Check("batch failure is reported inline", ((System.Windows.Forms.Control)Field(Form, "Status")).Text.Contains("Could not complete"));
            bool Rejected = false;
            var Batch = new List<ProgramShortcut> { new ProgramShortcut { FilePath = Sources[7] } };
            try { Call(Store, "ImportItems", Guid.NewGuid().ToString("N"), Batch, GroupId, -1); }
            catch (IOException) { Rejected = true; }
            Check("stale batch import rejected before publication", Rejected &&
                ((OrganizerDocument)Call(Store, "Load")).Revision == Revision);
            Batch.Add(new ProgramShortcut { FilePath = Path.Combine(Root, "missing-batch.exe") });
            Rejected = false;
            try { Call(Store, "ImportItems", Revision, Batch, GroupId, -1); }
            catch (IOException) { Rejected = true; }
            Check("late invalid batch item cannot leave an earlier item saved", Rejected &&
                ((OrganizerDocument)Call(Store, "Load")).Revision == Revision);
            Check("external imports never dispatch targets or alter source files", Launches == 0 &&
                File.ReadAllBytes(Sources[0]).SequenceEqual(SourceBytes) && Sources.All(File.Exists) && File.Exists(Link));
        }
        finally { Dispatch.SetValue(null, null); }
    }

    private static void VerifySurface()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath));
        File.Copy(Path.Combine(Root, "TaskbarGroups.exe"), ExePath);
        Call(Paths, "Initialize", ExePath, Profile);
        string[] Files = new string[4];
        for (int Index = 0; Index < 4; Index++) {
            Files[Index] = Path.Combine(Root, new[] { "Browser A.exe", "Browser B.exe", "Editor.exe", "Terminal.exe" }[Index]);
            File.Copy(Path.Combine(Root, "TaskbarGroups.exe"), Files[Index]);
        }
        Type Store = typeof(Category).Assembly.GetType("client.Classes.OrganizerStore");
        using (client.Forms.frmOrganizer Form = new client.Forms.frmOrganizer())
        {
            Form.Show(); System.Windows.Forms.Application.DoEvents();
            Check("empty surface has no unused member panel", !((System.Windows.Forms.Control)Field(Form, "MemberGrid")).Visible);
            var Tools = Form.Controls[0].Controls[0];
            Check("primary toolbar only offers add and undo controls", Tools.Controls.Cast<System.Windows.Forms.Control>().Select(Value => Value.Text).SequenceEqual(new[] { "Add apps", "Undo", "Redo" }));
            Check("surface activates organizer and displays empty layout", State(Form).State.Layout.Count == 0 && (bool)Store.GetProperty("IsActive").GetValue(null, null));
            Invoke(Form, "AddPaths", new object[] { Files });
            Check("adding files needs no group configuration", State(Form).State.Layout.Count == 4);
            Check("multi-select Add apps creates one undo entry", State(Form).Undo.Count == 1);
            var FileData = new System.Windows.Forms.DataObject();
            FileData.SetData(System.Windows.Forms.DataFormats.FileDrop, Files);
            var FileArgs = new System.Windows.Forms.DragEventArgs(FileData, 0, 0, 0,
                System.Windows.Forms.DragDropEffects.Copy | System.Windows.Forms.DragDropEffects.Move, System.Windows.Forms.DragDropEffects.None);
            Raise((System.Windows.Forms.Control)Field(Form, "LayoutGrid"), "OnDragEnter", FileArgs);
            Check("external file drops advertise copy, never source-file movement", FileArgs.Effect == System.Windows.Forms.DragDropEffects.Copy);
            string A = State(Form).State.Items[0].Id, B = State(Form).State.Items[1].Id, C = State(Form).State.Items[2].Id, D = State(Form).State.Items[3].Id;
            Invoke(Form, "AddPaths", new object[] { new[] { Files[0], Path.Combine(Root, "missing.exe") } });
            Check("duplicate and missing imports do not alter state", State(Form).State.Items.Count == 4);
            object Before = Payload(Form, A);
            var Self = Tile(Form, A); var SelfArgs = DragEvent(Before, Self, 60);
            Raise(Self, "OnDragEnter", SelfArgs);
            Check("self-drop preview rejected", SelfArgs.Effect == System.Windows.Forms.DragDropEffects.None);
            string Revision = State(Form).Revision;
            var Cancel = new System.Windows.Forms.QueryContinueDragEventArgs(0, true, System.Windows.Forms.DragAction.Continue);
            typeof(System.Windows.Forms.Control).GetMethod("OnQueryContinueDrag", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Self, new object[] { Cancel });
            Check("Escape cancels native drag without mutation", Cancel.Action == System.Windows.Forms.DragAction.Cancel && State(Form).Revision == Revision);
            Drop(Form, B, A, 60);
            var Group = State(Form).State.Groups[0]; string GroupId = Group.Id;
            Check("new group offers inline Windows pin options without dispatch", ((System.Windows.Forms.Control)Field(Form, "PinGuide")).Visible);
            Check("center drop creates group with target then source", Group.Members[0] == A && Group.Members[1] == B && State(Form).State.Layout.Count == 3);
            Check("automatic group name and generated launcher", Group.Name == "Group 1" &&
                File.Exists(Path.Combine(Profile, "TaskbarGroups", "Shortcuts", Group.Id + ".lnk")));
            Check("selected group exposes draggable members", ((System.Windows.Forms.Control)Field(Form, "MemberGrid")).Controls.Count == 2);
            Drop(Form, C, GroupId, 60);
            Check("drop onto existing group appends member", State(Form).State.Groups[0].Members[2] == C);
            Drop(Form, C, A, 2, true, true);
            Check("member edge drop reorders without regrouping", State(Form).State.Groups[0].Members[0] == C);
            object Out = Payload(Form, B, true);
            var Background = (System.Windows.Forms.Control)Field(Form, "LayoutGrid");
            var OutArgs = DragEvent(Out, Background, Background.Width - 10);
            Raise(Background, "OnDragEnter", OutArgs); Raise(Background, "OnDragDrop", OutArgs);
            Check("drag member to blank grid moves it out", State(Form).State.Layout[2].Id == B && State(Form).State.Groups[0].Members.Count == 2);
            Drop(Form, C, D, 2, true);
            Check("moving out dissolves two-member group into survivor", State(Form).State.Layout[0].Id == A &&
                State(Form).State.Groups[0].RedirectItemId == A);
            Check("old group identity still resolves to survivor popup data", new Category(GroupId).ShortcutList[0].Id == A);
            Drop(Form, B, A, 2);
            Check("left edge reorders top-level entries without grouping", State(Form).State.Layout[0].Id == B);
            Drop(Form, B, D, 130);
            Check("right edge reorders to after target", State(Form).State.Layout[State(Form).State.Layout.Count - 1].Id == B);
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z);
            Check("keyboard undo restores order", State(Form).State.Layout[0].Id == B);
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Y);
            Check("keyboard redo reapplies order", State(Form).State.Layout[State(Form).State.Layout.Count - 1].Id == B);
            Tile(Form, B).PerformClick();
            Key(Form, System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.Left);
            Check("keyboard move changes focused entry position", State(Form).State.Layout[State(Form).State.Layout.Count - 2].Id == B);
            Drop(Form, B, A, 60);
            GroupId = State(Form).State.Groups[1].Id;
            Tile(Form, B, true).PerformClick();
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.M);
            Check("keyboard move-out dissolves group", State(Form).State.Groups[1].RedirectItemId == A);
            Drop(Form, B, A, 60); GroupId = State(Form).State.Groups[2].Id;
            // A stale native payload cannot mutate a freshly loaded layout.
            object Stale = Payload(Form, D);
            Call(Store, "Reorder", State(Form).Revision, D, 0);
            Invoke(Form, "ReloadDocument");
            var Target = Tile(Form, GroupId); var StaleArgs = DragEvent(Stale, Target, 60);
            Raise(Target, "OnDragEnter", StaleArgs);
            Check("stale drag preview rejected after reload", StaleArgs.Effect == System.Windows.Forms.DragDropEffects.None);
            // Conflict that occurs after preview is caught at the commit boundary.
            object Conflict = Payload(Form, D);
            Call(Store, "Reorder", State(Form).Revision, D, 1);
            Target = Tile(Form, GroupId);
            Raise(Target, "OnDragDrop", DragEvent(Conflict, Target, 60));
            Check("concurrent writer conflict reloads instead of overwriting", ((System.Windows.Forms.Control)Field(Form, "Status")).Text.StartsWith("Could not complete") &&
                State(Form).Revision == ((OrganizerDocument)Call(Store, "Load")).Revision);
            FieldInfo Fault = Store.GetField("Checkpoint", BindingFlags.Static | BindingFlags.NonPublic);
            Fault.SetValue(null, new Action<string>(delegate(string Stage) { throw new IOException("Injected write failure"); }));
            Revision = State(Form).Revision;
            try { Drop(Form, D, GroupId, 60); }
            finally { Fault.SetValue(null, null); }
            Check("failed drag commit retains saved membership and status", State(Form).Revision == Revision &&
                ((System.Windows.Forms.Control)Field(Form, "Status")).Text.StartsWith("Could not complete"));
            // Shared launcher receives a plan; no fixture application is executed.
            Type Launch = typeof(Category).Assembly.GetType("client.Classes.LaunchService");
            FieldInfo Dispatch = Launch.GetField("DispatchOverride", BindingFlags.NonPublic | BindingFlags.Static);
            LaunchPlan Seen = null;
            Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Plan) { Seen = Plan; }));
            try {
                Tile(Form, D).PerformClick(); Invoke(Form, "OpenSelected", Form, EventArgs.Empty);
                Check("standalone Open uses shared launcher", Seen != null && Seen.Target == Files[3]);
                Tile(Form, GroupId).PerformClick(); Invoke(Form, "OpenSelected", Form, EventArgs.Empty);
                Check("group Open starts existing popup entry path", Seen.Target == ExePath && Seen.Arguments == GroupId);
            } finally { Dispatch.SetValue(null, null); }
            var ContextMenu = (System.Windows.Forms.ContextMenuStrip)Field(Form, "TileMenu");
            ContextMenu.Show(Tile(Form, GroupId), new Point(10, 10));
            Check("group context menu exposes optional customization and publishing",
                ContextMenu.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(Value => Value.Text).SequenceEqual(new[] { "Open", "Rename…", "Choose icon…", "Pin to taskbar…" }));
            ContextMenu.Close(); System.Windows.Forms.Application.DoEvents();
            bool AutoIcon = State(Form).State.Groups.Single(Value => Value.Id == GroupId).AutoIcon;
            Invoke(Form, "EditSelected", Form, EventArgs.Empty);
            Check("rename stays inline without opening old editor or manager",
                ((System.Windows.Forms.Control)Field(Form, "Customize")).Visible &&
                !System.Windows.Forms.Application.OpenForms.OfType<client.Forms.frmGroup>().Any() &&
                !System.Windows.Forms.Application.OpenForms.OfType<client.Forms.frmClient>().Any());
            ((System.Windows.Forms.TextBox)Field(Form, "NameEditor")).Text = "Browsers";
            Key(Form, System.Windows.Forms.Keys.Enter);
            Check("inline Enter rename retains stable identity and automatic icon",
                State(Form).State.Groups.Single(Value => Value.Id == GroupId).Name == "Browsers" &&
                State(Form).State.Groups.Single(Value => Value.Id == GroupId).AutoIcon == AutoIcon);
            string BeforeCancel = State(Form).Revision;
            Invoke(Form, "EditSelected", Form, EventArgs.Empty);
            ((System.Windows.Forms.TextBox)Field(Form, "NameEditor")).Text = "Cancelled";
            Key(Form, System.Windows.Forms.Keys.Escape);
            Check("Escape cancels inline rename without a save", State(Form).Revision == BeforeCancel &&
                !((System.Windows.Forms.Control)Field(Form, "Customize")).Visible);
            string CustomIcon = Path.Combine(Root, "CustomGroup.png");
            using (var Picture = new Bitmap(32, 32)) { using (var Canvas = Graphics.FromImage(Picture)) Canvas.Clear(Color.CornflowerBlue);
                Picture.Save(CustomIcon, System.Drawing.Imaging.ImageFormat.Png); }
            Invoke(Form, "ApplyGroupIcon", CustomIcon);
            Check("optional icon action saves custom artwork without editor",
                !State(Form).State.Groups.Single(Value => Value.Id == GroupId).AutoIcon);
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z);
            Check("undo icon restores prior automatic artwork policy", State(Form).State.Groups.Single(Value => Value.Id == GroupId).AutoIcon == AutoIcon);
            Tile(Form, D).PerformClick();
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.G);
            var Menu = (System.Windows.Forms.ContextMenuStrip)Field(Form, "GroupMenu");
            Check("keyboard grouping exposes eligible named targets", Menu != null && Menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(Value => Value.Text == "Browsers"));
            Menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(Value => Value.Text == "Browsers").PerformClick();
            if (!Menu.IsDisposed) Menu.Close();
            System.Windows.Forms.Application.DoEvents();
            Check("keyboard grouping moves app into chosen group", State(Form).State.Groups.Single(Value => Value.Id == GroupId).Members.Contains(D));
            Key(Form, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Z);
            Tile(Form, GroupId).PerformClick();
            Type Publishing = typeof(Category).Assembly.GetType("client.Classes.GroupPublishing");
            string LinkPath = (string)Call(Publishing, "GetShortcut", State(Form).Revision, GroupId);
            Check("publishing selects stable group shortcut path", LinkPath == Path.Combine(Profile, "TaskbarGroups", "Shortcuts", GroupId + ".lnk"));
            File.Delete(LinkPath);
            Call(Publishing, "GetShortcut", State(Form).Revision, GroupId);
            Check("missing generated shortcut is repaired before offering pin guidance", File.Exists(LinkPath));
            object LinkData = Call(typeof(Category).Assembly.GetType("client.Classes.ShellLink"), "ReadShortcut", LinkPath);
            Check("prepared shortcut preserves group arguments and AppUserModelID", (string)Field(LinkData, "Arguments") == GroupId &&
                ((string)Field(LinkData, "AppId")).EndsWith(GroupId));
            string BeforePin = State(Form).Revision;
            Invoke(Form, "ShowPinGuide", Form, EventArgs.Empty);
            Check("pin action shows named inline guidance without a modal", ((System.Windows.Forms.Control)Field(Form, "PinGuide")).Visible &&
                ((System.Windows.Forms.Control)Field(Form, "PinInstructions")).Text.Contains("Browsers"));
            Check("pin preparation does not create organizer history", State(Form).Revision == BeforePin);
            bool RevealDispatched = false;
            Dispatch.SetValue(null, new Action<LaunchPlan>(delegate(LaunchPlan Plan) { Seen = Plan; RevealDispatched = true; }));
            try
            {
                Invoke(Form, "ShowPinShortcut", Form, EventArgs.Empty);
                Check("show-shortcut dispatch selects exact generated file", RevealDispatched && Seen.Target.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                    Seen.Arguments == "/select,\"" + LinkPath + "\"");
                Check("guidance does not claim pin success", ((System.Windows.Forms.Control)Field(Form, "Status")).Text.Contains("not tracked"));
                Invoke(Form, "OpenPinWindow", Form, EventArgs.Empty);
                Check("native publishing starts dedicated group process with exact snapshot",
                    Seen.Target == ExePath && Seen.Arguments == "--pin-group " + GroupId + " " + BeforePin);
                Check("opening native pin window does not claim success",
                    ((System.Windows.Forms.Control)Field(Form, "Status")).Text.Contains("requires your action"));
                RevealDispatched = false;
                LaunchResult NativeStale = (LaunchResult)Call(Publishing, "OpenPinWindow", Guid.NewGuid().ToString("N"), GroupId);
                Check("stale native publishing cannot start a child", !NativeStale.Success && !RevealDispatched);
                LaunchResult StaleResult = (LaunchResult)Call(Publishing, "ShowShortcut", Guid.NewGuid().ToString("N"), GroupId);
                Check("stale publishing request fails before Explorer dispatch", !StaleResult.Success && !RevealDispatched);
                string Redirect = State(Form).State.Groups.First(Value => Value.RedirectItemId != null).Id;
                LaunchResult RedirectResult = (LaunchResult)Call(Publishing, "ShowShortcut", State(Form).Revision, Redirect);
                Check("hidden dissolved group cannot be newly offered for pinning", !RedirectResult.Success && !RevealDispatched);
                File.WriteAllText(LinkPath, "stale shortcut");
                File.SetAttributes(LinkPath, FileAttributes.ReadOnly);
                try {
                    LaunchResult Failed = (LaunchResult)Call(Publishing, "ShowShortcut", State(Form).Revision, GroupId);
                    Check("failed shortcut repair cannot dispatch stale projection", !Failed.Success && !RevealDispatched && File.ReadAllText(LinkPath) == "stale shortcut");
                } finally { File.SetAttributes(LinkPath, FileAttributes.Normal); }
                Call(Publishing, "GetShortcut", State(Form).Revision, GroupId);
            }
            finally { Dispatch.SetValue(null, null); }
            VerifyPinWindow(State(Form).Revision, GroupId, Publishing);
            VerifyExternalDrops(Form, Store);
            using (Bitmap Screenshot = new Bitmap(Form.Width, Form.Height)) {
                Form.DrawToBitmap(Screenshot, new Rectangle(0, 0, Form.Width, Form.Height));
                Screenshot.Save(Path.Combine(Root, "Organizer.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Revision = State(Form).Revision;
            using (client.Forms.frmOrganizer Reopened = new client.Forms.frmOrganizer()) {
                Check("reopened surface restores current layout and durable history", State(Reopened).Revision == Revision && State(Reopened).Undo.Count > 0);
            }
            Form.Close();
        }
        bool Dialog = false;
        EnumDesktopWindows(Desktop, delegate(IntPtr Window, IntPtr Parameter) {
            StringBuilder Class = new StringBuilder(256); GetClassName(Window, Class, 256);
            if (IsWindowVisible(Window) && Class.ToString() == "#32770") Dialog = true;
            return true;
        }, IntPtr.Zero);
        Check("no native error dialog during organizer gestures", !Dialog);
    }

    private static void VerifyPinnedImport()
    {
        string Source = Path.Combine(Root, "PinnedSource");
        Directory.CreateDirectory(Source);
        Call(Paths, "Initialize", ExePath, Path.Combine(Root, "PinnedProfile"));
        Type Store = typeof(Category).Assembly.GetType("client.Classes.OrganizerStore");
        Type Pins = typeof(Category).Assembly.GetType("client.Classes.PinnedApps");
        Type Links = typeof(Category).Assembly.GetType("client.Classes.ShellLink");
        string Target = Path.Combine(Root, "Browser A.exe");
        string First = Path.Combine(Source, "Alpha.lnk");
        string Second = Path.Combine(Source, "Beta.lnk");
        Call(Links, "InstallShortcut", Target, "fixture.alpha", "Alpha", Root, "", First, "--alpha \"two words\"");
        Call(Links, "InstallShortcut", Target, "fixture.beta", "Beta", Root, "", Second, "--beta");
        Call(Links, "InstallShortcut", ExePath, "tjackenpacken.taskbarGroup.menu.fixture", "Own group", Root, "", Path.Combine(Source, "Own.lnk"), "fixture");
        string Bad = Path.Combine(Source, "Broken.lnk");
        File.WriteAllText(Bad, "not a shell link");
        OrganizerDocument Imported;
        using (client.Forms.frmOrganizer Form = new client.Forms.frmOrganizer(Source))
        {
            Form.Show(); System.Windows.Forms.Application.DoEvents();
            Imported = State(Form);
            Check("startup imports available pins and isolates invalid and own launchers", Imported.State.Items.Count == 2 && Imported.PinnedSources.Count == 2);
            Check("initial pin discovery has deterministic name order", Imported.State.Items.Select(Value => Value.name).SequenceEqual(new[] { "Alpha", "Beta" }));
            Check("strip is compact and does not wrap", Form.ClientSize.Height == 350 && !((System.Windows.Forms.FlowLayoutPanel)Field(Form, "LayoutGrid")).WrapContents);
            Check("startup import is one undo operation", Imported.Undo.Count == 1);
            Check("source shortcuts are untouched", File.Exists(First) && File.Exists(Second));
            using (Bitmap Screenshot = new Bitmap(Form.Width, Form.Height)) {
                Form.DrawToBitmap(Screenshot, new Rectangle(0, 0, Form.Width, Form.Height));
                Screenshot.Save(Path.Combine(Root, "PinnedStrip.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Form.Close();
        }
        ProgramShortcut Item = Imported.State.Items.First();
        Check("import owns an independent snapshot", Item.FilePath != First && File.Exists(Item.FilePath));
        object Link = Call(Links, "ReadShortcut", Item.FilePath);
        Check("snapshot preserves exact arguments and working directory", (string)Field(Link, "Arguments") == "--alpha \"two words\"" && (string)Field(Link, "WorkingDirectory") == Root);
        File.Delete(First);
        Call(typeof(Category).Assembly.GetType("client.Classes.LaunchService"), "Build", Item);
        Check("removing original pin leaves imported launch specification valid", File.Exists(Item.FilePath));
        OrganizerImportResult Again = (OrganizerImportResult)Call(Pins, "Import", Imported, Source);
        Check("restart does not duplicate existing pins", Again.Document.Revision == Imported.Revision && Again.Added == 0);
        OrganizerDocument Undone = (OrganizerDocument)Call(Store, "Undo", Imported.Revision);
        Check("undo removes import but retains receipts", Undone.State.Items.Count == 0 && Undone.PinnedSources.Count == 2);
        Again = (OrganizerImportResult)Call(Pins, "Import", Undone, Source);
        Check("restart respects undone imports and preserves redo", Again.Document.Revision == Undone.Revision && Again.Document.Redo.Count == 1);
        OrganizerDocument Redone = (OrganizerDocument)Call(Store, "Redo", Undone.Revision);
        Check("redo restores imported identities", Redone.State.Items[0].Id == Item.Id);
        string New = Path.Combine(Source, "Gamma.lnk");
        Call(Links, "InstallShortcut", Target, "fixture.gamma", "Gamma", Root, "", New, "--gamma");
        FieldInfo Checkpoint = Store.GetField("Checkpoint", BindingFlags.Static | BindingFlags.NonPublic);
        Checkpoint.SetValue(null, new Action<string>(Name => { if (Name == "PointerFlushed") throw new IOException("fixture pinned commit failure"); }));
        bool Failed = false;
        try { Call(Pins, "Import", Redone, Source); } catch (IOException) { Failed = true; }
        finally { Checkpoint.SetValue(null, null); }
        OrganizerDocument Reopened = (OrganizerDocument)Call(Store, "Load");
        Check("failed import publishes neither items nor receipts", Failed && Reopened.Revision == Redone.Revision && !Reopened.PinnedSources.Contains(New));
        Again = (OrganizerImportResult)Call(Pins, "Import", Reopened, Source);
        Check("failed import retries successfully", Again.Added == 1 && Again.Document.PinnedSources.Contains(New));
        bool Stale = false;
        try { Call(Pins, "Import", Redone, Source); } catch (IOException) { Stale = true; }
        Check("stale imports cannot overwrite newer layout", Stale && ((OrganizerDocument)Call(Store, "Load")).Revision == Again.Document.Revision);
        File.Delete(Bad);
        Call(Links, "InstallShortcut", Target, "fixture.repaired", "Repaired", Root, "", Bad, "--repaired");
        OrganizerImportResult Repaired = (OrganizerImportResult)Call(Pins, "Import", Again.Document, Source);
        Check("previously invalid pins are retried when repaired", Repaired.Added == 1 && Repaired.Document.PinnedSources.Contains(Bad));
        string Duplicate = Path.Combine(Source, "Copy.lnk");
        File.Copy(Second, Duplicate);
        OrganizerImportResult Duplicated = (OrganizerImportResult)Call(Pins, "Import", Repaired.Document, Source);
        Check("identical links share a snapshot without duplicate items or undo", Duplicated.Added == 0 && Duplicated.Document.PinnedSources.Contains(Duplicate) && Duplicated.Document.Undo.Count == Repaired.Document.Undo.Count);
    }

    private static void VerifyRunningImport()
    {
        Type Running = typeof(Category).Assembly.GetType("client.Classes.RunningApps");
        Check("window discovery accepts visible normal and minimized windows", (bool)Call(Running, "IsCandidate", true, false, false, 0));
        Check("window discovery excludes hidden windows", !(bool)Call(Running, "IsCandidate", false, false, false, 0));
        Check("window discovery excludes cloaked windows", !(bool)Call(Running, "IsCandidate", true, true, false, 0));
        Check("window discovery excludes tool windows", !(bool)Call(Running, "IsCandidate", true, false, false, 0x80));
        Check("owned dialog is excluded unless explicitly a taskbar app", !(bool)Call(Running, "IsCandidate", true, false, true, 0) && (bool)Call(Running, "IsCandidate", true, false, true, 0x40000));
        string Target = Path.Combine(Root, "Browser A.exe");
        ProgramShortcut Parsed = (ProgramShortcut)Call(Running, "ParseRelaunch", "\"" + Target + "\" --profile \"two words\"");
        Check("relaunch parser preserves quoted arguments", Parsed.FilePath == Target && Parsed.Arguments == "--profile \"two words\"");
        Parsed = (ProgramShortcut)Call(Running, "ParseRelaunch", Target);
        Check("unquoted existing executable with spaces remains whole", Parsed.FilePath == Target);
        Check("unterminated quoted command rejected", Call(Running, "ParseRelaunch", "\"broken") == null);
        Call(Paths, "Initialize", ExePath, Path.Combine(Root, "RunningProfile"));
        Type Store = typeof(Category).Assembly.GetType("client.Classes.OrganizerStore");
        Type Pins = typeof(Category).Assembly.GetType("client.Classes.PinnedApps");
        OrganizerDocument Document = (OrganizerDocument)Call(Store, "Initialize");
        string Source = Path.Combine(Root, "RunningPinSource");
        Directory.CreateDirectory(Source);
        Type Links = typeof(Category).Assembly.GetType("client.Classes.ShellLink");
        Call(Links, "InstallShortcut", Target, "fixture.browser", "Browser", Root, "", Path.Combine(Source, "Browser.lnk"), "--profile special");
        List<ProgramShortcut> Items = new List<ProgramShortcut> {
            new ProgramShortcut { FilePath = Target, name = "Browser" },
            new ProgramShortcut { FilePath = Path.Combine(Root, "Editor.exe"), name = "Editor" },
            new ProgramShortcut { FilePath = "Fixture.Package_test!App", name = "Packaged", isWindowsApp = true },
            new ProgramShortcut { FilePath = Path.Combine(Root, "missing.exe"), name = "Missing" }
        };
        OrganizerImportResult Imported = (OrganizerImportResult)Call(Pins, "ImportTaskbar", Document, Source, Items);
        Check("pins and running apps merge in one undo operation", Imported.Added == 3 && Imported.Document.Undo.Count == 1);
        Check("running executable deduplicates against resolved pin target", Imported.Document.State.Items.Count == 3 && Imported.Document.State.Items.Count(Value => Value.name == "Browser") == 1);
        Check("invalid running item isolated and packaged identity retained", Imported.Skipped == 1 && Imported.Document.State.Items.Any(Value => Value.isWindowsApp));
        OrganizerImportResult Again = (OrganizerImportResult)Call(Pins, "ImportTaskbar", Imported.Document, Source, Items);
        Check("repeated running discovery is a no-op", Again.Added == 0 && Again.Document.Revision == Imported.Document.Revision);
        OrganizerDocument Undone = (OrganizerDocument)Call(Store, "Undo", Imported.Document.Revision);
        Again = (OrganizerImportResult)Call(Pins, "ImportTaskbar", Undone, Source, Items);
        Check("running receipts preserve undo and redo on refresh", Again.Document.State.Items.Count == 0 && Again.Document.Revision == Undone.Revision && Again.Document.Redo.Count == 1);
        Items.Add(new ProgramShortcut { FilePath = Path.Combine(Root, "Terminal.exe"), name = "New app" });
        Again = (OrganizerImportResult)Call(Pins, "ImportTaskbar", Undone, Path.Combine(Root, "NoPinnedFolder"), Items);
        Check("new running app imports even without pinned directory", Again.Added == 1 && Again.Document.State.Items.Single().name == "New app");
        using (client.Forms.frmOrganizer Form = new client.Forms.frmOrganizer(Source))
        {
            Form.Show(); System.Windows.Forms.Application.DoEvents();
            string Extra = Path.Combine(Source, "NewPin.lnk");
            Call(Links, "InstallShortcut", Target, "fixture.extra", "Extra", Root, "", Extra, "--extra");
            Key(Form, System.Windows.Forms.Keys.F5);
            Check("F5 rescans injected pinned source", State(Form).State.Items.Any(Value => Value.name == "NewPin"));
            Form.Close();
        }
    }

    private static void VerifyPackageArtwork()
    {
        PortableExecutableKinds Kind; ImageFileMachine Machine;
        typeof(Category).Assembly.ManifestModule.GetPEKind(out Kind, out Machine);
        Check("release executable does not request 32-bit preferred startup", ((int)Kind & 16) == 0 && ((int)Kind & 2) == 0);
        Type Packages = typeof(Category).Assembly.GetType("client.Classes.handleWindowsApp");
        string Folder = Path.Combine(Root, "PackageArtwork");
        Directory.CreateDirectory(Folder);
        string Manifest = Path.Combine(Folder, "AppxManifest.xml");
        using (Bitmap Picture = new Bitmap(64, 64)) {
            using (Graphics Canvas = Graphics.FromImage(Picture)) Canvas.Clear(Color.Lime);
            Picture.Save(Path.Combine(Folder, "Logo.png"));
            Picture.Save(Path.Combine(Folder, "App.scale-200.png"));
        }
        File.WriteAllText(Manifest, "<Package><Properties><Logo>Logo.png</Logo></Properties></Package>");
        using (Bitmap Picture = (Bitmap)Call(Packages, "GetPackageIcon", Folder, "App"))
            Check("package root logo loads without directory substring", Picture.GetPixel(32, 32).G > 200);
        File.WriteAllText(Manifest, "<Package><Properties><Logo>Missing.png</Logo></Properties><Applications><Application Id='Other'><VisualElements Square44x44Logo='Missing.png'/></Application><Application Id='App'><VisualElements Square44x44Logo='App.png'/></Application></Applications></Package>");
        using (Bitmap Picture = (Bitmap)Call(Packages, "GetPackageIcon", Folder, "App"))
            Check("application-specific scale-qualified logo resolves", Picture.GetPixel(32, 32).G > 200);
        File.WriteAllText(Path.Combine(Folder, "App.png"), "broken image");
        using (Bitmap Picture = (Bitmap)Call(Packages, "GetPackageIcon", Folder, "App"))
            Check("corrupt exact logo falls back to qualified candidate", Picture.GetPixel(32, 32).G > 200);
        File.WriteAllText(Manifest, "<Package><Properties><Logo>../outside.png</Logo></Properties></Package>");
        bool Rejected = false;
        try { using (Bitmap Picture = (Bitmap)Call(Packages, "GetPackageIcon", Folder, "App")) { } }
        catch (InvalidDataException) { Rejected = true; }
        Check("package logo cannot escape its installation root", Rejected);
        File.WriteAllText(Manifest, "<Package/>");
        Rejected = false;
        try { using (Bitmap Picture = (Bitmap)Call(Packages, "GetPackageIcon", Folder, "App")) { } }
        catch (InvalidDataException) { Rejected = true; }
        Check("missing logo reports controlled failure", Rejected);
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
            string Exe = Path.Combine(Root, "SurfaceProbe.exe");
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
                VerifySurface();
                VerifyPinnedImport();
                VerifyRunningImport();
                VerifyPackageArtwork();
            }
            catch (Exception Error) { Console.WriteLine("[Verification:Error] " + Error); Failures++; }
            Console.WriteLine("[Verification:Result] Unexpected failures: " + Failures);
        }
        // The OS releases the private desktop at process exit. Never switch the input desktop.
        return Failures == 0 ? 0 : 1;
    }
}
