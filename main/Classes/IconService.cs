using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace client.Classes
{
    internal static class IconService
    {
        internal static Func<string, Bitmap> PackageIconOverride = null;
        public static bool IsExpectedError(Exception Error)
        {
            return GroupStore.IsDataError(Error) || Error is OutOfMemoryException ||
                Error is NullReferenceException || Error is System.ComponentModel.Win32Exception;
        }

        public static Bitmap GetIcon(ProgramShortcut Item)
        {
            bool Success;
            return GetIcon(Item, out Success);
        }

        public static Bitmap GetIcon(ProgramShortcut Item, out bool Success)
        {
            try
            {
                if (Item == null || string.IsNullOrWhiteSpace(Item.FilePath))
                    throw new InvalidDataException("Missing icon target.");
                Bitmap Result = Item.isWindowsApp ? PackageIcon(Item.FilePath) :
                    Extract(Item.FilePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (Result == null) throw new InvalidDataException("No icon was returned.");
                Success = true;
                return Result;
            }
            catch (Exception Error) when (IsExpectedError(Error))
            {
                MainPath.Log("Icon unavailable: " + Error.Message, "Icons");
                Success = false;
                return new Bitmap(global::client.Properties.Resources.Error);
            }
        }

        private static Bitmap PackageIcon(string Identity)
        {
            const string Prefix = @"shell:AppsFolder\";
            if (Identity.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) Identity = Identity.Substring(Prefix.Length);
            return PackageIconOverride == null ? handleWindowsApp.getWindowsAppIcon(Identity, true) : PackageIconOverride(Identity);
        }

        private static Bitmap Extract(string Target, HashSet<string> Links)
        {
            Uri Address;
            if (Uri.TryCreate(Target, UriKind.Absolute, out Address) && !Address.IsFile)
                return SystemIcons.Application.ToBitmap();
            string PathName = LaunchService.AbsolutePath(Target);
            if (Directory.Exists(PathName))
                using (Icon Folder = handleFolder.GetFolderIcon(PathName)) return Folder.ToBitmap();
            if (!File.Exists(PathName)) throw new FileNotFoundException("Icon target is missing.");
            if (string.Equals(Path.GetExtension(PathName), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (Links.Count >= 8 || !Links.Add(PathName)) throw new InvalidDataException("Cyclic icon shortcut.");
                ShellShortcut Link = ShellLink.ReadShortcut(PathName);
                if (!string.IsNullOrWhiteSpace(Link.IconPath))
                {
                    try { return ResourceIcon(LaunchService.AbsolutePath(Link.IconPath), Link.IconIndex); }
                    catch (Exception Error) when (IsExpectedError(Error)) { MainPath.Log(Error.Message, "Icons"); }
                }
                if (string.IsNullOrWhiteSpace(Link.Target)) return PackageIcon(Link.AppId ?? "");
                return Extract(Link.Target, Links);
            }
            if (string.Equals(Path.GetExtension(PathName), ".url", StringComparison.OrdinalIgnoreCase))
                return SystemIcons.Application.ToBitmap();
            using (Icon Picture = Icon.ExtractAssociatedIcon(PathName))
            {
                if (Picture == null) throw new InvalidDataException("No associated icon.");
                return Picture.ToBitmap();
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint ExtractIconExW(string PathName, int Index, out IntPtr Large, out IntPtr Small, uint Count);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr Icon);

        private static Bitmap ResourceIcon(string PathName, int Index)
        {
            IntPtr Large = IntPtr.Zero, Small = IntPtr.Zero;
            try
            {
                uint Count = ExtractIconExW(PathName, Index, out Large, out Small, 1);
                if (Count == 0 || Count == uint.MaxValue || Large == IntPtr.Zero)
                    throw new InvalidDataException("The shortcut's icon resource is unavailable.");
                using (Icon Picture = Icon.FromHandle(Large)) return Picture.ToBitmap();
            }
            finally
            {
                if (Large != IntPtr.Zero) DestroyIcon(Large);
                if (Small != IntPtr.Zero) DestroyIcon(Small);
            }
        }

        public static string GetName(ProgramShortcut Item)
        {
            if (Item == null) return "Unavailable item";
            if (!string.IsNullOrWhiteSpace(Item.name)) return Item.name;
            try
            {
                if (Item.isWindowsApp) return Item.FilePath ?? "Unavailable app";
                return Path.GetFileNameWithoutExtension(Item.FilePath) ?? "Unavailable item";
            }
            catch (ArgumentException) { return "Unavailable item"; }
        }
    }
}
