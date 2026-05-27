using System.Text;
using System.Xml.Linq;

namespace L5xploderLib.Serialization;

/// <summary>
/// Converts XElement / XDocument instances to Markdown strings optimized for RAG ingestion.
/// </summary>
internal static class XElementMarkdownConverter
{
    private const int UniformChildTableThreshold = 50;

    public static string ConvertElement(XElement element)
    {
        var sb = new StringBuilder();
        WriteElementHeading(sb, element, level: 1);
        WriteAttributes(sb, element, excludeName: true);
        WriteChildren(sb, element, headingLevel: 2);
        return sb.ToString();
    }

    public static string ConvertRootDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return string.Empty;

        var sb = new StringBuilder();
        WriteElementHeading(sb, root, level: 1);
        WriteAttributes(sb, root, excludeName: true);

        // List top-level child element group names as a section index
        var topLevelDirs = root.Elements()
            .Select(e => e.Name.LocalName)
            .Distinct()
            .ToList();

        if (topLevelDirs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Sections");
            sb.AppendLine();
            foreach (var dir in topLevelDirs)
                sb.AppendLine($"- {dir}/");
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void WriteElementHeading(StringBuilder sb, XElement element, int level)
    {
        var hashes = new string('#', level);
        var tag = element.Name.LocalName;
        var name = element.Attribute("Name")?.Value;

        sb.AppendLine(name is not null ? $"{hashes} {tag}: {name}" : $"{hashes} {tag}");
    }

    private static void WriteAttributes(StringBuilder sb, XElement element, bool excludeName)
    {
        var attrs = element.Attributes()
            .Where(a => !excludeName || a.Name.LocalName != "Name")
            .ToList();

        if (attrs.Count == 0)
            return;

        sb.AppendLine();
        foreach (var attr in attrs)
            sb.AppendLine($"**{attr.Name.LocalName}:** {EscapeMarkdown(attr.Value)}  ");
    }

    private static void WriteChildren(StringBuilder sb, XElement element, int headingLevel)
    {
        var children = element.Elements().ToList();
        if (children.Count == 0)
        {
            // Leaf text / CDATA content
            var text = element.Value?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine();
                sb.AppendLine(text);
            }
            return;
        }

        // Group children by tag name to decide rendering strategy per group
        var groups = children
            .GroupBy(c => c.Name.LocalName)
            .ToList();

        foreach (var group in groups)
        {
            var items = group.ToList();
            var hashes = new string('#', headingLevel);

            sb.AppendLine();
            sb.AppendLine($"{hashes} {group.Key}");
            sb.AppendLine();

            if (ShouldRenderAsTable(items))
                WriteTable(sb, items);
            else
                WriteXmlCodeBlock(sb, items);
        }
    }

    /// <summary>
    /// True when all items share the same tag, are ≤ threshold, and have no child elements
    /// (attributes only), making them suitable for a Markdown table.
    /// </summary>
    private static bool ShouldRenderAsTable(List<XElement> items)
    {
        if (items.Count > UniformChildTableThreshold)
            return false;

        return items.All(item => !item.Elements().Any());
    }

    private static void WriteTable(StringBuilder sb, List<XElement> items)
    {
        // Collect the union of all attribute names in order of first appearance
        var columns = items
            .SelectMany(item => item.Attributes().Select(a => a.Name.LocalName))
            .Distinct()
            .ToList();

        // Header row
        sb.AppendLine("| " + string.Join(" | ", columns) + " |");
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

        // Data rows
        foreach (var item in items)
        {
            var cells = columns.Select(col =>
            {
                var val = item.Attribute(col)?.Value ?? string.Empty;
                return EscapeTableCell(val);
            });
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }
    }

    private static void WriteXmlCodeBlock(StringBuilder sb, List<XElement> items)
    {
        sb.AppendLine("```xml");
        foreach (var item in items)
            sb.AppendLine(item.ToString());
        sb.AppendLine("```");
    }

    private static string EscapeMarkdown(string value)
    {
        // Minimal escape: prevent unintended formatting inside attribute values
        return value.Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
    }

    private static string EscapeTableCell(string value)
    {
        return value.Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
    }
}
