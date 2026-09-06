using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace CodingSahayi;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog()
    {
        this.InitializeComponent();
        
        ApiKeyBox.Password = SettingsManager.SecureApiKey;
        EndpointBox.Text = SettingsManager.ApiEndpoint;
        
        LocalApiKeyBox.Password = SettingsManager.LocalApiKey;
        LocalEndpointBox.Text = SettingsManager.LocalApiBaseUrl;
        LocalModelNameBox.Text = SettingsManager.LocalModelName;
        
        // Populate model ComboBox with saved models, select the active one
        ModelNameBox.ItemsSource = SettingsManager.AvailableModels;
        ModelNameBox.SelectedItem = SettingsManager.ModelName;
        
        SystemPromptBox.Text = SettingsManager.SystemPrompt;

        SoupPathBox.Text = SettingsManager.SoupPath;
        SoupBaseModelBox.Text = SettingsManager.SoupBaseModel;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        SettingsManager.SecureApiKey = ApiKeyBox.Password;
        SettingsManager.ApiEndpoint = EndpointBox.Text;
        
        SettingsManager.LocalApiKey = LocalApiKeyBox.Password;
        SettingsManager.LocalApiBaseUrl = LocalEndpointBox.Text;
        SettingsManager.LocalModelName = LocalModelNameBox.Text?.Trim() ?? "local-model";
        
        string selectedModel = ModelNameBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(selectedModel))
        {
            SettingsManager.ModelName = selectedModel;
            SettingsManager.EnsureModelInList(selectedModel);
        }
        
        SettingsManager.SystemPrompt = SystemPromptBox.Text;

        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";
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

