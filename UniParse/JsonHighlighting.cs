using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace UniParse;

/// <summary>Provides a JSON syntax-highlighting definition for the AvalonEdit editor.</summary>
internal static class JsonHighlighting
{
    public static IHighlightingDefinition Definition { get; } = Load();

    private static IHighlightingDefinition Load()
    {
        using StringReader stringReader = new(Xshd);
        using XmlReader xmlReader = XmlReader.Create(stringReader);
        return HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
    }

    // Keys (quoted strings followed by ':') are coloured distinctly from string values.
    private const string Xshd = """
<SyntaxDefinition name="Json" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Key" foreground="#9CDCFE" />
  <Color name="String" foreground="#CE9178" />
  <Color name="Number" foreground="#B5CEA8" />
  <Color name="Keyword" foreground="#569CD6" fontWeight="bold" />
  <Color name="Punctuation" foreground="#D4D4D4" />
  <RuleSet>
    <Rule color="Key">"[^"\\]*(?:\\.[^"\\]*)*"(?=\s*:)</Rule>
    <Span color="String" multiline="false">
      <Begin>"</Begin>
      <End>"</End>
      <RuleSet>
        <Span begin="\\" end="." />
      </RuleSet>
    </Span>
    <Keywords color="Keyword">
      <Word>true</Word>
      <Word>false</Word>
      <Word>null</Word>
    </Keywords>
    <Rule color="Number">[\-+]?\b\d+(\.\d+)?([eE][\-+]?\d+)?\b</Rule>
    <Rule color="Punctuation">[{}\[\]:,]</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
}
