using System.Diagnostics;
using System.IO;
using System.Windows;

namespace WindowsApp;

public partial class MainWindow : Window
{
    private readonly string _schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ninja_extracted_contracts.json");
    private bool _isInitialized = false;

    public MainWindow()
    {
        InitializeComponent();
        _isInitialized = true;
        if (MarketProfileTab != null)
        {
            MarketProfileTab.IsChecked = true;
        }
        LoadExtractedSchema();

        Ninja.Services.NinjaConnectionManager.Instance.ConnectionStateChanged += UpdateTopConnectionDisplay;
        UpdateTopConnectionDisplay();
    }

    private void UpdateTopConnectionDisplay()
    {
        Dispatcher.Invoke(() =>
        {
            if (TopConnectionText == null || TopConnectionDot == null || TopConnectionBorder == null) return;
            var active = Ninja.Services.NinjaConnectionManager.Instance.ActiveConnection;
            if (active != null)
            {
                TopConnectionText.Text = $"● {active.Name} ({active.LatencyMs}ms)";
                TopConnectionText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 250, 229));
                TopConnectionDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                TopConnectionBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
                TopConnectionBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            }
            else
            {
                TopConnectionText.Text = "● Disconnected";
                TopConnectionText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 202, 202));
                TopConnectionDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                TopConnectionBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 10, 10));
                TopConnectionBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
            }
        });
    }

    private void TopConnectionBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ConnectionsTab != null)
        {
            ConnectionsTab.IsChecked = true;
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || MarketProfileView == null || NinjaView == null || OrderFlowView == null || ConnectionsView == null || ReaView == null || SysView == null) return;

        if (MarketProfileTab?.IsChecked == true)
        {
            MarketProfileView.Visibility = Visibility.Visible;
            OrderFlowView.Visibility = Visibility.Collapsed;
            NinjaView.Visibility = Visibility.Collapsed;
            ConnectionsView.Visibility = Visibility.Collapsed;
            ReaView.Visibility = Visibility.Collapsed;
            SysView.Visibility = Visibility.Collapsed;
        }
        else if (OrderFlowTab?.IsChecked == true)
        {
            MarketProfileView.Visibility = Visibility.Collapsed;
            OrderFlowView.Visibility = Visibility.Visible;
            NinjaView.Visibility = Visibility.Collapsed;
            ConnectionsView.Visibility = Visibility.Collapsed;
            ReaView.Visibility = Visibility.Collapsed;
            SysView.Visibility = Visibility.Collapsed;
        }
        else if (NinjaTab?.IsChecked == true)
        {
            MarketProfileView.Visibility = Visibility.Collapsed;
            OrderFlowView.Visibility = Visibility.Collapsed;
            NinjaView.Visibility = Visibility.Visible;
            ConnectionsView.Visibility = Visibility.Collapsed;
            ReaView.Visibility = Visibility.Collapsed;
            SysView.Visibility = Visibility.Collapsed;
        }
        else if (ConnectionsTab?.IsChecked == true)
        {
            MarketProfileView.Visibility = Visibility.Collapsed;
            OrderFlowView.Visibility = Visibility.Collapsed;
            NinjaView.Visibility = Visibility.Collapsed;
            ConnectionsView.Visibility = Visibility.Visible;
            ReaView.Visibility = Visibility.Collapsed;
            SysView.Visibility = Visibility.Collapsed;
        }
        else if (ReaTab?.IsChecked == true)
        {
            MarketProfileView.Visibility = Visibility.Collapsed;
            OrderFlowView.Visibility = Visibility.Collapsed;
            NinjaView.Visibility = Visibility.Collapsed;
            ConnectionsView.Visibility = Visibility.Collapsed;
            ReaView.Visibility = Visibility.Visible;
            SysView.Visibility = Visibility.Collapsed;
            LoadExtractedSchema();
        }
        else if (SysTab?.IsChecked == true)
        {
            MarketProfileView.Visibility = Visibility.Collapsed;
            OrderFlowView.Visibility = Visibility.Collapsed;
            NinjaView.Visibility = Visibility.Collapsed;
            ConnectionsView.Visibility = Visibility.Collapsed;
            ReaView.Visibility = Visibility.Collapsed;
            SysView.Visibility = Visibility.Visible;
        }
    }

    private void LoadExtractedSchema()
    {
        if (ReaSchemaBox == null) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== MODULE 2: AMIBROKER 50CHARTS MARKET PROFILE REA SCHEMA ===");
            string mpSchemaPath = @"D:\AK10X\AK10X\WindowsApp\50charts_extracted_schema.json";
            if (File.Exists(mpSchemaPath))
            {
                sb.AppendLine(File.ReadAllText(mpSchemaPath));
            }
            else
            {
                sb.AppendLine("50Charts schema not generated yet.");
            }

            sb.AppendLine("\n\n=== MODULE 1: NINJATRADER 8 CONTRACTS & ENGINE REA SCHEMA ===");
            string projectPath = @"D:\AK10X\AK10X\WindowsApp\ninja_extracted_contracts.json";
            string reaPath = @"D:\AK10X\rea\ninja-contracts-schema.json";
            string target = File.Exists(projectPath) ? projectPath : (File.Exists(reaPath) ? reaPath : string.Empty);
            if (!string.IsNullOrEmpty(target) && File.Exists(target))
            {
                sb.AppendLine(File.ReadAllText(target));
            }

            ReaSchemaBox.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            ReaSchemaBox.Text = $"Error reading schemas: {ex.Message}";
        }
    }

    private void RunReaBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string scriptPath = @"D:\AK10X\rea\scripts\extract_ninja_contracts.ps1";
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show($"REA script not found at: {scriptPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(startInfo);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                LoadExtractedSchema();
                MessageBox.Show("REA Extraction succeeded!\n\n" + output, "REA Reverse Engineering", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to execute REA extraction: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ExportViewSnapshots(string outputDir)
    {
        try
        {
            Directory.CreateDirectory(outputDir);

            // 1. Snapshot MarketProfileView - Merged Mode
            if (MarketProfileTab != null) MarketProfileTab.IsChecked = true;
            Tab_Checked(this, new RoutedEventArgs());
            if (MarketProfileView.MergedRadio != null) MarketProfileView.MergedRadio.IsChecked = true;
            MarketProfileView.RenderProfileChart();
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "market_profile_50charts_merged.png"));

            // 2. Snapshot MarketProfileView - Split Mode
            if (MarketProfileView.SplitRadio != null) MarketProfileView.SplitRadio.IsChecked = true;
            MarketProfileView.RenderProfileChart();
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "market_profile_50charts_split.png"));

            // Restore Merged Mode
            if (MarketProfileView.MergedRadio != null) MarketProfileView.MergedRadio.IsChecked = true;
            MarketProfileView.RenderProfileChart();

            // 3. Snapshot OrderFlowView with Accelpix NIFTY_I Footprint
            if (OrderFlowTab != null) OrderFlowTab.IsChecked = true;
            Tab_Checked(this, new RoutedEventArgs());
            OrderFlowView.RedrawAll();
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "orderflow_footprint_nifty.png"));
            SaveVisualToPng(Path.Combine(outputDir, "chart_things_view.png"));

            // 3b. Snapshot OrderFlowView with Accelpix TATASTEEL_I Footprint
            OrderFlowView.SelectAndLoadSymbol("TATASTEEL_I");
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "orderflow_footprint_tatasteel.png"));

            // 4. Snapshot NinjaTrader 8 mzFootprint Properties Modal
            OrderFlowView.OpenProperties_Click(this, new RoutedEventArgs());
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "orderflow_properties_modal.png"));
            OrderFlowView.CloseProperties_Click(this, new RoutedEventArgs());

            // 5. Snapshot Symbol Search Modal
            OrderFlowView.SymbolSearchModal.Visibility = Visibility.Visible;
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "orderflow_symbol_search.png"));
            OrderFlowView.SymbolSearchModal.Visibility = Visibility.Collapsed;

            // 6. Snapshot ConnectionsView
            if (ConnectionsTab != null) ConnectionsTab.IsChecked = true;
            Tab_Checked(this, new RoutedEventArgs());
            UpdateLayout();
            SaveVisualToPng(Path.Combine(outputDir, "connections_view.png"));

            // Restore OrderFlowTab for interactive user session
            if (OrderFlowTab != null) OrderFlowTab.IsChecked = true;
            Tab_Checked(this, new RoutedEventArgs());
            UpdateLayout();
        }
        catch (Exception ex)
        {
            File.AppendAllText(@"D:\AK10X\app_error.log", $"Snapshot error: {ex.Message}\n{ex.StackTrace}\n");
        }
    }

    private void SaveVisualToPng(string filePath)
    {
        if (Content is not FrameworkElement root) return;
        int width = (int)Math.Max(1280, ActualWidth > 0 ? ActualWidth : 1280);
        int height = (int)Math.Max(800, ActualHeight > 0 ? ActualHeight : 800);

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }
}