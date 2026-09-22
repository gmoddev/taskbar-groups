using System;
using System.IO;
using System.Linq;

namespace client.Classes
{
    internal static class GroupPublishing
    {
        // Prepare a projection of committed data, never an arbitrary caller-supplied path.
        public static string GetShortcut(string Revision, string GroupId)
        {
            OrganizerDocument Current = OrganizerStore.Load();
            if (Current.Revision != Revision) throw new IOException("The layout changed. Reload and select the group again.");
            if (!Current.State.Layout.Any(Value => Value.IsGroup && Value.Id == GroupId))
                throw new InvalidDataException("Select an active group to prepare its shortcut.");
            Category Group = OrganizerStore.LoadGroup(GroupId);
            if (Group == null) throw new InvalidDataException("The group is unavailable.");
            string Committed = Path.Combine(Group.ResourceDirectory, "Launcher.lnk");
            string Shortcut = GroupStore.GetLink(Group);
            MainPath.GetFolder(MainPath.ShortcutDirectory);
            GroupStore.RepairLink(Group);
            MainPath.RejectReparsePoint(Shortcut);
            if (!File.ReadAllBytes(Committed).SequenceEqual(File.ReadAllBytes(Shortcut)))
                throw new IOException("The group shortcut could not be updated. Check write access and use Reload to retry.");
            // Recheck a concurrent writer before offering this snapshot's shortcut.
            if (OrganizerStore.Load().Revision != Revision)
                throw new IOException("The layout changed while preparing the shortcut. Reload and try again.");
            return Shortcut;
        }

        public static LaunchResult OpenPinWindow(string Revision, string GroupId)
        {
            try
            {
                GroupStore.Token(Revision);
                GroupStore.Token(GroupId);
                GetShortcut(Revision, GroupId);
                return LaunchService.Launch(new ProgramShortcut { FilePath = MainPath.ExecutablePath,
                    Arguments = "--pin-group " + GroupId + " " + Revision, WorkingDirectory = MainPath.InstallDirectory });
            }
            catch (Exception Error) when (GroupStore.IsDataError(Error))
            {
                MainPath.Log(Error.Message, "Publishing");
                return new LaunchResult { Error = Error.Message };
            }
        }

        // ProgramsFolder is the per-user Programs known folder, or a disposable test root.
        // Never mutate unrelated links or silently register groups during ordinary editing.
        public static string RegisterStartEntry(string Revision, string GroupId, string ProgramsFolder)
        {
            if (string.IsNullOrWhiteSpace(ProgramsFolder) || !Path.IsPathRooted(ProgramsFolder))
                throw new IOException("The per-user Start menu is unavailable.");
            using (FileStream Lock = GroupStore.GetLock())
            {
                GetShortcut(Revision, GroupId);
                Category Group = OrganizerStore.LoadGroup(GroupId);
                string Folder = Path.Combine(Path.GetFullPath(ProgramsFolder), "TaskbarGroups");
                for (DirectoryInfo Parent = new DirectoryInfo(Folder); Parent != null; Parent = Parent.Parent)
                    if (Parent.Exists) MainPath.RejectReparsePoint(Parent.FullName);
                MainPath.GetFolder(Folder);
                string Destination = Path.Combine(Folder, GroupStore.Token(GroupId) + ".lnk");
                byte[] Bytes = File.ReadAllBytes(Path.Combine(Group.ResourceDirectory, "Launcher.lnk"));
                if (File.Exists(Destination))
                {
                    MainPath.RejectReparsePoint(Destination);
                    ShellShortcut Existing = ShellLink.ReadShortcut(Destination);
                    if (Existing.AppId != "tjackenpacken.taskbarGroup.menu." + Group.AppIdKey ||
                        Existing.Arguments != Group.Id ||
                        !string.Equals(Existing.Target, MainPath.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("A different Start-menu shortcut already uses this location. It was left unchanged.");
                    if (File.ReadAllBytes(Destination).SequenceEqual(Bytes)) return Destination;
                }
                string Temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".pending");
                try
                {
                    using (FileStream Output = new FileStream(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        Output.Write(Bytes, 0, Bytes.Length);
                        Output.Flush(true);
                    }
                    if (OrganizerStore.Load().Revision != Revision)
                        throw new IOException("The layout changed. Reopen the pin window.");
                    if (File.Exists(Destination)) File.Replace(Temporary, Destination, null);
                    else File.Move(Temporary, Destination);
                    if (!File.ReadAllBytes(Destination).SequenceEqual(Bytes))
                        throw new IOException("The Start-menu shortcut could not be verified.");
                    return Destination;
                }
                finally
                {
                    if (File.Exists(Temporary))
                        try { File.Delete(Temporary); }
                        catch (Exception Error) when (MainPath.IsStorageError(Error)) { MainPath.Log(Error.Message, "Publishing"); }
                }
            }
        }

        public static LaunchResult ShowShortcut(string Revision, string GroupId)
        {
            try
            {
                string Shortcut = GetShortcut(Revision, GroupId);
                return LaunchService.Launch(new ProgramShortcut {
                    FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    Arguments = "/select,\"" + Shortcut + "\"", WorkingDirectory = "" });
            }
            catch (Exception Error) when (GroupStore.IsDataError(Error))
            {
                MainPath.Log(Error.Message, "Publishing");
                return new LaunchResult { Success = false, Error = Error.Message };
            }
        }
    }
}
