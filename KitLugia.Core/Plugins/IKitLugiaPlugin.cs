using System;
using System.Threading;
using System.Threading.Tasks;

namespace KitLugia.Core.Plugins
{
    /// <summary>
    /// Tipo de plugin — define quando e como ele é executado pelo kit.
    /// </summary>
    public enum PluginKind
    {
        /// <summary>Ferramenta sob demanda: o usuário clica "Executar" na página de Plugins.</summary>
        Tool = 0,
        /// <summary>Twear de sistema/registro: também executável sob demanda, mas agrupado como otimização.</summary>
        Tweak = 1,
        /// <summary>Monitor/background: inicia automaticamente com o kit quando habilitado.</summary>
        Background = 2,
    }

    /// <summary>
    /// Resultado de uma execução de plugin.
    /// </summary>
    public sealed class PluginResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public TimeSpan Duration { get; set; }

        public static PluginResult Ok(string message = "Executado com sucesso.") => new() { Success = true, Message = message };
        public static PluginResult Fail(string message) => new() { Success = false, Message = message };
    }

    /// <summary>
    /// Contrato base de um plugin do KitLugia.
    /// Qualquer DLL .NET colocada na pasta <c>Plugins</c> (ou subpastas) do kit
    /// que exporte uma classe pública implementando esta interface é descoberta
    /// automaticamente pelo <see cref="PluginManager"/> no próximo carregamento.
    /// </summary>
    public interface IKitLugiaPlugin
    {
        /// <summary>Nome exibido na página de Plugins (ex.: "Limpeza de Prefetch").</summary>
        string Name { get; }

        /// <summary>Versão do plugin (ex.: "1.0.0").</summary>
        string Version { get; }

        /// <summary>Autor/desenvolvedor do plugin.</summary>
        string Author { get; }

        /// <summary>Descrição curta do que o plugin faz.</summary>
        string Description { get; }

        /// <summary>Tipo de plugin (Tool / Tweak / Background).</summary>
        PluginKind Kind { get; }

        /// <summary>
        /// Chamado uma única vez quando o plugin é carregado (antes de qualquer execução).
        /// Retorne false se o plugin não puder ser usado (ex.: falta de privilégio/admin).
        /// </summary>
        bool Initialize(PluginContext context);

        /// <summary>
        /// Executa a ação principal do plugin. Chamado pelo usuário (Tool/Tweak) ou
        /// automaticamente no arranque do kit (Background).
        /// </summary>
        Task<PluginResult> ExecuteAsync(PluginContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Chamado quando o kit fecha (ou quando o plugin é desabilitado em runtime).
        /// Use para liberar recursos, parar timers, desfazer mudanças, etc.
        /// </summary>
        void Shutdown();
    }
}
