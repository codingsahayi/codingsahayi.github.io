using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace CodingSahayi;

public static class DiffManager
{
    public static SideBySideDiffModel GenerateDiff(string oldText, string newText)
    {
        var diffBuilder = new SideBySideDiffBuilder(new Differ());
        return diffBuilder.BuildDiffModel(oldText ?? "", newText ?? "");
    }

    /// <summary>
    /// Builds an inline (unified) diff model for the VS Code-style diff inspector.
    /// </summary>
    public static DiffPaneModel GenerateInlineDiff(string oldText, string newText)
    {
        var builder = new InlineDiffBuilder(new Differ());
        return builder.BuildDiffModel(oldText ?? "", newText ?? "");
    }
}
