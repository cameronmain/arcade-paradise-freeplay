using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader;

namespace ArcadeParadiseFreePlayMod
{
    /// <summary>
    /// Preferences-only config for a FreePlay cabinet.
    /// ROMs are always auto-detected from the roms/ folder.
    /// cabinet.json is optional; only currently used to set default ROM game.
    /// 
    /// Example:
    ///   { "defaultRom": "wjammers.zip", "core": "auto", "systemDir": "auto" }
    /// </summary>
    public class CabinetConfig
    {
        public string defaultRom { get; set; }
        public string core { get; set; } = "auto";
        public string systemDir { get; set; } = "auto";
    }

    public static class ConfigLoader
    {
        private static readonly string _modDir =
            Path.GetDirectoryName(typeof(Core).Assembly.Location) ?? ".";

        /// <summary>Path to the FreePlay mod folder (Mods/FreePlay).</summary>
        public static readonly string FreePlayDir =
            Path.Combine(_modDir, "FreePlay");

        /// <summary>Path to the ROMs folder (Mods/FreePlay/roms).</summary>
        public static readonly string RomsDir =
            Path.Combine(FreePlayDir, "roms");

        private static readonly string _configPath =
            Path.Combine(FreePlayDir, "cabinet.json");

        private static readonly string _lastRomPath =
            Path.Combine(FreePlayDir, "lastrom.txt");


        private static readonly string[] _availableCores = new[]
        {
            "fbalpha2012_libretro.dll",
        };

        // prevent BIOS filenames that should not appearing in the game list
        private static readonly HashSet<string> _biosFiles = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "neogeo.zip", "pgm.zip", "bios.zip", "awbios.zip",
            "isgsm.zip", "nmk004.zip", "skns.zip", "ym2608.zip",
        };

        /// <summary>
        /// Scan the roms/ folder and return full paths to all .zip files, excluding known BIOS files. 
        /// Sorted alphabetically for predictable order.
        /// </summary>
        public static string[] ScanRoms()
        {
            Directory.CreateDirectory(RomsDir);
            var files = Directory.GetFiles(RomsDir, "*.zip");
            files = Array.FindAll(files, f => !_biosFiles.Contains(Path.GetFileName(f)));
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public static CabinetConfig Load()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<CabinetConfig>(json);
                    if (config != null)
                    {
                        MelonLogger.Msg(config.defaultRom != null
                            ? $"[ConfigLoader] Loaded cabinet.json: defaultRom={config.defaultRom}, core={config.core}"
                            : $"[ConfigLoader] Loaded cabinet.json: core={config.core}");
                        ResolvePaths(config);
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[ConfigLoader] Failed to parse cabinet.json: {ex.Message}");
                }
            }

            MelonLogger.Msg("[ConfigLoader] No cabinet.json found, using defaults (auto-detect everything)");
            var defaultConfig = new CabinetConfig();
            ResolvePaths(defaultConfig);
            return defaultConfig;
        }

        /// <summary>
        /// Find the index of defaultRom in the scanned ROM list.
        /// Returns 0 if defaultRom is null or not found.
        /// </summary>
        public static int GetDefaultRomIndex(string[] romPaths, CabinetConfig config)
        {
            if (config?.defaultRom == null || romPaths == null || romPaths.Length == 0)
                return 0;

            string target = config.defaultRom;
            for (int i = 0; i < romPaths.Length; i++)
            {
                if (string.Equals(Path.GetFileName(romPaths[i]), target, StringComparison.OrdinalIgnoreCase))
                    return i;

                // try matching the full path if target is a path
                if (string.Equals(romPaths[i], target, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            MelonLogger.Warning($"[ConfigLoader] defaultRom '{target}' not found in roms/, starting at first ROM");
            return 0;
        }

        /// <summary>
        /// Resolve "auto" values and convert core + systemDir to full paths.
        /// </summary>
        public static void ResolvePaths(CabinetConfig config)
        {
            // core DLL
            if (config.core == "auto")
                config.core = AutoDetectCore();
            else if (!Path.IsPathRooted(config.core) && !config.core.Contains("/") && !config.core.Contains("\\"))
                config.core = Path.Combine(FreePlayDir, config.core);
            else if (!Path.IsPathRooted(config.core))
                config.core = Path.GetFullPath(Path.Combine(FreePlayDir, config.core));

            // system directory
            if (config.systemDir == "auto")
                config.systemDir = FindSystemDir();
            else if (!Path.IsPathRooted(config.systemDir))
                config.systemDir = Path.GetFullPath(Path.Combine(FreePlayDir, config.systemDir));
        }

        /// <summary>
        /// Auto-detect the best core by checking which DLLs are available.
        /// Returns the full path to the best available core DLL.
        /// </summary>
        private static string AutoDetectCore()
        {
            foreach (var coreName in _availableCores)
            {
                string path = Path.Combine(FreePlayDir, coreName);
                if (File.Exists(path))
                {
                    MelonLogger.Msg($"[ConfigLoader] Auto-detected core: {coreName}");
                    return path;
                }
            }

            // fallback to first core name (caller will handle missing file)
            string fallback = Path.Combine(FreePlayDir, _availableCores[0]);
            MelonLogger.Warning($"[ConfigLoader] No core DLL found, trying: {fallback}");
            return fallback;
        }

        /// <summary>
        /// Save the last-played ROM filename so the cabinet resumes on it next time.
        /// </summary>
        public static void SaveLastRom(string romFilename)
        {
            try
            {
                File.WriteAllText(_lastRomPath, romFilename);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ConfigLoader] Failed to save lastrom.txt: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the last-played ROM filename, or null if none  exists.
        /// Returns null if the file doesnt exist or the saved ROM isnt in the current list
        /// </summary>
        public static string LoadLastRom(string[] availableRoms)
        {
            if (!File.Exists(_lastRomPath))
                return null;

            try
            {
                string saved = File.ReadAllText(_lastRomPath).Trim();
                if (string.IsNullOrEmpty(saved))
                    return null;

                // verify its still in the available list
                foreach (var rom in availableRoms)
                {
                    if (string.Equals(Path.GetFileName(rom), saved, StringComparison.OrdinalIgnoreCase))
                    {
                        MelonLogger.Msg($"[ConfigLoader] Resuming last ROM: {saved}");
                        return saved;
                    }
                }

                MelonLogger.Warning($"[ConfigLoader] Saved ROM '{saved}' no longer in roms/, using default");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ConfigLoader] Failed to read lastrom.txt: {ex.Message}");
                return null;
            }
        }

        private static string FindSystemDir()
        {
            return Path.Combine(FreePlayDir, "system");
        }
    }
}
