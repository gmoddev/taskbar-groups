using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace client.Classes
{
    // Read-only discovery. Explorer's shortcut directory is not its ordered pin list.
    internal static class PinnedApps
    {
        private static string Identity(LaunchPlan Plan) { return Plan.Kind.ToString() + ":" + Plan.Target; }

        public static string SourceFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"); }
        }

        public static OrganizerImportResult Import(OrganizerDocument Document, string Source)
        { return ImportTaskbar(Document, Source, new List<ProgramShortcut>()); }

        public static OrganizerImportResult ImportTaskbar(OrganizerDocument Document, string Source, List<ProgramShortcut> Running)
        {
            List<ProgramShortcut> Items = new List<ProgramShortcut>();
            List<string> Receipts = new List<string>();
            int Unavailable = 0;
            if (Directory.Exists(Source)) MainPath.RejectReparsePoint(Source);
            foreach (string PathName in (Directory.Exists(Source) ? Directory.GetFiles(Source, "*.lnk") : new string[0]).OrderBy(Value => Value, StringComparer.OrdinalIgnoreCase))
            {
                string FullPath = Path.GetFullPath(PathName);
                if (Document.PinnedSources.Contains(FullPath, StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    MainPath.RejectReparsePoint(FullPath);
                    if (new FileInfo(FullPath).Length > 1048576) throw new InvalidDataException("Pinned shortcut is too large.");
                    byte[] Bytes = File.ReadAllBytes(FullPath);
                    string Key;
                    using (SHA256 Hash = SHA256.Create()) Key = BitConverter.ToString(Hash.ComputeHash(Bytes)).Replace("-", "");
                    string Folder = Path.Combine(MainPath.DataDirectory, "ImportedPins");
                    MainPath.GetFolder(Folder);
                    string SavedPath = Path.Combine(Folder, Key + ".lnk");
                    if (File.Exists(SavedPath)) MainPath.RejectReparsePoint(SavedPath);
                    if (!File.Exists(SavedPath))
                    {
                        string Temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
                        try
                        {
                            using (FileStream Stream = new FileStream(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            { Stream.Write(Bytes, 0, Bytes.Length); Stream.Flush(true); }
                            try { File.Move(Temporary, SavedPath); }
                            catch (IOException) { if (!File.Exists(SavedPath)) throw; }
                        }
                        finally { if (File.Exists(Temporary)) File.Delete(Temporary); }
                    }
                    MainPath.RejectReparsePoint(SavedPath);
                    if (!File.ReadAllBytes(SavedPath).SequenceEqual(Bytes)) throw new InvalidDataException("Pinned snapshot changed.");
                    ShellShortcut Link = ShellLink.ReadShortcut(SavedPath);
                    if ((Link.AppId ?? "").StartsWith("tjackenpacken.taskbarGroup.", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Link.Target, MainPath.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
                    ProgramShortcut Item = new ProgramShortcut { FilePath = SavedPath, name = Path.GetFileNameWithoutExtension(FullPath) };
                    LaunchService.Build(Item);
                    Items.Add(Item);
                    Receipts.Add(FullPath);
                }
                catch (Exception Error) when (IconService.IsExpectedError(Error))
                { Unavailable++; MainPath.Warn("[TaskbarGroups:PinnedImport] " + Path.GetFileName(FullPath) + ": " + Error.Message); }
            }
            HashSet<string> Targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProgramShortcut Existing in Document.State.Items.Concat(Items))
            {
                try { Targets.Add(Identity(LaunchService.Build(Existing))); }
                catch (Exception Error) when (IconService.IsExpectedError(Error)) { }
            }
            foreach (ProgramShortcut Item in Running)
            {
                try
                {
                    string Key = Identity(LaunchService.Build(Item));
                    string Receipt = "running:" + Key;
                    if (Document.PinnedSources.Contains(Receipt, StringComparer.OrdinalIgnoreCase)) continue;
                    Receipts.Add(Receipt);
                    if (Targets.Add(Key)) Items.Add(Item);
                }
                catch (Exception Error) when (IconService.IsExpectedError(Error)) { Unavailable++; }
            }
            OrganizerImportResult Result = OrganizerStore.ImportBatch(Document.Revision, Items, null, Document.State.Layout.Count, Receipts);
            Result.Skipped += Unavailable;
            return Result;
        }
    }
}
