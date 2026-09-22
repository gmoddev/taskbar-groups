using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;

namespace client.Classes
{
    internal static class MainPath
    {
        public static string ExecutablePath { get; private set; }
        public static string InstallDirectory { get; private set; }
        public static string DataDirectory { get; private set; }
        public static string ConfigDirectory { get { return Path.Combine(DataDirectory, "config"); } }
        public static string ShortcutDirectory { get { return Path.Combine(DataDirectory, "Shortcuts"); } }
        public static string ProfileDirectory { get { return Path.Combine(DataDirectory, "JITComp"); } }
        public static string LogDirectory { get { return Path.Combine(DataDirectory, "Logs"); } }
        public static string MigrationDirectory { get { return Path.Combine(DataDirectory, "Migration"); } }
        public static string LocalAppDataDirectory { get; private set; }
        public static List<string> StorageWarnings { get; private set; } = new List<string>();

        public static void Initialize(string ExePath, string LocalAppData)
        {
            StorageWarnings.Clear();
            if (string.IsNullOrWhiteSpace(LocalAppData) || !Path.IsPathRooted(LocalAppData))
                throw new IOException("The current user's LocalAppData folder is unavailable.");
            LocalAppDataDirectory = Path.GetFullPath(LocalAppData);
            ExecutablePath = Path.GetFullPath(ExePath);
            InstallDirectory = Path.GetDirectoryName(ExecutablePath);
            DataDirectory = Path.Combine(LocalAppDataDirectory, "TaskbarGroups");
            GetFolder(DataDirectory);
            GetFolder(ConfigDirectory);
            GetFolder(ShortcutDirectory);
            GetFolder(ProfileDirectory);
            GetFolder(LogDirectory);
            GetFolder(MigrationDirectory);
            // A real startup log also verifies existing read-only state fails quietly.
            using (FileStream LogFile = new FileStream(Path.Combine(LogDirectory, "Storage.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (StreamWriter Writer = new StreamWriter(LogFile))
                Writer.WriteLine(DateTime.UtcNow.ToString("O") + " [TaskbarGroups:Storage] Starting with user state at " + DataDirectory);
            LegacyMigration.Import();
        }

        public static string GetFolder(string Folder)
        {
            Directory.CreateDirectory(Folder);
            RejectReparsePoint(Folder);
            return Folder;
        }

        public static void RejectReparsePoint(string Item)
        {
            if ((File.GetAttributes(Item) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked storage paths are not supported: " + Item);
        }

        public static string GetGroupDirectory(string Name)
        {
            if (!IsValidGroupName(Name)) throw new IOException("Invalid group folder name: " + Name);
            string Folder = Path.Combine(ConfigDirectory, Name);
            if (Directory.Exists(Folder)) RejectReparsePoint(Folder);
            return Folder;
        }

        public static bool IsValidGroupName(string Name)
        {
            return !(string.IsNullOrWhiteSpace(Name) || Name == "." || Name == ".." ||
                Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Name.EndsWith(".", StringComparison.Ordinal) || Name.EndsWith(" ", StringComparison.Ordinal) ||
                Regex.IsMatch(Name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase));
        }

        public static string ResolveGroupDirectory(string Group)
        {
            if (Path.IsPathRooted(Group))
            {
                string FullPath = Path.GetFullPath(Group).TrimEnd(Path.DirectorySeparatorChar);
                if (!string.Equals(Path.GetDirectoryName(FullPath), ConfigDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Group must be inside the current user's configuration folder.");
                return GetGroupDirectory(Path.GetFileName(FullPath));
            }
            if (Group.StartsWith("config\\", StringComparison.OrdinalIgnoreCase)) Group = Group.Substring(7);
            return GetGroupDirectory(Group);
        }

        public static string GetShortcutPath(string Name)
        {
            GetGroupDirectory(Name);
            return Path.Combine(ShortcutDirectory, Regex.Replace(Name, @"(_)+", " ") + ".lnk");
        }

        public static bool IsStorageError(Exception Error)
        {
            return Error is IOException || Error is UnauthorizedAccessException || Error is SecurityException;
        }

        public static void Warn(string Message)
        {
            StorageWarnings.Add(Message);
            Log(Message);
        }

        public static void Log(string Message, string Subsystem = "Storage")
        {
            string Line = DateTime.UtcNow.ToString("O") + " [TaskbarGroups:" + Subsystem + "] " + Message + Environment.NewLine;
            Trace.WriteLine(Line);
            try { File.AppendAllText(Path.Combine(LogDirectory, "Storage.log"), Line); return; }
            catch (Exception Error) when (IsStorageError(Error) || Error is ArgumentException) { }
            // A blocked application folder must not trigger a dialog or an install-directory write.
            try { File.AppendAllText(Path.Combine(LocalAppDataDirectory, "TaskbarGroups-Startup.log"), Line); }
            catch (Exception Error) when (IsStorageError(Error) || Error is ArgumentException) { }
        }
    }
}
