using M3LParser.Models;
using MDDBooster.Models;

namespace MDDBooster.Processors;

/// <summary>
/// Processor that resolves inheritance and collects all fields from base classes and interfaces
/// </summary>
public class InheritanceProcessor : IModelProcessor
{
    public void Process(MDDDocument document)
    {
        AppLog.Information("Processing model inheritance and collecting fields...");

        // First process interface inheritance to collect all interface fields
        ProcessInterfaceInheritance(document);

        // Build the correct inheritance hierarchy to avoid circular references
        ValidateInheritanceHierarchy(document);

        // Then apply default inheritance where needed
        ApplyDefaultInheritance(document);

        // Finally resolve model inheritance hierarchies
        ResolveModelInheritance(document);

        AppLog.Information("Inheritance processing completed.");
    }

    /// <summary>
    /// Validates the inheritance hierarchy to ensure there are no circular references
    /// </summary>
    private void ValidateInheritanceHierarchy(MDDDocument document)
    {
        AppLog.Debug("Validating inheritance hierarchy...");
        var inheritanceGraph = new Dictionary<string, HashSet<string>>();

        // Build inheritance graph for models
        foreach (var model in document.BaseDocument.Models)
        {
            if (!inheritanceGraph.ContainsKey(model.Name))
            {
                inheritanceGraph[model.Name] = new HashSet<string>();
            }

            foreach (var inheritedName in model.Inherits)
            {
                // Only add model inheritance, not interfaces
                if (document.BaseDocument.Models.Any(m => m.Name == inheritedName))
                {
                    inheritanceGraph[model.Name].Add(inheritedName);
                }
            }
        }

        // Detect and fix circular references
        foreach (var model in document.BaseDocument.Models)
        {
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            if (HasCyclicDependency(model.Name, inheritanceGraph, visited, recursionStack))
            {
                AppLog.Warning("Detected circular inheritance for model: {ModelName}", model.Name);
                FixCircularInheritance(model, document);
            }
        }
    }

    /// <summary>
    /// Checks if a model has a cyclic dependency in its inheritance hierarchy
    /// </summary>
    private bool HasCyclicDependency(string modelName, Dictionary<string, HashSet<string>> inheritanceGraph, HashSet<string> visited, HashSet<string> recursionStack)
    {
        if (!visited.Contains(modelName))
        {
            visited.Add(modelName);
            recursionStack.Add(modelName);

            if (inheritanceGraph.ContainsKey(modelName))
            {
                foreach (var neighbor in inheritanceGraph[modelName])
                {
                    if (!visited.Contains(neighbor) &&
                        HasCyclicDependency(neighbor, inheritanceGraph, visited, recursionStack))
                    {
                        return true;
                    }
                    else if (recursionStack.Contains(neighbor))
                    {
                        return true;
                    }
                }
            }
        }

        recursionStack.Remove(modelName);
        return false;
    }

    /// <summary>
    /// Fixes circular inheritance by removing problematic inheritance relationships
    /// </summary>
    private void FixCircularInheritance(M3LModel model, MDDDocument document)
    {
        AppLog.Information("Fixing circular inheritance for model: {ModelName}", model.Name);

        // Create a list of allowed models to inherit from (no circular references)
        var allowedModels = new List<string>();

        foreach (var inheritName in model.Inherits.ToList())
        {
            // Check if it's a model (not an interface)
            if (document.BaseDocument.Models.Any(m => m.Name == inheritName))
            {
                // Check if a circular path exists
                if (InheritsFromRecursive(document.BaseDocument, inheritName, model.Name))
                {
                    AppLog.Warning("Removing circular inheritance: {Model} → {BaseModel}", model.Name, inheritName);
                    model.Inherits.Remove(inheritName);
                }
                else
                {
                    allowedModels.Add(inheritName);
                }
            }
        }

        // If all base models were removed, try to find a suitable replacement
        if (!model.Inherits.Any(i => document.BaseDocument.Models.Any(m => m.Name == i)))
        {
            var defaultModel = document.BaseDocument.Models.FirstOrDefault(m =>
                m.Attributes != null && m.Attributes.Contains("@default") && m.Name != model.Name);

            if (defaultModel != null && !InheritsFromRecursive(document.BaseDocument, defaultModel.Name, model.Name))
            {
                AppLog.Information("Adding default inheritance after fixing circularity: {DefaultModel} → {Model}",
                    defaultModel.Name, model.Name);
                model.Inherits.Add(defaultModel.Name);
            }
        }
    }

    /// <summary>
    /// Checks if baseModel inherits from targetModel directly or indirectly
    /// </summary>
    private bool InheritsFromRecursive(M3LDocument document, string baseModelName, string targetModelName)
    {
        var processed = new HashSet<string>();
        return InheritsFromRecursiveInternal(document, baseModelName, targetModelName, processed);
    }

    private bool InheritsFromRecursiveInternal(M3LDocument document, string currentModelName, string targetModelName, HashSet<string> processed)
    {
        if (processed.Contains(currentModelName))
            return false;

        processed.Add(currentModelName);

        var currentModel = document.Models.FirstOrDefault(m => m.Name == currentModelName);
        if (currentModel == null)
            return false;

        if (currentModel.Inherits.Contains(targetModelName))
            return true;

        foreach (var inheritedName in currentModel.Inherits)
        {
            // Only check model inheritance, not interfaces
            if (document.Models.Any(m => m.Name == inheritedName))
            {
                if (InheritsFromRecursiveInternal(document, inheritedName, targetModelName, processed))
                    return true;
            }
        }

        return false;
    }

    private void ApplyDefaultInheritance(MDDDocument document)
    {
        // Find the model marked with @default attribute
        var defaultModel = document.BaseDocument.Models.FirstOrDefault(m =>
            m.Attributes != null && m.Attributes.Contains("@default"));

        if (defaultModel == null)
        {
            AppLog.Debug("No default model found. Skipping default inheritance.");
            return;
        }

        AppLog.Information("Found default model: {DefaultModel}", defaultModel.Name);

        // For each model that doesn't inherit from another model, apply default inheritance
        foreach (var model in document.BaseDocument.Models)
        {
            // Skip the default model itself
            if (model == defaultModel)
                continue;

            // Skip abstract models - they don't need default inheritance
            if (model.IsAbstract)
                continue;

            // Skip models that already inherit from classes (not just interfaces)
            bool inheritsFromAnyModel = false;
            foreach (var inheritedName in model.Inherits)
            {
                if (document.BaseDocument.Models.Any(m => m.Name == inheritedName))
                {
                    inheritsFromAnyModel = true;
                    break;
                }
            }

            // If it doesn't inherit from any model, make it inherit from the default model
            if (!inheritsFromAnyModel && !model.Inherits.Contains(defaultModel.Name))
            {
                AppLog.Information("Adding default inheritance from {DefaultModel} to {Model}",
                    defaultModel.Name, model.Name);
                model.Inherits.Add(defaultModel.Name);
            }
        }
    }

    /// <summary>
    /// Processes interface inheritance to ensure all interface fields include those from parent interfaces
    /// </summary>
    private void ProcessInterfaceInheritance(MDDDocument document)
    {
        foreach (var mddInterface in document.Interfaces)
        {
            // Skip if no inheritance
            if (mddInterface.BaseInterface.Inherits.Count == 0)
                continue;

            var processedEntities = new HashSet<string>();
            var allInheritedFields = new List<MDDField>();

            // Process each parent interface
            foreach (var inheritedName in mddInterface.BaseInterface.Inherits)
            {
                CollectFieldsFromEntity(document, inheritedName, processedEntities, allInheritedFields);
            }

            // Add inherited fields to this interface
            foreach (var inheritedField in allInheritedFields)
            {
                if (!mddInterface.Fields.Any(f => f.BaseField.Name == inheritedField.BaseField.Name))
                {
                    mddInterface.Fields.Add(inheritedField);
                    AppLog.Debug("Added inherited field {FieldName} to interface {InterfaceName}",
                        inheritedField.BaseField.Name, mddInterface.BaseInterface.Name);
                }
            }

            AppLog.Information("Interface {InterfaceName} now has {FieldCount} fields after inheritance resolution",
                mddInterface.BaseInterface.Name, mddInterface.Fields.Count);
        }
    }

    /// <summary>
    /// Resolves inheritance for model entities by collecting fields from parent entities
    /// </summary>
    private void ResolveModelInheritance(MDDDocument document)
    {
        // Process each model to collect all inherited fields
        foreach (var mddModel in document.Models)
        {
            // Create a tracking set for processed entities to avoid infinite recursion
            var processedEntities = new HashSet<string>();

            // List to collect all inherited fields
            var allInheritedFields = new List<MDDField>();

            // Track implemented interfaces to ensure we collect all interface fields
            var implementedInterfaces = new HashSet<string>();

            // Process inheritance - trace all parents and collect fields
            foreach (var inheritedTypeName in mddModel.BaseModel.Inherits)
            {
                // If it's an interface, add it to implemented interfaces
                if (document.BaseDocument.Interfaces.Any(i => i.Name == inheritedTypeName))
                {
                    implementedInterfaces.Add(inheritedTypeName);
                }

                CollectFieldsFromEntity(document, inheritedTypeName, processedEntities, allInheritedFields);
            }

            // Debug log all fields we are adding through inheritance
            AppLog.Debug("Model {ModelName} inherits {FieldCount} fields", mddModel.BaseModel.Name, allInheritedFields.Count);
            foreach (var field in allInheritedFields)
            {
                AppLog.Debug("  Inherited field: {FieldName} ({IsPrimary})",
                    field.BaseField.Name, field.BaseField.IsPrimaryKey ? "Primary Key" : "");
            }

            // Merge inherited fields with model's own fields
            // (priority to model's own fields if names clash)
            foreach (var inheritedField in allInheritedFields)
            {
                if (!mddModel.Fields.Any(f => f.BaseField.Name == inheritedField.BaseField.Name))
                {
                    mddModel.Fields.Add(inheritedField);
                    AppLog.Debug("Added inherited field {FieldName} to model {ModelName}",
                        inheritedField.BaseField.Name, mddModel.BaseModel.Name);
                }
            }

            AppLog.Information("Model {ModelName} now has {FieldCount} fields after inheritance resolution",
                mddModel.BaseModel.Name, mddModel.Fields.Count);
        }
    }

    private void CollectFieldsFromEntity(
        MDDDocument document,
        string entityName,
        HashSet<string> processedEntities,
        List<MDDField> collectedFields)
    {
        // Skip if already processed to prevent infinite recursion
        if (processedEntities.Contains(entityName))
            return;

        // Mark as processed
        processedEntities.Add(entityName);

        // Check if it's a model
        var model = document.BaseDocument.Models.FirstOrDefault(m => m.Name == entityName);
        if (model != null)
        {
            // Find the corresponding MDDModel
            var mddModel = document.Models.FirstOrDefault(m => m.BaseModel.Name == entityName);
            if (mddModel != null)
            {
                // Process parent inheritances first (depth-first traversal)
                foreach (var parentName in model.Inherits)
                {
                    CollectFieldsFromEntity(document, parentName, processedEntities, collectedFields);
                }

                // Add this model's fields (copy to avoid modifying original)
                foreach (var field in mddModel.Fields)
                {
                    // Only add fields defined directly in this model, not ones it inherits
                    if (model.Fields.Any(f => f.Name == field.BaseField.Name))
                    {
                        if (!collectedFields.Any(f => f.BaseField.Name == field.BaseField.Name))
                        {
                            // Create deep copy to avoid modification of source field
                            var fieldCopy = new MDDField
                            {
                                BaseField = CloneField(field.BaseField),
                                RawText = field.RawText,
                                FrameworkAttributes = new List<FrameworkAttribute>(field.FrameworkAttributes),
                                ExtendedMetadata = new Dictionary<string, object>(field.ExtendedMetadata)
                            };

                            collectedFields.Add(fieldCopy);
                            AppLog.Debug("Collected field {FieldName} from model {ModelName}",
                                field.BaseField.Name, mddModel.BaseModel.Name);
                        }
                    }
                }
            }
        }
        else
        {
            // Check if it's an interface
            CollectInterfaceFields(document, entityName, processedEntities, collectedFields, false);
        }
    }

    /// <summary>
    /// Collects fields from an interface and its parent interfaces
    /// </summary>
    private void CollectInterfaceFields(
        MDDDocument document,
        string interfaceName,
        HashSet<string> processedEntities,
        List<MDDField> collectedFields,
        bool isDirectImplementation)
    {
        var interface_ = document.BaseDocument.Interfaces.FirstOrDefault(i => i.Name == interfaceName);
        if (interface_ == null)
            return;

        // Find the corresponding MDDInterface
        var mddInterface = document.Interfaces.FirstOrDefault(i => i.BaseInterface.Name == interfaceName);
        if (mddInterface == null)
            return;

        // Process interface inheritance first
        foreach (var parentName in interface_.Inherits)
        {
            CollectFieldsFromEntity(document, parentName, processedEntities, collectedFields);
        }

        // Add all fields from this interface (both directly defined and inherited)
        foreach (var field in mddInterface.Fields)
        {
            if (!collectedFields.Any(f => f.BaseField.Name == field.BaseField.Name))
            {
                var fieldCopy = new MDDField
                {
                    BaseField = CloneField(field.BaseField),
                    RawText = field.RawText,
                    FrameworkAttributes = new List<FrameworkAttribute>(field.FrameworkAttributes),
                    ExtendedMetadata = new Dictionary<string, object>(field.ExtendedMetadata)
                };

                // Add metadata to track direct interface implementation
                if (isDirectImplementation)
                {
                    fieldCopy.ExtendedMetadata["DirectInterfaceImplementation"] = interfaceName;
                }

                collectedFields.Add(fieldCopy);
                AppLog.Debug("Collected field {FieldName} from interface {InterfaceName}",
                    field.BaseField.Name, mddInterface.BaseInterface.Name);
            }
        }
    }

    /// <summary>
    /// Creates a deep copy of an M3LField
    /// </summary>
    private M3LParser.Models.M3LField CloneField(M3LParser.Models.M3LField field)
    {
        // We need to make a deep copy to avoid modifying the original field
        var newField = new M3LParser.Models.M3LField
        {
            Name = field.Name,
            Type = field.Type,
            IsNullable = field.IsNullable,
            Length = field.Length,
            Description = field.Description,
            DefaultValue = field.DefaultValue,
            // Deep copy collections
            Attributes = new List<string>(field.Attributes),
            FrameworkAttributes = new List<string>(field.FrameworkAttributes),
            Metadata = new Dictionary<string, object>(field.Metadata)
        };

        return newField;
    }
}