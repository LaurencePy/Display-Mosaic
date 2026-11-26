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
    }

    public class AppProfile
    {
        public string Position { get; set; }
        public string OledMatch { get; set; }
        public PresetConfig Oled { get; set; }
        public PresetConfig Other { get; set; }

        public AppProfile()
        {
            Position = "Fill";
            OledMatch = "";
            Oled = new PresetConfig();
            Other = new PresetConfig();
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

                string oledMatch = GetString(dict, "oledMatch");
                if (!string.IsNullOrEmpty(oledMatch)) profile.OledMatch = oledMatch;

                ReadPreset(dict, "oled", profile.Oled);
                ReadPreset(dict, "other", profile.Other);
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

        public static void Save(AppProfile profile)
        {
            Directory.CreateDirectory(ProfileDir);
            JavaScriptSerializer ser = new JavaScriptSerializer();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat("  \"position\": {0},\r\n", ser.Serialize(profile.Position ?? "Fill"));
            sb.AppendFormat("  \"oledMatch\": {0},\r\n", ser.Serialize(profile.OledMatch ?? ""));
            sb.AppendLine("  \"oled\": {");
            sb.AppendFormat("    \"image\": {0},\r\n", ser.Serialize((profile.Oled != null ? profile.Oled.Image : "") ?? ""));
            sb.AppendFormat("    \"theme\": {0}\r\n", ser.Serialize((profile.Oled != null ? profile.Oled.Theme : "") ?? ""));
            sb.AppendLine("  },");
            sb.AppendLine("  \"other\": {");
            sb.AppendFormat("    \"image\": {0},\r\n", ser.Serialize((profile.Other != null ? profile.Other.Image : "") ?? ""));
            sb.AppendFormat("    \"theme\": {0}\r\n", ser.Serialize((profile.Other != null ? profile.Other.Theme : "") ?? ""));
            sb.AppendLine("  }");
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
            bool hasOled = false;
            if (target != null)
            {
                hasOled = target.IsOled;
            }
            else if (monitors != null)
            {
                foreach (MonitorInfo m in monitors)
                {
                    if (m.IsOled) { hasOled = true; break; }
                }
            }

            string theme = hasOled
                ? (profile.Oled != null ? profile.Oled.Theme : "")
                : (profile.Other != null ? profile.Other.Theme : "");
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
