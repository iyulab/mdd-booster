namespace M3LParser.Parsers;

/// <summary>
/// Parser for model definition lines
/// </summary>
public class ModelDefinitionParser : BaseParser
{
    public ModelDefinitionParser(ParserContext context) : base(context) { }

    /// <summary>
    /// Parse the model definition line to extract name, label, inheritance, and attributes
    /// </summary>
    public void ParseModelDefinition(M3LModel model, string definitionLine)
    {
        var mainPart = definitionLine;

        AppLog.Debug("Parsing model definition: {Line}", mainPart);

        // Extract attributes
        if (mainPart.Contains('@'))
        {
            var attributeParts = mainPart.Split('@', StringSplitOptions.RemoveEmptyEntries);
            mainPart = attributeParts[0].Trim();

            for (int i = 1; i < attributeParts.Length; i++)
            {
                var attr = "@" + attributeParts[i].Trim();
                model.Attributes.Add(attr);
                AppLog.Debug("Added model attribute: {Attribute}", attr);
            }
        }

        // Extract model name and label
        if (mainPart.Contains('(') && mainPart.Contains(')'))
        {
            var match = Regex.Match(mainPart, @"(.+?)\((.+?)\)(.*)");
            if (match.Success)
            {
                var namePart = match.Groups[1].Value.Trim();
                model.Label = match.Groups[2].Value.Trim();
                var rest = match.Groups[3].Value.Trim();

                ParseModelNameWithInheritance(model, namePart);

                AppLog.Debug("Model name: {ModelName}, label: {Label}", model.Name, model.Label);

                // Check if there's a description after the label
                if (!string.IsNullOrEmpty(rest) && rest.StartsWith("#"))
                {
                    model.Description = rest.Substring(1).Trim();
                    AppLog.Debug("Model description: {Description}", model.Description);
                }
            }
        }
        else if (mainPart.Contains(':'))
        {
            ParseModelNameWithInheritance(model, mainPart);
        }
        else if (mainPart.Contains('#'))
        {
            var descParts = mainPart.Split('#', 2);
            model.Name = descParts[0].Trim();
            model.Description = descParts[1].Trim();
            AppLog.Debug("Model name: {ModelName}, description: {Description}", model.Name, model.Description);
        }
        else
        {
            model.Name = mainPart.Trim();
            AppLog.Debug("Model name: {ModelName}", model.Name);
        }
    }

    /// <summary>
    /// Parse model name with inheritance information
    /// </summary>
    private void ParseModelNameWithInheritance(M3LModel model, string namePart)
    {
        if (namePart.Contains(':'))
        {
            var inheritanceParts = namePart.Split(':', StringSplitOptions.RemoveEmptyEntries);
            model.Name = inheritanceParts[0].Trim();

            if (inheritanceParts.Length > 1)
            {
                var inheritsList = inheritanceParts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(i => i.Trim())
                    .ToList();

                model.Inherits.AddRange(inheritsList);
                AppLog.Debug("Model {ModelName} inherits from: {InheritanceList}",
                    model.Name, string.Join(", ", model.Inherits));
            }
        }
        else
        {
            model.Name = namePart;
        }
    }
}
