using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KitLugia.Core.Plugins;

using Button = System.Windows.Controls.Button;
using Application = System.Windows.Application;

namespace KitLugia.GUI.Pages
{
    /// <summary>Item exibido na lista de plugins (wrapper de PluginInstance para a UI).</summary>
    public sealed class PluginItem
    {
        public required PluginInstance Instance { get; init; }

        public string Name => Instance.Plugin.Name;
        public string Version => $"v{Instance.Plugin.Version}";
        public string Author => $"por {Instance.Plugin.Author}";
        public string Description => Instance.Plugin.Description;

        public string KindBadge => Instance.Plugin.Kind switch
        {
            PluginKind.Tool => "FERRAMENTA",
            PluginKind.Tweak => "TWEAK",
            PluginKind.Background => "BACKGROUND",
            _ => "PLUGIN",
        };

        public string KindColor => Instance.Plugin.Kind switch
        {
            PluginKind.Tweak => "#00D4AA",
            PluginKind.Background => "#FFB020",
            _ => "#58A6FF",
        };

        public string StatusText => Instance.State switch
        {
            PluginLoadState.Enabled => "Status: habilitado",
            PluginLoadState.Disabled => "Status: desabilitado",
            PluginLoadState.Executing => "Status: executando...",
            PluginLoadState.LoadError or PluginLoadState.Failed => $"Erro: {Instance.LastMessage}",
            _ => "Status: desconhecido",
        };

        public string StatusColor => Instance.State switch
        {
            PluginLoadState.Enabled => "#4CAF50",
            PluginLoadState.Disabled => "#999",
            PluginLoadState.Executing => "#FFB020",
            PluginLoadState.LoadError or PluginLoadState.Failed => "#FF6F61",
            _ => "#999",
        };

        public string ToggleText => Instance.State == PluginLoadState.Disabled ? "HABILITAR" : "DESABILITAR";
    }

    public partial class PluginsPage : Page
    {
        private readonly ObservableCollection<PluginItem> _items = new();

        public PluginsPage()
        {
            InitializeComponent();
            PluginList.ItemsSource = _items;
            PluginManager.Instance.PluginsChanged += OnPluginsChanged;
            this.Unloaded += (_, _) => PluginManager.Instance.PluginsChanged -= OnPluginsChanged;
            Loaded += (_, _) => Refresh();
        }

        private void OnPluginsChanged() => Dispatcher.InvokeAsync(Refresh);

        private void Refresh()
        {
            TxtPluginsDir.Text = PluginManager.Instance.PluginsDirectory;
            _items.Clear();
            foreach (var p in PluginManager.Instance.Plugins)
                _items.Add(new PluginItem { Instance = p });

            var (total, enabled, errors) = (PluginManager.Instance.Plugins.Count, 0, 0);
            foreach (var p in PluginManager.Instance.Plugins)
            {
                if (p.State == PluginLoadState.Enabled || p.State == PluginLoadState.Executing) enabled++;
                else if (p.State == PluginLoadState.LoadError || p.State == PluginLoadState.Failed) errors++;
            }

            TxtSummary.Text = total == 0
                ? "Nenhum plugin encontrado. Coloque DLLs .NET na pasta Plugins (ou subpastas) e clique em RECARREGAR PLUGINS."
                : $"{total} plugin(s) carregado(s) — {enabled} habilitado(s)";
            if (errors > 0) TxtSummary.Text += $" — {errors} com erro de carregamento";
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PluginManager.Instance.LoadPlugins();
                ShowMessage("PLUGINS", "Plugins recarregados com sucesso.");
            }
            catch (Exception ex)
            {
                ShowMessage("ERRO", $"Falha ao recarregar plugins: {ex.Message}");
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = PluginManager.Instance.PluginsDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }

        private async void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PluginItem item)
            {
                btn.IsEnabled = false;
                try
                {
                    var result = await PluginManager.Instance.ExecuteAsync(item.Name, default);
                    if (result.Success) ShowMessage(item.Name, result.Message);
                    else ShowMessage("ERRO", result.Message);
                }
                finally
                {
                    btn.IsEnabled = true;
                    Refresh();
                }
            }
        }

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PluginItem item)
            {
                var enabled = item.Instance.State == PluginLoadState.Disabled;
                PluginManager.Instance.SetEnabled(item.Name, enabled);
                ShowMessage(item.Name, enabled ? "Plugin habilitado." : "Plugin desabilitado.");
            }
        }

        private void ShowMessage(string title, string message)
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                if (message.StartsWith("ERRO") || title == "ERRO")
                    mw.ShowError(title, message);
                else
                    mw.ShowInfo(title, message);
            }
        }
    }
}
