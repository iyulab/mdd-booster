using MDDBooster.Models;

namespace MDDBooster.Utilities;

/// <summary>
/// Utility class for working with MDDModel inheritance
/// </summary>
public static class ModelUtilities
{
    // Cache for storing already processed model fields to avoid redundant calculations
    private static readonly Dictionary<string, List<MDDField>> _fieldCache = new Dictionary<string, List<MDDField>>();

    /// <summary>
    /// Gets all fields for a model, including fields inherited from base classes and interfaces
    /// </summary>
    public static List<MDDField> GetAllFields(MDDDocument document, MDDModel model)
    {
        // Use caching to avoid recalculating fields for the same model
        string cacheKey = model.BaseModel.Name;

        if (_fieldCache.TryGetValue(cacheKey, out var cachedFields))
        {
            // Return a copy of the cached fields to prevent modification of the cache
            return new List<MDDField>(cachedFields);
        }

        // Start with the model's own fields
        var fields = new List<MDDField>(model.Fields);

        // Create a set of field names that are already included
        var includedFieldNames = new HashSet<string>(fields.Select(f => f.BaseField.Name));

        // Process direct inheritance from base classes
        foreach (var inheritedName in model.BaseModel.Inherits)
        {
            if (document.BaseDocument.Models.Any(m => m.Name == inheritedName))
            {
                var baseModel = document.Models.FirstOrDefault(m => m.BaseModel.Name == inheritedName);
                if (baseModel != null)
                {
                    var baseFields = GetAllFields(document, baseModel);

                    // Add fields from base model that aren't already included
                    foreach (var baseField in baseFields)
                    {
                        if (!includedFieldNames.Contains(baseField.BaseField.Name))
                        {
                            // Mark the field as inherited from a class
                            baseField.ExtendedMetadata["InheritedFromClass"] = baseModel.BaseModel.Name;

                            fields.Add(baseField);
                            includedFieldNames.Add(baseField.BaseField.Name);
                        }
                    }
                }
            }
        }

        // Process direct implementations of interfaces
        foreach (var interfaceName in model.BaseModel.Inherits)
        {
            if (document.BaseDocument.Interfaces.Any(i => i.Name == interfaceName))
            {
                var interface_ = document.Interfaces.FirstOrDefault(i => i.BaseInterface.Name == interfaceName);
                if (interface_ != null)
                {
                    // Add fields from the interface that aren't already included
                    foreach (var interfaceField in interface_.Fields)
                    {
                        if (!includedFieldNames.Contains(interfaceField.BaseField.Name))
                        {
                            // Mark the field as coming from this interface
                            interfaceField.ExtendedMetadata["ImplementedFromInterface"] = interface_.BaseInterface.Name;

                            fields.Add(interfaceField);
                            includedFieldNames.Add(interfaceField.BaseField.Name);
                        }
                    }
                }
            }
        }

        // Cache the result
        _fieldCache[cacheKey] = fields;

        return new List<MDDField>(fields); // Return a copy to prevent modification of the cache
    }

    /// <summary>
    /// Clear the fields cache to refresh calculations when needed
    /// </summary>
    public static void ClearFieldsCache()
    {
        _fieldCache.Clear();
    }

    /// <summary>
    /// Determines if a model inherits from another specific model
    /// </summary>
    public static bool InheritsFrom(MDDDocument document, MDDModel model, string baseModelName)
    {
        // Direct inheritance check
        if (model.BaseModel.Inherits.Contains(baseModelName))
            return true;

        // Check inheritance chain
        var processedModels = new HashSet<string>();
        return InheritsFromInternal(document, model, baseModelName, processedModels);
    }

    /// <summary>
    /// Determine if a model implements a specific interface
    /// </summary>
    public static bool ImplementsInterface(MDDDocument document, MDDModel model, string interfaceName)
    {
        // Direct implementation
        if (model.BaseModel.Inherits.Contains(interfaceName))
            return true;

        // Check if any base class implements the interface
        foreach (var inheritedName in model.BaseModel.Inherits)
        {
            if (document.BaseDocument.Models.Any(m => m.Name == inheritedName))
            {
                var baseModel = document.Models.FirstOrDefault(m => m.BaseModel.Name == inheritedName);
                if (baseModel != null && ImplementsInterface(document, baseModel, interfaceName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool InheritsFromInternal(
        MDDDocument document,
        MDDModel model,
        string baseModelName,
        HashSet<string> processedModels)
    {
        // Avoid circular references
        if (processedModels.Contains(model.BaseModel.Name))
            return false;

        processedModels.Add(model.BaseModel.Name);

        // Check direct inheritance
        if (model.BaseModel.Inherits.Contains(baseModelName))
            return true;

        // Check indirect inheritance
        foreach (var inheritedName in model.BaseModel.Inherits)
        {
            var inheritedModel = document.Models.FirstOrDefault(m => m.BaseModel.Name == inheritedName);
            if (inheritedModel != null)
            {
                if (InheritsFromInternal(document, inheritedModel, baseModelName, processedModels))
                    return true;
            }
        }

        return false;
    }
}