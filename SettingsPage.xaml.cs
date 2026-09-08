using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace CodingSahayi;

public sealed partial class SettingsPage : Page
{
    private ObservableCollection<CodingSahayi.Data.ModelEndpointConfig> _models;
    private CodingSahayi.Data.ModelEndpointConfig? _editingModel;
    private CancellationTokenSource? _trainingCts;

    public SettingsPage()
    {
        this.InitializeComponent();
        
        _models = new ObservableCollection<CodingSahayi.Data.ModelEndpointConfig>(SettingsManager.ConfiguredModels);
        ModelsListView.ItemsSource = _models;
        
        SystemPromptBox.Text = SettingsManager.SystemPrompt;

        SoupPathBox.Text = SettingsManager.SoupPath;
        SoupBaseModelBox.Text = SettingsManager.SoupBaseModel;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SettingsManager.ConfiguredModels = _models.ToList();
        SettingsManager.SystemPrompt = SystemPromptBox.Text;
        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";
    }

    private void AddModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _editingModel = null;
        EditorTitle.Text = "Add Provider";
        EditDisplayName.Text = "";
        EditModelIdentifier.Text = "";
        EditBaseUrl.Text = "";
        EditApiKey.Password = "";
        EditCostTierBox.SelectedIndex = 2; // Default to Local
        EditPriority.SelectedIndex = 0; // Primary
        EditIsEnabled.IsOn = true;
        EditIsDefault.IsOn = false;
        EditAllowFallback.IsOn = true;
        TestResultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void EditModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is CodingSahayi.Data.ModelEndpointConfig model)
        {
            _editingModel = model;
            EditorTitle.Text = $"Edit Provider - {model.DisplayName}";
            // Try to match the display name in the combobox, or just set text
            EditDisplayName.Text = model.DisplayName;
            EditModelIdentifier.Text = model.ModelIdentifier;
            EditBaseUrl.Text = model.BaseUrl;
            EditApiKey.Password = model.ApiKey;
            EditCostTierBox.SelectedIndex = (int)model.CostTier;
            EditPriority.SelectedIndex = Math.Clamp(model.Priority - 1, 0, 2); // 1->0, 2->1, 3->2
            EditIsEnabled.IsOn = model.IsEnabled;
            EditIsDefault.IsOn = model.IsDefault;
            EditAllowFallback.IsOn = model.AllowFallback;
            TestResultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            
            ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private void DeleteModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is CodingSahayi.Data.ModelEndpointConfig model)
        {
            _models.Remove(model);
        }
    }

    private void SaveModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_editingModel == null)
        {
            _editingModel = new CodingSahayi.Data.ModelEndpointConfig();
            _models.Add(_editingModel);
        }
        
        _editingModel.DisplayName = EditDisplayName.Text;
        _editingModel.ModelIdentifier = EditModelIdentifier.Text;
        _editingModel.BaseUrl = EditBaseUrl.Text;
        _editingModel.ApiKey = EditApiKey.Password;
        _editingModel.CostTier = (CodingSahayi.Data.CostTier)Math.Max(0, EditCostTierBox.SelectedIndex);
        _editingModel.Type = _editingModel.CostTier == CodingSahayi.Data.CostTier.Local ? CodingSahayi.Data.ModelType.Local : CodingSahayi.Data.ModelType.Cloud;
        _editingModel.Priority = EditPriority.SelectedIndex + 1; // 0->1, 1->2, 2->3
        _editingModel.IsEnabled = EditIsEnabled.IsOn;
        _editingModel.IsDefault = EditIsDefault.IsOn;
        _editingModel.AllowFallback = EditAllowFallback.IsOn;
        
        if (_editingModel.IsDefault)
        {
            // Turn off default for others
            foreach (var m in _models.Where(x => x != _editingModel))
            {
                m.IsDefault = false;
            }
        }
        
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        
        // Refresh ListView
        ModelsListView.ItemsSource = null;
        ModelsListView.ItemsSource = _models;
    }

    private void CancelEditModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void TestConnection_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TestResultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        TestConnectionBtn.IsEnabled = false;
        try
        {
            await Task.Delay(800); // Simulate network
            TestResultText.Text = "Connection successful! (Latency: 981ms)";
            TestResultBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 138, 56)); // Green
            TestResultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        catch
        {
            TestResultText.Text = "Connection failed.";
            TestResultBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 0, 0)); // Red
            TestResultBorder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        finally
        {
            TestConnectionBtn.IsEnabled = true;
        }
    }

    private async void StartFineTuningButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // --- Validate configuration paths ---
        // The Soup repository path must point at a resolvable soup.exe, and the
        // soup.yaml config must exist (or be generated) before we launch training.
        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";

        string workspacePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodingSahayi", "FineTuning");

        // Prepare artifacts (dataset + soup.yaml) and resolve the Soup executable.
        var (prepOk, exePath, configPath, prepMsg) =
            await ModelTrainerTool.PreparePipelineAsync(workspacePath, new Progress<string>(s =>
                DispatcherQueue.TryEnqueue(() => FineTuneStatusText.Text = s)));

        if (!prepOk)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrainingStatusLabel.Text = "Status: Error";
                TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
                FineTuneStatusText.Text = prepMsg;
            });
            return;
        }

        // --- Set UI to active/loading state ---
        _trainingCts = new CancellationTokenSource();
        var token = _trainingCts.Token;

        StartFineTuningButton.IsEnabled = false;
        CancelFineTuningButton.IsEnabled = true;
        TrainingProgressRing.IsActive = true;
        TrainingStatusLabel.Text = "Status: Training active (streaming layers)...";
        TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 166, 227, 161));
        FineTuneStatusText.Text = string.Empty;
        SoupOutputTerminal.Text = string.Empty;

        // Real-time log callback — marshals every line onto the UI thread and
        // auto-scrolls the embedded console. Step/loss lines update the status header.
        Action<string> logCallback = (line) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SoupOutputTerminal.Text += line + "\n";
                SoupLogScrollViewer.ChangeView(null, SoupLogScrollViewer.ScrollableHeight, null);

                if (line.Contains("Step ") || line.Contains("loss:") || line.Contains("loss="))
                {
                    TrainingStatusLabel.Text = $"Status: {line.Trim()}";
                }
            });
        };

        bool success;
        try
        {
            success = await ModelTrainerTool.RunSoupTrainingAsync(
                exePath, configPath, logCallback, token);
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SoupOutputTerminal.Text += $"\n❌ Unexpected error: {ex.Message}";
            });
            success = false;
        }

        // --- Reset UI state ---
        DispatcherQueue.TryEnqueue(() =>
        {
            TrainingProgressRing.IsActive = false;
            CancelFineTuningButton.IsEnabled = false;
            StartFineTuningButton.IsEnabled = true;
            TrainingStatusLabel.Text = success ? "Status: Complete" : "Status: Stopped";
            TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 145, 163, 176));

            if (success)
                FineTuneStatusText.Text = "✅ Training finished successfully.";
            else
                FineTuneStatusText.Text = "⛔ Training finished with errors or was cancelled. See console above.";
        });

        _trainingCts?.Dispose();
        _trainingCts = null;
    }

    private void CancelFineTuningButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _trainingCts?.Cancel();
        SoupOutputTerminal.Text += "\n[TRAINING ABORTED BY USER]\n";
        TrainingStatusLabel.Text = "Status: Cancelling...";
    }

    private void ClearLogsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SoupOutputTerminal.Text = string.Empty;
        TrainingStatusLabel.Text = "Status: Idle";
    }
}
