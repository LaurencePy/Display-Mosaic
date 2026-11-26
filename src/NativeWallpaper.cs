using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DisplayMosaic
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public enum WallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(WallpaperPosition position);
        WallpaperPosition GetPosition();
        void SetSlideshow(IntPtr items);
        void GetSlideshow(out IntPtr items);
        void SetSlideshowOptions(uint options, uint slideshowTick);
        void GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
        int GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    internal class DesktopWallpaperClass
    {
    }

    public class MonitorInfo
    {
        public int Index;
        public string Id;
        public string Model;
        public bool IsPrimary;
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public string Wallpaper;
    }

    public static class Wallpaper
    {
        private delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr h, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr h);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr h);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr h,
            uint msg,
            UIntPtr w,
            string l,
            uint flags,
            uint timeout,
            out UIntPtr result);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        public static void AttachParentConsole()
        {
            AttachConsole(-1);
        }

        public static void BroadcastSetting(string area)
        {
            UIntPtr r;
            SendMessageTimeout((IntPtr)0xffff, 0x1A, UIntPtr.Zero, area, 2, 5000, out r);
        }

        private static IDesktopWallpaper Get()
        {
            return (IDesktopWallpaper)new DesktopWallpaperClass();
        }

        // Monitors that are listed but not attached fail GetMonitorRECT and are skipped.
        public static MonitorInfo[] List()
        {
            IDesktopWallpaper dw = Get();
            uint n = dw.GetMonitorDevicePathCount();
            List<MonitorInfo> res = new List<MonitorInfo>();
            for (uint i = 0; i < n; i++)
            {
                string id = dw.GetMonitorDevicePathAt(i);
                if (string.IsNullOrEmpty(id)) continue;

                RECT r;
                try
                {
                    r = dw.GetMonitorRECT(id);
                }
                catch (COMException)
                {
                    continue;
                }

                string wp = "";
                try
                {
                    wp = dw.GetWallpaper(id) ?? "";
                }
                catch (COMException)
                {
                }

                MonitorInfo m = new MonitorInfo();
                m.Id = id;
                m.IsPrimary = (r.Left == 0 && r.Top == 0);
                m.Left = r.Left;
                m.Top = r.Top;
                m.Width = r.Right - r.Left;
                m.Height = r.Bottom - r.Top;
                m.Wallpaper = wp;
                m.Model = "";
                res.Add(m);
            }
            return res.ToArray();
        }

        public static void Set(string monitorId, string path)
        {
            Get().SetWallpaper(monitorId, path);
        }

        public static void SetPosition(WallpaperPosition p)
        {
            Get().SetPosition(p);
        }

        private static bool BelongsTo(IntPtr h, string[] names)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0) return false;
            try
            {
                string n = Process.GetProcessById((int)pid).ProcessName;
                foreach (string s in names)
                {
                    if (string.Equals(s, n, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        // Centre of the foreground window if it belongs to one of the processes; otherwise (unless foregroundOnly) of the largest visible one.
        public static bool TryGetWindowCenter(string[] names, bool foregroundOnly, out int x, out int y)
        {
            x = 0;
            y = 0;
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && !IsIconic(fg) && BelongsTo(fg, names))
            {
                RECT fr;
                if (!GetWindowRect(fg, out fr)) return false;
                x = (fr.Left + fr.Right) / 2;
                y = (fr.Top + fr.Bottom) / 2;
                return true;
            }
            if (foregroundOnly) return false;

            long best = 0;
            int bx = 0;
            int by = 0;
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0 || !BelongsTo(h, names))
                {
                    return true;
                }
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > best)
                {
                    best = area;
                    bx = (r.Left + r.Right) / 2;
                    by = (r.Top + r.Bottom) / 2;
                    found = true;
                }
                return true;
            }, IntPtr.Zero);

            x = bx;
            y = by;
            return found;
        }
    }
}
