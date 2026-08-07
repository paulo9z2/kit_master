using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitLugia.Core.Plugins
{
    /// <summary>Estado de um plugin carregado.</summary>
    public enum PluginLoadState
    {
        Disabled,
        Enabled,
        LoadError,
        Executing,
        Failed,
    }

    /// <summary>Uma instância descoberta + seus metadados e estado de execução.</summary>
    public sealed class PluginInstance
    {
        public required string AssemblyPath { get; init; }
        public required IKitLugiaPlugin Plugin { get; init; }
        public string? LoadError { get; set; }
        public PluginLoadState State { get; set; }
        public string LastMessage { get; set; } = "";
        public bool IsRunning { get; set; }
    }

    /// <summary>
    /// Gerenciador central de plugins do KitLugia.
    /// <para>
    /// Varre a pasta <c>Plugins</c> (raiz do kit + subpastas), carrega cada DLL,
    /// descobre tipos que implementam <see cref="IKitLugiaPlugin"/> e permite
    /// habilitar/desabilitar/executar com estado persistido em JSON
    /// (por padrão em %LOCALAPPDATA%\KitLugia\plugins_state.json).
    /// </para>
    /// </summary>
    public sealed class PluginManager
    {
        public static PluginManager Instance { get; } = new PluginManager();

        private readonly object _lock = new();
        private readonly List<PluginInstance> _plugins = new();
        private readonly Dictionary<string, bool> _state = new();
        private string _pluginsDir = "Plugins";
        private Action<string>? _logger;

        public event Action? PluginsChanged;

        public IReadOnlyList<PluginInstance> Plugins
        {
            get { lock (_lock) return _plugins.ToList(); }
        }

        public string PluginsDirectory
        {
            get { lock (_lock) return _pluginsDir; }
        }

        private PluginManager() { }

        /// <summary>Configura o logger (chamado pelo MainWindow na inicialização).</summary>
        public void SetLogger(Action<string>? logger) => _logger = logger;

        /// <summary>
        /// Varre a pasta de plugins e carrega todas as DLLs que exportam
        /// <see cref="IKitLugiaPlugin"/>. Chamado no arranque do kit e ao clicar
        /// "Recarregar" na página de Plugins.
        /// </summary>
        public void LoadPlugins(string? baseDirectory = null)
        {
            var root = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            _pluginsDir = Path.Combine(root, "Plugins");
            Directory.CreateDirectory(_pluginsDir);

            List<PluginInstance> discovered = new();

            foreach (var dll in EnumeratePluginDlls(_pluginsDir))
            {
                var instance = TryLoadAssembly(dll);
                if (instance != null) discovered.Add(instance);
            }

            lock (_lock)
            {
                _plugins.Clear();
                _plugins.AddRange(discovered);
                foreach (var p in _plugins)
                {
                    p.State = IsEnabled(p.Plugin.Name) ? PluginLoadState.Enabled : PluginLoadState.Disabled;
                    if (p.State == PluginLoadState.Enabled && p.Plugin is IKitLugiaPlugin plugin)
                    {
                        try
                        {
                            var ctx = new PluginContext(root, _pluginsDir, msg => _logger?.Invoke($"[Plugin:{p.Plugin.Name}] {msg}"));
                            if (!plugin.Initialize(ctx))
                            {
                                p.State = PluginLoadState.Failed;
                                p.LastMessage = "Initialize() retornou false (plugin recusou carregar).";
                                _logger?.Invoke($"[Plugins] '{p.Plugin.Name}' recusou a inicialização.");
                            }
                            else
                            {
                                _logger?.Invoke($"[Plugins] '{p.Plugin.Name}' v{p.Plugin.Version} inicializado ({p.Plugin.Kind}).");
                            }
                        }
                        catch (Exception ex)
                        {
                            p.State = PluginLoadState.Failed;
                            p.LoadError = ex.Message;
                            p.LastMessage = ex.Message;
                            _logger?.Invoke($"[Plugins] Falha ao inicializar '{p.Plugin.Name}': {ex.Message}");
                        }
                    }
                }
            }

            SaveState();
            PluginsChanged?.Invoke();
        }

        /// <summary>Inicia plugins Background habilitados (chamado após o carregamento inicial).</summary>
        public void StartBackgroundPlugins()
        {
            foreach (var instance in Plugins.Where(p => p.State == PluginLoadState.Enabled && p.Plugin.Kind == PluginKind.Background))
            {
                _ = Task.Run(() => ExecuteAsync(instance.Plugin.Name, CancellationToken.None));
            }
        }

        public bool IsEnabled(string pluginName)
        {
            lock (_lock) return _state.TryGetValue(pluginName, out var en) && en;
        }

        public void SetEnabled(string pluginName, bool enabled)
        {
            lock (_lock) _state[pluginName] = enabled;
            SaveState();

            foreach (var p in Plugins.Where(p => string.Equals(p.Plugin.Name, pluginName, StringComparison.OrdinalIgnoreCase)))
            {
                p.State = enabled ? PluginLoadState.Enabled : PluginLoadState.Disabled;
                if (!enabled)
                {
                    try { p.Plugin.Shutdown(); }
                    catch (Exception ex) { _logger?.Invoke($"[Plugins] Erro no Shutdown de '{pluginName}': {ex.Message}"); }
                }
            }
            PluginsChanged?.Invoke();
        }

        /// <summary>Executa um plugin pelo nome (retorna resultado; thread-safe contra execução dupla).</summary>
        public async Task<PluginResult> ExecuteAsync(string pluginName, CancellationToken cancellationToken)
        {
            PluginInstance? instance = null;
            lock (_lock) instance = _plugins.FirstOrDefault(p => string.Equals(p.Plugin.Name, pluginName, StringComparison.OrdinalIgnoreCase));

            if (instance == null) return PluginResult.Fail($"Plugin '{pluginName}' não encontrado.");
            if (instance.State == PluginLoadState.LoadError || instance.State == PluginLoadState.Failed)
                return PluginResult.Fail(instance.LastMessage);
            if (instance.IsRunning) return PluginResult.Fail("O plugin já está em execução.");
            if (instance.State != PluginLoadState.Enabled)
                return PluginResult.Fail("O plugin está desabilitado. Habilite-o antes de executar.");

            instance.IsRunning = true;
            instance.State = PluginLoadState.Executing;
            PluginsChanged?.Invoke();

            var started = DateTime.UtcNow;
            try
            {
                var ctx = new PluginContext(AppDomain.CurrentDomain.BaseDirectory, _pluginsDir, msg => _logger?.Invoke($"[Plugin:{pluginName}] {msg}"));
                var result = await instance.Plugin.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(true);
                result.Duration = DateTime.UtcNow - started;
                instance.LastMessage = result.Message;
                _logger?.Invoke($"[Plugins] '{pluginName}': {(result.Success ? "OK" : "FALHA")} em {result.Duration.TotalSeconds:0.0}s — {result.Message}");
                return result;
            }
            catch (OperationCanceledException)
            {
                instance.LastMessage = "Execução cancelada.";
                return PluginResult.Fail("Execução cancelada.");
            }
            catch (Exception ex)
            {
                instance.LastMessage = ex.Message;
                _logger?.Invoke($"[Plugins] Exceção em '{pluginName}': {ex.Message}");
                return PluginResult.Fail(ex.Message);
            }
            finally
            {
                instance.IsRunning = false;
                instance.State = instance.State == PluginLoadState.Executing ? PluginLoadState.Enabled : instance.State;
                PluginsChanged?.Invoke();
            }
        }

        public void ShutdownAll()
        {
            foreach (var p in Plugins)
            {
                try { p.Plugin.Shutdown(); }
                catch (Exception ex) { _logger?.Invoke($"[Plugins] Erro no Shutdown de '{p.Plugin.Name}': {ex.Message}"); }
            }
        }

        // ---------------------------------------------------------------
        // Internos
        // ---------------------------------------------------------------

        private static IEnumerable<string> EnumeratePluginDlls(string dir)
        {
            var files = new List<string>();
            if (!Directory.Exists(dir)) return files;

            files.AddRange(Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly));
            foreach (var sub in Directory.EnumerateDirectories(dir))
                files.AddRange(Directory.EnumerateFiles(sub, "*.dll", SearchOption.TopDirectoryOnly));

            return files;
        }

        private PluginInstance? TryLoadAssembly(string dllPath)
        {
            try
            {
                var asm = Assembly.LoadFrom(dllPath);
                foreach (var type in asm.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IKitLugiaPlugin).IsAssignableFrom(type)) continue;

                    if (Activator.CreateInstance(type) is not IKitLugiaPlugin plugin)
                    {
                        _logger?.Invoke($"[Plugins] Tipo '{type.FullName}' não pôde ser instanciado.");
                        continue;
                    }

                    return new PluginInstance
                    {
                        AssemblyPath = dllPath,
                        Plugin = plugin,
                        State = PluginLoadState.Disabled,
                    };
                }

                _logger?.Invoke($"[Plugins] Nenhum IKitLugiaPlugin encontrado em '{Path.GetFileName(dllPath)}'.");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Plugins] Falha ao carregar '{Path.GetFileName(dllPath)}': {ex.Message}");
                return new PluginInstance
                {
                    AssemblyPath = dllPath,
                    Plugin = new InvalidPlugin(Path.GetFileNameWithoutExtension(dllPath)),
                    State = PluginLoadState.LoadError,
                    LoadError = ex.Message,
                    LastMessage = ex.Message,
                };
            }
        }

        private void SaveState()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KitLugia");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "plugins_state.json");
                lock (_lock)
                {
                    File.WriteAllText(file, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[Plugins] Falha ao salvar estado: {ex.Message}");
            }
        }

        /// <summary>Stub usado para DLLs que falharam ao carregar (para exibir o erro na UI).</summary>
        private sealed class InvalidPlugin : IKitLugiaPlugin
        {
            private readonly string _name;
            public InvalidPlugin(string name) => _name = name;

            public string Name => _name;
            public string Version => "—";
            public string Author => "—";
            public string Description => "Falha ao carregar (DLL inválida ou dependência ausente).";
            public PluginKind Kind => PluginKind.Tool;
            public bool Initialize(PluginContext context) => false;
            public Task<PluginResult> ExecuteAsync(PluginContext context, CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Fail("Falha no carregamento."));
            public void Shutdown() { }
        }
    }
}
