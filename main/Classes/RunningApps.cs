using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace client.Classes
{
    internal static class RunningApps
    {
        private delegate bool WindowCallback(IntPtr Window, IntPtr Data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback Callback, IntPtr Data);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr Parent, WindowCallback Callback, IntPtr Data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr Window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr Window, uint Command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr Window, out uint Id);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr Window, int Index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr Window, StringBuilder Name, int Size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr Window, int Attribute, out int Value, int Size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint Access, bool Inherit, uint Id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr Handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr Process, uint Flags, StringBuilder Name, ref uint Size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr Process, ref uint Size, StringBuilder Name);

        internal static bool IsCandidate(bool Visible, bool Cloaked, bool Owned, int Style)
        { return Visible && !Cloaked && (Style & 0x80) == 0 && (!Owned || (Style & 0x40000) != 0); }

        internal static ProgramShortcut ParseRelaunch(string Command)
        {
            if (string.IsNullOrWhiteSpace(Command)) return null;
            Command = Environment.ExpandEnvironmentVariables(Command.Trim());
            if (File.Exists(Command)) return new ProgramShortcut { FilePath = Command };
            int End = Command[0] == '"' ? Command.IndexOf('"', 1) : Command.IndexOf(' ');
            if (Command[0] == '"' && End < 0) return null;
            if (End < 0) End = Command.Length;
            string Target = Command[0] == '"' ? Command.Substring(1, End - 1) : Command.Substring(0, End);
            int Arguments = End + (Command[0] == '"' ? 1 : 0);
            return new ProgramShortcut { FilePath = Target, Arguments = Command.Substring(Math.Min(Arguments, Command.Length)).TrimStart() };
        }

        public static List<ProgramShortcut> Read()
        {
            List<ProgramShortcut> Items = new List<ProgramShortcut>();
            HashSet<string> Seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EnumWindows((Window, Data) => {
                try
                {
                    int Cloaked;
                    if (!IsCandidate(IsWindowVisible(Window), DwmGetWindowAttribute(Window, 14, out Cloaked, 4) == 0 && Cloaked != 0,
                        GetWindow(Window, 4) != IntPtr.Zero, GetWindowLong(Window, -20))) return true;
                    StringBuilder Class = new StringBuilder(256);
                    GetClassName(Window, Class, Class.Capacity);
                    if (new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW" }.Contains(Class.ToString())) return true;
                    ProgramShortcut Item = ReadWindow(Window);
                    if (Item != null && Seen.Add((Item.isWindowsApp ? "package:" : "exe:") + Item.FilePath)) Items.Add(Item);
                }
                catch (Exception Error) when (IconService.IsExpectedError(Error)) { MainPath.Log("Window discovery skipped: " + Error.Message, "Discovery"); }
                return true;
            }, IntPtr.Zero);
            return Items.OrderBy(Value => Value.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ProgramShortcut ReadWindow(IntPtr Window)
        {
            uint Id;
            GetWindowThreadProcessId(Window, out Id);
            using (Process Current = Process.GetCurrentProcess()) if (Id == Current.Id) return null;
            IntPtr ProcessHandle = OpenProcess(0x1000, false, Id);
            if (ProcessHandle == IntPtr.Zero) return null;
            string Target, AppId;
            try
            {
                StringBuilder PathName = new StringBuilder(32768);
                uint Size = (uint)PathName.Capacity;
                if (!QueryFullProcessImageName(ProcessHandle, 0, PathName, ref Size)) return null;
                Target = PathName.ToString();
                StringBuilder Identity = new StringBuilder(1024);
                Size = (uint)Identity.Capacity;
                AppId = GetApplicationUserModelId(ProcessHandle, ref Size, Identity) == 0 ? Identity.ToString() : null;
            }
            finally { CloseHandle(ProcessHandle); }
            string FileName = Path.GetFileName(Target);
            if (FileName.Equals("TaskbarGroups.exe", StringComparison.OrdinalIgnoreCase)) return null;
            string WindowId = ShellLink.ReadWindowProperty(Window, 5);
            if (FileName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) && !(WindowId ?? "").Contains("!"))
            {
                ProgramShortcut Hosted = null;
                EnumChildWindows(Window, (Child, Data) => {
                    uint ChildId; GetWindowThreadProcessId(Child, out ChildId);
                    if (ChildId != Id && Hosted == null) Hosted = ReadWindow(Child);
                    return Hosted == null;
                }, IntPtr.Zero);
                return Hosted;
            }
            if ((WindowId ?? "").StartsWith("tjackenpacken.taskbarGroup.", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.IsNullOrEmpty(AppId) && (WindowId ?? "").Contains("!")) AppId = WindowId;
            ProgramShortcut Item = string.IsNullOrEmpty(AppId) ? ParseRelaunch(ShellLink.ReadWindowProperty(Window, 2)) :
                new ProgramShortcut { FilePath = AppId, isWindowsApp = true };
            if (Item == null)
            {
                // Steam's taskbar window belongs to its embedded browser, not its launcher.
                if (FileName.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string Client = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Target), "..", "..", "..", "steam.exe"));
                    if (!File.Exists(Client)) return null;
                    Target = Client;
                }
                Item = new ProgramShortcut { FilePath = Target };
            }
            Item.name = FileVersionInfo.GetVersionInfo(Item.isWindowsApp ? Target : Item.FilePath).FileDescription;
            if (string.IsNullOrWhiteSpace(Item.name)) Item.name = Path.GetFileNameWithoutExtension(Target);
            LaunchService.Build(Item);
            return Item;
        }
    }
}
