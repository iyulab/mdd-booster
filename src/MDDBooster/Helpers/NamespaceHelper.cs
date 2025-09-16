namespace MDDBooster.Helpers;

/// <summary>
/// Helper for managing namespaces and file paths across all builders
/// </summary>
public static class NamespaceHelper
{
    /// <summary>
    /// Configuration for namespace generation
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Custom namespace mappings for specific paths
        /// </summary>
        public static Dictionary<string, string> CustomNamespaceMappings { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets namespace for a given base namespace and path
    /// </summary>
    public static string GetNamespace(string baseNamespace, string path, string documentNamespace = null)
    {
        if (string.IsNullOrEmpty(path))
            return GetSafeNamespace(baseNamespace, documentNamespace);

        // Check custom mappings first
        if (Config.CustomNamespaceMappings.TryGetValue(path, out var customMapping))
        {
            return CombineNamespaces(GetSafeNamespace(baseNamespace, documentNamespace), customMapping);
        }

        // Check if path ends with underscore (special pattern)
        if (path.EndsWith("_"))
        {
            // Remove underscore and use as namespace part
            string cleanPath = path.TrimEnd('_');
            return CombineNamespaces(GetSafeNamespace(baseNamespace, documentNamespace), cleanPath);
        }

        // Use folder name as namespace part
        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName))
            return GetSafeNamespace(baseNamespace, documentNamespace);

        return CombineNamespaces(GetSafeNamespace(baseNamespace, documentNamespace), folderName);
    }

    /// <summary>
    /// Adds a custom namespace mapping
    /// </summary>
    public static void AddCustomNamespaceMapping(string path, string namespaceSegment)
    {
        if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(namespaceSegment))
        {
            Config.CustomNamespaceMappings[path] = namespaceSegment;
        }
    }

    /// <summary>
    /// Gets a safe namespace, using document namespace as fallback
    /// </summary>
    public static string GetSafeNamespace(string baseNamespace, string documentNamespace)
    {
        if (!string.IsNullOrEmpty(baseNamespace))
            return baseNamespace;

        if (!string.IsNullOrEmpty(documentNamespace))
            return documentNamespace;

        return "DefaultNamespace";
    }

    /// <summary>
    /// Combines namespace parts safely
    /// </summary>
    public static string CombineNamespaces(string baseNamespace, string namespacePart)
    {
        if (string.IsNullOrEmpty(namespacePart))
            return baseNamespace;

        // Clean namespace part
        string cleanPart = SanitizeNamespacePart(namespacePart);

        if (string.IsNullOrEmpty(cleanPart))
            return baseNamespace;

        return $"{baseNamespace}.{cleanPart}";
    }

    /// <summary>
    /// Sanitizes a namespace part to ensure it's a valid C# identifier
    /// </summary>
    public static string SanitizeNamespacePart(string namespacePart)
    {
        if (string.IsNullOrEmpty(namespacePart))
            return string.Empty;

        // Remove invalid characters and ensure it starts with a letter or underscore
        var sb = new StringBuilder();

        for (int i = 0; i < namespacePart.Length; i++)
        {
            char c = namespacePart[i];

            if (i == 0)
            {
                // First character must be letter or underscore
                if (char.IsLetter(c) || c == '_')
                    sb.Append(c);
                else if (char.IsDigit(c))
                    sb.Append('_').Append(c); // Prefix with underscore if starts with digit
            }
            else
            {
                // Subsequent characters can be letters, digits, or underscores
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            }
        }

        string result = sb.ToString();

        // Ensure we have at least something
        if (string.IsNullOrEmpty(result))
            return "Generated";

        // Ensure it's properly capitalized for namespace
        return StringHelper.NormalizeName(result);
    }
}