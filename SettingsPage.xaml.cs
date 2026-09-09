using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Storage.Pickers;

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

        DatabasePathBox.Text = SettingsManager.DatabasePath;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SettingsManager.ConfiguredModels = _models.ToList();
        SettingsManager.SystemPrompt = SystemPromptBox.Text;
        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";
        var dbPath = DatabasePathBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(dbPath))
            SettingsManager.DatabasePath = dbPath;
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
        TestConnectionInfoBar.IsOpen = false;
        
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
            TestConnectionInfoBar.IsOpen = false;
            
            ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private void DeleteModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is CodingSahayi.Data.ModelEndpointConfig model)
        {
            _models.Remove(model);

            // Persist immediately (not deferred to OnNavigatedFrom) and purge the
            // provider's secret from the PasswordVault.
            SettingsManager.ConfiguredModels = _models.ToList();
            SettingsManager.RemoveModelApiKey(model.Id);

            ModelsListView.ItemsSource = null;
            ModelsListView.ItemsSource = _models;
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
        
        // Persist immediately (not deferred to OnNavigatedFrom) so the provider and its
        // API key survive an app restart without navigating away.
        SettingsManager.ConfiguredModels = _models.ToList();

        // Hide the editor and refresh the Providers list, then sync the MainWindow dropdown.
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        ModelsListView.ItemsSource = null;
        ModelsListView.ItemsSource = _models;

        // Trigger a dropdown refresh so the new/edited model appears in the MainWindow selector.
        if (Microsoft.UI.Xaml.Application.Current is App app && app._window is MainWindow mw)
        {
            mw.RefreshModelDropdown();
        }
    }

    private void CancelEditModel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ModelEditorPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void TestConnectionButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TestConnectionInfoBar.IsOpen = false;
        TestConnectionButton.IsEnabled = false;

        try
        {
            // Read the current editor fields directly (don't rely on the saved model).
            string endpoint = EditBaseUrl.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(endpoint))
                endpoint = "http://localhost:11434/v1";
            string key = string.IsNullOrWhiteSpace(EditApiKey.Password) ? "ollama" : EditApiKey.Password.Trim();
            string model = EditModelIdentifier.Text?.Trim() ?? "";

            // Query the endpoint's /models list with a strict 5-second timeout.
            string baseUrl = endpoint.TrimEnd('/');

            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var http = new System.Net.Http.HttpClient();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            http.Timeout = TimeSpan.FromSeconds(5);

            string requestUrl = baseUrl + "/models";
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, requestUrl);
            if (!string.Equals(key, "ollama", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(EditApiKey.Password))
            {
                // Only send an auth header when a real key is configured.
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            }

            var response = await http.SendAsync(request, cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                TestConnectionInfoBar.Severity = InfoBarSeverity.Success;
                TestConnectionInfoBar.Message = $"Connection successful! Model endpoint reachable (HTTP {(int)response.StatusCode}, latency {sw.ElapsedMilliseconds} ms).";
                TestConnectionInfoBar.IsOpen = true;
            }
            else
            {
                TestConnectionInfoBar.Severity = InfoBarSeverity.Error;
                TestConnectionInfoBar.Message = $"Connection failed: Server returned HTTP {(int)response.StatusCode}.";
                TestConnectionInfoBar.IsOpen = true;
            }
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            TestConnectionInfoBar.Severity = InfoBarSeverity.Error;
            TestConnectionInfoBar.Message = "Cannot connect to server. Ensure Ollama/service is running on the specified port. (" + (ex.InnerException?.Message ?? ex.Message) + ")";
            TestConnectionInfoBar.IsOpen = true;
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            TestConnectionInfoBar.Severity = InfoBarSeverity.Error;
            TestConnectionInfoBar.Message = "Connection timed out after 5 seconds.";
            TestConnectionInfoBar.IsOpen = true;
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            TestConnectionInfoBar.Severity = InfoBarSeverity.Error;
            TestConnectionInfoBar.Message = "Connection failed: " + ex.Message;
            TestConnectionInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            TestConnectionInfoBar.Severity = InfoBarSeverity.Error;
            TestConnectionInfoBar.Message = "Connection failed: " + ex.Message;
            TestConnectionInfoBar.IsOpen = true;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private async void StartFineTuningButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // --- Validate configuration paths ---
        // The Soup repository path must point at a resolvable soup.exe, and the
        // soup.yaml config must exist (or be generated) before we launch training.
        SettingsManager.SoupPath = SoupPathBox.Text?.Trim() ?? @"D:\Soup";
        SettingsManager.SoupBaseModel = SoupBaseModelBox.Text?.Trim() ?? "Qwen/Qwen2.5-Coder-1.5B";

        // RESOLVE A NON-VIRTUALIZED WORKSPACE.
        // MSIX File System Virtualization redirects %LocalAppData% writes away from external
        // Win32 tools (soup.exe / python), so a config written to the virtualized AppData path
        // is invisible to the process. We therefore place artifacts on a plain disk location.
        string workspacePath;
        string soupRepo = SettingsManager.SoupPath?.Trim() ?? @"D:\Soup";
        if (!string.IsNullOrWhiteSpace(soupRepo) && System.IO.Directory.Exists(soupRepo))
        {
            // Configured Soup repo is present — locate the workspace inside it.
            workspacePath = System.IO.Path.Combine(soupRepo, "workspace");
        }
        else if (System.IO.Directory.Exists(@"D:\"))
        {
            // Fallback to a plain (non-AppData) location on the D: drive.
            workspacePath = @"D:\CodingSahayi\FineTuning";
        }
        else
        {
            // Last-resort fallback under the user profile (still outside virtualized AppData).
            workspacePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codingsahayi", "FineTuning");
        }
        workspacePath = workspacePath.TrimEnd('\\', '/');

        // Soup CLI searches for a config relative to its working directory. To cover both
        // the ".soup/" sub-directory and the workspace root, we write the config to BOTH.
        // Ensure the non-virtualized directory structure exists first.
        var dotSoupDir = System.IO.Path.Combine(workspacePath, ".soup");
        if (!System.IO.Directory.Exists(workspacePath))
            System.IO.Directory.CreateDirectory(workspacePath);
        if (!System.IO.Directory.Exists(dotSoupDir))
            System.IO.Directory.CreateDirectory(dotSoupDir);

        // Artifact paths all live in the non-virtualized workspace.
        var datasetPath = System.IO.Path.Combine(workspacePath, "dataset.jsonl");
        var configPath  = System.IO.Path.Combine(dotSoupDir, "soup.yaml");
        var rootConfigPath = System.IO.Path.Combine(workspacePath, "soup.yaml");

        // Initialise the terminal buffer so diagnostics stream visibly.
        SoupOutputTerminal.Text =
            $"[DIAGNOSTIC] Workspace: {workspacePath}\n" +
            $"[DIAGNOSTIC] Writing config to: {configPath}\n" +
            $"[DIAGNOSTIC] Root config path: {rootConfigPath}\n";

        // 1. Export the ProjectKnowledge database to JSONL (guaranteed >=1 line).
        DispatcherQueue.TryEnqueue(() => FineTuneStatusText.Text = "📦 Exporting ProjectKnowledge to JSONL dataset…");
        int recordCount;
        try
        {
            recordCount = await DatasetExporter.ExportAsync(datasetPath);
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrainingStatusLabel.Text = "Status: Error";
                TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
                SoupOutputTerminal.Text += $"❌ Dataset export failed: {ex.Message}\n";
                FineTuneStatusText.Text = $"❌ Dataset export failed: {ex.Message}";
            });
            return;
        }
        SoupOutputTerminal.Text += $"[DIAGNOSTIC] Dataset records written: {recordCount} → {datasetPath}\n";

        // 2. Write soup.yaml to BOTH .soup/soup.yaml and workspace root, synchronously
        //    flushed to physical disk before the process is spawned.
        DispatcherQueue.TryEnqueue(() => FineTuneStatusText.Text = "⚙️  Writing soup.yaml LoRA config…");
        try
        {
            await SoupConfigManager.WriteConfigFileAsync(configPath, workspacePath, datasetPath);
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrainingStatusLabel.Text = "Status: Error";
                TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
                SoupOutputTerminal.Text += $"❌ Config generation failed: {ex.Message}\n";
                FineTuneStatusText.Text = $"❌ Config generation failed: {ex.Message}";
            });
            return;
        }

        // Physical verification on disk for the primary config.
        bool configExists = System.IO.File.Exists(configPath);
        long configLength = configExists ? new System.IO.FileInfo(configPath).Length : 0;
        bool rootExists = System.IO.File.Exists(rootConfigPath);
        long rootLength = rootExists ? new System.IO.FileInfo(rootConfigPath).Length : 0;

        SoupOutputTerminal.Text +=
            $"[DIAGNOSTIC] File Verified on Disk: {configExists} (Size: {configLength} bytes)\n" +
            $"[DIAGNOSTIC] Root copy Verified on Disk: {rootExists} (Size: {rootLength} bytes)\n";

        // Abort if the config failed to reach physical disk — never spawn soup without it.
        if (!configExists || configLength == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrainingStatusLabel.Text = "Status: Error";
                TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
                SoupOutputTerminal.Text += $"⛔ Aborting: soup.yaml failed to write to physical disk ({configPath}).\n";
                FineTuneStatusText.Text = $"⛔ Aborting: soup.yaml failed to write to physical disk ({configPath}).";
            });
            return;
        }

        // 3. Resolve the Soup executable to spawn.
        var (soupExe, _, _) = ModelTrainerTool.ResolveSoupInvocation(configPath, workspacePath);
        if (string.IsNullOrWhiteSpace(soupExe) || soupExe == "soup")
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrainingStatusLabel.Text = "Status: Error";
                TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
                SoupOutputTerminal.Text += "❌ Could not locate a soup.exe. Verify the Soup repository path or install Soup into PATH.\n";
                FineTuneStatusText.Text = "❌ Could not locate a soup.exe. Verify the Soup repository path or install Soup into PATH.";
            });
            return;
        }
        SoupOutputTerminal.Text += $"[DIAGNOSTIC] Soup executable: {soupExe}\n";

        // 4. Pre-spawn guard re-check (defence in depth).
        if (!System.IO.File.Exists(configPath) || new System.IO.FileInfo(configPath).Length == 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TrainingStatusLabel.Text = "Status: Error";
                TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
                SoupOutputTerminal.Text += $"❌ Config not found: {configPath}\n⛔ Aborting: soup.yaml is missing.\n";
                FineTuneStatusText.Text = $"❌ Config not found: {configPath}";
            });
            return;
        }

        SoupOutputTerminal.Text += $"[DIAGNOSTIC] Pre-spawn check passed — spawning soup train...\n";
        DispatcherQueue.TryEnqueue(() => FineTuneStatusText.Text = $"✅ Config written → {configPath}");

        // --- Set UI to active/loading state ---
        _trainingCts = new CancellationTokenSource();
        var token = _trainingCts.Token;

        StartFineTuningButton.IsEnabled = false;
        CancelFineTuningButton.IsEnabled = true;
        TrainingProgressRing.IsActive = true;
        TrainingStatusLabel.Text = "Status: Training active (streaming layers)...";
        TrainingStatusLabel.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 166, 227, 161));
        FineTuneStatusText.Text = string.Empty;
        // Keep the [DIAGNOSTIC] lines already appended to the terminal (do NOT clear here).

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
                soupExe, configPath, logCallback, token, workspacePath);
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

    private async void BrowseDbPathButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".db");

        // Initialise the picker with the app window handle (required in WinUI 3).
        var window = (Application.Current as App)?._window;
        if (window != null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            DatabasePathBox.Text = file.Path;
            DbPathInfoBar.IsOpen = false;
        }
    }

    private void SaveDbPathButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var newPath = DatabasePathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(newPath))
        {
            DbPathInfoBar.Severity = InfoBarSeverity.Error;
            DbPathInfoBar.Message = "Please enter a valid database path before saving.";
            DbPathInfoBar.Title = "Invalid Path";
            DbPathInfoBar.IsOpen = true;
            return;
        }

        var dir = Path.GetDirectoryName(newPath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            DbPathInfoBar.Severity = InfoBarSeverity.Error;
            DbPathInfoBar.Message = "The database path must point to a file inside a valid directory.";
            DbPathInfoBar.Title = "Invalid Path";
            DbPathInfoBar.IsOpen = true;
            return;
        }

        try
        {
            // Persist the path and materialise the directory.
            SettingsManager.DatabasePath = newPath;
            Directory.CreateDirectory(dir);

            // Ensure the schema/tables and WAL mode exist at the new location.
            CodingSahayi.Data.AppDbContext.InitializeDatabase();

            DbPathInfoBar.Severity = InfoBarSeverity.Informational;
            DbPathInfoBar.Message = "Database path updated. Existing connections will use the new path on next query.";
            DbPathInfoBar.Title = "Database Path";
            DbPathInfoBar.IsOpen = true;

            DatabasePathBox.Text = SettingsManager.DatabasePath;
        }
        catch (Exception ex)
        {
            DbPathInfoBar.Severity = InfoBarSeverity.Error;
            DbPathInfoBar.Message = $"Failed to apply database path: {ex.Message}";
            DbPathInfoBar.Title = "Database Path Error";
            DbPathInfoBar.IsOpen = true;
        }
    }

    private void ResetDbPathButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SettingsManager.ResetDatabasePathToDefault();
        DatabasePathBox.Text = SettingsManager.DatabasePath;

        DbPathInfoBar.Severity = InfoBarSeverity.Informational;
        DbPathInfoBar.Message = "Database path reset to the default AppData location.";
        DbPathInfoBar.Title = "Database Path";
        DbPathInfoBar.IsOpen = true;
    }
}
