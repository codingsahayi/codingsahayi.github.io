using System;
using System.Collections.ObjectModel;
using System.IO;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CodingSahayi;

/// <summary>
/// A single row in the VS Code-style diff viewer, carrying precomputed gutter line numbers,
/// change marker and background brush so the XAML stays declarative.
/// </summary>
public sealed class DiffLineViewModel
{
    public string OldLineNo { get; set; } = "";
    public string NewLineNo { get; set; } = "";
    public string Marker { get; set; } = " ";
    public SolidColorBrush MarkerBrush { get; set; }
    public SolidColorBrush BackgroundBrush { get; set; }
    public string LineText { get; set; } = "";
}

/// <summary>
/// A clean, modern VS Code-style diff inspector (Unified/Split) built on DiffPlex.
/// Shows per-line gutter numbers, add/delete styling and an Accept/Reject footer.
/// </summary>
public sealed partial class DiffReviewDialog : ContentDialog
{
    public bool IsAccepted { get; private set; }

    private static readonly SolidColorBrush AddBg = new(Windows.UI.Color.FromArgb(255, 31, 61, 42));
    private static readonly SolidColorBrush DelBg = new(Windows.UI.Color.FromArgb(255, 61, 31, 36));
    private static readonly SolidColorBrush NeutralBg = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    private static readonly SolidColorBrush AddMarker = new(Windows.UI.Color.FromArgb(255, 125, 200, 125));
    private static readonly SolidColorBrush DelMarker = new(Windows.UI.Color.FromArgb(255, 224, 122, 122));
    private static readonly SolidColorBrush NeutralMarker = new(Windows.UI.Color.FromArgb(255, 90, 107, 122));

    public DiffReviewDialog(string filePath, string oldText, string newText)
        : this(filePath, BuildDiffModel(oldText, newText))
    {
    }

    /// <summary>
    /// Accepts a prebuilt inline diff model so the caller can compute the diff off the UI thread
    /// (e.g. via Task.Run) and hand it to the dialog.
    /// </summary>
    public DiffReviewDialog(string filePath, DiffPaneModel diffModel)
    {
        this.InitializeComponent();

        // Header: display file name (basename) with full path as tooltip.
        FileNameText.Text = $"Editing: {Path.GetFileName(filePath)}";
        ToolTipService.SetToolTip(FileNameText, filePath);

        // Populate rows + compute stats. DiffPiece exposes only Position (old line #);
        // track the running new-line number for the gutter.
        var rows = new ObservableCollection<DiffLineViewModel>();
        int added = 0, deleted = 0, newLineCounter = 0;
        foreach (var piece in diffModel.Lines)
        {
            var row = BuildRow(piece);
            if (piece.Type == ChangeType.Inserted || piece.Type == ChangeType.Modified || piece.Type == ChangeType.Unchanged)
                newLineCounter++;
            row.NewLineNo = (piece.Type == ChangeType.Deleted || piece.Type == ChangeType.Imaginary) ? "" : newLineCounter.ToString();
            rows.Add(row);

            if (piece.Type == ChangeType.Inserted) added++;
            else if (piece.Type == ChangeType.Deleted) deleted++;
            else if (piece.Type == ChangeType.Modified) { deleted++; added++; }
        }

        HeaderStatsText.Text = $"+{added}  -{deleted}";
        DiffListView.ItemsSource = rows;

        // Wire Accept (Primary) / Reject (Close) buttons.
        this.PrimaryButtonClick += (s, e) => { IsAccepted = true; };
        this.CloseButtonClick += (s, e) => { IsAccepted = false; };
    }

    private static DiffPaneModel BuildDiffModel(string oldText, string newText)
    {
        var differ = new Differ();
        var builder = new InlineDiffBuilder(differ);
        return builder.BuildDiffModel(oldText ?? "", newText ?? "");
    }

    private static DiffLineViewModel BuildRow(DiffPiece piece)
    {
        var vm = new DiffLineViewModel
        {
            OldLineNo = piece.Position.HasValue ? (piece.Position.Value + 1).ToString() : "",
            LineText = piece.Text ?? ""
        };

        switch (piece.Type)
        {
            case ChangeType.Inserted:
                vm.Marker = "+";
                vm.MarkerBrush = AddMarker;
                vm.BackgroundBrush = AddBg;
                break;
            case ChangeType.Deleted:
                vm.Marker = "-";
                vm.MarkerBrush = DelMarker;
                vm.BackgroundBrush = DelBg;
                break;
            case ChangeType.Modified:
                vm.Marker = "~";
                vm.MarkerBrush = DelMarker;
                vm.BackgroundBrush = DelBg;
                break;
            default:
                vm.Marker = " ";
                vm.MarkerBrush = NeutralMarker;
                vm.BackgroundBrush = NeutralBg;
                break;
        }
        return vm;
    }
}
