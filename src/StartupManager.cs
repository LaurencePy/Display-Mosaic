using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace DisplayMosaic
{
    public static class StartupManager
    {
        public static string StartupLinkPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    "DisplayMosaic.lnk");
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        public static bool IsInstalled()
        {
            if (!File.Exists(StartupLinkPath)) return false;

            try
            {
                IShellLinkW link = (IShellLinkW)new ShellLink();
                IPersistFile file = (IPersistFile)link;
                file.Load(StartupLinkPath, 0);

                StringBuilder target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);

                StringBuilder args = new StringBuilder(1024);
                link.GetArguments(args, args.Capacity);

                string exePath = Application.ExecutablePath;
                return string.Equals(target.ToString(), exePath, StringComparison.OrdinalIgnoreCase)
                    && args.ToString().IndexOf("--minimized", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Install()
        {
            string exePath = Application.ExecutablePath;
            string workDir = Path.GetDirectoryName(exePath);
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLinkPath));

            IShellLinkW link = (IShellLinkW)new ShellLink();
            link.SetPath(exePath);
            link.SetArguments("--minimized --startup-delay");
            link.SetWorkingDirectory(workDir);
            link.SetDescription("DisplayMosaic (starts minimised in taskbar hidden icons)");
            link.SetIconLocation(exePath, 0);

            IPersistFile file = (IPersistFile)link;
            file.Save(StartupLinkPath, true);
        }

        public static void Remove()
        {
            if (File.Exists(StartupLinkPath))
            {
                File.Delete(StartupLinkPath);
            }
        }
    }
}
