using System.Text;
using System.Xml.Linq;

namespace L5xploderLib.Serialization;

/// <summary>
/// Converts XElement / XDocument instances to readable Markdown.
/// Rules:
///   - Attributes rendered inline as **Key:** Value · **Key:** Value
///   - Description/Comment/RevisionNote child elements surfaced as blockquotes
///   - Uniform leaf-element groups rendered as tables (with Description column when present)
///   - Text elements (ladder logic) rendered as iecst code blocks
///   - Data elements with Format="L5K" (binary) are silently skipped
///   - Everything else recursed into as headed sections — no XML fallback
/// </summary>
internal static class XElementMarkdownConverter
{
    private static readonly HashSet<string> ProseElements =
        new(StringComparer.OrdinalIgnoreCase) { "Description", "Comment", "RevisionNote" };

    private static readonly HashSet<string> CodeElements =
        new(StringComparer.OrdinalIgnoreCase) { "Text" };

    private static readonly HashSet<string> BinaryDataFormats =
        new(StringComparer.OrdinalIgnoreCase) { "L5K" };

    public static string ConvertElement(XElement element)
    {
        var sb = new StringBuilder();
        AppendElementHeading(sb, element, level: 1);
        AppendProseChildren(sb, element);
        AppendInlineAttributes(sb, element);
        AppendChildSections(sb, element, headingLevel: 2);
        return sb.ToString();
    }

    public static string ConvertRootDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return string.Empty;

        var sb = new StringBuilder();
        AppendElementHeading(sb, root, level: 1);
        AppendProseChildren(sb, root);
        AppendInlineAttributes(sb, root);

        var sectionNames = root.Elements()
            .Select(e => e.Name.LocalName)
            .Distinct()
            .ToList();

        if (sectionNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Sections");
            sb.AppendLine();
            foreach (var name in sectionNames)
                sb.AppendLine($"- {name}/");
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------

    private static void AppendElementHeading(StringBuilder sb, XElement element, int level)
    {
        var hashes = new string('#', Math.Clamp(level, 1, 6));
        var tag = element.Name.LocalName;
        var label = element.Attribute("Name")?.Value
                    ?? element.Attribute("Number")?.Value
                    ?? element.Attribute("Id")?.Value;

        sb.AppendLine(label is not null ? $"{hashes} {tag}: {label}" : $"{hashes} {tag}");
    }

    private static void AppendProseChildren(StringBuilder sb, XElement element)
    {
        foreach (var child in element.Elements().Where(e => ProseElements.Contains(e.Name.LocalName)))
        {
            var text = child.Value.Trim();
            if (string.IsNullOrEmpty(text))
                continue;
            sb.AppendLine();
            foreach (var line in text.Split('\n'))
                sb.AppendLine($"> {line.TrimEnd()}");
        }
    }

    private static void AppendInlineAttributes(StringBuilder sb, XElement element)
    {
        // Name/Number/Id are consumed into the heading; skip them here
        var attrs = element.Attributes()
            .Where(a =>
            {
                var n = a.Name.LocalName;
                return n != "Name" && n != "Number" && n != "Id";
            })
            .ToList();

        if (attrs.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine(string.Join(" · ", attrs.Select(a => $"**{a.Name.LocalName}:** {EscapeInline(a.Value)}")));
    }

    private static void AppendChildSections(StringBuilder sb, XElement element, int headingLevel)
    {
        var children = element.Elements()
            .Where(e => !ProseElements.Contains(e.Name.LocalName))
            .ToList();

        if (children.Count == 0)
        {
            AppendLeafContent(sb, element);
            return;
        }

        foreach (var group in children.GroupBy(c => c.Name.LocalName))
        {
            var items = group.ToList();

            if (IsBinaryDataGroup(items))
                continue;

            // Code elements (e.g. <Text> in RLL rungs) render directly as code blocks — no heading, no table
            if (CodeElements.Contains(group.Key))
            {
                foreach (var codeItem in items)
                {
                    var code = codeItem.Value.Trim();
                    if (string.IsNullOrEmpty(code))
                        continue;
                    sb.AppendLine();
                    sb.AppendLine("```iecst");
                    sb.AppendLine(code);
                    sb.AppendLine("```");
                }
                continue;
            }

            if (CanRenderAsTable(items))
            {
                // Table: show group heading for context, then the table
                var hashes = new string('#', Math.Clamp(headingLevel, 1, 6));
                sb.AppendLine();
                sb.AppendLine($"{hashes} {group.Key}");
                sb.AppendLine();
                AppendTable(sb, items);
            }
            else
            {
                // Recursive: if every item has its own label (Name/Number/Id), skip the
                // redundant group heading and give each item a heading at the current level.
                // Otherwise emit the group heading and recurse one level deeper.
                bool allLabeled = items.All(item =>
                    item.Attribute("Name") != null ||
                    item.Attribute("Number") != null ||
                    item.Attribute("Id") != null);

                if (allLabeled)
                {
                    sb.AppendLine();
                    AppendItemsRecursively(sb, items, headingLevel);
                }
                else
                {
                    var hashes = new string('#', Math.Clamp(headingLevel, 1, 6));
                    sb.AppendLine();
                    sb.AppendLine($"{hashes} {group.Key}");
                    sb.AppendLine();
                    AppendItemsRecursively(sb, items, headingLevel + 1);
                }
            }
        }
    }

    private static void AppendItemsRecursively(StringBuilder sb, IEnumerable<XElement> items, int headingLevel)
    {
        foreach (var item in items)
        {
            var label = item.Attribute("Name")?.Value
                        ?? item.Attribute("Number")?.Value
                        ?? item.Attribute("Id")?.Value;

            if (label is not null && headingLevel <= 6)
                AppendElementHeading(sb, item, headingLevel);

            AppendProseChildren(sb, item);
            AppendInlineAttributes(sb, item);
            AppendChildSections(sb, item, headingLevel + 1);
        }
    }

    private static void AppendLeafContent(StringBuilder sb, XElement element)
    {
        var text = element.Value.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        sb.AppendLine();
        if (CodeElements.Contains(element.Name.LocalName))
        {
            sb.AppendLine("```iecst");
            sb.AppendLine(text);
            sb.AppendLine("```");
        }
        else
        {
            sb.AppendLine(text);
        }
    }

    // -------------------------------------------------------------------------
    // Table rendering
    // -------------------------------------------------------------------------

    /// <summary>
    /// Elements can be rendered as a table when every item has no child elements
    /// other than optional prose (Description / Comment / RevisionNote).
    /// </summary>
    private static bool CanRenderAsTable(List<XElement> items)
        => items.All(item => item.Elements().All(e => ProseElements.Contains(e.Name.LocalName)));

    private static void AppendTable(StringBuilder sb, List<XElement> items)
    {
        var hasProse = items.Any(item => item.Elements().Any(e => ProseElements.Contains(e.Name.LocalName)));

        var columns = items
            .SelectMany(item => item.Attributes().Select(a => a.Name.LocalName))
            .Distinct()
            .ToList();

        if (hasProse)
            columns.Add("Description");

        sb.AppendLine("| " + string.Join(" | ", columns) + " |");
        sb.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

        foreach (var item in items)
        {
            var cells = columns.Select(col =>
            {
                if (col == "Description")
                {
                    var prose = item.Elements().FirstOrDefault(e => ProseElements.Contains(e.Name.LocalName));
                    return EscapeCell(prose?.Value.Trim() ?? string.Empty);
                }
                return EscapeCell(item.Attribute(col)?.Value ?? string.Empty);
            });
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }
    }

    // -------------------------------------------------------------------------

    private static bool IsBinaryDataGroup(List<XElement> items)
        => items.All(item => BinaryDataFormats.Contains(item.Attribute("Format")?.Value ?? string.Empty));

    private static string EscapeInline(string value)
        => value.Replace("\r\n", " ").Replace("\n", " ");

    private static string EscapeCell(string value)
        => value.Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
}
