using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KitLugia.Core.Plugins;

namespace KitLugia.Plugin.Example
{
    /// <summary>
    /// Plugin de exemplo: mostra as informações do sistema no log do kit
    /// e cria um relatório de exemplo na pasta de dados do plugin.
    /// Copie a DLL compilada para a pasta "Plugins" ao lado do KitLugia.GUI.exe.
    /// </summary>
    public sealed class SystemInfoPlugin : IKitLugiaPlugin
    {
        private PluginContext? _ctx;

        public string Name => "Informações do Sistema";
        public string Version => "1.0.0";
        public string Author => "Equipe KitLugia";
        public string Description => "Coleta informações do sistema (SO, RAM, disco) e salva um relatório em %LOCALAPPDATA%\\KitLugia\\Plugins.";

        public PluginKind Kind => PluginKind.Tool;

        public bool Initialize(PluginContext context)
        {
            _ctx = context;
            context.Log("Plugin de exemplo inicializado.");
            return true;
        }

        public async Task<PluginResult> ExecuteAsync(PluginContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();

            var os = Environment.OSVersion.VersionString;
            var is64 = Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits";
            var ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0);
            var processor = Environment.ProcessorCount;

            var report = $"KitLugia Plugin Exemplo\n" +
                         $"SO: {os} ({is64})\n" +
                         $"Processadores: {processor} núcleos lógicos\n" +
                         $"RAM disponível (GC): {ramGb:0.0} GB\n" +
                         $"Pasta do kit: {context.KitBaseDirectory}\n" +
                         $"Pasta de dados do plugin: {context.PluginDataDirectory}\n" +
                         $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

            var saved = context.WriteSettings("SystemInfoPlugin", new SystemInfoSettings
            {
                LastRun = DateTime.Now,
                OsVersion = os,
                ProcessorCount = processor,
            });

            context.Log(report);
            if (saved)
                context.Log($"Relatório salvo em {context.PluginDataDirectory}\\SystemInfoPlugin.json");

            var pluginFile = Path.Combine(context.PluginDataDirectory, "system_report.txt");
            try
            {
                Directory.CreateDirectory(context.PluginDataDirectory);
                File.WriteAllText(pluginFile, report);
                return PluginResult.Ok($"Relatório gerado: {pluginFile}");
            }
            catch (Exception ex)
            {
                return PluginResult.Fail($"Falha ao gravar relatório: {ex.Message}");
            }
        }

        public void Shutdown()
        {
            _ctx?.Log("Plugin de exemplo encerrado.");
        }
    }

    public sealed class SystemInfoSettings
    {
        public DateTime LastRun { get; set; }
        public string OsVersion { get; set; } = "";
        public int ProcessorCount { get; set; }
    }
}
