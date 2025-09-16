namespace M3LParser.Parsers;

/// <summary>
/// Parser for relationship definitions
/// </summary>
public class RelationParser : BaseParser
{
    public RelationParser(ParserContext context) : base(context)
    {
    }

    /// <summary>
    /// Parse a relation definition
    /// </summary>
    public M3LRelation Parse()
    {
        var currentLine = Context.CurrentLineTrimmed;

        // If not a relation definition, return null
        if (!currentLine.StartsWith("-"))
        {
            return null;
        }

        var relation = new M3LRelation();
        AppLog.Debug("Parsing relation at line {LineNumber}", Context.CurrentLineIndex + 1);

        // Remove the leading dash
        var content = currentLine.Substring(1).Trim();

        // Parse the relation definition
        if (content.StartsWith(">") || content.StartsWith("<"))
        {
            relation.IsToOne = content.StartsWith(">");
            AppLog.Debug("Relation type: {RelationType}", relation.IsToOne ? "to-one" : "to-many");
            var nameWithDesc = content.Substring(1).Trim();

            // Parse description if present
            if (nameWithDesc.Contains("\""))
            {
                var match = Regex.Match(nameWithDesc, @"(.+?)\s+""(.+?)""");
                if (match.Success)
                {
                    relation.Name = match.Groups[1].Value.Trim();
                    relation.Description = match.Groups[2].Value;
                    AppLog.Debug("Relation name: {RelationName}, description: {Description}",
                        relation.Name, relation.Description);
                }
                else
                {
                    relation.Name = nameWithDesc;
                    AppLog.Debug("Relation name: {RelationName}", relation.Name);
                }
            }
            else
            {
                relation.Name = nameWithDesc;
                AppLog.Debug("Relation name: {RelationName}", relation.Name);
            }

            // Check for extended relation properties
            if (!Context.NextLine())
                return relation;

            while (Context.HasMoreLines)
            {
                var subLine = Context.CurrentLineTrimmed;

                // End of extended relation definition
                if (!subLine.StartsWith("-") || !subLine.Substring(1).TrimStart().StartsWith("-"))
                {
                    break;
                }

                // Parse subproperty
                var subProperty = subLine.Substring(1).Trim().Substring(1).Trim();
                if (subProperty.StartsWith("target:"))
                {
                    relation.Target = subProperty.Substring("target:".Length).Trim();
                    AppLog.Debug("Relation target: {Target}", relation.Target);
                }
                else if (subProperty.StartsWith("from:"))
                {
                    relation.From = subProperty.Substring("from:".Length).Trim();
                    AppLog.Debug("Relation from field: {FromField}", relation.From);
                }
                else if (subProperty.StartsWith("on_delete:"))
                {
                    relation.OnDelete = subProperty.Substring("on_delete:".Length).Trim();
                    AppLog.Debug("Relation on delete: {OnDelete}", relation.OnDelete);
                }
                else if (subProperty.StartsWith("on_update:"))
                {
                    relation.OnUpdate = subProperty.Substring("on_update:".Length).Trim();
                    AppLog.Debug("Relation on update: {OnUpdate}", relation.OnUpdate);
                }
                else if (subProperty.Contains(':'))
                {
                    // Add other properties to metadata
                    var parts = subProperty.Split(':', 2);
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    relation.Metadata[key] = value;
                    AppLog.Debug("Relation metadata: {Key} = {Value}", key, value);
                }

                Context.NextLine();
            }
        }
        else
        {
            // Not a relation definition
            return null;
        }

        return relation;
    }
}