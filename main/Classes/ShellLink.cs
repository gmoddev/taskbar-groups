using System;
using System.Runtime.InteropServices;
using System.Text;

namespace client.Classes
{
    internal sealed class ShellShortcut
    {
        public string Target, Arguments, WorkingDirectory, AppId, IconPath;
        public int IconIndex;
    }

    static class ShellLink
    {
        public static void InstallShortcut(string exePath, string appId, string desc, string wkDirec, string iconLocation, string saveLocation, string arguments)
        {
            object Shortcut = new CShellLink();
            try
            {
                IShellLinkW Link = (IShellLinkW)Shortcut;
                Link.SetPath(exePath);
                Link.SetDescription(desc);
                Link.SetWorkingDirectory(wkDirec);
                Link.SetArguments(arguments);
                Link.SetIconLocation(iconLocation, 0);
                using (PropVariantHelper Value = new PropVariantHelper())
                {
                    Value.SetValue(appId);
                    PROPVARIANT Variant = Value.Propvariant;
                    PROPERTYKEY Key = PROPERTYKEY.AppUserModel_ID;
                    ((IPropertyStore)Shortcut).SetValue(ref Key, ref Variant);
                    ((IPropertyStore)Shortcut).Commit();
                }
                ((IPersistFile)Shortcut).Save(saveLocation, true);
            }
            finally { Marshal.FinalReleaseComObject(Shortcut); }
        }

        public static ShellShortcut ReadShortcut(string PathName)
        {
            object Shortcut = new CShellLink();
            try
            {
                ((IPersistFile)Shortcut).Load(PathName, 0);
                IShellLinkW Link = (IShellLinkW)Shortcut;
                StringBuilder Target = new StringBuilder(32768);
                StringBuilder Arguments = new StringBuilder(32768);
                StringBuilder Working = new StringBuilder(32768);
                // Read the stored target without Resolve (which may search or show UI).
                Link.GetPath(Target, Target.Capacity, IntPtr.Zero, 4 /* SLGP_RAWPATH */);
                Link.GetArguments(Arguments, Arguments.Capacity);
                Link.GetWorkingDirectory(Working, Working.Capacity);
                StringBuilder IconPath = new StringBuilder(32768);
                int IconIndex;
                Link.GetIconLocation(IconPath, IconPath.Capacity, out IconIndex);
                string AppId = null;
                PROPERTYKEY Key = PROPERTYKEY.AppUserModel_ID;
                PROPVARIANT Value;
                ((IPropertyStore)Shortcut).GetValue(ref Key, out Value);
                try
                {
                    if (Value.vt == (ushort)VarEnum.VT_LPWSTR) AppId = Marshal.PtrToStringUni(Value.unionmember);
                }
                finally { ClearVariant(ref Value); }
                return new ShellShortcut { Target = Target.ToString(), Arguments = Arguments.ToString(),
                    WorkingDirectory = Working.ToString(), AppId = AppId, IconPath = IconPath.ToString(), IconIndex = IconIndex };
            }
            finally { Marshal.FinalReleaseComObject(Shortcut); }
        }

        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHGetPropertyStoreForWindow(IntPtr Window, ref Guid Interface, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore Store);

        internal static string ReadWindowProperty(IntPtr Window, uint Id)
        {
            Guid Interface = typeof(IPropertyStore).GUID;
            IPropertyStore Store;
            SHGetPropertyStoreForWindow(Window, ref Interface, out Store);
            try
            {
                PROPERTYKEY Key = new PROPERTYKEY(PROPERTYKEY.AppUserModel_ID.fmtid, Id);
                PROPVARIANT Value;
                Store.GetValue(ref Key, out Value);
                try { return Value.vt == (ushort)VarEnum.VT_LPWSTR ? Marshal.PtrToStringUni(Value.unionmember) : null; }
                finally { ClearVariant(ref Value); }
            }
            finally { Marshal.FinalReleaseComObject(Store); }
        }

        [DllImport("Ole32.dll", EntryPoint = "PropVariantClear", PreserveSig = false)]
        private static extern void ClearVariant(ref PROPVARIANT Value);

        #region COM APIs
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLinkW
        {
            void GetPath([Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotKey(out short wHotKey);
            void SetHotKey(short wHotKey);
            void GetShowCmd(out uint iShowCmd);
            void SetShowCmd(uint iShowCmd);
            void GetIconLocation([Out(), MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int iIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPersistFile
        {
            void GetClassID(out Guid ClassId);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile(out IntPtr FileName);
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct PROPVARIANT
        {
            [FieldOffset(0)]
            public ushort vt;
            [FieldOffset(8)]
            public IntPtr unionmember;
            [FieldOffset(8)]
            public UInt64 forceStructToLargeEnoughSize;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount([Out] out uint propertyCount);
            void GetAt([In] uint propertyIndex, [Out, MarshalAs(UnmanagedType.Struct)] out PROPERTYKEY key);
            void GetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.Struct)] out PROPVARIANT pv);
            void SetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [In, MarshalAs(UnmanagedType.Struct)] ref PROPVARIANT pv);
            void Commit();
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
        internal class CShellLink { }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;

            public PROPERTYKEY(Guid guid, uint id)
            {
                fmtid = guid;
                pid = id;
            }

            public static readonly PROPERTYKEY AppUserModel_ID = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        }
        #endregion

        internal class PropVariantHelper : IDisposable
        {
            private static class NativeMethods
            {
                [DllImport("Ole32.dll", PreserveSig = false)]
                internal static extern void PropVariantClear(ref PROPVARIANT pvar);
            }

            private PROPVARIANT variant;
            public PROPVARIANT Propvariant => variant;

            public void Dispose() { NativeMethods.PropVariantClear(ref variant); }

            public void SetValue(string val)
            {
                NativeMethods.PropVariantClear(ref variant);
                variant.vt = (ushort)VarEnum.VT_LPWSTR;
                variant.unionmember = Marshal.StringToCoTaskMemUni(val);
            }
        }
    }
}
