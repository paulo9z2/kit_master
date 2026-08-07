using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KitLugia.Core
{
    /// <summary>
    /// Configurações do DeepUninstaller baseadas nas lições do Revo Uninstaller
    /// e do PC Manager (ver análises em "plano kit"):
    /// - Ignorar arquivos acessados/modificados recentemente (&lt; 24h) no scan
    /// - Exclusões persistentes do usuário (arquivos e chaves de registro)
    /// - Marcadores AppData pré-uninstall (ADCU/ADAU do Revo)
    /// </summary>
    public class AppDataMarker
    {
        public string AppName { get; set; } = "";
        public string Path { get; set; } = "";
        public bool AllUsers { get; set; } = false;
        public DateTime RecordedAt { get; set; }
    }

    public class DeepUninstallerSettings
    {
        // Revo: "Ignore files accessed in the last 24 hours"
        public bool IgnoreRecentFiles { get; set; } = true;
        public int IgnoreRecentFilesHours { get; set; } = 24;

        // Revo: RegExclude / Junk Files\Exclude
        public List<string> FileExclusions { get; set; } = new();
        public List<string> RegistryExclusions { get; set; } = new();

        // Revo: ADCU (AppData Current User) / ADAU (AppData All Users)
        public List<AppDataMarker> AppDataMarkers { get; set; } = new();

        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KitLugia");
        private static readonly string FilePath = Path.Combine(FolderPath, "DeepUninstallerSettings.json");
        private static readonly object _lock = new();

        public static DeepUninstallerSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(FilePath))
                        return new DeepUninstallerSettings();
                    string json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<DeepUninstallerSettings>(json) ?? new DeepUninstallerSettings();
                }
                catch { return new DeepUninstallerSettings(); }
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(FolderPath))
                        Directory.CreateDirectory(FolderPath);
                    string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(FilePath, json);
                }
                catch (Exception ex)
                {
                    Logger.LogError("DeepUninstallerSettings.Save", ex.Message);
                }
            }
        }

        /// <summary>
        /// Registra marcadores ADCU/ADAU: as pastas de dados do app que existem
        /// AGORA (pré-uninstall). Usadas como alvo preciso no pós-scan.
        /// </summary>
        public static void RecordAppDataMarkers(string appName, string installLocation)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appName)) return;
                var settings = Load();
                string name = appName.Trim();

                var targets = new List<(string path, bool allUsers)>
                {
                    (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), false),      // Roaming
                    (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), false), // Local
                    (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), true), // ProgramData
                };

                var toAdd = new List<AppDataMarker>();
                foreach (var (root, allUsers) in targets)
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                    try
                    {
                        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
                        {
                            string leaf = Path.GetFileName(dir);
                            if (string.IsNullOrEmpty(leaf)) continue;
                            // Match exato (case-insensitive) ou compressão sem espaços/símbolos
                            string compressed = NativeRegex.Replace(name, @"[\s\-_.]+", "");
                            bool exact = leaf.Equals(name, StringComparison.OrdinalIgnoreCase);
                            bool compressedMatch = compressed.Length >= 3 &&
                                leaf.Equals(compressed, StringComparison.OrdinalIgnoreCase);
                            if (exact || compressedMatch)
                            {
                                // Evita duplicatas
                                if (settings.AppDataMarkers.Any(m =>
                                    m.AppName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                    m.Path.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                                    continue;
                                toAdd.Add(new AppDataMarker
                                {
                                    AppName = name,
                                    Path = dir,
                                    AllUsers = allUsers,
                                    RecordedAt = DateTime.Now
                                });
                            }
                        }
                    }
                    catch { }
                }

                if (toAdd.Count == 0) return;
                settings.AppDataMarkers.AddRange(toAdd);
                // Limita histórico por app (10 marcadores)
                var perApp = settings.AppDataMarkers.Where(m => m.AppName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (perApp.Count > 10)
                {
                    var keep = perApp.OrderByDescending(x => x.RecordedAt).Take(10).Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    settings.AppDataMarkers.RemoveAll(m => m.AppName.Equals(name, StringComparison.OrdinalIgnoreCase) && !keep.Contains(m.Path));
                }
                settings.Save();
            }
            catch { }
        }

        /// <summary>
        /// Retorna marcadores válidos (ainda existentes) para o app informado.
        /// </summary>
        public static List<string> GetValidMarkers(string appName)
        {
            var result = new List<string>();
            try
            {
                var settings = Load();
                foreach (var m in settings.AppDataMarkers)
                {
                    if (!m.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Directory.Exists(m.Path))
                        result.Add(m.Path);
                }
            }
            catch { }
            return result;
        }

        /// <summary>
        /// Adiciona exclusão persistente de arquivo/pasta.
        /// </summary>
        public static void AddFileExclusion(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var s = Load();
            string p = path.TrimEnd('\\', '/');
            if (!s.FileExclusions.Any(e => e.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                s.FileExclusions.Add(p);
                s.Save();
            }
        }

        /// <summary>
        /// Adiciona exclusão persistente de chave de registro.
        /// </summary>
        public static void AddRegistryExclusion(string regPath)
        {
            if (string.IsNullOrWhiteSpace(regPath)) return;
            var s = Load();
            string p = regPath.Trim();
            if (!s.RegistryExclusions.Any(e => e.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                s.RegistryExclusions.Add(p);
                s.Save();
            }
        }

        public static void RemoveFileExclusion(string path)
        {
            var s = Load();
            s.FileExclusions.RemoveAll(e => e.Equals(path, StringComparison.OrdinalIgnoreCase));
            s.Save();
        }

        public static void RemoveRegistryExclusion(string regPath)
        {
            var s = Load();
            s.RegistryExclusions.RemoveAll(e => e.Equals(regPath, StringComparison.OrdinalIgnoreCase));
            s.Save();
        }

        public static bool IsFileExcluded(string path)
        {
            try
            {
                var s = Load();
                if (s.FileExclusions.Count == 0) return false;
                foreach (var ex in s.FileExclusions)
                {
                    if (string.IsNullOrEmpty(ex)) continue;
                    if (path.Equals(ex, StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith(ex + "\\", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        public static bool IsRegistryExcluded(string regPath)
        {
            try
            {
                var s = Load();
                if (s.RegistryExclusions.Count == 0) return false;
                foreach (var ex in s.RegistryExclusions)
                {
                    if (string.IsNullOrEmpty(ex)) continue;
                    if (regPath.Equals(ex, StringComparison.OrdinalIgnoreCase) ||
                        regPath.StartsWith(ex + "\\", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
