using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace client.Classes
{
    public enum LaunchKind { Executable, Directory, Uri, PackagedApp }

    public class LaunchPlan
    {
        public LaunchKind Kind;
        public string Target;
        public string Arguments;
        public string WorkingDirectory;
        public string SourceLink;
    }

    public class LaunchResult
    {
        public bool Success;
        public string Error;
    }

    internal static class LaunchService
    {
        // Verification seam only; no CLI/environment switch enables it in production.
        internal static Action<LaunchPlan> DispatchOverride = null;

        public static bool IsExpectedError(Exception Error)
        {
            return GroupStore.IsDataError(Error) || Error is Win32Exception || Error is FormatException;
        }

        internal static string AbsolutePath(string Value)
        {
            if (string.IsNullOrWhiteSpace(Value)) throw new InvalidDataException("A target path is required.");
            Value = Environment.ExpandEnvironmentVariables(Value);
            bool Drive = Value.Length >= 3 && char.IsLetter(Value[0]) && Value[1] == ':' && (Value[2] == '\\' || Value[2] == '/');
            if (!Drive && !Value.StartsWith(@"\\", StringComparison.Ordinal))
                throw new InvalidDataException("Use a full target path; relative paths and PATH searches are not supported.");
            return Path.GetFullPath(Value);
        }

        private static string WorkingDirectory(string Value, string Fallback)
        {
            string Folder = string.IsNullOrWhiteSpace(Value) ? Fallback : AbsolutePath(Value);
            if (!Directory.Exists(Folder)) throw new DirectoryNotFoundException("The configured working directory is unavailable.");
            return Folder;
        }

        private static LaunchPlan Packaged(string Target, string Arguments)
        {
            const string Prefix = @"shell:AppsFolder\";
            if (Target.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) Target = Target.Substring(Prefix.Length);
            if (!Regex.IsMatch(Target, @"^[A-Za-z0-9._-]+![A-Za-z0-9._-]+$"))
                throw new InvalidDataException("A packaged app requires a package-family!application identity.");
            return new LaunchPlan { Kind = LaunchKind.PackagedApp, Target = Target, Arguments = Arguments, WorkingDirectory = "" };
        }

        private static LaunchPlan UriPlan(string Target, string Arguments)
        {
            Uri Address;
            if (!Uri.TryCreate(Target, UriKind.Absolute, out Address) || Address.IsFile ||
                new[] { "shell", "javascript", "data" }.Contains(Address.Scheme, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported URI target.");
            if (!string.IsNullOrEmpty(Arguments)) throw new InvalidDataException("URI targets cannot take separate command-line arguments.");
            using (RegistryKey Protocol = Registry.ClassesRoot.OpenSubKey(Address.Scheme))
                if (Protocol == null || Protocol.GetValue("URL Protocol") == null)
                    throw new InvalidDataException("No registered handler for this URI scheme.");
            return new LaunchPlan { Kind = LaunchKind.Uri, Target = Address.AbsoluteUri, Arguments = "", WorkingDirectory = MainPath.InstallDirectory };
        }

        public static LaunchPlan Build(ProgramShortcut Item)
        {
            if (Item == null || string.IsNullOrWhiteSpace(Item.FilePath)) throw new InvalidDataException("This item has no launch target.");
            string Arguments = Item.Arguments ?? "";
            if (Arguments.IndexOf('\0') >= 0 || Item.FilePath.IndexOf('\0') >= 0) throw new InvalidDataException("Launch text contains a null character.");
            if (Item.isWindowsApp) return Packaged(Item.FilePath, Arguments);
            return BuildTarget(Item.FilePath, Arguments, Item.WorkingDirectory, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static LaunchPlan BuildTarget(string Target, string Arguments, string Working, HashSet<string> Links)
        {
            if (Regex.IsMatch(Target, @"^[A-Za-z][A-Za-z0-9+.-]{1,}:") && !Regex.IsMatch(Target, @"^[A-Za-z]:[\\/]"))
                return UriPlan(Target, Arguments);
            string PathName = AbsolutePath(Target);
            if (Directory.Exists(PathName))
            {
                if (!string.IsNullOrEmpty(Arguments)) throw new InvalidDataException("Folder targets cannot take command-line arguments.");
                return new LaunchPlan { Kind = LaunchKind.Directory, Target = PathName, Arguments = "", WorkingDirectory = WorkingDirectory(Working, PathName) };
            }
            if (!File.Exists(PathName)) throw new FileNotFoundException("The launch target no longer exists.");
            string Extension = Path.GetExtension(PathName).ToLowerInvariant();
            if (Extension == ".lnk")
            {
                if (Links.Count >= 8 || !Links.Add(PathName)) throw new InvalidDataException("Shortcut chain is cyclic or too deep.");
                ShellShortcut Link = ShellLink.ReadShortcut(PathName);
                string Combined = string.IsNullOrEmpty(Link.Arguments) ? Arguments : string.IsNullOrEmpty(Arguments) ? Link.Arguments : Link.Arguments + " " + Arguments;
                if (Combined.IndexOf('\0') >= 0) throw new InvalidDataException("Shortcut arguments contain a null character.");
                string EffectiveWorking = string.IsNullOrWhiteSpace(Working) ? Link.WorkingDirectory : Working;
                LaunchPlan Plan;
                if (string.IsNullOrWhiteSpace(Link.Target)) Plan = Packaged(Link.AppId ?? "", Combined);
                else Plan = BuildTarget(Link.Target, Combined, EffectiveWorking, Links);
                Plan.SourceLink = PathName;
                return Plan;
            }
            if (Extension == ".url")
            {
                if (new FileInfo(PathName).Length > 65536) throw new InvalidDataException("Internet shortcut is too large.");
                bool Section = false;
                string Url = null;
                foreach (string Line in File.ReadAllLines(PathName))
                {
                    string Trimmed = Line.Trim();
                    if (Trimmed.StartsWith("[", StringComparison.Ordinal)) Section = Trimmed.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                    else if (Section && Trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Url != null) throw new InvalidDataException("Internet shortcut has multiple URL entries.");
                        Url = Trimmed.Substring(4);
                    }
                }
                LaunchPlan Plan = UriPlan(Url, Arguments);
                Plan.SourceLink = PathName;
                return Plan;
            }
            if (Extension != ".exe" && Extension != ".com") throw new InvalidDataException("Choose an executable, shortcut, folder, packaged app or registered URI.");
            return new LaunchPlan { Kind = LaunchKind.Executable, Target = PathName, Arguments = Arguments,
                WorkingDirectory = WorkingDirectory(Working, Path.GetDirectoryName(PathName)) };
        }

        internal static ProcessStartInfo StartInfo(LaunchPlan Plan)
        {
            return new ProcessStartInfo { FileName = Plan.Target, Arguments = Plan.Arguments,
                WorkingDirectory = Plan.WorkingDirectory, UseShellExecute = true, ErrorDialog = false, Verb = "open" };
        }

        public static LaunchResult Launch(ProgramShortcut Item)
        {
            try
            {
                LaunchPlan Plan = Build(Item);
                if (DispatchOverride != null) DispatchOverride(Plan);
                else if (Plan.Kind == LaunchKind.PackagedApp) Activate(Plan);
                else using (Process Started = Process.Start(StartInfo(Plan))) { }
                return new LaunchResult { Success = true };
            }
            catch (Exception Error) when (IsExpectedError(Error))
            {
                MainPath.Log(Error.Message, "Launch");
                return new LaunchResult { Success = false, Error = Error.Message };
            }
        }

        [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivationManager
        {
            [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string AppId,
                [MarshalAs(UnmanagedType.LPWStr)] string Arguments, uint Options, out uint ProcessId);
        }

        private static void Activate(LaunchPlan Plan)
        {
            object Manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true));
            try
            {
                uint ProcessId;
                int Result = ((IActivationManager)Manager).ActivateApplication(Plan.Target, Plan.Arguments, 2 /* AO_NOERRORUI */, out ProcessId);
                Marshal.ThrowExceptionForHR(Result);
            }
            finally { Marshal.FinalReleaseComObject(Manager); }
        }
    }
}
