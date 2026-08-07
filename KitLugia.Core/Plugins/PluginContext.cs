using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KitLugia.Core.Plugins
{
    /// <summary>
    /// Contexto passado aos plugins: caminhos do kit, logger e um armazenamento
    /// de configurações persistente em JSON (por plugin).
    /// </summary>
    public sealed class PluginContext
    {
        /// <summary>Pasta raiz do kit (onde o KitLugia.GUI.exe está).</summary>
        public string KitBaseDirectory { get; }

        /// <summary>Pasta de plugins (ex.: C:\...\Plugins).</summary>
        public string PluginsDirectory { get; }

        /// <summary>Pasta onde o plugin pode guardar dados próprios (é criada se preciso).</summary>
        public string PluginDataDirectory { get; }

        /// <summary>Função de log usada pela página de Plugins e pelo arquivo de log do kit.</summary>
        public Action<string> Log { get; }

        public PluginContext(string kitBaseDirectory, string pluginsDirectory, Action<string> log)
        {
            KitBaseDirectory = kitBaseDirectory;
            PluginsDirectory = pluginsDirectory;
            Log = log;

            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            PluginDataDirectory = Path.Combine(localData, "KitLugia", "Plugins");
        }

        /// <summary>Lê um JSON de configurações do plugin (cria o arquivo vazio se não existir).</summary>
        public T? ReadSettings<T>(string pluginName) where T : class, new()
        {
            try
            {
                var file = SettingsFile(pluginName);
                if (!File.Exists(file)) return new T();
                var json = File.ReadAllText(file);
                return string.IsNullOrWhiteSpace(json)
                    ? new T()
                    : JsonSerializer.Deserialize<T>(json) ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        /// <summary>Grava o JSON de configurações do plugin.</summary>
        public bool WriteSettings<T>(string pluginName, T settings)
        {
            try
            {
                Directory.CreateDirectory(PluginDataDirectory);
                File.WriteAllText(SettingsFile(pluginName), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Falha ao salvar configurações de '{pluginName}': {ex.Message}");
                return false;
            }
        }

        private string SettingsFile(string pluginName) => Path.Combine(PluginDataDirectory, $"{Sanitize(pluginName)}.json");

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Where(c => !invalid.Contains(c)));
        }
    }
}
