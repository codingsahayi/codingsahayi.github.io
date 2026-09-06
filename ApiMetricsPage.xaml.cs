using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CodingSahayi.Data;
using Microsoft.EntityFrameworkCore;

namespace CodingSahayi
{
    public sealed partial class ApiMetricsPage : Page
    {
        private List<ApiRequestLog> _allLogs = new();

        public ApiMetricsPage()
        {
            this.InitializeComponent();
            this.Loaded += ApiMetricsPage_Loaded;
        }

        private void ApiMetricsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                using var db = new AppDbContext();
                _allLogs = db.ApiRequestLogs.OrderByDescending(l => l.Timestamp).ToList();
                ApplyFilters();
            }
            catch { }
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_allLogs == null) return;

            var filtered = _allLogs.AsEnumerable();

            // Timeframe
            if (TimeframeCombo != null)
            {
                if (TimeframeCombo.SelectedIndex == 1) // 24 Hours
                    filtered = filtered.Where(l => l.Timestamp >= DateTime.UtcNow.AddHours(-24));
                else if (TimeframeCombo.SelectedIndex == 2) // 7 Days
                    filtered = filtered.Where(l => l.Timestamp >= DateTime.UtcNow.AddDays(-7));
            }

            // Status
            if (StatusCombo != null)
            {
                if (StatusCombo.SelectedIndex == 1) // Success
                    filtered = filtered.Where(l => l.StatusCode >= 200 && l.StatusCode < 300);
                else if (StatusCombo.SelectedIndex == 2) // Error
                    filtered = filtered.Where(l => l.StatusCode < 200 || l.StatusCode >= 300);
            }

            // Search
            string searchText = SearchBox?.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(l => 
                    (l.FullPrompt?.ToLower().Contains(searchText) ?? false) || 
                    (l.ResponseContent?.ToLower().Contains(searchText) ?? false));
            }

            var result = filtered.ToList();

            // Update Cards
            TotalCallsText.Text = result.Count.ToString();
            TotalTokensText.Text = result.Sum(l => l.TotalTokens).ToString();
            AvgLatencyText.Text = result.Any() ? $"{Math.Round(result.Average(l => l.LatencyMs))} ms" : "0 ms";
            EstimatedCostText.Text = $"${result.Sum(l => l.EstimatedCostUsd):F4}";
            FailedCallsText.Text = result.Count(l => l.StatusCode < 200 || l.StatusCode >= 300).ToString();
            ActiveProvidersText.Text = result.Select(l => l.Provider).Distinct().Count().ToString();

            LogsList.ItemsSource = result;
        }

        private async void InspectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var log = _allLogs.FirstOrDefault(l => l.Id == id);
                if (log != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = $"Log {log.Id} - {log.Model}",
                        PrimaryButtonText = "Close",
                        XamlRoot = this.XamlRoot
                    };

                    var sp = new StackPanel { Spacing = 8 };
                    sp.Children.Add(new TextBlock { Text = "Prompt:", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                    var scrollPrompt = new ScrollViewer { MaxHeight = 200 };
                    scrollPrompt.Content = new TextBlock { Text = log.FullPrompt, TextWrapping = TextWrapping.Wrap };
                    sp.Children.Add(scrollPrompt);
                    
                    sp.Children.Add(new TextBlock { Text = "Response:", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                    var scrollResponse = new ScrollViewer { MaxHeight = 200 };
                    scrollResponse.Content = new TextBlock { Text = log.ResponseContent, TextWrapping = TextWrapping.Wrap };
                    sp.Children.Add(scrollResponse);
                    
                    sp.Children.Add(new TextBlock { Text = $"Tokens: {log.TotalTokens} | Latency: {log.LatencyMs}ms | Status: {log.StatusCode} | Cost: ${log.EstimatedCostUsd:F4}" });

                    dialog.Content = sp;
                    await dialog.ShowAsync();
                }
            }
        }
    }
}
