namespace M3LParser.Parsers;

/// <summary>
/// Parser for enum definitions
/// </summary>
public class EnumParser : BaseParser
{
    public EnumParser(ParserContext context) : base(context)
    {
    }

    /// <summary>
    /// Parse an enum definition
    /// </summary>
    public M3LEnum Parse(string definitionLine)
    {
        var enum_ = new M3LEnum();
        AppLog.Debug("Parsing enum at line {LineNumber}: {Line}", Context.CurrentLineIndex + 1, definitionLine);

        var parts = definitionLine.Split(new[] { "::enum" }, StringSplitOptions.None);

        // Parse name and inheritance
        var namePart = parts[0].Trim();
        ParseEnumNameWithInheritance(enum_, namePart);

        // Parse description (if any)
        if (parts.Length > 1 && parts[1].Contains('#'))
        {
            enum_.Description = parts[1].Split('#', 2)[1].Trim();
            AppLog.Debug("Enum description: {Description}", enum_.Description);
        }

        // Parse enum values
        if (!Context.NextLine())
            return enum_;

        string currentGroup = null;

        while (Context.HasMoreLines && !IsEndOfDefinition(Context.CurrentLineTrimmed))
        {
            var currentLine = Context.CurrentLineTrimmed;

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(currentLine) ||
                currentLine.StartsWith("<!--") ||
                currentLine.StartsWith("-->"))
            {
                Context.NextLine();
                continue;
            }

            // Group definition
            if (currentLine.StartsWith("- ") && !currentLine.Contains(':'))
            {
                currentGroup = currentLine.Substring(2).Trim();
                AppLog.Debug("Found enum group: {GroupName}", currentGroup);
                Context.NextLine();
                continue;
            }

            // Enum value
            if (currentLine.StartsWith("- "))
            {
                var enumValue = ParseEnumValue(currentLine, currentGroup);
                AppLog.Debug("Added enum value: {ValueName} (group: {GroupName})",
                    enumValue.Name, enumValue.Group ?? "null");
                enum_.Values.Add(enumValue);
            }

            Context.NextLine();
        }

        AppLog.Debug("Completed parsing enum {EnumName} with {ValueCount} values",
            enum_.Name, enum_.Values.Count);

        return enum_;
    }

    /// <summary>
    /// Parse enum name with inheritance information
    /// </summary>
    private void ParseEnumNameWithInheritance(M3LEnum enum_, string namePart)
    {
        if (namePart.Contains(':'))
        {
            var inheritanceParts = namePart.Split(':', StringSplitOptions.RemoveEmptyEntries);
            enum_.Name = inheritanceParts[0].Trim();

            if (inheritanceParts.Length > 1)
            {
                var inheritsList = inheritanceParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(i => i.Trim())
                    .ToList();

                enum_.Inherits.AddRange(inheritsList);
                AppLog.Debug("Enum {EnumName} inherits from: {InheritanceList}",
                    enum_.Name, string.Join(", ", enum_.Inherits));
            }
        }
        else
        {
            enum_.Name = namePart;
        }
    }

    /// <summary>
    /// Parse an enum value definition
    /// </summary>
    private M3LEnumValue ParseEnumValue(string line, string group)
    {
        // Remove the leading dash and space
        var content = line.Substring(2).Trim();

        var enumValue = new M3LEnumValue { Group = group };

        // Parse name and description
        if (content.Contains(':'))
        {
            var parts = content.Split(':', 2);
            enumValue.Name = parts[0].Trim();

            var valuePart = parts[1].Trim();

            // Check if we have a type and value
            if (valuePart.StartsWith("integer =") || valuePart.StartsWith("string ="))
            {
                var typeParts = valuePart.Split('=', 2);
                enumValue.Type = typeParts[0].Trim();

                // Handle description in quotes
                var valueAndDesc = typeParts[1].Trim();
                var quotesMatch = Regex.Match(valueAndDesc, @"^([^ ""]+)\s+""(.+)""$");

                if (quotesMatch.Success)
                {
                    enumValue.Value = quotesMatch.Groups[1].Value;
                    enumValue.Description = quotesMatch.Groups[2].Value;
                }
                else
                {
                    enumValue.Value = valueAndDesc;
                }
            }
            else if (valuePart.StartsWith("\"") && valuePart.EndsWith("\""))
            {
                // Just a description in quotes
                enumValue.Description = valuePart.Substring(1, valuePart.Length - 2);
            }
            else
            {
                enumValue.Description = valuePart;
            }
        }
        else
        {
            enumValue.Name = content;
        }

        return enumValue;
    }
}