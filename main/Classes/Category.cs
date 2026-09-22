using client.User_controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace client.Classes
{
    public class Category
    {
        public string Name;
        public string ColorString = System.Drawing.ColorTranslator.ToHtml(Color.FromArgb(31, 31, 31));
        public bool allowOpenAll = false;
        public List<ProgramShortcut> ShortcutList;
        public int Width; // not used aon
        public double Opacity = 10;
        public int SchemaVersion;
        public string Id;
        public string Revision;
        public string AppIdKey;
        [System.Xml.Serialization.XmlIgnore] public string StoreKey;
        [System.Xml.Serialization.XmlIgnore] public string ResourceDirectory;
        [System.Xml.Serialization.XmlIgnore] public bool Recovered;
        [System.Xml.Serialization.XmlIgnore] public string OrganizerRevision;
        [System.Xml.Serialization.XmlIgnore]
        public string CacheDirectory { get { return Path.Combine(MainPath.GetGroupDirectory(StoreKey), "Cache"); } }

        public Category(string PathName)
        {
            Category Loaded = GroupStore.Load(PathName);
            Name = Loaded.Name;
            ShortcutList = Loaded.ShortcutList;
            Width = Loaded.Width;
            ColorString = Loaded.ColorString;
            Opacity = Loaded.Opacity;
            allowOpenAll = Loaded.allowOpenAll;
            SchemaVersion = Loaded.SchemaVersion;
            Id = Loaded.Id;
            Revision = Loaded.Revision;
            AppIdKey = Loaded.AppIdKey;
            StoreKey = Loaded.StoreKey;
            ResourceDirectory = Loaded.ResourceDirectory;
            Recovered = Loaded.Recovered;
            OrganizerRevision = Loaded.OrganizerRevision;
        }

        public Category() { }
        public void CreateConfig(Image Picture) { GroupStore.Save(this, Picture); }

        public Bitmap LoadIconImage()
        {
            using (MemoryStream Buffer = new MemoryStream(File.ReadAllBytes(Path.Combine(ResourceDirectory, "GroupImage.png"))))
            using (Image Picture = Image.FromStream(Buffer))
                return new Bitmap(Picture);
        }

        // Caches are disposable and separate from immutable saved generations.
        public void cacheIcons() { MainPath.GetFolder(CacheDirectory); }

        public Image loadImageCache(ProgramShortcut Shortcut)
        {
            string CachePath = null;
            try
            {
                Guid ItemId;
                if (Shortcut == null || !Guid.TryParseExact(Shortcut.Id, "N", out ItemId))
                    throw new InvalidDataException("Missing item identity.");
                string TargetKey;
                string Key = "icons-v2|" + Shortcut.isWindowsApp + "|" + Shortcut.FilePath;
                string Target = Environment.ExpandEnvironmentVariables(Shortcut.FilePath ?? "");
                if (File.Exists(Target))
                {
                    FileInfo File = new FileInfo(Target);
                    Key += "|" + File.Length + "|" + File.LastWriteTimeUtc.Ticks;
                }
                using (System.Security.Cryptography.SHA256 Hash = System.Security.Cryptography.SHA256.Create())
                    TargetKey = BitConverter.ToString(Hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Key))).Replace("-", "").Substring(0, 16);
                CachePath = Path.Combine(CacheDirectory, Shortcut.Id + "-" + TargetKey + ".png");
                MainPath.GetFolder(CacheDirectory);
                if (File.Exists(CachePath))
                {
                    MainPath.RejectReparsePoint(CachePath);
                    // Decode and detach before releasing the file; corrupt caches fall through to extraction.
                    using (Image Picture = Image.FromFile(CachePath)) return new Bitmap(Picture);
                }
            }
            catch (Exception Error) when (IconService.IsExpectedError(Error)) { MainPath.Log(Error.Message, "Icons"); }

            bool Success;
            Bitmap Extracted = IconService.GetIcon(Shortcut, out Success);
            if (Success && CachePath != null)
            {
                string Temporary = CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    MainPath.GetFolder(CacheDirectory);
                    if (File.Exists(CachePath)) MainPath.RejectReparsePoint(CachePath);
                    using (FileStream File = new FileStream(Temporary, FileMode.CreateNew, FileAccess.Write))
                    {
                        Extracted.Save(File, ImageFormat.Png);
                        File.Flush(true);
                    }
                    if (File.Exists(CachePath)) File.Replace(Temporary, CachePath, null);
                    else File.Move(Temporary, CachePath);
                }
                catch (Exception Error) when (IconService.IsExpectedError(Error)) { MainPath.Log(Error.Message, "Icons"); }
                finally
                {
                    try { if (File.Exists(Temporary)) File.Delete(Temporary); }
                    catch (Exception Error) when (GroupStore.IsDataError(Error)) { MainPath.Log(Error.Message, "Icons"); }
                }
            }
            return Extracted;
        }
    }
}
