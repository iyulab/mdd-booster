namespace MDDBooster.Processors;

/// <summary>
/// Processes nested field metadata based on indentation and filters out incorrectly parsed nested items
/// </summary>
public class NestedFieldProcessor : IModelProcessor
{
    public void Process(MDDDocument document)
    {
        AppLog.Information("Processing nested field metadata based on indentation");

        foreach (var model in document.Models)
        {
            ProcessModelFields(model, document);
        }

        foreach (var interface_ in document.Interfaces)
        {
            ProcessInterfaceFields(interface_, document);
        }

        AppLog.Information("Completed nested field metadata processing");
    }

    private void ProcessModelFields(MDDModel model, MDDDocument document)
    {
        var fieldsToRemove = new List<MDDField>();
        var fieldsToUpdate = new Dictionary<MDDField, Dictionary<string, object>>();

        // Parse the raw text to understand the actual structure
        var fieldStructure = ParseFieldStructure(model.RawText);

        foreach (var field in model.Fields)
        {
            var fieldName = field.BaseField.Name;

            if (fieldStructure.TryGetValue(fieldName, out var structure))
            {
                if (structure.IsNestedMetadata)
                {
                    // This field is actually nested metadata for another field
                    fieldsToRemove.Add(field);

                    // Find the parent field and add this as metadata
                    var parentField = model.Fields.FirstOrDefault(f => f.BaseField.Name == structure.ParentFieldName);
                    if (parentField != null)
                    {
                        if (!fieldsToUpdate.ContainsKey(parentField))
                        {
                            fieldsToUpdate[parentField] = new Dictionary<string, object>();
                        }
                        fieldsToUpdate[parentField][fieldName] = field.BaseField.Type;

                        AppLog.Debug("Moving nested metadata {FieldName} to parent field {ParentField} in model {ModelName}",
                            fieldName, structure.ParentFieldName, model.BaseModel.Name);
                    }
                }
                else if (structure.HasNestedMetadata)
                {
                    // This field has nested metadata that needs to be extracted
                    if (!fieldsToUpdate.ContainsKey(field))
                    {
                        fieldsToUpdate[field] = new Dictionary<string, object>();
                    }

                    foreach (var metadata in structure.NestedMetadata)
                    {
                        fieldsToUpdate[field][metadata.Key] = metadata.Value;
                    }
                }
            }
        }

        // Remove fields that are actually nested metadata
        foreach (var fieldToRemove in fieldsToRemove)
        {
            model.Fields.Remove(fieldToRemove);

            var baseFieldToRemove = model.BaseModel.Fields.FirstOrDefault(f => f.Name == fieldToRemove.BaseField.Name);
            if (baseFieldToRemove != null)
            {
                model.BaseModel.Fields.Remove(baseFieldToRemove);
            }
        }

        // Update fields with extracted metadata
        foreach (var kvp in fieldsToUpdate)
        {
            var field = kvp.Key;
            var metadata = kvp.Value;

            foreach (var metadataKvp in metadata)
            {
                field.ExtendedMetadata[metadataKvp.Key] = metadataKvp.Value;
            }
        }
    }

    private void ProcessInterfaceFields(MDDInterface interface_, MDDDocument document)
    {
        var fieldsToRemove = new List<MDDField>();
        var fieldsToUpdate = new Dictionary<MDDField, Dictionary<string, object>>();

        var fieldStructure = ParseFieldStructure(interface_.RawText);

        foreach (var field in interface_.Fields)
        {
            var fieldName = field.BaseField.Name;

            if (fieldStructure.TryGetValue(fieldName, out var structure))
            {
                if (structure.IsNestedMetadata)
                {
                    fieldsToRemove.Add(field);

                    var parentField = interface_.Fields.FirstOrDefault(f => f.BaseField.Name == structure.ParentFieldName);
                    if (parentField != null)
                    {
                        if (!fieldsToUpdate.ContainsKey(parentField))
                        {
                            fieldsToUpdate[parentField] = new Dictionary<string, object>();
                        }
                        fieldsToUpdate[parentField][fieldName] = field.BaseField.Type;
                    }
                }
                else if (structure.HasNestedMetadata)
                {
                    if (!fieldsToUpdate.ContainsKey(field))
                    {
                        fieldsToUpdate[field] = new Dictionary<string, object>();
                    }

                    foreach (var metadata in structure.NestedMetadata)
                    {
                        fieldsToUpdate[field][metadata.Key] = metadata.Value;
                    }
                }
            }
        }

        // Remove nested metadata fields
        foreach (var fieldToRemove in fieldsToRemove)
        {
            interface_.Fields.Remove(fieldToRemove);

            var baseFieldToRemove = interface_.BaseInterface.Fields.FirstOrDefault(f => f.Name == fieldToRemove.BaseField.Name);
            if (baseFieldToRemove != null)
            {
                interface_.BaseInterface.Fields.Remove(baseFieldToRemove);
            }
        }

        // Update fields with metadata
        foreach (var kvp in fieldsToUpdate)
        {
            var field = kvp.Key;
            var metadata = kvp.Value;

            foreach (var metadataKvp in metadata)
            {
                field.ExtendedMetadata[metadataKvp.Key] = metadataKvp.Value;
            }
        }
    }

    private Dictionary<string, FieldStructure> ParseFieldStructure(string rawText)
    {
        var result = new Dictionary<string, FieldStructure>();

        if (string.IsNullOrEmpty(rawText))
            return result;

        var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var currentField = string.Empty;
        var currentIndent = 0;
        var fieldStack = new Stack<(string name, int indent)>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
            var trimmedLine = line.Trim();

            if (!trimmedLine.StartsWith("-"))
                continue;

            var fieldMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^-\s+([^:\s]+)(?:\s*:\s*(.+))?");
            if (!fieldMatch.Success)
                continue;

            var fieldName = fieldMatch.Groups[1].Value;
            var fieldValue = fieldMatch.Groups[2].Success ? fieldMatch.Groups[2].Value : string.Empty;

            // Pop fields from stack if current indent is less than or equal to their indent
            while (fieldStack.Count > 0 && indent <= fieldStack.Peek().indent)
            {
                fieldStack.Pop();
            }

            if (fieldStack.Count > 0)
            {
                // This is nested under another field
                var parentField = fieldStack.Peek().name;

                if (!result.ContainsKey(fieldName))
                {
                    result[fieldName] = new FieldStructure();
                }

                result[fieldName].IsNestedMetadata = true;
                result[fieldName].ParentFieldName = parentField;

                // Add to parent's nested metadata
                if (!result.ContainsKey(parentField))
                {
                    result[parentField] = new FieldStructure();
                }

                result[parentField].HasNestedMetadata = true;
                result[parentField].NestedMetadata[fieldName] = fieldValue;
            }
            else
            {
                // This is a top-level field
                if (!result.ContainsKey(fieldName))
                {
                    result[fieldName] = new FieldStructure();
                }
            }

            fieldStack.Push((fieldName, indent));
        }

        return result;
    }

    private class FieldStructure
    {
        public bool IsNestedMetadata { get; set; }
        public string ParentFieldName { get; set; }
        public bool HasNestedMetadata { get; set; }
        public Dictionary<string, string> NestedMetadata { get; set; } = new Dictionary<string, string>();
    }
}