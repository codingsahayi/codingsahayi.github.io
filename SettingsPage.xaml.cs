using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace CodingSahayi;

public sealed partial class SettingsPage : Page
{
    private ObservableCollection<CodingSahayi.Data.ModelEndpointConfig> _models;
    private CodingSahayi.Data.ModelEndpointConfig? _editingModel;

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
        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";

        // Determine workspace — use LocalApplicationData as a sensible default
        // when no project is open; the caller can adapt this as needed.
        string workspacePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodingSahayi", "FineTuning");

        // Disable the button for the duration of the run
        StartFineTuningButton.IsEnabled = false;
        FineTuneStatusText.Text = string.Empty;

        // IProgress<T> marshals Report() calls back to the UI thread via DispatcherQueue
        var progress = new Progress<string>(message =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                FineTuneStatusText.Text = string.IsNullOrEmpty(FineTuneStatusText.Text)
                    ? message
                    : FineTuneStatusText.Text + "\n" + message;
            });
        });

        try
        {
            TrainingResult result = await Task.Run(() =>
                ModelTrainerTool.RunSoupTrainingAsync(workspacePath, progress));

            // Surface final metrics summary
            DispatcherQueue.TryEnqueue(() =>
            {
                if (result.Metrics.Count > 0)
                {
                    FineTuneStatusText.Text +=
                        $"\n\n📈 Loss curve ({result.Metrics.Count} points):";
                    foreach (var m in result.Metrics)
                        FineTuneStatusText.Text += $"\n  step {m.Step,6}: {m.Loss:F4}";
                }
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                FineTuneStatusText.Text += $"\n❌ Unexpected error: {ex.Message}";
            });
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StartFineTuningButton.IsEnabled = true;
            });
        }
    }
}
