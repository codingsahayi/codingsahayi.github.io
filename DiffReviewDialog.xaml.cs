using Microsoft.UI.Xaml.Controls;
using DiffPlex.DiffBuilder.Model;
using System.Linq;

namespace CodingSahayi;

public sealed partial class DiffReviewDialog : ContentDialog
{
    public bool IsAccepted { get; private set; }

    public DiffReviewDialog(SideBySideDiffModel diff)
    {
        this.InitializeComponent();
        OldLinesControl.ItemsSource = diff.OldText.Lines;
        NewLinesControl.ItemsSource = diff.NewText.Lines;
        
        this.PrimaryButtonClick += (s, e) => { IsAccepted = true; };
    }

    private void Accept_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Accept the diff and close the dialog.
        IsAccepted = true;
        Hide();
    }

    private void Reject_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Reject the diff and close the dialog.
        IsAccepted = false;
        Hide();
    }
}
