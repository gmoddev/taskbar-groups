using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace client.Classes
{
    internal static class LegacyMigration
    {
        public static void Import()
        {
            string Source = Path.Combine(MainPath.InstallDirectory, "config");
            if (string.Equals(Source, MainPath.ConfigDirectory, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Source)) return;
            try
            {
                MainPath.RejectReparsePoint(Source);
                using (FileStream MigrationLock = GetLock())
                    foreach (string SourceGroup in Directory.GetDirectories(Source)) ImportGroup(SourceGroup);
            }
            catch (Exception Error) when (MainPath.IsStorageError(Error))
            {
                MainPath.Warn("Legacy import will be retried on next launch. Original files were retained. " + Error.Message);
            }
        }

        private static FileStream GetLock()
        {
            string LockPath = Path.Combine(MainPath.MigrationDirectory, "Import.lock");
            for (int Attempt = 0; ; Attempt++)
            {
                try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (Attempt < 20) { Thread.Sleep(100); }
            }
        }

        private static string GetReceipt(string SourceGroup)
        {
            using (SHA256 Hash = SHA256.Create())
            {
                byte[] Key = Hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(SourceGroup).ToUpperInvariant()));
                return Path.Combine(MainPath.MigrationDirectory, BitConverter.ToString(Key).Replace("-", "") + ".done");
            }
        }

        private static Category Validate(string Folder, string Name)
        {
            MainPath.RejectReparsePoint(Folder);
            string Config = Path.Combine(Folder, "ObjectData.xml");
            MainPath.RejectReparsePoint(Config);
            Category Group;
            XmlReaderSettings Settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
            using (XmlReader Reader = XmlReader.Create(Config, Settings))
                Group = (Category)new XmlSerializer(typeof(Category)).Deserialize(Reader);
            if (Group == null || !string.Equals(Group.Name, Name, StringComparison.OrdinalIgnoreCase) ||
                Group.ShortcutList == null || Group.ShortcutList.Count == 0 || Group.Width < 1 || Group.Width > 100 ||
                double.IsNaN(Group.Opacity) || double.IsInfinity(Group.Opacity) || Group.Opacity < 0 || Group.Opacity > 100)
                throw new InvalidDataException("Invalid legacy group configuration: " + Name);
            MainPath.GetGroupDirectory(Group.Name);
            foreach (ProgramShortcut Shortcut in Group.ShortcutList)
                if (Shortcut == null || string.IsNullOrWhiteSpace(Shortcut.FilePath))
                    throw new InvalidDataException("Invalid shortcut in legacy group: " + Name);
            Color Color = ImageFunctions.FromString(Group.ColorString);
            if (Color.IsEmpty || Color.A != 255) throw new InvalidDataException("Legacy group color must be opaque: " + Name);
            string ImagePath = Path.Combine(Folder, "GroupImage.png");
            string IconPath = Path.Combine(Folder, "GroupIcon.ico");
            MainPath.RejectReparsePoint(ImagePath);
            MainPath.RejectReparsePoint(IconPath);
            try
            {
                using (Image Picture = Image.FromFile(ImagePath)) { }
                using (Icon Picture = new Icon(IconPath)) { }
            }
            catch (OutOfMemoryException Error)
            {
                // GDI+ reports malformed/unsupported image data with this exception type.
                throw new InvalidDataException("Could not decode legacy group image: " + Name, Error);
            }
            return Group;
        }

        private static void ImportGroup(string SourceGroup)
        {
            string Receipt = GetReceipt(SourceGroup);
            if (File.Exists(Receipt)) return;
            string StageRoot = Path.Combine(MainPath.MigrationDirectory, Guid.NewGuid().ToString("N"));
            try
            {
                string Name = Path.GetFileName(SourceGroup);
                string Destination = MainPath.GetGroupDirectory(Name);
                Category Group;
                if (Directory.Exists(Destination))
                {
                    // Resumes an interruption after directory publication; never overwrites user data.
                    Group = Validate(Destination, Name);
                    MainPath.Log("Keeping existing user group during import: " + Name);
                }
                else
                {
                    Validate(SourceGroup, Name);
                    MainPath.GetFolder(StageRoot);
                    string StageGroup = Path.Combine(StageRoot, "Group");
                    CopyTree(SourceGroup, StageGroup);
                    Group = Validate(StageGroup, Name);
                    Directory.Move(StageGroup, Destination);
                }
                string Link = MainPath.GetShortcutPath(Group.Name);
                if (!File.Exists(Link))
                {
                    MainPath.GetFolder(StageRoot);
                    string StageLink = Path.Combine(StageRoot, "Group.lnk");
                    ShellLink.InstallShortcut(MainPath.ExecutablePath, "tjackenpacken.taskbarGroup.menu." + Group.Name,
                        Group.Name + " shortcut", MainPath.InstallDirectory, Path.Combine(Destination, "GroupIcon.ico"), StageLink, Group.Name);
                    File.Move(StageLink, Link);
                }
                MainPath.GetFolder(StageRoot);
                string StageReceipt = Path.Combine(StageRoot, "Receipt");
                File.WriteAllText(StageReceipt, "Imported or retained: " + SourceGroup + Environment.NewLine);
                File.Move(StageReceipt, Receipt);
                MainPath.Log("Legacy group import completed; source retained: " + Name);
            }
            catch (Exception Error) when (MainPath.IsStorageError(Error) || Error is InvalidDataException || Error is InvalidOperationException || Error is ArgumentException || Error is System.Runtime.InteropServices.ExternalException || Error is XmlException)
            {
                MainPath.Warn("Could not import " + Path.GetFileName(SourceGroup) + ". Original files were retained; relaunch after correcting the files. " + Error.Message);
            }
            finally
            {
                // Only this invocation's generated staging directory is disposable.
                if (Path.GetDirectoryName(StageRoot) == MainPath.MigrationDirectory && Directory.Exists(StageRoot))
                {
                    try { Directory.Delete(StageRoot, true); }
                    catch (Exception Error) when (MainPath.IsStorageError(Error)) { MainPath.Log("Could not remove import staging folder: " + Error.Message); }
                }
            }
        }

        private static void CopyTree(string Source, string Destination)
        {
            MainPath.RejectReparsePoint(Source);
            Directory.CreateDirectory(Destination);
            foreach (string FilePath in Directory.GetFiles(Source))
            {
                MainPath.RejectReparsePoint(FilePath);
                string Copy = Path.Combine(Destination, Path.GetFileName(FilePath));
                File.Copy(FilePath, Copy);
                File.SetAttributes(Copy, FileAttributes.Normal);
            }
            foreach (string Folder in Directory.GetDirectories(Source)) CopyTree(Folder, Path.Combine(Destination, Path.GetFileName(Folder)));
        }
    }
}
