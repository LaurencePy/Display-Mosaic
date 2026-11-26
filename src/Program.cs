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

            string preset = null;
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
                    case "preset":
                        if (i + 1 >= args.Length) return Fail("-Preset requires a value (Oled or Other).");
                        preset = args[++i];
                        break;
                    case "image":
                        if (i + 1 >= args.Length) return Fail("-Image requires a file path.");
                        image = args[++i];
                        break;
                    case "theme":
                        if (i + 1 >= args.Length) return Fail("-Theme requires a theme name.");
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
                return RunSet(preset, image, theme, oledMatch, position);
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
            Console.WriteLine("Profile file: {0}  (fit: {1}; OLED match: '{2}')", ProfileStore.ProfilePath, p.Position, p.OledMatch);
            Console.WriteLine("OLED preset : image={0}  theme={1}", p.Oled.Image, p.Oled.Theme);
            Console.WriteLine("Other preset: image={0}  theme={1}", p.Other.Image, p.Other.Theme);

            MonitorInfo[] ms = MonitorService.GetMonitors(p.OledMatch);
            MonitorInfo vs = MonitorService.GetVsCodeMonitor(ms, false);
            Console.WriteLine("VS Code window is on monitor: {0}", vs != null ? "#" + vs.Index : "not found");
            Console.WriteLine();
            Console.WriteLine("{0,-3} {1,-10} {2,-22} {3,-11} {4,-6} {5}", "#", "Code", "Model", "Resolution", "Preset", "Current");
            Console.WriteLine(new string('-', 90));
            foreach (MonitorInfo m in ms)
            {
                Console.WriteLine(
                    "{0,-3} {1,-10} {2,-22} {3,-11} {4,-6} {5}",
                    m.Index,
                    MonitorService.GetMonitorCode(m.Id),
                    m.Model ?? "",
                    string.Format("{0}x{1}", m.Width, m.Height),
                    m.IsOled ? "OLED" : "Other",
                    m.Wallpaper ?? "");
            }
        }

        private static int RunSet(string preset, string image, string theme, string oledMatch, string position)
        {
            try
            {
                AppProfile p = ProfileStore.Load(Warn);
                if (!string.IsNullOrEmpty(oledMatch)) p.OledMatch = oledMatch;
                if (!string.IsNullOrEmpty(position))
                {
                    WallpaperPosition parsedPos = (WallpaperPosition)Enum.Parse(typeof(WallpaperPosition), position, true);
                    p.Position = parsedPos.ToString();
                }

                if (!string.IsNullOrEmpty(image) || !string.IsNullOrEmpty(theme))
                {
                    if (string.IsNullOrEmpty(preset))
                    {
                        return Fail("-Image/-Theme need -Preset Oled or -Preset Other.");
                    }

                    PresetConfig target;
                    if (string.Equals(preset, "Oled", StringComparison.OrdinalIgnoreCase)) target = p.Oled;
                    else if (string.Equals(preset, "Other", StringComparison.OrdinalIgnoreCase)) target = p.Other;
                    else return Fail("Invalid -Preset. Use Oled or Other.");

                    if (!string.IsNullOrEmpty(image)) target.Image = ProfileStore.ResolveImagePath(image);
                    if (!string.IsNullOrEmpty(theme)) target.Theme = theme;
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

        private static void PrintHelp()
        {
            Console.WriteLine("DisplayMosaic.exe [options]");
            Console.WriteLine("  (no args)          Opens Settings and stays minimised in taskbar hidden icons");
            Console.WriteLine("  --minimized        Starts minimised in taskbar hidden icons and watches monitors");
            Console.WriteLine("  --list             Shows connected monitors, OLED match, VS Code monitor, and presets");
            Console.WriteLine("  --list-themes      Lists installed VS Code theme names");
            Console.WriteLine("  --set              Updates presets (-Preset Oled|Other -Image <path> -Theme <name> -OledMatch <id> -Position <Fill|Fit|...>)");
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
