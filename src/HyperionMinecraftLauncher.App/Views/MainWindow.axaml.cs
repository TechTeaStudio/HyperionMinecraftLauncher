using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Sidebar radio buttons call these instead of two-way binding so SelectedSection
    // is set even when the radio "Click" happens on an already-checked item (the
    // RadioButton.IsChecked-bound path swallows that case under compiled bindings).
    private void OnNavHome(object? sender, RoutedEventArgs e) => SetSection(NavSection.Home);
    private void OnNavInstallations(object? sender, RoutedEventArgs e) => SetSection(NavSection.Installations);
    private void OnNavSkins(object? sender, RoutedEventArgs e) => SetSection(NavSection.Skins);
    private void OnNavServers(object? sender, RoutedEventArgs e) => SetSection(NavSection.Servers);
    private void OnNavNews(object? sender, RoutedEventArgs e) => SetSection(NavSection.News);
    private void OnNavSettings(object? sender, RoutedEventArgs e) => SetSection(NavSection.Settings);

    private void SetSection(NavSection section)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedSection = section;
    }

    // News "Read more" -> open the article in the user's default browser. Tag holds the URL.
    private void OnOpenExternalLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort: a missing default browser shouldn't crash the launcher.
            }
        }
    }
}
