using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FixletBuilder.Core;

public static class XmlHighlight
{
    private static readonly Regex Token = new(
        @"(?<comment><!--.*?-->)|(?<cdata><!\[CDATA\[.*?\]\]>)|(?<decl><\?.*?\?>)|(?<tag></?[\w:.-]+)|(?<attr>[\w:.-]+(?==))|(?<str>""[^""]*""|'[^']*')|(?<close>/?>)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly SolidColorBrush Fg = new(Color.FromRgb(0xE0, 0xE0, 0xE0));
    private static readonly SolidColorBrush Comment = new(Color.FromRgb(0x6A, 0x99, 0x55));
    private static readonly SolidColorBrush Orange = new(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush Tag = new(Color.FromRgb(0x56, 0x9C, 0xD6));
    private static readonly SolidColorBrush Attr = new(Color.FromRgb(0x9C, 0xDC, 0xFE));

    public static void Apply(RichTextBox box, string text)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(4) };
        var p = new Paragraph();
        int pos = 0;

        foreach (Match m in Token.Matches(text))
        {
            if (m.Index > pos)
                p.Inlines.Add(new Run(text.Substring(pos, m.Index - pos)) { Foreground = Fg });

            var brush = Orange;
            if (m.Groups["comment"].Success) brush = Comment;
            else if (m.Groups["cdata"].Success) brush = Orange;
            else if (m.Groups["decl"].Success) brush = Tag;
            else if (m.Groups["tag"].Success) brush = Tag;
            else if (m.Groups["attr"].Success) brush = Attr;
            else if (m.Groups["close"].Success) brush = Tag;

            p.Inlines.Add(new Run(m.Value) { Foreground = brush });
            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
            p.Inlines.Add(new Run(text.Substring(pos)) { Foreground = Fg });

        doc.Blocks.Add(p);
        box.Document = doc;
    }
}