using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DisplayMosaic
{
    public class PresetConfig
    {
        public string Image { get; set; }
        public string Theme { get; set; }

        public PresetConfig()
        {
            Image = "";
            Theme = "";
        }

        public PresetConfig Clone()
        {
            return new PresetConfig
            {
                Image = Image ?? "",
                Theme = Theme ?? ""
            };
        }

        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(Image) && string.IsNullOrWhiteSpace(Theme);
        }
    }

    public class MonitorPreset
    {
        public string Id { get; set; }
        public string Code { get; set; }
        public string Model { get; set; }
        public string Name { get; set; }
        public string Image { get; set; }
        public string Theme { get; set; }
        public string LastResolution { get; set; }

        public MonitorPreset()
        {
            Id = "";
            Code = "";
            Model = "";
            Name = "";
            Image = "";
            Theme = "";
            LastResolution = "";
        }

        public MonitorPreset Clone()
        {
            return new MonitorPreset
            {
                Id = Id ?? "",
                Code = Code ?? "",
                Model = Model ?? "",
                Name = Name ?? "",
                Image = Image ?? "",
                Theme = Theme ?? "",
                LastResolution = LastResolution ?? ""
            };
        }

        public bool HasCustomSettings()
        {
            return !string.IsNullOrWhiteSpace(Name)
                || !string.IsNullOrWhiteSpace(Image)
                || !string.IsNullOrWhiteSpace(Theme);
        }
    }

    public class AppProfile
    {
        public int Version { get; set; }
        public string Position { get; set; }
        public bool UpdateTaskbars { get; set; }
        public PresetConfig Default { get; set; }
        public List<MonitorPreset> Monitors { get; set; }

        // Legacy v1 migration fields (in-memory only once migrated)
        public bool LoadedFromLegacy { get; set; }
        public string LegacyOledMatch { get; set; }
        public PresetConfig LegacyOled { get; set; }

        public AppProfile()
        {
            Version = 2;
            Position = "Fill";
            UpdateTaskbars = true;
            Default = new PresetConfig();
            Monitors = new List<MonitorPreset>();
            LegacyOledMatch = "";
            LegacyOled = new PresetConfig();
        }

        public AppProfile Clone()
        {
            AppProfile copy = new AppProfile();
            copy.Version = Version;
            copy.Position = Position ?? "Fill";
            copy.UpdateTaskbars = UpdateTaskbars;
            copy.Default = Default != null ? Default.Clone() : new PresetConfig();
            copy.LoadedFromLegacy = LoadedFromLegacy;
            copy.LegacyOledMatch = LegacyOledMatch ?? "";
            copy.LegacyOled = LegacyOled != null ? LegacyOled.Clone() : new PresetConfig();
            if (Monitors != null)
            {
                foreach (MonitorPreset mp in Monitors)
                {
                    if (mp != null) copy.Monitors.Add(mp.Clone());
                }
            }
            return copy;
        }
    }

    public static class ProfileStore
    {
        private static readonly Regex ImageExtRegex = new Regex(
            @"\.(jpe?g|png|bmp|gif|tiff?|webp|jfif)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ProfileDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayMosaic"); }
        }

        public static string ProfilePath
        {
            get { return Path.Combine(ProfileDir, "profiles.json"); }
        }

        private static string LegacyProfilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MonitorWallpaper",
                    "profiles.json");
            }
        }

        public static AppProfile Load(Action<string> warn)
        {
            AppProfile profile = new AppProfile();
            if (!File.Exists(ProfilePath) && File.Exists(LegacyProfilePath))
            {
                try
                {
                    Directory.CreateDirectory(ProfileDir);
                    File.Copy(LegacyProfilePath, ProfilePath, false);
                }
                catch (Exception)
                {
                }
            }

            if (!File.Exists(ProfilePath))
            {
                return profile;
            }

            try
            {
                string raw = File.ReadAllText(ProfilePath, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> dict = ser.DeserializeObject(raw) as Dictionary<string, object>;
                if (dict == null) return profile;

                string pos = GetString(dict, "position");
                if (!string.IsNullOrEmpty(pos)) profile.Position = pos;

                object updateTaskbarsObj;
                if (dict.TryGetValue("updateTaskbars", out updateTaskbarsObj) && updateTaskbarsObj is bool)
                {
                    profile.UpdateTaskbars = (bool)updateTaskbarsObj;
                }

                if (dict.ContainsKey("monitors") || dict.ContainsKey("default"))
                {
                    profile.Version = 2;
                    ReadPreset(dict, "default", profile.Default);

                    object monitorsObj;
                    if (dict.TryGetValue("monitors", out monitorsObj))
                    {
                        object[] arr = monitorsObj as object[];
                        if (arr != null)
                        {
                            foreach (object item in arr)
                            {
                                Dictionary<string, object> mDict = item as Dictionary<string, object>;
                                if (mDict == null) continue;
                                string id = GetString(mDict, "id");
                                if (string.IsNullOrEmpty(id)) continue;

                                MonitorPreset mp = new MonitorPreset();
                                mp.Id = id;
                                mp.Code = GetString(mDict, "code") ?? "";
                                mp.Model = GetString(mDict, "model") ?? "";
                                mp.Name = GetString(mDict, "name") ?? "";
                                mp.Image = GetString(mDict, "image") ?? "";
                                mp.Theme = GetString(mDict, "theme") ?? "";
                                mp.LastResolution = GetString(mDict, "lastResolution") ?? "";
                                profile.Monitors.Add(mp);
                            }
                        }
                    }
                }
                else
                {
                    // Migrate legacy v1 profile (oledMatch / oled / other)
                    profile.LoadedFromLegacy = true;
                    profile.LegacyOledMatch = GetString(dict, "oledMatch") ?? "";
                    ReadPreset(dict, "oled", profile.LegacyOled);
                    ReadPreset(dict, "other", profile.Default);
                }
            }
            catch (Exception ex)
            {
                if (warn != null)
                {
                    warn(string.Format("Could not read {0} ({1}). Starting with an empty profile.", ProfilePath, ex.Message));
                }
            }

            return profile;
        }

        public static void EnsureLegacyMigration(AppProfile profile, MonitorInfo[] monitors)
        {
            if (profile == null || !profile.LoadedFromLegacy || monitors == null) return;

            foreach (MonitorInfo m in monitors)
            {
                if (m == null || string.IsNullOrEmpty(m.Id)) continue;
                if (FindMonitorPreset(profile, m.Id) != null) continue;

                PresetConfig src = MatchesPattern(m, profile.LegacyOledMatch) ? profile.LegacyOled : profile.Default;
                if (src == null || src.IsEmpty()) continue;

                MonitorPreset mp = new MonitorPreset();
                mp.Id = m.Id;
                mp.Code = MonitorService.GetMonitorCode(m.Id);
                mp.Model = m.Model ?? "";
                mp.LastResolution = string.Format("{0}x{1}", m.Width, m.Height);
                mp.Image = src.Image ?? "";
                mp.Theme = src.Theme ?? "";
                profile.Monitors.Add(mp);
            }

            profile.LoadedFromLegacy = false;
        }

        public static MonitorPreset FindMonitorPreset(AppProfile profile, string monitorId)
        {
            if (profile == null || profile.Monitors == null || string.IsNullOrEmpty(monitorId)) return null;
            foreach (MonitorPreset mp in profile.Monitors)
            {
                if (mp != null && string.Equals(mp.Id, monitorId, StringComparison.OrdinalIgnoreCase))
                {
                    return mp;
                }
            }
            return null;
        }

        public static MonitorPreset GetOrCreateMonitorPreset(AppProfile profile, MonitorInfo monitor)
        {
            if (profile == null || monitor == null || string.IsNullOrEmpty(monitor.Id)) return null;
            MonitorPreset mp = FindMonitorPreset(profile, monitor.Id);
            if (mp == null)
            {
                mp = new MonitorPreset();
                mp.Id = monitor.Id;
                profile.Monitors.Add(mp);
            }

            mp.Code = MonitorService.GetMonitorCode(monitor.Id);
            mp.Model = monitor.Model ?? "";
            mp.LastResolution = string.Format("{0}x{1}", monitor.Width, monitor.Height);
            return mp;
        }

        public static PresetConfig ResolvePreset(AppProfile profile, MonitorInfo monitor)
        {
            if (profile == null) return new PresetConfig();

            if (monitor != null)
            {
                MonitorPreset mp = FindMonitorPreset(profile, monitor.Id);
                if (mp != null)
                {
                    return new PresetConfig
                    {
                        Image = mp.Image ?? "",
                        Theme = mp.Theme ?? ""
                    };
                }

                if (profile.LoadedFromLegacy && MatchesPattern(monitor, profile.LegacyOledMatch))
                {
                    return profile.LegacyOled != null ? profile.LegacyOled.Clone() : new PresetConfig();
                }
            }

            return profile.Default != null ? profile.Default.Clone() : new PresetConfig();
        }

        public static bool MatchesPattern(MonitorInfo m, string match)
        {
            if (m == null || string.IsNullOrEmpty(match)) return false;
            string code = MonitorService.GetMonitorCode(m.Id);
            if (string.Equals(code, match, StringComparison.OrdinalIgnoreCase)) return true;
            string model = m.Model ?? "";
            return model.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Save(AppProfile profile)
        {
            Directory.CreateDirectory(ProfileDir);
            JavaScriptSerializer ser = new JavaScriptSerializer();

            List<MonitorPreset> toSave = new List<MonitorPreset>();
            if (profile.Monitors != null)
            {
                foreach (MonitorPreset mp in profile.Monitors)
                {
                    if (mp != null && !string.IsNullOrEmpty(mp.Id) && mp.HasCustomSettings())
                    {
                        toSave.Add(mp);
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat("  \"version\": 2,\r\n");
            sb.AppendFormat("  \"position\": {0},\r\n", ser.Serialize(profile.Position ?? "Fill"));
            sb.AppendFormat("  \"updateTaskbars\": {0},\r\n", profile.UpdateTaskbars ? "true" : "false");
            sb.AppendLine("  \"default\": {");
            sb.AppendFormat("    \"image\": {0},\r\n", ser.Serialize((profile.Default != null ? profile.Default.Image : "") ?? ""));
            sb.AppendFormat("    \"theme\": {0}\r\n", ser.Serialize((profile.Default != null ? profile.Default.Theme : "") ?? ""));
            sb.AppendLine("  },");
            sb.Append("  \"monitors\": [");
            if (toSave.Count == 0)
            {
                sb.AppendLine("]");
            }
            else
            {
                sb.AppendLine();
                for (int i = 0; i < toSave.Count; i++)
                {
                    MonitorPreset mp = toSave[i];
                    sb.AppendLine("    {");
                    sb.AppendFormat("      \"id\": {0},\r\n", ser.Serialize(mp.Id ?? ""));
                    sb.AppendFormat("      \"code\": {0},\r\n", ser.Serialize(mp.Code ?? ""));
                    sb.AppendFormat("      \"model\": {0},\r\n", ser.Serialize(mp.Model ?? ""));
                    sb.AppendFormat("      \"name\": {0},\r\n", ser.Serialize(mp.Name ?? ""));
                    sb.AppendFormat("      \"image\": {0},\r\n", ser.Serialize(mp.Image ?? ""));
                    sb.AppendFormat("      \"theme\": {0},\r\n", ser.Serialize(mp.Theme ?? ""));
                    sb.AppendFormat("      \"lastResolution\": {0}\r\n", ser.Serialize(mp.LastResolution ?? ""));
                    sb.Append(i + 1 < toSave.Count ? "    },\r\n" : "    }\r\n");
                }
                sb.AppendLine("  ]");
            }
            sb.AppendLine("}");

            File.WriteAllText(ProfilePath, sb.ToString(), new UTF8Encoding(false));
        }

        public static string ResolveImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("Image not found: " + path);
            }

            string full = Path.GetFullPath(path);
            if (!ImageExtRegex.IsMatch(full))
            {
                throw new InvalidOperationException("Unsupported image type: " + full);
            }

            return full;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                return Convert.ToString(val);
            }
            return null;
        }

        private static void ReadPreset(Dictionary<string, object> root, string key, PresetConfig target)
        {
            object obj;
            if (!root.TryGetValue(key, out obj)) return;
            Dictionary<string, object> sub = obj as Dictionary<string, object>;
            if (sub == null) return;

            string img = GetString(sub, "image");
            if (!string.IsNullOrEmpty(img)) target.Image = img;

            string theme = GetString(sub, "theme");
            if (!string.IsNullOrEmpty(theme)) target.Theme = theme;
        }
    }

    public static class ThemeService
    {
        private static readonly Regex ThemeSettingRegex = new Regex(
            @"(?m)^(?!\s*//)(\s*""workbench\.colorTheme""\s*:\s*)(""(?:[^""\\]|\\.)*"")",
            RegexOptions.Compiled);

        private static readonly Regex NlsPlaceholderRegex = new Regex(
            @"^%(.+)%$",
            RegexOptions.Compiled);

        public static string[] GetInstalledThemes()
        {
            List<string> files = new List<string>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] extRoots = new string[]
            {
                Path.Combine(userProfile, ".vscode-insiders", "extensions"),
                Path.Combine(userProfile, ".vscode", "extensions")
            };

            foreach (string root in extRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        files.Add(Path.Combine(dir, "package.json"));
                    }
                }
                catch (Exception)
                {
                }
            }

            string[] appRoots = new string[]
            {
                Path.Combine(localAppData, "Programs", "Microsoft VS Code Insiders"),
                Path.Combine(localAppData, "Programs", "Microsoft VS Code")
            };

            foreach (string app in appRoots)
            {
                if (!Directory.Exists(app)) continue;
                AddBuiltinThemePackages(Path.Combine(app, "resources", "app", "extensions"), files);
                try
                {
                    foreach (string sub in Directory.GetDirectories(app))
                    {
                        AddBuiltinThemePackages(Path.Combine(sub, "resources", "app", "extensions"), files);
                    }
                }
                catch (Exception)
                {
                }
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;

            foreach (string file in files)
            {
                if (!File.Exists(file)) continue;
                try
                {
                    string raw = File.ReadAllText(file, Encoding.UTF8);
                    if (raw.IndexOf("\"themes\"", StringComparison.Ordinal) < 0) continue;

                    Dictionary<string, object> pkg = ser.DeserializeObject(raw) as Dictionary<string, object>;
                    if (pkg == null) continue;

                    object contributesObj;
                    if (!pkg.TryGetValue("contributes", out contributesObj)) continue;
                    Dictionary<string, object> contributes = contributesObj as Dictionary<string, object>;
                    if (contributes == null) continue;

                    object themesObj;
                    if (!contributes.TryGetValue("themes", out themesObj)) continue;
                    object[] themesArr = themesObj as object[];
                    if (themesArr == null) continue;

                    Dictionary<string, object> nls = null;
                    foreach (object item in themesArr)
                    {
                        Dictionary<string, object> t = item as Dictionary<string, object>;
                        if (t == null) continue;

                        string name = GetField(t, "id");
                        if (string.IsNullOrEmpty(name)) name = GetField(t, "label");
                        if (string.IsNullOrEmpty(name)) continue;

                        Match m = NlsPlaceholderRegex.Match(name);
                        if (m.Success)
                        {
                            if (nls == null)
                            {
                                nls = LoadNls(Path.Combine(Path.GetDirectoryName(file), "package.nls.json"), ser);
                            }
                            name = ResolveNls(nls, m.Groups[1].Value);
                        }

                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            List<string> sorted = new List<string>(names);
            sorted.Sort(StringComparer.CurrentCultureIgnoreCase);
            return sorted.ToArray();
        }

        public static void ApplyVsCodeTheme(AppProfile profile, MonitorInfo[] monitors, MonitorInfo target, Action<string> log, Action<string> warn)
        {
            MonitorInfo effectiveTarget = target;
            if (effectiveTarget == null && monitors != null && monitors.Length == 1)
            {
                effectiveTarget = monitors[0];
            }

            if (effectiveTarget == null) return;

            PresetConfig preset = ProfileStore.ResolvePreset(profile, effectiveTarget);
            ApplyVsCodeTheme(preset != null ? preset.Theme : "", log, warn);
        }

        public static void ApplyVsCodeTheme(string theme, Action<string> log, Action<string> warn)
        {
            if (string.IsNullOrEmpty(theme)) return;

            JavaScriptSerializer ser = new JavaScriptSerializer();
            string json = ser.Serialize(theme);
            string replacement = json.Replace("$", "$$");
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            foreach (string dir in new string[] { "Code", "Code - Insiders" })
            {
                string file = Path.Combine(appData, dir, "User", "settings.json");
                if (!File.Exists(file)) continue;

                try
                {
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    string updated = null;
                    Match m = ThemeSettingRegex.Match(text);
                    if (m.Success)
                    {
                        if (string.Equals(m.Groups[2].Value, json, StringComparison.Ordinal))
                        {
                            continue;
                        }
                        updated = ThemeSettingRegex.Replace(text, "${1}" + replacement, 1);
                    }
                    else
                    {
                        Match openBrace = Regex.Match(text, @"^\s*\{");
                        if (openBrace.Success)
                        {
                            bool isEmptyObject = Regex.IsMatch(text, @"^\s*\{\s*\}\s*$");
                            string comma = isEmptyObject ? "" : ",";
                            updated = Regex.Replace(
                                text,
                                @"^(\s*\{)",
                                "${1}\r\n    \"workbench.colorTheme\": " + replacement + comma);
                        }
                        else
                        {
                            if (warn != null) warn("Could not update " + file);
                            continue;
                        }
                    }

                    File.WriteAllText(file, updated, new UTF8Encoding(false));
                    if (log != null) log(string.Format("VS Code ({0}): {1}", dir, theme));
                }
                catch (Exception ex)
                {
                    if (warn != null) warn(string.Format("Could not update {0}: {1}", file, ex.Message));
                }
            }
        }

        private static void AddBuiltinThemePackages(string extensionsDir, List<string> files)
        {
            if (!Directory.Exists(extensionsDir)) return;
            try
            {
                foreach (string dir in Directory.GetDirectories(extensionsDir, "theme-*"))
                {
                    files.Add(Path.Combine(dir, "package.json"));
                }
            }
            catch (Exception)
            {
            }
        }

        private static Dictionary<string, object> LoadNls(string path, JavaScriptSerializer ser)
        {
            if (!File.Exists(path)) return new Dictionary<string, object>();
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                return ser.DeserializeObject(raw) as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        private static string ResolveNls(Dictionary<string, object> nls, string key)
        {
            object val;
            if (nls == null || !nls.TryGetValue(key, out val) || val == null) return "";
            string s = val as string;
            if (s != null) return s;
            Dictionary<string, object> dict = val as Dictionary<string, object>;
            if (dict != null) return GetField(dict, "message") ?? "";
            return "";
        }

        private static string GetField(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                return Convert.ToString(val);
            }
            return null;
        }
    }
}
