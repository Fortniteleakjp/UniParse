using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace UniParse.Controls;

/// <summary>
/// Detects Unity asset references (PPtr) in the JSON view and renders them as
/// coloured, underlined, clickable links. Clicking a link asks the host to
/// navigate to the referenced asset.
///
/// PPtrs are emitted by AssetRipper's <c>DefaultJsonWalker</c> as a single-line
/// object, e.g. <c>{ "m_FileID": 0, "m_PathID": 12345 }</c>. Null references
/// (PathID == 0) are left as plain text.
/// </summary>
public sealed class PPtrLinkElementGenerator : VisualLineElementGenerator
{
    /// <summary>Matches a serialized PPtr. Group 1 = FileID, group 2 = PathID.</summary>
    public static readonly Regex PPtrPattern = new(
        "\\{ \"m_FileID\": (-?\\d+), \"m_PathID\": (-?\\d+) \\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Raised (on the UI thread) when the user clicks a non-null reference.</summary>
    public event Action<int, long>? Navigate;

    internal void RaiseNavigate(int fileId, long pathId) => Navigate?.Invoke(fileId, pathId);

    private Match GetMatch(int startOffset, out int matchOffset)
    {
        int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        var relevant = CurrentContext.GetText(startOffset, endOffset - startOffset);
        Match m = PPtrPattern.Match(relevant.Text, relevant.Offset, relevant.Count);
        matchOffset = m.Success ? m.Index - relevant.Offset + startOffset : -1;
        return m;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        // Scan forward, skipping null references so they stay plain text.
        int searchFrom = startOffset;
        while (true)
        {
            Match m = GetMatch(searchFrom, out int matchOffset);
            if (!m.Success)
                return -1;
            if (long.TryParse(m.Groups[2].Value, out long pathId) && pathId != 0)
                return matchOffset;
            searchFrom = matchOffset + m.Length;
        }
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        Match m = GetMatch(offset, out int matchOffset);
        if (!m.Success || matchOffset != offset)
            return null;

        if (!long.TryParse(m.Groups[2].Value, out long pathId) || pathId == 0)
            return null;
        int fileId = int.TryParse(m.Groups[1].Value, out int f) ? f : 0;

        return new PPtrLinkVisualLineText(CurrentContext.VisualLine, m.Length, fileId, pathId, this);
    }
}

/// <summary>A clickable, coloured run for a single PPtr reference.</summary>
internal sealed class PPtrLinkVisualLineText : VisualLineText
{
    private static readonly Brush LinkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0))); // teal

    private readonly int _fileId;
    private readonly long _pathId;
    private readonly PPtrLinkElementGenerator _owner;

    public PPtrLinkVisualLineText(VisualLine parentVisualLine, int length, int fileId, long pathId, PPtrLinkElementGenerator owner)
        : base(parentVisualLine, length)
    {
        _fileId = fileId;
        _pathId = pathId;
        _owner = owner;
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        TextRunProperties.SetForegroundBrush(LinkBrush);
        TextRunProperties.SetTextDecorations(TextDecorations.Underline);
        return base.CreateTextRun(startVisualColumn, context);
    }

    protected override void OnQueryCursor(QueryCursorEventArgs e)
    {
        e.Handled = true;
        e.Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && !e.Handled)
        {
            _owner.RaiseNavigate(_fileId, _pathId);
            e.Handled = true;
        }
    }

    protected override VisualLineText CreateInstance(int length)
        => new PPtrLinkVisualLineText(ParentVisualLine, length, _fileId, _pathId, _owner);

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
