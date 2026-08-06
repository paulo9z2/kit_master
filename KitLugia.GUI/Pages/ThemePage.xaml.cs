using System;
using System.IO;
using System.Text.Json;
using KitLugia.Core;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using LinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using GradientStop = System.Windows.Media.GradientStop;
using Point = System.Windows.Point;
using Window = System.Windows.Window;

namespace KitLugia.GUI.Pages
{
    public partial class ThemePage : System.Windows.Controls.Page
    {
        private static readonly string ThemeConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KitLugia",
            "theme.json");

        public ThemePage()
        {
            InitializeComponent();
        }

        private void BtnThemeModern_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyModernTheme();
            SaveTheme("modern");
            Logger.Log("🎨 Tema aplicado e salvo: Moderno (Ciano/Roxo)");
        }

        private void BtnThemeGold_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ApplyGoldTheme();
            SaveTheme("gold");
            Logger.Log("🎨 Tema aplicado e salvo: Original Dourado");
        }

        public static void ApplySavedTheme()
        {
            try
            {
                var theme = "modern";
                if (File.Exists(ThemeConfigPath))
                {
                    var json = File.ReadAllText(ThemeConfigPath);
                    theme = JsonSerializer.Deserialize<string>(json) ?? "modern";
                }

                if (theme == "gold")
                    ApplyGoldTheme();
                else
                    ApplyModernTheme();
            }
            catch (Exception ex)
            {
                Logger.Log($"❌ Erro ao carregar tema salvo: {ex.Message}");
                ApplyModernTheme();
            }
        }

        private static void SaveTheme(string name)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemeConfigPath) ?? "");
                File.WriteAllText(ThemeConfigPath, JsonSerializer.Serialize(name));
            }
            catch (Exception ex)
            {
                Logger.Log($"❌ Erro ao salvar tema: {ex.Message}");
            }
        }

        public static void ApplyModernTheme()
        {
            var res = Application.Current.Resources;

            res["WindowBackground"] = CreateGradient(
                (Color.FromRgb(0x0F, 0x0F, 0x23), 0.0),
                (Color.FromRgb(0x18, 0x18, 0x25), 0.4),
                (Color.FromRgb(0x1A, 0x0A, 0x2A), 1.0));

            res["AccentColor"] = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA));
            res["AccentPurple"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
            res["AccentCoral"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            res["CardBorder"] = new SolidColorBrush(Color.FromArgb(0x33, 0x8B, 0x5C, 0xF6));
            res["CardBackground"] = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            res["SidebarBackground"] = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            res["GoldGradient"] = CreateGradient(
                (Color.FromRgb(0x00, 0xD4, 0xAA), 0.0),
                (Color.FromRgb(0x8B, 0x5C, 0xF6), 1.0));

            RefreshWindow();
        }

        public static void ApplyGoldTheme()
        {
            var res = Application.Current.Resources;

            res["WindowBackground"] = CreateGradient(
                (Color.FromRgb(0x1A, 0x15, 0x05), 0.0),
                (Color.FromRgb(0x0A, 0x0A, 0x0A), 0.3),
                (Color.FromRgb(0x05, 0x05, 0x05), 1.0));

            res["AccentColor"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            res["AccentPurple"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
            res["AccentCoral"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            res["CardBorder"] = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xD7, 0x00));
            res["CardBackground"] = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            res["SidebarBackground"] = new SolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x12));
            res["GoldGradient"] = CreateGradient(
                (Color.FromRgb(0xFF, 0xD7, 0x00), 0.0),
                (Color.FromRgb(0xFF, 0xA5, 0x00), 1.0));

            RefreshWindow();
        }

        private static LinearGradientBrush CreateGradient(params (Color Color, double Offset)[] stops)
        {
            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            foreach (var (color, offset) in stops)
                brush.GradientStops.Add(new GradientStop(color, offset));
            brush.Freeze();
            return brush;
        }

        private static void RefreshWindow()
        {
            foreach (Window win in Application.Current.Windows)
            {
                win.InvalidateVisual();
                win.UpdateLayout();
            }
        }
    }
}
