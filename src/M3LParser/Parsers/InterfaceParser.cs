namespace M3LParser.Parsers;

/// <summary>
/// Parser for interface definitions
/// </summary>
public class InterfaceParser : BaseParser
{
    private readonly FieldParser _fieldParser;
    private readonly MetadataParser _metadataParser;

    public InterfaceParser(ParserContext context) : base(context)
    {
        _fieldParser = new FieldParser(context);
        _metadataParser = new MetadataParser(context);
    }

    /// <summary>
    /// Parse an interface definition
    /// </summary>
    public M3LInterface Parse(string definitionLine)
    {
        var interface_ = new M3LInterface();
        AppLog.Debug("Parsing interface at line {LineNumber}: {Line}", Context.CurrentLineIndex + 1, definitionLine);

        var parts = definitionLine.Split(new[] { "::interface" }, StringSplitOptions.None);

        // Parse name and inheritance
        var namePart = parts[0].Trim();
        ParseInterfaceNameWithInheritance(interface_, namePart);

        // Parse description (if any)
        if (parts.Length > 1 && parts[1].Contains('#'))
        {
            interface_.Description = parts[1].Split('#', 2)[1].Trim();
            AppLog.Debug("Interface description: {Description}", interface_.Description);
        }

        // Parse fields and metadata
        if (!Context.NextLine())
            return interface_;

        string currentSection = null;

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

            // Check for section headers
            if (IsStartOfSection(currentLine))
            {
                currentSection = GetSectionName(currentLine);
                AppLog.Debug("Found section: {SectionName}", currentSection);
                Context.NextLine();
                continue;
            }

            // If next line is just text and we don't have a description yet, assume it's the description
            if (!currentLine.StartsWith("-") && !currentLine.StartsWith("#") &&
                string.IsNullOrEmpty(interface_.Description))
            {
                interface_.Description = currentLine;
                AppLog.Debug("Interface description: {Description}", interface_.Description);
                Context.NextLine();
                continue;
            }

            // Parse field or metadata based on current section
            if (currentLine.StartsWith("-"))
            {
                switch (currentSection?.ToLowerInvariant())
                {
                    case "metadata":
                        var (key, value) = _metadataParser.Parse();
                        interface_.Metadata[key] = value;
                        AppLog.Debug("Added metadata to interface {InterfaceName}: {Key} = {Value}",
                            interface_.Name, key, value);
                        break;

                    default:
                        // If no section is specified, assume it's a field
                        var field = _fieldParser.Parse();
                        if (field != null)
                        {
                            AppLog.Debug("Added field to interface {InterfaceName}: {FieldName} ({FieldType})",
                                interface_.Name, field.Name, field.Type);
                            interface_.Fields.Add(field);
                        }
                        break;
                }
            }
            else
            {
                Context.NextLine();
            }
        }

        AppLog.Debug("Completed parsing interface {InterfaceName} with {FieldCount} fields",
            interface_.Name, interface_.Fields.Count);

        return interface_;
    }

    /// <summary>
    /// Parse interface name with inheritance information
    /// </summary>
    private void ParseInterfaceNameWithInheritance(M3LInterface interface_, string namePart)
    {
        if (namePart.Contains(':'))
        {
            var inheritanceParts = namePart.Split(':', StringSplitOptions.RemoveEmptyEntries);
            interface_.Name = inheritanceParts[0].Trim();

            if (inheritanceParts.Length > 1)
            {
                var inheritsList = inheritanceParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(i => i.Trim())
                    .ToList();

                interface_.Inherits.AddRange(inheritsList);
                AppLog.Debug("Interface {InterfaceName} inherits from: {InheritanceList}",
                    interface_.Name, string.Join(", ", interface_.Inherits));
            }
        }
        else
        {
            interface_.Name = namePart;
        }
    }
}