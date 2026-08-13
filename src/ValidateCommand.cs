using System.Xml;

namespace Civ6WorkshopUploader;

public static class ValidateCommand
{
    /// <summary>
    /// Advisory check of a workspace before uploading (Civ6-specific addition).
    /// Mirrors the checks the official Firaxis uploader performs, but is
    /// intentionally non-blocking: it reports issues and returns a non-zero
    /// exit code only as a signal, it never prevents the upload itself.
    /// </summary>
    public static int Validate(DirectoryInfo workspaceDirectory)
    {
        if (!workspaceDirectory.Exists)
        {
            Log.Error($"No directory at {workspaceDirectory}!");
            return 1;
        }

        FileInfo configJsonInfo = new(Path.Combine(workspaceDirectory.FullName, "workshop.json"));
        if (!configJsonInfo.Exists)
        {
            Log.Error("There is no file named workshop.json in the workspace!");
            return 1;
        }

        DirectoryInfo contentDirectoryInfo = new(Path.Combine(workspaceDirectory.FullName, "content"));
        if (!contentDirectoryInfo.Exists)
        {
            Log.Error("There is no 'content' directory inside the workspace!");
            return 1;
        }

        FileInfo? modInfoFile = contentDirectoryInfo.GetFiles("*.modinfo", SearchOption.AllDirectories).FirstOrDefault();
        if (modInfoFile == null)
        {
            Log.Error($"No .modinfo file found under content/ ({contentDirectoryInfo.FullName}).");
            return 1;
        }

        Log.Info($"Found modinfo: {modInfoFile.FullName}");

        bool issuesFound = false;

        try
        {
            XmlDocument doc = new();
            doc.Load(modInfoFile.FullName);

            XmlNode? modNode = doc.SelectSingleNode("/Mod");
            if (modNode == null)
            {
                Log.Error("Missing <Mod> root element.");
                return 1;
            }

            string? id = modNode.Attributes?["id"]?.Value;
            if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out _))
            {
                Log.Error($"Mod ID is not a valid GUID: '{id}'");
                issuesFound = true;
            }
            else
            {
                Log.Info($"Mod ID is a valid GUID: {id}");
            }

            string? version = modNode.Attributes?["version"]?.Value;
            Log.Info(version == null ? "Mod version: <not set>" : $"Mod version: {version}");

            // Properties/Name and Properties/Description hold localization keys in Civ6
            // (e.g. LOC_FOO_MOD_NAME); the actual title lives in <LocalizedText>. We only
            // check that the keys are present, and surface the localized en_US text if any.
            XmlNode? nameNode = modNode.SelectSingleNode("Properties/Name");
            string? nameKey = nameNode?.InnerText;
            if (string.IsNullOrWhiteSpace(nameKey))
            {
                Log.Error("Mod Title (Properties/Name) is empty or missing.");
                issuesFound = true;
            }
            else
            {
                Log.Info($"Title key: {nameKey}");
                string? localized = GetLocalizedText(doc, nameKey, "en_US");
                if (localized != null)
                {
                    Log.Info($"  (en_US) {localized}");
                }
            }

            XmlNode? descNode = modNode.SelectSingleNode("Properties/Description");
            string? descKey = descNode?.InnerText;
            if (string.IsNullOrWhiteSpace(descKey))
            {
                Log.Error("Mod Description (Properties/Description) is empty or missing.");
                issuesFound = true;
            }
            else
            {
                Log.Info($"Description key: {descKey}");
                string? localized = GetLocalizedText(doc, descKey, "en_US");
                if (localized != null)
                {
                    Log.Info($"  (en_US) {localized}");
                }
            }

            // Collect every <File> declared anywhere in the modinfo (actions AND a top-level
            // <Files> section, if present) and check that the referenced files exist.
            XmlNodeList fileNodes = doc.SelectNodes("//File") ?? throw new InvalidOperationException("SelectNodes returned null");
            string[] files = fileNodes.Cast<XmlNode>().Select(n => n.InnerText.Trim()).Where(f => f.Length > 0).Distinct().ToArray();

            if (files.Length == 0)
            {
                Log.Warn("No <File> entries found in the modinfo. A Civ6 modinfo normally declares its action files.");
            }

            foreach (string f in files)
            {
                string extension = Path.GetExtension(f);
                bool executableLike = extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                                      extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
                bool archiveLike = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                                   extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
                                   extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);

                if (executableLike)
                {
                    Log.Warn($"File '{f}' is a DLL/EXE. Informational only: Civ6 mods commonly ship native DLLs.");
                }

                if (archiveLike)
                {
                    Log.Warn($"File '{f}' is a compressed archive. Informational only: this does not block upload.");
                }

                string fullPath = Path.Combine(modInfoFile.DirectoryName ?? "", f);
                if (!File.Exists(fullPath))
                {
                    Log.Error($"File referenced in modinfo does not exist: {f}");
                    issuesFound = true;
                }
            }

            if (files.Length > 0)
            {
                Log.Info($"Checked {files.Length} referenced file(s) for existence.");
            }
        }
        catch (XmlException e)
        {
            Log.Error($"Failed to parse modinfo XML: {e.Message}");
            return 1;
        }

        if (issuesFound)
        {
            Log.Warn("Validation completed with issues. Upload may still proceed (validation is advisory).");
            return 2;
        }

        Log.Info("Validation completed. No issues found.");
        return 0;
    }

    private static string? GetLocalizedText(XmlDocument doc, string id, string locale)
    {
        XmlNode? textNode = doc.SelectSingleNode($"/Mod/LocalizedText/Text[@id='{id}']");
        return textNode?.SelectSingleNode(locale)?.InnerText;
    }
}