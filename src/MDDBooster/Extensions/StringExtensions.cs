using Humanizer;

namespace MDDBooster.Extensions;

/// <summary>
/// String extension methods
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Convert string to plural form using Humanizer
    /// </summary>
    public static string ToPlural(this string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Use StringHelper.NormalizeName to ensure proper casing, then apply Humanizer's Pluralize
        string normalized = StringHelper.NormalizeName(word);
        return StringHelper.NormalizeName(normalized.Pluralize());
    }

    /// <summary>
    /// Convert string to singular form using Humanizer
    /// </summary>
    public static string ToSingular(this string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        string normalized = StringHelper.NormalizeName(word);
        return StringHelper.NormalizeName(normalized.Singularize());
    }

    /// <summary>
    /// Check if type name represents a reference type in C#
    /// </summary>
    public static bool IsReferenceType(this string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;

        string lowerType = typeName.ToLowerInvariant();

        // Arrays are reference types
        if (lowerType.EndsWith("[]")) return true;

        // Collections are reference types
        if (lowerType.StartsWith("list<") || lowerType.StartsWith("icollection<") ||
            lowerType.StartsWith("ienumerable<") || lowerType.StartsWith("collection<"))
            return true;

        // Nullable value types are still value types
        if (lowerType.EndsWith("?")) return false;

        return lowerType switch
        {
            "string" => true,
            "text" => true,
            "object" => true,

            // Common value types
            "int" or "integer" => false,
            "bool" or "boolean" => false,
            "decimal" or "double" or "float" => false,
            "datetime" or "date" or "timestamp" => false,
            "guid" or "identifier" => false,
            "byte" or "sbyte" or "short" or "ushort" or "uint" or "long" or "ulong" => false,
            "char" => false,

            // Enums are value types
            "enum" => false,

            // Default: assume it's a reference type (class, interface, etc.)
            _ => true
        };
    }

    /// <summary>
    /// Convert string to camelCase using Humanizer
    /// </summary>
    public static string ToCamelCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Camelize();
    }

    /// <summary>
    /// Convert string to PascalCase using Humanizer
    /// </summary>
    public static string ToPascalCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Pascalize();
    }

    /// <summary>
    /// Check if the word is already in plural form using Humanizer
    /// </summary>
    public static bool IsPlural(this string word)
    {
        if (string.IsNullOrEmpty(word)) return false;

        // Compare the word with its singular form
        // If they're different, the original word is likely plural
        return !string.Equals(word, word.Singularize(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Convert string to human-readable title case
    /// </summary>
    public static string ToTitleCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Humanize(LetterCasing.Title);
    }

    /// <summary>
    /// Convert string to human-readable sentence case
    /// </summary>
    public static string ToSentenceCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Humanize(LetterCasing.Sentence);
    }
}