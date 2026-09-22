using client.Forms;
using System;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using client.Classes;

namespace client
{
    static class client
    {
        [DllImport("shell32.dll", SetLastError = true)]
        static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [STAThread]
        static void Main()
        {
            Run(Environment.GetCommandLineArgs(), Assembly.GetExecutingAssembly().Location,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        internal static void Run(string[] Arguments, string ExePath, string LocalAppData)
        {
            try
            {
                MainPath.Initialize(ExePath, LocalAppData);
                System.Runtime.ProfileOptimization.SetProfileRoot(MainPath.ProfileDirectory);
            }
            catch (Exception Error) when (GroupStore.IsDataError(Error))
            {
                MainPath.Log("Startup stopped. Check write access/free space for " + MainPath.DataDirectory +
                    " and launch Taskbar Groups again. " + Error.Message);
                Environment.ExitCode = 1;
                return;
            }

            int CursorX = Cursor.Position.X;
            int CursorY = Cursor.Position.Y;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (Arguments.Length == 4 && Arguments[1] == "--pin-group")
                {
                    GroupStore.Token(Arguments[2]);
                    GroupStore.Token(Arguments[3]);
                    GroupPublishing.GetShortcut(Arguments[3], Arguments[2]);
                    Category Group = OrganizerStore.LoadGroup(Arguments[2]);
                    Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("tjackenpacken.taskbarGroup.menu." + Group.AppIdKey));
                    Application.Run(new frmGroupPin(Arguments[3], Arguments[2]));
                }
                else if (Arguments.Length > 1)
                {
                    Category Group = GroupStore.Load(Arguments[1]);
                    SetCurrentProcessExplicitAppUserModelID("tjackenpacken.taskbarGroup.menu." + Group.AppIdKey);
                    Application.Run(new frmMain(Arguments[1], CursorX, CursorY));
                }
                else
                {
                    SetCurrentProcessExplicitAppUserModelID("tjackenpacken.taskbarGroup.main");
                    bool UserProfile = string.Equals(Path.GetFullPath(LocalAppData), Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), StringComparison.OrdinalIgnoreCase);
                    Application.Run(new frmOrganizer(UserProfile ? PinnedApps.SourceFolder : null, UserProfile));
                }
            }
            catch (Exception Error) when (GroupStore.IsDataError(Error))
            {
                MainPath.Log("Could not open the requested group or application data. Check Storage.log for migration warnings and retry. " + Error.Message);
                Environment.ExitCode = 1;
            }
        }
    }
}
