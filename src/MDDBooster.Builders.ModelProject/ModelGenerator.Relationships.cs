namespace MDDBooster.Builders.ModelProject;

/// <summary>
/// ModelGenerator - Relationship mapping and building methods
/// </summary>
public partial class ModelGenerator
{
    private Dictionary<string, List<RelationInfo>> BuildRelationshipMap()
    {
        var relationMap = new Dictionary<string, List<RelationInfo>>();

        // Build relationships from field references
        BuildFieldBasedRelationships(relationMap);

        // Build relationships from explicit relations
        BuildExplicitRelationships(relationMap);

        return relationMap;
    }

    private void BuildFieldBasedRelationships(Dictionary<string, List<RelationInfo>> relationMap)
    {
        foreach (var model in Document.Models)
        {
            foreach (var field in model.Fields)
            {
                if (!field.BaseField.IsReference || string.IsNullOrEmpty(field.BaseField.ReferenceTarget))
                    continue;

                string targetModelName = field.BaseField.ReferenceTarget;
                string sourceModelName = model.BaseModel.Name;
                string fieldName = field.BaseField.Name;

                // Forward relationship using the helper
                string navigationPropertyName = NavigationPropertyHelper.GetNavigationPropertyName(targetModelName, fieldName);
                AddRelationship(relationMap, sourceModelName, targetModelName, navigationPropertyName, false, fieldName, true);

                // Back reference using the helper
                string backrefPropertyName = NavigationPropertyHelper.GetBackReferencePropertyName(sourceModelName, fieldName);
                AddRelationship(relationMap, targetModelName, sourceModelName, backrefPropertyName, true);
            }
        }
    }

    private void BuildExplicitRelationships(Dictionary<string, List<RelationInfo>> relationMap)
    {
        foreach (var model in Document.Models)
        {
            foreach (var relation in model.BaseModel.Relations)
            {
                if (string.IsNullOrEmpty(relation.Target))
                    continue;

                string sourceModelName = model.BaseModel.Name;
                string targetModelName = relation.Target;
                string propertyName = relation.Name;
                string foreignKeyField = !string.IsNullOrEmpty(relation.From) ? relation.From : null;

                AddRelationship(relationMap, sourceModelName, targetModelName, propertyName, !relation.IsToOne, foreignKeyField);

                if (relation.IsToOne)
                {
                    string backrefPropertyName = NavigationPropertyHelper.GetCollectionPropertyName(sourceModelName);
                    AddRelationship(relationMap, targetModelName, sourceModelName, backrefPropertyName, true);
                }
            }
        }
    }

    private void AddRelationship(Dictionary<string, List<RelationInfo>> relationMap, string sourceModel,
        string targetModel, string propertyName, bool isToMany, string navigationField = null, bool isForeignKey = false)
    {
        if (!relationMap.ContainsKey(sourceModel))
        {
            relationMap[sourceModel] = new List<RelationInfo>();
        }

        var existingRelation = relationMap[sourceModel]
            .FirstOrDefault(r => r.TargetModel == targetModel && r.PropertyName == propertyName);

        if (existingRelation == null)
        {
            relationMap[sourceModel].Add(new RelationInfo
            {
                SourceModel = sourceModel,
                TargetModel = targetModel,
                PropertyName = propertyName,
                IsToMany = isToMany,
                NavigationField = navigationField,
                IsForeignKey = isForeignKey
            });

            AppLog.Debug("Added relationship: {SourceModel} -> {TargetModel} ({PropertyName}, ToMany: {IsToMany})",
                sourceModel, targetModel, propertyName, isToMany);
        }
        else if (!string.IsNullOrEmpty(navigationField))
        {
            existingRelation.NavigationField = navigationField;
            existingRelation.IsForeignKey = isForeignKey;

            AppLog.Debug("Updated existing relationship: {SourceModel} -> {TargetModel} with navigation field {NavigationField}",
                sourceModel, targetModel, navigationField);
        }
    }

    /// <summary>
    /// Gets all relationships for a specific model
    /// </summary>
    internal List<RelationInfo> GetModelRelationships(string modelName)
    {
        return _modelRelations.TryGetValue(modelName, out var relations) ? relations : new List<RelationInfo>();
    }

    /// <summary>
    /// Checks if a model has any relationships
    /// </summary>
    internal bool HasRelationships(string modelName)
    {
        return _modelRelations.ContainsKey(modelName) && _modelRelations[modelName].Any();
    }

    /// <summary>
    /// Gets all foreign key relationships for a model
    /// </summary>
    internal List<RelationInfo> GetForeignKeyRelationships(string modelName)
    {
        return GetModelRelationships(modelName).Where(r => r.IsForeignKey).ToList();
    }

    /// <summary>
    /// Gets all navigation relationships for a model
    /// </summary>
    internal List<RelationInfo> GetNavigationRelationships(string modelName)
    {
        return GetModelRelationships(modelName).Where(r => !r.IsForeignKey).ToList();
    }
}