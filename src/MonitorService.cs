using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayMosaic
{
    public static class MonitorService
    {
        public static readonly string[] VsCodeProcesses = new string[] { "Code - Insiders", "Code" };

        private static readonly Regex MonitorCodeRegex = new Regex(
            @"DISPLAY[#\\]([^#\\]+)[#\\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WmiSuffixRegex = new Regex(
            @"_\d+$",
            RegexOptions.Compiled);

        private static readonly Regex PrefixRegex = new Regex(
            @"^\\\\\?\\",
            RegexOptions.Compiled);

        private static readonly Regex GuidSuffixRegex = new Regex(
            @"#\{[^}]+\}$",
            RegexOptions.Compiled);

        public static string GetMonitorCode(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            Match m = MonitorCodeRegex.Match(id);
            return m.Success ? m.Groups[1].Value : "?";
        }

        public static Dictionary<string, string> GetModelMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\wmi");
                ObjectQuery query = new ObjectQuery("SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, query))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject w in results)
                    {
                        using (w)
                        {
                            string instanceName = Convert.ToString(w["InstanceName"]) ?? "";
                            if (string.IsNullOrEmpty(instanceName)) continue;

                            ushort[] chars = w["UserFriendlyName"] as ushort[];
                            string name = DecodeFriendlyName(chars);
                            string key = WmiSuffixRegex.Replace(instanceName, "");
                            map[key] = name;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return map;
        }

        private static string DecodeFriendlyName(ushort[] chars)
        {
            if (chars == null || chars.Length == 0) return "";
            StringBuilder sb = new StringBuilder(chars.Length);
            foreach (ushort c in chars)
            {
                if (c != 0) sb.Append((char)c);
            }
            return sb.ToString();
        }

        public static bool TestOled(MonitorInfo m, string match)
        {
            if (m == null || string.IsNullOrEmpty(match)) return false;
            string code = GetMonitorCode(m.Id);
            if (string.Equals(code, match, StringComparison.OrdinalIgnoreCase)) return true;
            string model = m.Model ?? "";
            return model.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static MonitorInfo[] GetMonitors(string match)
        {
            MonitorInfo[] ms = Wallpaper.List();
            Array.Sort(ms, delegate(MonitorInfo a, MonitorInfo b)
            {
                int cmp = a.Left.CompareTo(b.Left);
                return cmp != 0 ? cmp : a.Top.CompareTo(b.Top);
            });

            Dictionary<string, string> models = GetModelMap();
            for (int i = 0; i < ms.Length; i++)
            {
                MonitorInfo m = ms[i];
                m.Index = i + 1;
                string key = PrefixRegex.Replace(m.Id ?? "", "");
                key = GuidSuffixRegex.Replace(key, "");
                key = key.Replace('#', '\\');
                string modelName;
                if (models.TryGetValue(key, out modelName))
                {
                    m.Model = modelName;
                }
                m.IsOled = TestOled(m, match);
            }

            return ms;
        }

        public static string GetMonitorSignature()
        {
            MonitorInfo[] ms = Wallpaper.List();
            string[] ids = new string[ms.Length];
            for (int i = 0; i < ms.Length; i++)
            {
                ids[i] = ms[i].Id ?? "";
            }
            Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
            return string.Join("|", ids);
        }

        public static MonitorInfo GetVsCodeMonitor(MonitorInfo[] monitors, bool foregroundOnly)
        {
            if (monitors == null || monitors.Length == 0) return null;
            int x;
            int y;
            if (!Wallpaper.TryGetWindowCenter(VsCodeProcesses, foregroundOnly, out x, out y))
            {
                return null;
            }

            foreach (MonitorInfo m in monitors)
            {
                if (x >= m.Left && x < (m.Left + m.Width) && y >= m.Top && y < (m.Top + m.Height))
                {
                    return m;
                }
            }

            return null;
        }

        public static void UpdateTaskbars(Action<string> log, Action<string> warn)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    true))
                {
                    if (key == null) return;
                    key.SetValue("MMTaskbarEnabled", 1, RegistryValueKind.DWord);
                    key.SetValue("MMTaskbarMode", 1, RegistryValueKind.DWord);
                    Wallpaper.BroadcastSetting("TraySettings");
                    Thread.Sleep(500);
                    key.SetValue("MMTaskbarMode", 0, RegistryValueKind.DWord);
                    Wallpaper.BroadcastSetting("TraySettings");
                    if (log != null) log("Taskbar apps: all taskbars");
                }
            }
            catch (Exception ex)
            {
                if (warn != null) warn("Taskbar refresh: " + ex.Message);
            }
        }

        public static void ApplyAll(Action<string> log, Action<string> warn)
        {
            AppProfile p = ProfileStore.Load(warn);
            MonitorInfo[] monitors = GetMonitors(p.OledMatch);

            try
            {
                WallpaperPosition pos = (WallpaperPosition)Enum.Parse(typeof(WallpaperPosition), p.Position ?? "Fill", true);
                Wallpaper.SetPosition(pos);
            }
            catch (Exception ex)
            {
                if (warn != null) warn("Could not set position: " + ex.Message);
            }

            foreach (MonitorInfo m in monitors)
            {
                PresetConfig preset = m.IsOled ? p.Oled : p.Other;
                if (preset == null || string.IsNullOrEmpty(preset.Image)) continue;
                if (!File.Exists(preset.Image))
                {
                    if (warn != null) warn(string.Format("Monitor {0}: image missing: {1}", m.Index, preset.Image));
                    continue;
                }

                try
                {
                    Wallpaper.Set(m.Id, preset.Image);
                    if (log != null)
                    {
                        log(string.Format("Monitor {0} ({1}): {2}", m.Index, m.IsOled ? "OLED" : "other", preset.Image));
                    }
                }
                catch (Exception ex)
                {
                    if (warn != null) warn(string.Format("Monitor {0}: {1}", m.Index, ex.Message));
                }
            }

            try
            {
                ThemeService.ApplyVsCodeTheme(p, monitors, GetVsCodeMonitor(monitors, false), log, warn);
            }
            catch (Exception ex)
            {
                if (warn != null) warn("VS Code theme: " + ex.Message);
            }

            UpdateTaskbars(log, warn);
        }

        public static void IdentifyMonitors()
        {
            AppProfile p = ProfileStore.Load(null);
            MonitorInfo[] monitors = GetMonitors(p.OledMatch);
            foreach (MonitorInfo m in monitors)
            {
                Form overlay = new Form();
                overlay.FormBorderStyle = FormBorderStyle.None;
                overlay.StartPosition = FormStartPosition.Manual;
                overlay.TopMost = true;
                overlay.ShowInTaskbar = false;
                overlay.BackColor = Color.FromArgb(30, 30, 30);
                overlay.Opacity = 0.85;
                overlay.Bounds = new Rectangle(m.Left, m.Top, m.Width, m.Height);

                Label lbl = new Label();
                lbl.Dock = DockStyle.Fill;
                lbl.TextAlign = ContentAlignment.MiddleCenter;
                lbl.ForeColor = Color.White;
                lbl.Font = new Font("Segoe UI", 120f, FontStyle.Bold);
                lbl.Text = m.Index.ToString();
                overlay.Controls.Add(lbl);

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 2000;
                timer.Tick += delegate(object sender, EventArgs e)
                {
                    timer.Stop();
                    overlay.Close();
                    overlay.Dispose();
                    timer.Dispose();
                };

                overlay.Show();
                timer.Start();
            }
        }
    }
}
