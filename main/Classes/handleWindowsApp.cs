using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml;
using System.Collections.Generic;
using Windows.Management.Deployment;

namespace client.Classes
{
    class handleWindowsApp
    {
        public static Dictionary<string, string> fileDirectoryCache = new Dictionary<string, string>();

        private static PackageManager pkgManger = new PackageManager();
        public static Bitmap getWindowsAppIcon(string File, bool AlreadyAppID = false)
        {
            string Identity = AlreadyAppID ? File : GetLnkTarget(File);
            string[] Parts = Identity.Split('!');
            if (Parts.Length != 2) throw new InvalidDataException("Invalid packaged app identity.");
            return GetPackageIcon(findWindowsAppsFolder(Parts[0]), Parts[1]);
        }

        internal static Bitmap GetPackageIcon(string Folder, string AppId)
        {
            string Root = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            XmlDocument Manifest = new XmlDocument { XmlResolver = null };
            Manifest.Load(Path.Combine(Root, "AppxManifest.xml"));
            List<string> Logos = new List<string>();
            foreach (XmlNode App in Manifest.SelectNodes("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application']"))
            {
                if (App.Attributes["Id"] == null || App.Attributes["Id"].Value != AppId) continue;
                XmlNode Visual = App.SelectSingleNode("*[local-name()='VisualElements']");
                if (Visual == null) continue;
                foreach (string Name in new[] { "Square44x44Logo", "Square150x150Logo", "Logo" })
                    if (Visual.Attributes[Name] != null) Logos.Add(Visual.Attributes[Name].Value);
            }
            XmlNode StoreLogo = Manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='Logo']");
            if (StoreLogo != null) Logos.Add(StoreLogo.InnerText);
            foreach (string Logo in Logos)
            {
                if (string.IsNullOrWhiteSpace(Logo) || Logo.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) continue;
                string Exact = Path.GetFullPath(Path.Combine(Root, Logo.Replace('/', Path.DirectorySeparatorChar)));
                if (!Exact.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) continue;
                string DirectoryName = Path.GetDirectoryName(Exact);
                if (!Directory.Exists(DirectoryName)) continue;
                string Stem = Path.GetFileNameWithoutExtension(Exact), Extension = Path.GetExtension(Exact);
                IEnumerable<string> Candidates = new[] { Exact }.Concat(Directory.GetFiles(DirectoryName)
                    .Where(Value => Path.GetFileName(Value).StartsWith(Stem + ".", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetExtension(Value), Extension, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(Value => Value.IndexOf("targetsize-64", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ThenByDescending(Value => Value.IndexOf("scale-200", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ThenBy(Value => Value, StringComparer.OrdinalIgnoreCase));
                foreach (string Candidate in Candidates.Where(System.IO.File.Exists))
                {
                    try
                    {
                        using (MemoryStream Buffer = new MemoryStream(System.IO.File.ReadAllBytes(Candidate)))
                        using (Image Picture = Image.FromStream(Buffer))
                            return ImageFunctions.ResizeImage(Picture, 64, 64);
                    }
                    catch (Exception Error) when (IconService.IsExpectedError(Error))
                    { MainPath.Log("Package artwork skipped: " + Error.Message, "Icons"); }
                }
            }
            throw new InvalidDataException("No usable package logo was found.");
        }

        public static string GetLnkTarget(string lnkPath)
        {
            var shl = new Shell32.Shell();
            lnkPath = System.IO.Path.GetFullPath(lnkPath);
            var dir = shl.NameSpace(System.IO.Path.GetDirectoryName(lnkPath));
            var itm = dir.Items().Item(System.IO.Path.GetFileName(lnkPath));
            var lnk = (Shell32.ShellLinkObject)itm.GetLink;
            return lnk.Target.Path;
        }

        public static string findWindowsAppsFolder(string subAppName)
        {

            if (!fileDirectoryCache.ContainsKey(subAppName))
            {
                try
                {
                    IEnumerable<Windows.ApplicationModel.Package> packages = pkgManger.FindPackagesForUser("", subAppName);


                    String finalPath = packages.First().InstalledLocation.Path;
                    fileDirectoryCache[subAppName] = finalPath;
                    return finalPath;
                }
                catch (UnauthorizedAccessException) { };
                return "";
            }
            else
            {
                return fileDirectoryCache[subAppName];
            }
        }

        public static string findWindowsAppsName(string AppName)
        {
            String subAppName = AppName.Split('!')[0];
            String appPath = findWindowsAppsFolder(subAppName);

            

                // Load and read manifest to get the logo path
                XmlDocument appManifest = new XmlDocument();
            appManifest.Load(appPath + "\\AppxManifest.xml");

            XmlNamespaceManager appManifestNamespace = new XmlNamespaceManager(new NameTable());
            appManifestNamespace.AddNamespace("sm", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
            appManifestNamespace.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10");

            try
            {
                return appManifest.SelectSingleNode("/sm:Package/sm:Applications/sm:Application/uap:VisualElements", appManifestNamespace).Attributes.GetNamedItem("DisplayName").InnerText;
            } catch (Exception)
            {
                return appManifest.SelectSingleNode("/sm:Package/sm:Properties/sm:DisplayName", appManifestNamespace).InnerText;
            }
        }
    }
}
