using System;
using System.Threading;
using System.Windows.Forms;

namespace DisplayMosaic
{
    internal static class Program
    {
        private const string MutexName = @"Local\DisplayMosaicWatch";
        private const string ShowGuiEventName = @"Local\DisplayMosaicShowGui";

        [STAThread]
        private static int Main(string[] args)
        {
            Wallpaper.SetProcessDPIAware();

            bool list = false;
            bool listThemes = false;
            bool set = false;
            bool apply = false;
            bool watch = false;
            bool minimized = false;
            bool startupDelay = false;
            bool installStartup = false;
            bool removeStartup = false;
            bool help = false;

            string monitorArg = null;
            string preset = null;
            string name = null;
            string image = null;
            string theme = null;
            string oledMatch = null;
            string position = null;

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                string arg = raw.TrimStart('-', '/').ToLowerInvariant();
                switch (arg)
                {
                    case "list": list = true; break;
                    case "listthemes":
                    case "list-themes": listThemes = true; break;
                    case "set": set = true; break;
                    case "apply": apply = true; break;
                    case "watch": watch = true; break;
                    case "minimized":
                    case "minimised":
                    case "tray": minimized = true; break;
                    case "startupdelay":
                    case "startup-delay": startupDelay = true; break;
                    case "installstartup":
                    case "install-startup": installStartup = true; break;
                    case "removestartup":
                    case "remove-startup": removeStartup = true; break;
                    case "help":
                    case "h":
                    case "?": help = true; break;
                    case "monitor":
                        if (i + 1 >= args.Length) return Fail("-Monitor requires a monitor number, code, model, ID, or 'Default'.");
                        monitorArg = args[++i];
                        break;
                    case "preset":
                        if (i + 1 >= args.Length) return Fail("-Preset requires a value (Oled or Other).");
                        preset = args[++i];
                        break;
                    case "name":
                        if (i + 1 >= args.Length) return Fail("-Name requires a label.");
                        name = args[++i];
                        break;
                    case "image":
                        if (i + 1 >= args.Length) return Fail("-Image requires a file path (or empty string to clear).");
                        image = args[++i];
                        break;
                    case "theme":
                        if (i + 1 >= args.Length) return Fail("-Theme requires a theme name (or empty string to clear).");
                        theme = args[++i];
                        break;
                    case "oledmatch":
                    case "oled-match":
                        if (i + 1 >= args.Length) return Fail("-OledMatch requires a value.");
                        oledMatch = args[++i];
                        break;
                    case "position":
                        if (i + 1 >= args.Length) return Fail("-Position requires a value.");
                        position = args[++i];
                        break;
                    default:
                        return Fail("Unknown argument: " + raw);
                }
            }

            if (help)
            {
                Wallpaper.AttachParentConsole();
                PrintHelp();
                return 0;
            }

            if (list)
            {
                Wallpaper.AttachParentConsole();
                RunList();
                return 0;
            }

            if (listThemes)
            {
                Wallpaper.AttachParentConsole();
                foreach (string t in ThemeService.GetInstalledThemes())
                {
                    Console.WriteLine(t);
                }
                return 0;
            }

            if (set)
            {
                Wallpaper.AttachParentConsole();
                return RunSet(monitorArg, preset, name, image, theme, oledMatch, position);
            }

            if (apply)
            {
                Wallpaper.AttachParentConsole();
                MonitorService.ApplyAll(Console.WriteLine, Warn);
                return 0;
            }

            if (installStartup)
            {
                Wallpaper.AttachParentConsole();
                StartupManager.Install();
                Console.WriteLine("Installed: " + StartupManager.StartupLinkPath);
                return 0;
            }

            if (removeStartup)
            {
                Wallpaper.AttachParentConsole();
                StartupManager.Remove();
                Console.WriteLine("Removed.");
                return 0;
            }

            bool startMinimized = minimized || watch;

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            using (EventWaitHandle showGuiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowGuiEventName))
            {
                if (!created)
                {
                    if (!startMinimized)
                    {
                        showGuiEvent.Set();
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayAppContext(startMinimized, startupDelay, showGuiEvent));
            }

            return 0;
        }

        private static void RunList()
        {
            AppProfile p = ProfileStore.Load(Warn);
            MonitorInfo[] ms = MonitorService.GetMonitors();
            ProfileStore.EnsureLegacyMigration(p, ms);
            MonitorInfo vs = MonitorService.GetVsCodeMonitor(ms, false);

            Console.WriteLine("Profile file: {0}  (fit: {1}; update taskbars: {2})", ProfileStore.ProfilePath, p.Position, p.UpdateTaskbars);
            if (!p.Default.IsEmpty())
            {
                Console.WriteLine("Default preset: image={0}  theme={1}", p.Default.Image, p.Default.Theme);
            }
            Console.WriteLine("VS Code window is on monitor: {0}", vs != null ? "#" + vs.Index : "not found");
            Console.WriteLine();
            Console.WriteLine("{0,-3} {1,-10} {2,-22} {3,-11} {4,-24} {5}", "#", "Code", "Model / Name", "Resolution", "VS Code Theme", "Configured / Current Wallpaper");
            Console.WriteLine(new string('-', 105));
            foreach (MonitorInfo m in ms)
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(p, m.Id);
                PresetConfig resolved = ProfileStore.ResolvePreset(p, m);
                string label = MonitorService.GetDisplayLabel(m, mp);
                string wp = !string.IsNullOrEmpty(resolved.Image) ? resolved.Image : (m.Wallpaper ?? "");
                Console.WriteLine(
                    "{0,-3} {1,-10} {2,-22} {3,-11} {4,-24} {5}",
                    m.Index,
                    MonitorService.GetMonitorCode(m.Id),
                    Truncate(label, 22),
                    string.Format("{0}x{1}", m.Width, m.Height),
                    Truncate(resolved.Theme ?? "", 24),
                    wp);
            }
        }

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLen) return value ?? "";
            return value.Substring(0, maxLen - 1) + "…";
        }

        private static int RunSet(string monitorArg, string preset, string name, string image, string theme, string oledMatch, string position)
        {
            try
            {
                AppProfile p = ProfileStore.Load(Warn);
                MonitorInfo[] ms = MonitorService.GetMonitors();
                ProfileStore.EnsureLegacyMigration(p, ms);

                if (!string.IsNullOrEmpty(position))
                {
                    WallpaperPosition parsedPos = (WallpaperPosition)Enum.Parse(typeof(WallpaperPosition), position, true);
                    p.Position = parsedPos.ToString();
                }

                string resolvedImage = null;
                if (image != null)
                {
                    resolvedImage = string.IsNullOrWhiteSpace(image) ? "" : ProfileStore.ResolveImagePath(image);
                }

                if (name != null || resolvedImage != null || theme != null)
                {
                    if (!string.IsNullOrEmpty(monitorArg))
                    {
                        if (string.Equals(monitorArg, "Default", StringComparison.OrdinalIgnoreCase))
                        {
                            if (resolvedImage != null) p.Default.Image = resolvedImage;
                            if (theme != null) p.Default.Theme = theme.Trim();
                        }
                        else
                        {
                            MonitorInfo target = FindMonitorByArg(ms, p, monitorArg);
                            if (target == null)
                            {
                                return Fail("No connected monitor matched '" + monitorArg + "'. Run --list to see monitors.");
                            }
                            MonitorPreset mp = ProfileStore.GetOrCreateMonitorPreset(p, target);
                            if (name != null) mp.Name = name.Trim();
                            if (resolvedImage != null) mp.Image = resolvedImage;
                            if (theme != null) mp.Theme = theme.Trim();
                        }
                    }
                    else if (!string.IsNullOrEmpty(preset))
                    {
                        if (string.Equals(preset, "Other", StringComparison.OrdinalIgnoreCase))
                        {
                            if (resolvedImage != null) p.Default.Image = resolvedImage;
                            if (theme != null) p.Default.Theme = theme.Trim();
                        }
                        else if (string.Equals(preset, "Oled", StringComparison.OrdinalIgnoreCase))
                        {
                            string pattern = !string.IsNullOrEmpty(oledMatch) ? oledMatch : p.LegacyOledMatch;
                            if (string.IsNullOrEmpty(pattern))
                            {
                                return Fail("-Preset Oled requires -OledMatch <model|code> or use -Monitor <#|code|model>.");
                            }
                            foreach (MonitorInfo m in ms)
                            {
                                if (ProfileStore.MatchesPattern(m, pattern))
                                {
                                    MonitorPreset mp = ProfileStore.GetOrCreateMonitorPreset(p, m);
                                    if (resolvedImage != null) mp.Image = resolvedImage;
                                    if (theme != null) mp.Theme = theme.Trim();
                                }
                            }
                        }
                        else
                        {
                            return Fail("Invalid -Preset. Use -Monitor <#|code|model|Default>.");
                        }
                    }
                    else
                    {
                        return Fail("-Image/-Theme/-Name need -Monitor <#|code|model|Default>.");
                    }
                }

                ProfileStore.Save(p);
                MonitorService.ApplyAll(Console.WriteLine, Warn);
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        }

        private static MonitorInfo FindMonitorByArg(MonitorInfo[] ms, AppProfile p, string query)
        {
            int idx;
            if (int.TryParse(query, out idx))
            {
                foreach (MonitorInfo m in ms)
                {
                    if (m.Index == idx) return m;
                }
                return null;
            }

            foreach (MonitorInfo m in ms)
            {
                if (string.Equals(m.Id, query, StringComparison.OrdinalIgnoreCase)) return m;
                if (string.Equals(MonitorService.GetMonitorCode(m.Id), query, StringComparison.OrdinalIgnoreCase)) return m;
            }

            foreach (MonitorInfo m in ms)
            {
                MonitorPreset mp = ProfileStore.FindMonitorPreset(p, m.Id);
                if (mp != null && !string.IsNullOrEmpty(mp.Name) &&
                    mp.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return m;
                }
                if (!string.IsNullOrEmpty(m.Model) &&
                    m.Model.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return m;
                }
            }

            return null;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("DisplayMosaic.exe [options]");
            Console.WriteLine("  (no args)          Opens Settings and stays minimised in taskbar hidden icons");
            Console.WriteLine("  --minimized        Starts minimised in taskbar hidden icons and watches monitors");
            Console.WriteLine("  --list             Shows connected monitors, VS Code monitor, and presets");
            Console.WriteLine("  --list-themes      Lists installed VS Code theme names");
            Console.WriteLine("  --set              Updates presets (-Monitor <#|code|model|Default> -Image <path> -Theme <name> -Name <label> -Position <Fill|Fit|...>)");
            Console.WriteLine("  --apply            Applies current presets once");
            Console.WriteLine("  --install-startup  Installs Startup shortcut to run minimised at sign-in");
            Console.WriteLine("  --remove-startup   Removes Startup shortcut");
        }

        private static int Fail(string message)
        {
            Wallpaper.AttachParentConsole();
            Console.Error.WriteLine("Error: " + message);
            return 1;
        }

        private static void Warn(string message)
        {
            Console.Error.WriteLine("Warning: " + message);
        }
    }
}
