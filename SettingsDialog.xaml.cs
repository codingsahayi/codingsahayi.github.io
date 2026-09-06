using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace CodingSahayi;

public sealed partial class SettingsDialog : ContentDialog
{
    private ObservableCollection<CodingSahayi.Data.ModelEndpointConfig> _models;
    private CodingSahayi.Data.ModelEndpointConfig? _editingModel;

    public SettingsDialog()
    {
        this.InitializeComponent();
        
        _models = new ObservableCollection<CodingSahayi.Data.ModelEndpointConfig>(SettingsManager.ConfiguredModels);
        ModelsListView.ItemsSource = _models;
        
        SystemPromptBox.Text = SettingsManager.SystemPrompt;

        SoupPathBox.Text = SettingsManager.SoupPath;
        SoupBaseModelBox.Text = SettingsManager.SoupBaseModel;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        SettingsManager.ConfiguredModels = _models.ToList();
        
        SettingsManager.SystemPrompt = SystemPromptBox.Text;

        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";
    }

    private void AddModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _editingModel = null;
        EditDisplayName.Text = "";
        EditModelIdentifier.Text = "";
        EditBaseUrl.Text = "http://localhost:11434/v1";
        EditApiKey.Password = "";
        EditTypeBox.SelectedIndex = 1;
        EditCostTierBox.SelectedIndex = 2;
        EditPriority.Value = 1;
        EditFallbackId.Text = "";
        
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
    }

    private void EditModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is CodingSahayi.Data.ModelEndpointConfig model)
        {
            _editingModel = model;
            EditDisplayName.Text = model.DisplayName;
            EditModelIdentifier.Text = model.ModelIdentifier;
            EditBaseUrl.Text = model.BaseUrl;
            EditApiKey.Password = model.ApiKey;
            EditTypeBox.SelectedIndex = (int)model.Type;
            EditCostTierBox.SelectedIndex = (int)model.CostTier;
            EditPriority.Value = model.Priority;
            EditFallbackId.Text = model.FallbackModelId ?? "";
            
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
        _editingModel.Type = (CodingSahayi.Data.ModelType)EditTypeBox.SelectedIndex;
        _editingModel.CostTier = (CodingSahayi.Data.CostTier)EditCostTierBox.SelectedIndex;
        _editingModel.Priority = (int)EditPriority.Value;
        _editingModel.FallbackModelId = string.IsNullOrWhiteSpace(EditFallbackId.Text) ? null : EditFallbackId.Text;
        
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        
        // Refresh ListView
        ModelsListView.ItemsSource = null;
        ModelsListView.ItemsSource = _models;
    }

    private void CancelEditModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
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
