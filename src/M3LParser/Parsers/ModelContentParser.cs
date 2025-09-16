namespace M3LParser.Parsers;

/// <summary>
/// Parser for model content (fields, relations, indexes, etc.)
/// </summary>
public class ModelContentParser : BaseParser
{
    private readonly FieldParser _fieldParser;
    private readonly RelationParser _relationParser;
    private readonly IndexParser _indexParser;
    private readonly MetadataParser _metadataParser;

    public ModelContentParser(ParserContext context) : base(context)
    {
        _fieldParser = new FieldParser(context);
        _relationParser = new RelationParser(context);
        _indexParser = new IndexParser(context);
        _metadataParser = new MetadataParser(context);
    }

    /// <summary>
    /// Parse the content of a model (fields, relations, indexes, metadata)
    /// </summary>
    public void ParseModelContent(M3LModel model)
    {
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
                string.IsNullOrEmpty(model.Description))
            {
                model.Description = currentLine;
                AppLog.Debug("Model description: {Description}", model.Description);
                Context.NextLine();
                continue;
            }

            // Parse field, relation, index, etc. based on current section
            if (currentLine.StartsWith("-"))
            {
                ParseModelElement(model, currentSection);
                continue;
            }

            // Move to next line if we couldn't parse anything
            Context.NextLine();
        }
    }

    /// <summary>
    /// Parse a model element based on the current section
    /// </summary>
    private void ParseModelElement(M3LModel model, string currentSection)
    {
        switch (currentSection?.ToLowerInvariant())
        {
            case "relations":
                var relation = _relationParser.Parse();
                if (relation != null)
                {
                    AppLog.Debug("Added relation to model {ModelName}: {RelationName} -> {Target}",
                        model.Name, relation.Name, relation.Target);
                    model.Relations.Add(relation);
                }
                break;

            case "indexes":
                var index = _indexParser.Parse();
                if (index != null)
                {
                    AppLog.Debug("Added index to model {ModelName}: {IndexName} (fields: {Fields})",
                        model.Name, index.Name, string.Join(", ", index.Fields));
                    model.Indexes.Add(index);
                }
                break;

            case "metadata":
                var (key, value) = _metadataParser.Parse();
                model.Metadata[key] = value;
                AppLog.Debug("Added metadata to model {ModelName}: {Key} = {Value}",
                    model.Name, key, value);
                break;

            default:
                // 현재 라인 체크
                var currentLine = Context.CurrentLineTrimmed;

                // @unique로 시작하는 필드 줄인 경우, 인덱스로 처리
                if (currentLine.StartsWith("- @unique("))
                {
                    var modelIndex = ParseModelLevelIndex(currentLine);
                    if (modelIndex != null)
                    {
                        AppLog.Debug("Added model-level unique constraint to model {ModelName}: {IndexName} (fields: {Fields})",
                            model.Name, modelIndex.Name, string.Join(", ", modelIndex.Fields));
                        model.Indexes.Add(modelIndex);
                        Context.NextLine();
                        break;
                    }
                }

                // 만약 필드 정의라면 일반적인 방식으로 처리
                var field = _fieldParser.Parse();
                if (field != null)
                {
                    // @unique로 시작하는 필드 이름인 경우 이것도 인덱스로 변환
                    if (field.Name.StartsWith("@unique("))
                    {
                        var match = Regex.Match(field.Name, @"@unique\(([^)]+)\)");
                        if (match.Success)
                        {
                            var uniqueParams = match.Groups[1].Value;
                            var parameters = uniqueParams.Split(',')
                                .Select(p => p.Trim())
                                .ToList();

                            var index1 = new M3LIndex
                            {
                                Name = $"UK_{model.Name}_{string.Join("_", parameters)}",
                                Fields = parameters,
                                IsUnique = true
                            };

                            AppLog.Debug("Converted field to unique constraint in model {ModelName}: {IndexName} (fields: {Fields})",
                                model.Name, index1.Name, string.Join(", ", index1.Fields));
                            model.Indexes.Add(index1);
                        }
                    }
                    else
                    {
                        AppLog.Debug("Added field to model {ModelName}: {FieldName} ({FieldType})",
                            model.Name, field.Name, field.Type);
                        model.Fields.Add(field);
                    }
                    break;
                }

                // Check if it's an index or relation at model level
                if (currentLine.Contains("@index") || currentLine.Contains("@unique"))
                {
                    var modelIndex = ParseModelLevelIndex(currentLine);
                    if (modelIndex != null)
                    {
                        AppLog.Debug("Added model-level index to model {ModelName}: {IndexName} (fields: {Fields})",
                            model.Name, modelIndex.Name, string.Join(", ", modelIndex.Fields));
                        model.Indexes.Add(modelIndex);
                    }
                }
                else if (currentLine.Contains("@relation"))
                {
                    var modelRelation = ParseModelLevelRelation(currentLine);
                    if (modelRelation != null)
                    {
                        AppLog.Debug("Added model-level relation to model {ModelName}: {RelationName} -> {Target}",
                            model.Name, modelRelation.Name, modelRelation.Target);
                        model.Relations.Add(modelRelation);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Parse an index definition at the model level (using @index or @unique)
    /// </summary>
    private M3LIndex ParseModelLevelIndex(string line)
    {
        AppLog.Debug("Attempting to parse model-level index from line: '{Line}'", line);

        var index = new M3LIndex();

        // Check if it's an index
        if (line.Contains("@index("))
        {
            var match = Regex.Match(line, @"@index\(([^)]+)\)");
            if (match.Success)
            {
                AppLog.Debug("Parsing model-level index: {Line}", line);
                var indexParams = match.Groups[1].Value;
                AppLog.Debug("Index parameters: {Params}", indexParams);

                var parameters = ParseParameters(indexParams);
                AppLog.Debug("Parsed {Count} parameters: {Params}", parameters.Count, string.Join(", ", parameters));

                index.Fields = parameters.Where(p => !p.Contains(":")).ToList();
                index.IsUnique = false;

                // Check for name parameter
                var nameParam = parameters.FirstOrDefault(p => p.StartsWith("name:"));
                if (nameParam != null)
                {
                    index.Name = nameParam.Substring("name:".Length).Trim().Trim('"');
                    AppLog.Debug("Index name from parameter: {Name}", index.Name);
                }
                else if (index.Fields.Count > 0)
                {
                    index.Name = string.Join("_", index.Fields);
                    AppLog.Debug("Generated index name: {Name}", index.Name);
                }

                // Check for description (in quotes after the parentheses)
                var descMatch = Regex.Match(line, @"\)\s+""([^""]+)""");
                if (descMatch.Success)
                {
                    index.Description = descMatch.Groups[1].Value;
                    AppLog.Debug("Index description: {Description}", index.Description);
                }

                Context.NextLine();
                return index;
            }
            else
            {
                AppLog.Warning("Failed to match @index pattern in line: {Line}", line);
            }
        }
        // Check if it's a unique constraint
        else if (line.Contains("@unique("))
        {
            var match = Regex.Match(line, @"@unique\(([^)]+)\)");
            if (match.Success)
            {
                AppLog.Debug("Parsing model-level unique constraint: {Line}", line);
                var uniqueParams = match.Groups[1].Value;
                AppLog.Debug("Unique constraint parameters: {Params}", uniqueParams);

                var parameters = ParseParameters(uniqueParams);
                AppLog.Debug("Parsed {Count} parameters: {Params}", parameters.Count, string.Join(", ", parameters));

                index.Fields = parameters.Where(p => !p.Contains(":")).ToList();
                index.IsUnique = true;

                // Check for name parameter
                var nameParam = parameters.FirstOrDefault(p => p.StartsWith("name:"));
                if (nameParam != null)
                {
                    index.Name = nameParam.Substring("name:".Length).Trim().Trim('"');
                    AppLog.Debug("Unique constraint name from parameter: {Name}", index.Name);
                }
                else if (index.Fields.Count > 0)
                {
                    // Don't set the index name here - let the SQL generator create table-specific names
                    // to avoid conflicts between tables with the same field combinations
                    // index.Name = "UK_" + string.Join("_", index.Fields);
                    AppLog.Debug("Unique constraint name will be generated by SQL builder for fields: {Fields}", string.Join("_", index.Fields));
                }

                // Check for description (in quotes after the parentheses)
                var descMatch = Regex.Match(line, @"\)\s+""([^""]+)""");
                if (descMatch.Success)
                {
                    index.Description = descMatch.Groups[1].Value;
                    AppLog.Debug("Unique constraint description: {Description}", index.Description);
                }

                Context.NextLine();
                return index;
            }
            else
            {
                AppLog.Warning("Failed to match @unique pattern in line: {Line}", line);
            }
        }
        else
        {
            AppLog.Debug("Line does not contain @index or @unique pattern: {Line}", line);
        }

        return null;
    }

    /// <summary>
    /// Parse a relation definition at the model level (using @relation)
    /// </summary>
    private M3LRelation ParseModelLevelRelation(string line)
    {
        var relation = new M3LRelation();

        // Parse relation definition
        if (line.Contains("@relation("))
        {
            var match = Regex.Match(line, @"@relation\(([^)]+)\)");
            if (match.Success)
            {
                AppLog.Debug("Parsing model-level relation: {Line}", line);
                var relationParams = match.Groups[1].Value;
                var parameters = ParseParameters(relationParams);

                if (parameters.Count >= 2)
                {
                    relation.Name = parameters[0];
                    AppLog.Debug("Relation name: {Name}", relation.Name);

                    // Parse target and direction
                    var targetParam = parameters[1];
                    if (targetParam.StartsWith("->"))
                    {
                        relation.IsToOne = true;
                        relation.Target = targetParam.Substring(2).Trim();
                        AppLog.Debug("Relation is to-one to target: {Target}", relation.Target);
                    }
                    else if (targetParam.StartsWith("<-"))
                    {
                        relation.IsToOne = false;
                        relation.Target = targetParam.Substring(2).Trim();
                        AppLog.Debug("Relation is to-many to target: {Target}", relation.Target);
                    }

                    // Check for from parameter
                    var fromParam = parameters.FirstOrDefault(p => p.StartsWith("from:"));
                    if (fromParam != null)
                    {
                        relation.From = fromParam.Substring("from:".Length).Trim();
                        AppLog.Debug("Relation from field: {FromField}", relation.From);
                    }
                }

                // Check for description (in quotes after the parentheses)
                var descMatch = Regex.Match(line, @"\)\s+""([^""]+)""");
                if (descMatch.Success)
                {
                    relation.Description = descMatch.Groups[1].Value;
                    AppLog.Debug("Relation description: {Description}", relation.Description);
                }

                Context.NextLine();
                return relation;
            }
        }

        return null;
    }
}