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

        public static string GetDisplayLabel(MonitorInfo m, MonitorPreset preset)
        {
            if (preset != null && !string.IsNullOrWhiteSpace(preset.Name))
            {
                return preset.Name.Trim();
            }
            if (m != null && !string.IsNullOrWhiteSpace(m.Model))
            {
                return m.Model.Trim();
            }
            if (m != null)
            {
                return GetMonitorCode(m.Id);
            }
            return "Unknown";
        }

        public static MonitorInfo[] GetMonitors()
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
            MonitorInfo[] monitors = GetMonitors();
            if (p.LoadedFromLegacy)
            {
                ProfileStore.EnsureLegacyMigration(p, monitors);
            }

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
                PresetConfig preset = ProfileStore.ResolvePreset(p, m);
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
                        MonitorPreset mp = ProfileStore.FindMonitorPreset(p, m.Id);
                        string label = GetDisplayLabel(m, mp);
                        log(string.Format("Monitor {0} ({1}): {2}", m.Index, label, preset.Image));
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

            if (p.UpdateTaskbars)
            {
                UpdateTaskbars(log, warn);
            }
        }

        public static void IdentifyMonitors()
        {
            AppProfile p = ProfileStore.Load(null);
            MonitorInfo[] monitors = GetMonitors();
            foreach (MonitorInfo m in monitors)
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(p, m.Id);
                string label = GetDisplayLabel(m, mp);
                string code = GetMonitorCode(m.Id);

                Form overlay = new Form();
                overlay.FormBorderStyle = FormBorderStyle.None;
                overlay.StartPosition = FormStartPosition.Manual;
                overlay.TopMost = true;
                overlay.ShowInTaskbar = false;
                overlay.BackColor = Color.FromArgb(24, 28, 36);
                overlay.Opacity = 0.88;
                overlay.Bounds = new Rectangle(m.Left, m.Top, m.Width, m.Height);

                TableLayoutPanel panel = new TableLayoutPanel();
                panel.Dock = DockStyle.Fill;
                panel.ColumnCount = 1;
                panel.RowCount = 4;
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

                Label lblNum = new Label();
                lblNum.AutoSize = true;
                lblNum.Anchor = AnchorStyles.None;
                lblNum.ForeColor = Color.White;
                lblNum.Font = new Font("Segoe UI", 110f, FontStyle.Bold);
                lblNum.Text = m.Index.ToString();

                Label lblInfo = new Label();
                lblInfo.AutoSize = true;
                lblInfo.Anchor = AnchorStyles.None;
                lblInfo.ForeColor = Color.FromArgb(200, 212, 228);
                lblInfo.Font = new Font("Segoe UI", 16f, FontStyle.Regular);
                lblInfo.Text = string.Format("{0} ({1})  •  {2} × {3}{4}",
                    label,
                    code,
                    m.Width,
                    m.Height,
                    m.IsPrimary ? "  •  Primary" : "");

                panel.Controls.Add(lblNum, 0, 1);
                panel.Controls.Add(lblInfo, 0, 2);
                overlay.Controls.Add(panel);

                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 2500;
                Action closeOverlay = delegate
                {
                    timer.Stop();
                    if (!overlay.IsDisposed)
                    {
                        overlay.Close();
                        overlay.Dispose();
                    }
                    timer.Dispose();
                };

                timer.Tick += delegate { closeOverlay(); };
                overlay.Click += delegate { closeOverlay(); };
                panel.Click += delegate { closeOverlay(); };
                lblNum.Click += delegate { closeOverlay(); };
                lblInfo.Click += delegate { closeOverlay(); };

                overlay.Show();
                timer.Start();
            }
        }
    }
}
