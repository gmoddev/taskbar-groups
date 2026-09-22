using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace client.Classes
{
    public sealed class PinStatus
    {
        public bool Supported, Allowed, Pinned;
    }

    // Injectable boundary: automated UI tests never invoke an OS pin request.
    public interface IPinClient : IDisposable
    {
        Task<PinStatus> GetStatusAsync(CancellationToken Cancellation);
        Task<bool> RequestAsync(IntPtr Window, CancellationToken Cancellation);
    }

    internal sealed class NativePinClient : IPinClient
    {
        private IntPtr Manager;
        private bool Initialized, Busy;
        private readonly int ThreadId = Thread.CurrentThread.ManagedThreadId;

        [DllImport("combase.dll")] private static extern int RoInitialize(uint Kind);
        [DllImport("combase.dll")] private static extern void RoUninitialize();
        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string Value, int Length, out IntPtr Result);
        [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr Value);
        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr Name, ref Guid Id, out IntPtr Result);
        [DllImport("shell32.dll")] private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr Id);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ObjectMethod(IntPtr This, out IntPtr Result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BooleanMethod(IntPtr This, out byte Result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IntegerMethod(IntPtr This, out int Result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int VoidMethod(IntPtr This);

        // Windows.UI.Shell ABI from Microsoft's Windows metadata; IInspectable occupies slots 0-5.
        private static T Method<T>(IntPtr Instance, int Slot) where T : class
        {
            return Marshal.GetDelegateForFunctionPointer(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(Instance), Slot * IntPtr.Size), typeof(T)) as T;
        }

        public NativePinClient(string ExpectedId)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Pinning requires the group window's UI thread.");
            IntPtr Id;
            Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out Id));
            try {
                if (Marshal.PtrToStringUni(Id) != ExpectedId)
                    throw new InvalidOperationException("The process does not have this group's identity.");
            } finally { Marshal.FreeCoTaskMem(Id); }

            using (RegistryKey Key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\com.microsoft.windows.taskbar.pin"))
            {
                object Seed = Key == null ? null : Key.GetValue("4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4");
                if (Seed != null && (!(Seed is int) || (int)Seed != 0))
                    throw new NotSupportedException("This Windows version restricts native pin requests. Use Show shortcut.");
            }
            IntPtr Name = IntPtr.Zero, Factory = IntPtr.Zero, Marker = IntPtr.Zero;
            bool Ready = false;
            try
            {
                Marshal.ThrowExceptionForHR(RoInitialize(0));
                Initialized = true;
                const string ClassName = "Windows.UI.Shell.TaskbarManager";
                Marshal.ThrowExceptionForHR(WindowsCreateString(ClassName, ClassName.Length, out Name));
                Guid FactoryId = new Guid("db32ab74-de52-4fe6-b7b6-95ff9f8395df");
                Marshal.ThrowExceptionForHR(RoGetActivationFactory(Name, ref FactoryId, out Factory));
                Guid MarkerId = new Guid("cdfefd63-e879-4134-b9a7-8283f05f9480");
                if (Marshal.QueryInterface(Factory, ref MarkerId, out Marker) < 0)
                    throw new NotSupportedException("Desktop pin requests are unavailable. Use Show shortcut.");
                Marshal.ThrowExceptionForHR(Method<ObjectMethod>(Factory, 6)(Factory, out Manager));
                Ready = true;
            }
            finally
            {
                if (Marker != IntPtr.Zero) Marshal.Release(Marker);
                if (Factory != IntPtr.Zero) Marshal.Release(Factory);
                if (Name != IntPtr.Zero) WindowsDeleteString(Name);
                if (!Ready) Dispose();
            }
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ThreadId || Manager == IntPtr.Zero)
                throw new InvalidOperationException("The pin session is unavailable on this thread.");
        }

        public async Task<PinStatus> GetStatusAsync(CancellationToken Cancellation)
        {
            CheckThread();
            byte Supported;
            Marshal.ThrowExceptionForHR(Method<BooleanMethod>(Manager, 6)(Manager, out Supported));
            if (Supported == 0) return new PinStatus();
            bool Pinned = await ReadAsync(8, 5000, Cancellation);
            byte Allowed;
            Marshal.ThrowExceptionForHR(Method<BooleanMethod>(Manager, 7)(Manager, out Allowed));
            return new PinStatus { Supported = true, Allowed = Allowed != 0, Pinned = Pinned };
        }

        public Task<bool> RequestAsync(IntPtr Window, CancellationToken Cancellation)
        {
            CheckThread();
            if (Window == IntPtr.Zero || GetForegroundWindow() != Window)
                throw new InvalidOperationException("Keep this group window in the foreground, then try again.");
            byte Allowed;
            Marshal.ThrowExceptionForHR(Method<BooleanMethod>(Manager, 7)(Manager, out Allowed));
            if (Allowed == 0) throw new InvalidOperationException("Windows is not allowing a pin request now. Use Show shortcut or retry later.");
            // Only the explicit button action calls this slot. Never call it in automated probes.
            return ReadAsync(10, 120000, Cancellation);
        }

        private async Task<bool> ReadAsync(int Slot, int Timeout, CancellationToken Cancellation)
        {
            CheckThread();
            Cancellation.ThrowIfCancellationRequested();
            if (Busy) throw new InvalidOperationException("A pin operation is already in progress.");
            Busy = true;
            IntPtr Operation = IntPtr.Zero, Info = IntPtr.Zero;
            bool Complete = false;
            try
            {
                Marshal.ThrowExceptionForHR(Method<ObjectMethod>(Manager, Slot)(Manager, out Operation));
                Guid InfoId = new Guid("00000036-0000-0000-c000-000000000046");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(Operation, ref InfoId, out Info));
                Stopwatch Timer = Stopwatch.StartNew();
                while (true)
                {
                    Cancellation.ThrowIfCancellationRequested();
                    int Status;
                    Marshal.ThrowExceptionForHR(Method<IntegerMethod>(Info, 7)(Info, out Status));
                    if (Status != 0)
                    {
                        Complete = true;
                        if (Status == 2) throw new OperationCanceledException();
                        if (Status != 1)
                        {
                            int Error;
                            Marshal.ThrowExceptionForHR(Method<IntegerMethod>(Info, 8)(Info, out Error));
                            Marshal.ThrowExceptionForHR(Error);
                            throw new InvalidOperationException("Windows could not complete the pin operation.");
                        }
                        byte Result;
                        Marshal.ThrowExceptionForHR(Method<BooleanMethod>(Operation, 8)(Operation, out Result));
                        return Result != 0;
                    }
                    if (Timer.ElapsedMilliseconds >= Timeout)
                        throw new TimeoutException("Windows did not finish the pin operation. Check the taskbar before retrying.");
                    await Task.Delay(50, Cancellation);
                }
            }
            finally
            {
                if (Info != IntPtr.Zero)
                {
                    // Cleanup must not replace the original result/error.
                    if (!Complete) Method<VoidMethod>(Info, 9)(Info);
                    else Method<VoidMethod>(Info, 10)(Info);
                    Marshal.Release(Info);
                }
                if (Operation != IntPtr.Zero) Marshal.Release(Operation);
                Busy = false;
            }
        }

        public void Dispose()
        {
            if (Busy) throw new InvalidOperationException("Wait for the pin operation before releasing its session.");
            if (Manager != IntPtr.Zero) { Marshal.Release(Manager); Manager = IntPtr.Zero; }
            if (Initialized) { RoUninitialize(); Initialized = false; }
        }

        internal static bool IsExpectedError(Exception Error)
        {
            return GroupStore.IsDataError(Error) || Error is TimeoutException ||
                Error is DllNotFoundException || Error is EntryPointNotFoundException ||
                Error is System.ComponentModel.Win32Exception;
        }
    }
}
