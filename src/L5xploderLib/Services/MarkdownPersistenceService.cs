using System.Xml.Linq;
using L5xploderLib;
using L5xploderLib.Models;
using L5xploderLib.Serialization;

namespace L5xploderLib.Services;

internal sealed class MarkdownPersistenceService : PersistenceService
{
    protected override string FileExtension => Constants.MdFileExtension;

    protected override XElement LoadElementImpl(string absoluteFilePath)
        => throw new NotSupportedException("Markdown format does not support round-trip loading.");

    protected override XDocument LoadRootImpl(string absoluteFilePath)
        => throw new NotSupportedException("Markdown format does not support round-trip loading.");

    protected override void SaveElementImpl(XElement element, string absoluteFilePath)
    {
        var markdown = XElementMarkdownConverter.ConvertElement(element);
        File.WriteAllText(absoluteFilePath, markdown);
    }

    protected override void SaveRootImpl(XDocument xmlDoc, string absoluteFilePath)
    {
        var markdown = XElementMarkdownConverter.ConvertRootDocument(xmlDoc);
        File.WriteAllText(absoluteFilePath, markdown);
    }

    protected override void SaveCustomElement(CustomElementFile customFile)
    {
        // Wrap plain-text custom files (e.g. .st structured text) in a Markdown code block
        var isStructuredText = customFile.FileExt.Equals(
            Constants.StructuredTextFileExtension, StringComparison.OrdinalIgnoreCase);

        if (isStructuredText)
        {
            var routineName = Path.GetFileNameWithoutExtension(customFile.BaseFilePath);
            // Strip optional online-edit suffix (e.g. "MainRoutine.PendingEdits" → "MainRoutine")
            var dotIdx = routineName.IndexOf('.');
            if (dotIdx >= 0)
                routineName = routineName[..dotIdx];

            var mdContent =
                $"# Routine: {routineName}\n\n**Type:** ST  \n\n## Structured Text\n\n```iecst\n{customFile.Content}\n```\n";

            var mdPath = Path.Combine(ExplodedSubDir, customFile.BaseFilePath + Constants.MdFileExtension);
            File.WriteAllText(mdPath, mdContent);
        }
        else
        {
            // Unknown custom type — write as-is
            var absoluteFilePath = Path.Combine(ExplodedSubDir, customFile.FilePath);
            File.WriteAllText(absoluteFilePath, customFile.Content);
        }
    }
}
