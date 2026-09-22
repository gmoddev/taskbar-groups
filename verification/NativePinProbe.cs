// Read-only TaskbarManager feasibility probe. No pin request entry points are declared.
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
using Microsoft.Win32;
using System.Linq;
using System.Threading.Tasks;

internal static class NativePinProbe
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
    private static string Root, DesktopName;
    private static IntPtr Desktop;
    [DllImport("combase.dll")] private static extern int RoInitialize(uint Kind);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string Value, int Length, out IntPtr Result);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr Value);
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr Name, ref Guid Id, out IntPtr Result);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string Id);
    [DllImport("shell32.dll")]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr Id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ObjectMethod(IntPtr This, out IntPtr Result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BooleanMethod(IntPtr This, out byte Result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IntegerMethod(IntPtr This, out int Result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VoidMethod(IntPtr This);

    // ABI IIDs and slots from Microsoft's generated Windows.UI.Shell metadata.
    // IInspectable has six slots, followed by each interface's declared methods.
    private static T Method<T>(IntPtr Instance, int Slot) where T : class
    {
        IntPtr Address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(Instance), Slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer(Address, typeof(T)) as T;
    }
    private static void Check(string Name, bool Result)
    {
        Console.WriteLine("[Verification:NativePin] " + (Result ? "PASS " : "FAIL ") + Name);
        if (!Result) Failures++;
    }
    private static void Observe(string Name, object Value)
    { Console.WriteLine("[Verification:NativePin] OBSERVE " + Name + " = " + Value); }
    private static void HResult(int Result) { Marshal.ThrowExceptionForHR(Result); }

    private static void VerifyNative()
    {
        using (RegistryKey Version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            Observe("Windows build", Version.GetValue("CurrentBuildNumber") + "." + Version.GetValue("UBR"));
        const string Seed = "4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4";
        using (RegistryKey Laf = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\com.microsoft.windows.taskbar.pin"))
        {
            object Value = Laf == null ? null : Laf.GetValue(Seed);
            // Unknown types/read failures must not be treated as permission.
            if (Value != null && !(Value is int)) throw new InvalidDataException("Unexpected LAF registry value type.");
            Observe("LAF token required by documented predicate", Value != null && (int)Value != 0);
            if (Value != null && (int)Value != 0) {
                Observe("Capability queries", "Skipped: this probe does not unlock limited-access features.");
                return;
            }
        }
        string ExpectedId = "tjackenpacken.taskbarGroup.menu.probe." + Guid.NewGuid().ToString("N");
        HResult(SetCurrentProcessExplicitAppUserModelID(ExpectedId));
        IntPtr AppId;
        HResult(GetCurrentProcessExplicitAppUserModelID(out AppId));
        try {
            string Actual = Marshal.PtrToStringUni(AppId);
            Check("separate probe process has an explicit group-shaped identity",
                Actual == ExpectedId);
            Observe("Process AppUserModelID", Actual);
        } finally { Marshal.FreeCoTaskMem(AppId); }

        HResult(RoInitialize(0));
        IntPtr Name = IntPtr.Zero, Factory = IntPtr.Zero, Marker = IntPtr.Zero, Manager = IntPtr.Zero;
        try
        {
            const string ClassName = "Windows.UI.Shell.TaskbarManager";
            HResult(WindowsCreateString(ClassName, ClassName.Length, out Name));
            Guid StaticsId = new Guid("db32ab74-de52-4fe6-b7b6-95ff9f8395df");
            HResult(RoGetActivationFactory(Name, ref StaticsId, out Factory));
            Guid MarkerId = new Guid("cdfefd63-e879-4134-b9a7-8283f05f9480");
            int MarkerResult = Marshal.QueryInterface(Factory, ref MarkerId, out Marker);
            Observe("Desktop support marker HRESULT", "0x" + MarkerResult.ToString("X8"));
            if (MarkerResult == unchecked((int)0x80004002)) {
                Observe("Capability queries", "Skipped: no desktop support marker.");
                return;
            }
            HResult(MarkerResult);
            Check("desktop support marker exists", Marker != IntPtr.Zero);
            HResult(Method<ObjectMethod>(Factory, 6)(Factory, out Manager));
            byte Supported, Allowed;
            HResult(Method<BooleanMethod>(Manager, 6)(Manager, out Supported));
            HResult(Method<BooleanMethod>(Manager, 7)(Manager, out Allowed));
            Observe("IsSupported", Supported != 0);
            Observe("IsPinningAllowed on private non-foreground desktop", Allowed != 0);
            Check("TaskbarManager read-only properties return ABI booleans", Supported <= 1 && Allowed <= 1);
            ReadPinned(Manager);
            Observe("Scope", "No Start-menu entry registered; identity resolution and actual pinning remain unverified.");
        }
        finally
        {
            if (Manager != IntPtr.Zero) Marshal.Release(Manager);
            if (Marker != IntPtr.Zero) Marshal.Release(Marker);
            if (Factory != IntPtr.Zero) Marshal.Release(Factory);
            if (Name != IntPtr.Zero) WindowsDeleteString(Name);
            RoUninitialize();
        }
        VerifyProductionClient(ExpectedId);
    }

    private static void VerifyProductionClient(string ExpectedId)
    {
        string Application = Path.Combine(Root, "TaskbarGroups.exe");
        if (!File.Exists(Application)) {
            Observe("Production adapter", "Not present; standalone ABI probe only.");
            return;
        }
        Type ClientType = Assembly.LoadFrom(Application).GetType("client.Classes.NativePinClient", true);
        bool Rejected = false;
        try { Activator.CreateInstance(ClientType, new object[] { "different.identity" }); }
        catch (TargetInvocationException Error) { Rejected = Error.InnerException is InvalidOperationException; }
        Check("production adapter refuses a mismatched process identity", Rejected);
        SynchronizationContext Previous = SynchronizationContext.Current;
        using (var Context = new System.Windows.Forms.WindowsFormsSynchronizationContext())
        using (IDisposable Client = (IDisposable)Activator.CreateInstance(ClientType, new object[] { ExpectedId }))
        {
            SynchronizationContext.SetSynchronizationContext(Context);
            try
            {
                Task Work = (Task)ClientType.GetMethod("GetStatusAsync").Invoke(Client, new object[] { CancellationToken.None });
                Stopwatch Timer = Stopwatch.StartNew();
                while (!Work.IsCompleted && Timer.ElapsedMilliseconds < 7000) {
                    System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5);
                }
                if (!Work.IsCompleted) throw new TimeoutException("Production status query timed out.");
                Work.GetAwaiter().GetResult();
                object Status = Work.GetType().GetProperty("Result").GetValue(Work, null);
                Check("production adapter completes read-only status query", (bool)Status.GetType().GetField("Supported").GetValue(Status));
                Observe("Production IsPinningAllowed", Status.GetType().GetField("Allowed").GetValue(Status));
                Observe("Production IsCurrentAppPinned", Status.GetType().GetField("Pinned").GetValue(Status));
                using (var Cancellation = new CancellationTokenSource())
                {
                    Cancellation.Cancel();
                    Task Cancelled = (Task)ClientType.GetMethod("GetStatusAsync").Invoke(Client, new object[] { Cancellation.Token });
                    Rejected = false;
                    try { Cancelled.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { Rejected = true; }
                    Check("production query honors pre-cancellation", Rejected);
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(Previous); }
        }
        // Deliberately never call the production RequestAsync method.
    }

    private static void ReadPinned(IntPtr Manager)
    {
        IntPtr Operation = IntPtr.Zero, Info = IntPtr.Zero;
        try
        {
            // Slot 8 only queries pinned state. No RequestPin* slots are declared or called.
            HResult(Method<ObjectMethod>(Manager, 8)(Manager, out Operation));
            Guid InfoId = new Guid("00000036-0000-0000-c000-000000000046");
            HResult(Marshal.QueryInterface(Operation, ref InfoId, out Info));
            Stopwatch Timer = Stopwatch.StartNew();
            int Status;
            do {
                HResult(Method<IntegerMethod>(Info, 7)(Info, out Status));
                if (Status != 0) break;
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            } while (Timer.ElapsedMilliseconds < 5000);
            if (Status == 0) {
                HResult(Method<VoidMethod>(Info, 9)(Info)); // Cancel the read-only query.
                throw new TimeoutException("Pinned-state query exceeded five seconds.");
            }
            if (Status != 1) {
                int Error;
                HResult(Method<IntegerMethod>(Info, 8)(Info, out Error));
                Observe("Pinned-state query status / HRESULT", Status + " / 0x" + Error.ToString("X8"));
                throw new InvalidOperationException("Pinned-state query did not complete successfully.");
            }
            byte Pinned;
            HResult(Method<BooleanMethod>(Operation, 8)(Operation, out Pinned));
            Check("read-only pinned-state query completed", Pinned <= 1);
            Observe("IsCurrentAppPinned for unregistered unique probe identity", Pinned != 0);
            HResult(Method<VoidMethod>(Info, 10)(Info)); // Close completed async operation.
        }
        finally {
            if (Info != IntPtr.Zero) Marshal.Release(Info);
            if (Operation != IntPtr.Zero) Marshal.Release(Operation);
        }
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
            string Exe = Path.Combine(Root, "NativePinProbe.exe");
            if (!CreateProcess(Exe, new StringBuilder("\"" + Exe + "\" --Isolated " + DesktopName), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Root, ref Startup, out Info)) return 3;
            CloseHandle(Info.Thread);
            using (Process Child = Process.GetProcessById((int)Info.Id))
            {
                if (!Child.WaitForExit(20000)) { Child.Kill(); Child.WaitForExit(); CloseHandle(Info.Process); return 4; }
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
                VerifyNative();
            }
            catch (Exception Error) { Console.WriteLine("[Verification:Error] " + Error); Failures++; }
            Console.WriteLine("[Verification:Result] Unexpected failures: " + Failures);
        }
        // The OS releases the private desktop at process exit. Never switch the input desktop.
        return Failures == 0 ? 0 : 1;
    }
}
