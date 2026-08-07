# ModernWPF Implementation Log — KitLugia

> **Propósito**: este arquivo documenta EXATAMENTE cada passo feito para implementar ModernWPF no KitLugia. Uma IA que ler este arquivo deve conseguir replicar tudo sem precisar de contexto externo (exceto o código-fonte do KitLugia).

---

## Contexto Inicial (antes de qualquer mudança)

- Projeto: `KitLugia.GUI` (`KitLugia.GUI.csproj`)
- Target: `.net10.0-windows10.0.26100.0`
- Estado do build antes: `0 Erros / 0 Erros` (Core e GUI verificados com `dotnet build -c Release`)
- Estado atual do código: 48 arquivos XAML convertidos para `DynamicResource` (tema Gold/Modern); `App.xaml` com recursos próprios; `TrayIconService.cs` (4286 linhas); `DeepUninstaller.cs` (4241 linhas); `LargePageManager.cs` (258 linhas); `DeepUninstallerSettings.cs` (novo, criado nas melhorias do Revo/PC Manager)
- Referência local: `docs/ValidationOS.md` (linha 92, 101, 138, 141-149): `WPF-Support-Package` inclui WPF + .NET + WinUI3 (desde 2509); WPF funciona desde 2504 sem CAB WinUI3 extra.
- Comparação feita: `WINUI3_PLANO.md` (opções A/B/C/D) + `WINUI3_PLANO_RESUMO.md` (A vs B) + `WINUI3_SIMULACAO.md` (simulação aplicada ao projeto real).
- Decisão: implementar **Opção B — ModernWPF** (`ModernWpfUI`, github.com/Kinnara/ModernWpf, MIT, 4.9k stars, versão `1.0.0-preview.1`).
- Nenhum arquivo do KitLugia foi modificado até a criação deste log.

---

## Passo 1 — Criar este arquivo de log
Arquivo criado: `MODERNWPF_IMPLEMENTATION.md` (este arquivo). Nenhuma mudança no código ainda.

---

## Passo 2 — Adicionar referência ModernWpfUI no .csproj
Arquivo modificado: `KitLugia.GUI/KitLugia.GUI.csproj`
Ação: adicionada `PackageReference` `ModernWpfUI` versão `1.0.0-preview.1` após a `ProjectReference` do Core.
Comando equivalente: `dotnet add package ModernWpfUI --version 1.0.0-preview.1`
Arquivo resultante: `KitLugia.GUI/KitLugia.GUI.csproj` (linha 89-92 adicionada). Nenhum arquivo .cs modificado ainda.
Verificação: `dotnet restore` OK; `dotnet build -c Release` = `0 Erro(s)`.

---

## Passo 3 — Integrar ThemeResources + FluentControlsResources no App.xaml
Arquivo modificado: `KitLugia.GUI/App.xaml`
Mudança:
```xaml
<Application x:Class="KitLugia.GUI.App" ... xmlns:ui="http://schemas.modernwpf.com/2019">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemeResources />
                <ResourceDictionary Source="Themes/Generic.xaml"/>
                <ui:FluentControlsResources />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```
Regra de ouro (IMPORTANTE — precedência do WPF): em merged dictionaries, a PRIMEIRA ocorrência da chave vence. Logo a ordem é:
1) `<ui:ThemeResources />` — brushes de tema que os controles ModernWPF usam (é obrigatório existir para os estilos Fluent resolverem).
2) `<ResourceDictionary Source="Themes/Generic.xaml"/>` — estilos Gold/Modern do KitLugia (vencem nos controles stock: Button, CheckBox, DataGrid, etc., preservando o tema Gold atual).
3) `<ui:FluentControlsResources />` — fornece estilos/controles modernos APENAS para tipos que o kit NÃO define (InfoBar, NumberBox, NavigationView, etc.). Staged migration sem regressão visual.

⚠️ Armadilha comum: se o FluentControlsResources vier ANTES do Generic.xaml, os controles stock do app mudam para o visual Fluent azul, quebrando o tema Gold. A ordem acima é a correta.
Verificação: `dotnet build -c Release` = `0 Erro(s)`.

---

## Passo 4 — Validação em runtime (sem crash de XAML)
Método de teste usado (PowerShell, do diretório raiz do repo):
```powershell
# 1) Build Release
dotnet build -c Release

# 2) Teste com --tray (modo bandeja, sem janela): o app deve RODAR e ficar vivo
$exe = "KitLugia.GUI\bin\Release\net10.0-windows10.0.26100.0\KitLugia.GUI.exe"
$p = Start-Process -FilePath $exe -ArgumentList "--tray" -PassThru
Start-Sleep -Seconds 6
# Esperado: $p.HasExited = $false (app vivo no tray)

# 3) Encerrar o teste
Stop-Process -Name "KitLugia.GUI" -Force
```
Resultado observado (08/06/2026 23:48):
- Processo vivo no tray (PID 28540); log em `%LOCALAPPDATA%\KitLugia\Logs\KitLugia.log` mostrou `RAM Limiter timer iniciado (1000ms)`, `Verificação de inicialização concluída com sucesso`, `Tray Icon está saudável`.
- Nenhum erro de recurso XAML/ModernWPF no log.

⚠️ Interpretação do exit code 0: o app tem single-instance (`Mutex "Global\KitLugia_SingleInstance"` em `KitLugia.GUI/Program.cs`, linhas 46-62). Se uma instância já estiver rodando, um segundo launch adquire o mutex, chama `BringExistingToFront()` e SAI com exit code 0 — isto NÃO é crash. Para testar o arranque completo, garanta que nenhuma instância esteja aberta antes (ou use `--tray`).

Observação adicional: logs de `WARN ... Exception suppressed` no log são HISTÓRICOS (builds anteriores ao anti-spam) — os 977 catch silenciosos já foram removidos do código, os novos runs não produzem esse spam.

