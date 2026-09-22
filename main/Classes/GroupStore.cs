using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace client.Classes
{
    public class GroupPointer
    {
        public int SchemaVersion = 1;
        public string Revision;
        public bool Deleted;
    }

    // Only Current.xml is the commit point. Published generations are never edited.
    internal static class GroupStore
    {
        internal static Action<string> Checkpoint = null;
        private static void Step(string Name) { if (Checkpoint != null) Checkpoint(Name); }

        private static string Key(string Value)
        {
            using (SHA256 Hash = SHA256.Create())
                return new Guid(Hash.ComputeHash(Encoding.UTF8.GetBytes(Value)).Take(16).ToArray()).ToString("N");
        }

        internal static string Token(string Value)
        {
            Guid Id;
            if (!Guid.TryParseExact(Value, "N", out Id)) throw new InvalidDataException("Invalid storage identity.");
            return Value;
        }

        internal static T Read<T>(string PathName)
        {
            MainPath.RejectReparsePoint(PathName);
            XmlReaderSettings Settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
            using (FileStream File = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (XmlReader Reader = XmlReader.Create(File, Settings))
            {
                T Value = (T)new XmlSerializer(typeof(T)).Deserialize(Reader);
                if (ReferenceEquals(Value, null)) throw new InvalidDataException("Missing serialized storage object.");
                return Value;
            }
        }

        internal static void Write<T>(string PathName, T Value)
        {
            using (FileStream File = new FileStream(PathName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                new XmlSerializer(typeof(T)).Serialize(File, Value);
                File.Flush(true);
            }
        }

        public static bool IsDataError(Exception Error)
        {
            return MainPath.IsStorageError(Error) || Error is NotSupportedException || Error is InvalidDataException || Error is InvalidOperationException ||
                Error is ArgumentException || Error is XmlException || Error is System.Runtime.InteropServices.ExternalException;
        }

        private static string Generation(string Root, string Revision)
        {
            string Versions = Path.Combine(Root, "Versions");
            MainPath.RejectReparsePoint(Versions);
            string Folder = Path.Combine(Versions, Token(Revision));
            MainPath.RejectReparsePoint(Folder);
            return Folder;
        }

        private static Category ReadPointer(string Root, string PointerPath)
        {
            GroupPointer Pointer = Read<GroupPointer>(PointerPath);
            if (Pointer.SchemaVersion != 1) throw new NotSupportedException("Upgrade Taskbar Groups to read this storage version.");
            if (Pointer.Deleted) return null;
            return ReadGeneration(Path.GetFileName(Root), Pointer.Revision);
        }

        internal static Category ReadGeneration(string StoreKey, string Revision)
        {
            string Root = MainPath.GetGroupDirectory(StoreKey);
            string Folder = Generation(Root, Revision);
            Category Group = Read<Category>(Path.Combine(Folder, "ObjectData.xml"));
            if (Group.SchemaVersion > 2) throw new NotSupportedException("Upgrade Taskbar Groups to read this group version.");
            Validate(Group);
            if (Group.SchemaVersion != 2 || Group.Revision != Revision) throw new InvalidDataException("Group generation does not match its pointer.");
            Token(Group.Id);
            if (string.IsNullOrWhiteSpace(Group.AppIdKey)) throw new InvalidDataException("Missing launcher identity.");
            foreach (string FileName in new[] { "GroupImage.png", "GroupIcon.ico", "Launcher.lnk" })
                MainPath.RejectReparsePoint(Path.Combine(Folder, FileName));
            using (Image Picture = Image.FromFile(Path.Combine(Folder, "GroupImage.png"))) { }
            using (Icon Picture = new Icon(Path.Combine(Folder, "GroupIcon.ico"))) { }
            Group.StoreKey = Path.GetFileName(Root);
            Group.ResourceDirectory = Folder;
            return Group;
        }

        public static Category LoadFolder(string Root)
        {
            if (OrganizerStore.IsActive) return OrganizerStore.LoadGroup(Path.GetFileName(MainPath.ResolveGroupDirectory(Root)));
            return LoadUnmanaged(Root);
        }

        internal static Category LoadUnmanaged(string Root)
        {
            Root = MainPath.ResolveGroupDirectory(Root);
            string Pointer = Path.Combine(Root, "Current.xml");
            if (File.Exists(Pointer))
            {
                try { return ReadPointer(Root, Pointer); }
                catch (Exception Error) when (!(Error is NotSupportedException) && (IsDataError(Error) || Error is OutOfMemoryException))
                {
                    string PreviousPath = Path.Combine(Root, "Previous.xml");
                    Category Previous = File.Exists(PreviousPath) ? ReadPointer(Root, PreviousPath) : LoadLegacy(Root);
                    if (Previous == null) throw new IOException("No usable previous group generation.", Error);
                    if (Previous != null) Previous.Recovered = true;
                    MainPath.Warn("Recovered previous group generation: " + Path.GetFileName(Root) + ". " + Error.Message);
                    return Previous;
                }
            }
            return LoadLegacy(Root);
        }

        private static Category LoadLegacy(string Root)
        {
            string LegacyFile = Path.Combine(Root, "ObjectData.xml");
            if (!File.Exists(LegacyFile)) return null; // Unpublished first save.
            Category Legacy = Read<Category>(LegacyFile);
            Validate(Legacy);
            if (Legacy.SchemaVersion > 1) throw new InvalidDataException("Versioned group is missing its commit pointer.");
            Legacy.StoreKey = Path.GetFileName(Root);
            Legacy.Id = Key("Group:" + Legacy.StoreKey.ToUpperInvariant());
            Legacy.AppIdKey = Legacy.StoreKey;
            Legacy.ResourceDirectory = Root;
            using (SHA256 Hash = SHA256.Create())
                Legacy.Revision = BitConverter.ToString(Hash.ComputeHash(File.ReadAllBytes(LegacyFile))).Replace("-", "");
            for (int Index = 0; Index < Legacy.ShortcutList.Count; Index++)
                Legacy.ShortcutList[Index].Id = Key(Legacy.Id + ":" + Index);
            return Legacy;
        }

        public static IEnumerable<Category> LoadAll()
        {
            if (OrganizerStore.IsActive) return OrganizerStore.LoadGroups();
            return LoadUnmanagedGroups();
        }

        internal static IEnumerable<Category> LoadUnmanagedGroups()
        {
            List<Category> Groups = new List<Category>();
            foreach (string Folder in Directory.GetDirectories(MainPath.ConfigDirectory))
            {
                try { Category Group = LoadUnmanaged(Folder); if (Group != null) Groups.Add(Group); }
                catch (Exception Error) when (IsDataError(Error) || Error is OutOfMemoryException)
                { MainPath.Warn("Skipped unreadable group " + Path.GetFileName(Folder) + ": " + Error.Message); }
            }
            return Groups;
        }

        public static Category Load(string Identity)
        {
            if (OrganizerStore.IsActive)
            {
                string Key = Path.IsPathRooted(Identity) || Identity.StartsWith("config\\", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(MainPath.ResolveGroupDirectory(Identity)) : Identity;
                return OrganizerStore.LoadGroup(Key) ?? throw new IOException("Group was deleted.");
            }
            if (Path.IsPathRooted(Identity) || Identity.StartsWith("config\\", StringComparison.OrdinalIgnoreCase))
                return LoadFolder(MainPath.ResolveGroupDirectory(Identity)) ?? throw new IOException("Group was deleted or not committed.");
            string Direct = MainPath.GetGroupDirectory(Identity);
            if (Directory.Exists(Direct)) return LoadFolder(Direct) ?? throw new IOException("Group was deleted or not committed.");
            Category Match = LoadAll().SingleOrDefault(Group => string.Equals(Group.Id, Identity, StringComparison.OrdinalIgnoreCase));
            return Match ?? throw new IOException("Group was not found.");
        }

        public static Category Copy(Category Group)
        {
            using (MemoryStream Buffer = new MemoryStream())
            {
                XmlSerializer Serializer = new XmlSerializer(typeof(Category));
                Serializer.Serialize(Buffer, Group);
                Buffer.Position = 0;
                Category Copy = (Category)Serializer.Deserialize(Buffer);
                Copy.StoreKey = Group.StoreKey;
                Copy.ResourceDirectory = Group.ResourceDirectory;
                Copy.Recovered = Group.Recovered;
                Copy.OrganizerRevision = Group.OrganizerRevision;
                return Copy;
            }
        }

        private static void Validate(Category Group, bool RequireIds = true)
        {
            if (Group == null || string.IsNullOrWhiteSpace(Group.Name) || Group.Name.Length > 200 ||
                Group.ShortcutList == null || Group.ShortcutList.Count == 0 || Group.Width < 1 || Group.Width > 100 ||
                double.IsNaN(Group.Opacity) || double.IsInfinity(Group.Opacity) || Group.Opacity < 0 || Group.Opacity > 100)
                throw new InvalidDataException("Invalid group metadata.");
            Color Background = ImageFunctions.FromString(Group.ColorString);
            if (Background.IsEmpty || Background.A != 255) throw new InvalidDataException("Group color must be opaque.");
            XmlConvert.VerifyXmlChars(Group.Name);
            foreach (ProgramShortcut Item in Group.ShortcutList)
                if (Item == null || string.IsNullOrWhiteSpace(Item.FilePath)) throw new InvalidDataException("Invalid group member.");
            if (RequireIds && Group.SchemaVersion == 2)
            {
                HashSet<string> Ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ProgramShortcut Item in Group.ShortcutList)
                    if (!Ids.Add(Token(Item.Id))) throw new InvalidDataException("Duplicate item identity.");
            }
        }

        internal static FileStream GetLock()
        {
            string LockPath = Path.Combine(MainPath.DataDirectory, "Groups.lock");
            if (File.Exists(LockPath)) MainPath.RejectReparsePoint(LockPath);
            for (int Attempt = 0; ; Attempt++)
            {
                try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (Attempt < 10) { Thread.Sleep(50); }
            }
        }

        private static bool CheckRevision(Category Group, string Root)
        {
            Category Current = Directory.Exists(Root) ? LoadFolder(Root) : null;
            if (Group.StoreKey == null)
            {
                if (Current != null || File.Exists(Path.Combine(Root, "Current.xml"))) throw new IOException("Group identity already exists.");
            }
            else if (Current == null || Current.Id != Group.Id || Current.Revision != Group.Revision || Current.AppIdKey != Group.AppIdKey)
                throw new IOException("This group changed in another window. Reopen it before saving.");
            return Current != null && Current.Recovered;
        }

        private static void Publish(string Root, GroupPointer Pointer, bool Recovered)
        {
            string Pending = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".pending");
            Write(Pending, Pointer);
            Step("PointerFlushed");
            string Current = Path.Combine(Root, "Current.xml");
            if (File.Exists(Current))
            {
                MainPath.RejectReparsePoint(Current);
                string Backup = Path.Combine(Root, Recovered ? "Damaged.xml" : "Previous.xml");
                if (File.Exists(Backup)) MainPath.RejectReparsePoint(Backup);
                File.Replace(Pending, Current, Backup);
            }
            else File.Move(Pending, Current);
        }

        public static string GetLink(Category Group) { return Path.Combine(MainPath.ShortcutDirectory, Token(Group.Id) + ".lnk"); }

        public static void RepairLink(Category Group)
        {
            if (Group.SchemaVersion != 2) return;
            string Temporary = Path.Combine(MainPath.ShortcutDirectory, Guid.NewGuid().ToString("N") + ".pending");
            try
            {
                string Link = GetLink(Group);
                File.Copy(Path.Combine(Group.ResourceDirectory, "Launcher.lnk"), Temporary);
                using (FileStream File = new FileStream(Temporary, FileMode.Open, FileAccess.Write)) File.Flush(true);
                if (File.Exists(Link)) { MainPath.RejectReparsePoint(Link); File.Replace(Temporary, Link, null); }
                else File.Move(Temporary, Link);
            }
            catch (Exception Error) when (IsDataError(Error)) { MainPath.Warn("Group saved; launcher publication will be retried: " + Error.Message); }
            finally { try { if (File.Exists(Temporary)) File.Delete(Temporary); } catch (Exception Error) when (MainPath.IsStorageError(Error)) { MainPath.Log(Error.Message); } }
        }

        internal static Category Stage(Category Group, Image Picture)
        {
            Validate(Group, false);
            Category Next = Copy(Group);
            Next.Id = Group.Id ?? Guid.NewGuid().ToString("N");
            Token(Next.Id);
            Next.AppIdKey = Group.AppIdKey ?? Next.Id;
            Next.SchemaVersion = 2;
            Next.Revision = Guid.NewGuid().ToString("N");
            HashSet<string> ItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProgramShortcut Item in Next.ShortcutList)
            {
                Item.Id = Item.Id ?? Guid.NewGuid().ToString("N");
                if (!ItemIds.Add(Token(Item.Id))) throw new InvalidDataException("Duplicate item identity.");
            }
            string StoreKey = Group.StoreKey ?? Next.Id;
            string Root = MainPath.GetGroupDirectory(StoreKey);
            MainPath.GetFolder(Root);
            string Versions = MainPath.GetFolder(Path.Combine(Root, "Versions"));
            string Folder = MainPath.GetFolder(Path.Combine(Versions, Next.Revision));
            Write(Path.Combine(Folder, "ObjectData.xml"), Next);
            Step("ConfigurationFlushed");
            using (Bitmap Resized = ImageFunctions.ResizeImage(Picture, 256, 256))
            {
                Resized.Save(Path.Combine(Folder, "GroupImage.png"), ImageFormat.Png);
                using (FileStream File = new FileStream(Path.Combine(Folder, "GroupIcon.ico"), FileMode.CreateNew))
                {
                    List<Bitmap> Frames = new List<Bitmap>();
                    try
                    {
                        foreach (int Size in new[] { 16, 32, 48, 64, 128, 256 })
                            Frames.Add(ImageFunctions.ResizeImage(Resized, Size, Size));
                        IconFactory.SavePngsAsIcon(Frames, File);
                    }
                    finally { foreach (Bitmap Frame in Frames) Frame.Dispose(); }
                }
            }
            Step("ImagesWritten");
            ShellLink.InstallShortcut(MainPath.ExecutablePath, "tjackenpacken.taskbarGroup.menu." + Next.AppIdKey,
                Next.Name, MainPath.InstallDirectory, Path.Combine(Folder, "GroupIcon.ico"), Path.Combine(Folder, "Launcher.lnk"), Next.Id);
            foreach (string PathName in Directory.GetFiles(Folder))
                using (FileStream File = new FileStream(PathName, FileMode.Open, FileAccess.Write)) File.Flush(true);
            Step("ResourcesFlushed");
            // Validate the full generation before allowing it to become current.
            string ValidationPointer = Path.Combine(Folder, "Validation.xml");
            Write(ValidationPointer, new GroupPointer { Revision = Next.Revision });
            ReadPointer(Root, ValidationPointer);
            Step("Validated");
            Next.StoreKey = StoreKey;
            Next.ResourceDirectory = Folder;
            Next.Recovered = false;
            return Next;
        }

        public static void Save(Category Group, Image Picture)
        {
            if (OrganizerStore.IsActive) { OrganizerStore.SaveGroup(Group, Picture); return; }
            Step("BeforeWriteLock");
            using (FileStream Lock = GetLock())
            {
                // Activation shares this lock. Recheck after acquiring it before writing old pointers.
                if (!OrganizerStore.IsActive)
                {
                    Category Candidate = Copy(Group);
                    Candidate.Id = Group.Id ?? Guid.NewGuid().ToString("N");
                    string Root = MainPath.GetGroupDirectory(Candidate.StoreKey ?? Candidate.Id);
                    bool Recovered = CheckRevision(Candidate, Root);
                    Category Next = Stage(Candidate, Picture);
                    Root = MainPath.GetGroupDirectory(Next.StoreKey);
                    Publish(Root, new GroupPointer { Revision = Next.Revision }, Recovered);
                    Adopt(Group, Next);
                    Step("Committed");
                    RepairLink(Group);
                    return;
                }
            }
            OrganizerStore.SaveGroup(Group, Picture);
        }

        internal static void Adopt(Category Group, Category Next)
        {
            Group.Id = Next.Id;
            Group.AppIdKey = Next.AppIdKey;
            Group.SchemaVersion = Next.SchemaVersion;
            Group.Revision = Next.Revision;
            Group.ShortcutList = Next.ShortcutList;
            Group.StoreKey = Next.StoreKey;
            Group.ResourceDirectory = Next.ResourceDirectory;
            Group.Recovered = Next.Recovered;
            Group.OrganizerRevision = Next.OrganizerRevision;
        }

        public static void Delete(Category Group)
        {
            if (OrganizerStore.IsActive) { OrganizerStore.DeleteGroup(Group); return; }
            Step("BeforeWriteLock");
            using (FileStream Lock = GetLock())
            {
                if (!OrganizerStore.IsActive)
                {
                    string Root = MainPath.GetGroupDirectory(Group.StoreKey);
                    bool Recovered = CheckRevision(Group, Root);
                    Publish(Root, new GroupPointer { Deleted = true, Revision = Group.Revision }, Recovered);
                    // Keep immutable generations and old pin resources; deletion is a tombstone.
                    return;
                }
            }
            OrganizerStore.DeleteGroup(Group);
        }
    }
}
