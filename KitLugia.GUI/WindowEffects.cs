using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using KitLugia.Core;

namespace KitLugia.GUI
{
    /// <summary>
    /// Efeitos visuais da janela via DWM (Windows 11: Mica/Acrylic/rounded corners;
    /// Windows 10: fallback Acrylic via SetWindowCompositionAttribute).
    /// Requer que o Window.Background tenha alpha para o backdrop aparecer.
    /// </summary>
    public static class WindowEffects
    {
        // DWMWA_* (dwmapi)
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private const int DWMWA_SYSTEMBACKDROP_MICA = 2;
        private const int DWMWA_SYSTEMBACKDROP_ACRYLIC = 3;

        private const int DWMWCP_DEFAULT = 0;
        private const int DWMWCP_ROUND = 2;

        // SetWindowCompositionAttribute (user32, não documentado — fallback Win10)
        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        /// <summary>Versão mínima do Windows 11 (build 22000).</summary>
        private static bool IsWindows11OrGreater()
        {
            var v = Environment.OSVersion.Version;
            return v.Build >= 22000;
        }

        /// <summary>
        /// Aplica backdrop (Mica/Acrylic) + cantos arredondados à janela.
        /// Chamar no Loaded/SourceInitialized. Sempre seguro (try/catch interno).
        /// </summary>
        public static void ApplyBackdrop(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                if (IsWindows11OrGreater())
                {
                    // Preferência: Mica (sutil, wallpaper-aware). Se falhar, Acrylic.
                    int mica = DWMWA_SYSTEMBACKDROP_MICA;
                    int acrylic = DWMWA_SYSTEMBACKDROP_ACRYLIC;

                    bool ok = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int)) == 0;
                    if (!ok)
                        ok = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int)) == 0;

                    if (ok)
                    {
                        // Cantos arredondados (DWM clipa o conteúdo — combina com o WindowChrome custom)
                        int round = DWMWCP_ROUND;
                        if (DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)) != 0)
                        {
                            // Round corners não suportado (ex: builds antigas) — ok, segue sem.
                        }
                        Logger.Log("🎨 Backdrop DWM aplicado (Mica/Acrylic) + cantos arredondados");
                        return;
                    }

                    // Win11 mas sem suporte ao sistema de backdrops (ex: Server/PE sem DWM) → tenta fallback Win10
                    Logger.Log("⚠️ Backdrop DWM indisponível, tentando fallback Win10...");
                }

                // Fallback Windows 10/Server: Acrylic via SetWindowCompositionAttribute
                if (TryApplyLegacyAcrylic(hwnd))
                {
                    Logger.Log("🎨 Backdrop Acrylic (legacy Win10) aplicado");
                    return;
                }

                Logger.Log("⚠️ Backdrop não suportado neste sistema — mantendo fundo sólido");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("WindowEffects", $"Falha ao aplicar backdrop: {ex.Message}");
            }
        }

        private static bool TryApplyLegacyAcrylic(IntPtr hwnd)
        {
            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2, // ACCENT_APPLY_CORNER_ROUNDING? Não — flags 2 = draw all borders; mantém efeito
                    GradientColor = 0xCC0F0F23 // AABBGGRR — leve tint escuro (abaixa o alpha do backdrop)
                };

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                    Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>())
                };

                try
                {
                    Marshal.StructureToPtr(accent, data.Data, false);
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(data.Data);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
