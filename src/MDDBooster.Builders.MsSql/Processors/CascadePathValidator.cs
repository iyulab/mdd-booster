using MDDBooster.Models;
using MDDBooster.Utilities;
using MDDBooster.Builders.MsSql.Helpers;
using M3LParser.Helpers;

namespace MDDBooster.Builders.MsSql.Processors;

/// <summary>
/// Validates cascade paths to detect SQL Server multiple cascade path issues
/// </summary>
public class CascadePathValidator
{
    private readonly MDDDocument _document;

    public CascadePathValidator(MDDDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Validate all cascade paths and return warnings for potential SQL Server conflicts
    /// </summary>
    public List<CascadePathWarning> ValidateCascadePaths()
    {
        var warnings = new List<CascadePathWarning>();
        var cascadeGraph = BuildCascadeGraph();

        // Check each table for multiple cascade paths reaching the same target
        foreach (var sourceTable in cascadeGraph.Keys)
        {
            var pathGroups = FindAllCascadePaths(sourceTable, cascadeGraph)
                .Where(path => path.CascadeType == CascadeType.CASCADE)
                .GroupBy(path => path.TargetTable)
                .Where(group => group.Count() > 1)
                .ToList();

            foreach (var group in pathGroups)
            {
                var paths = group.ToList();
                warnings.Add(new CascadePathWarning
                {
                    TargetTable = group.Key,
                    ConflictingPaths = paths,
                    Severity = CascadePathSeverity.Error,
                    Message = $"Multiple CASCADE paths detected to table '{group.Key}': {string.Join(", ", paths.Select(p => $"{p.SourceTable}.{p.FieldName}"))}"
                });
            }
        }

        return warnings;
    }

    /// <summary>
    /// Build cascade relationship graph from all models
    /// </summary>
    private Dictionary<string, List<CascadeRelation>> BuildCascadeGraph()
    {
        var cascadeGraph = new Dictionary<string, List<CascadeRelation>>();

        var nonAbstractModels = _document.Models.Where(m => !m.BaseModel.IsAbstract).ToList();

        foreach (var model in nonAbstractModels)
        {
            var tableName = StringHelper.NormalizeName(model.BaseModel.Name);
            cascadeGraph[tableName] = new List<CascadeRelation>();

            var allFields = ModelUtilities.GetAllFields(_document, model);
            var referenceFields = allFields.Where(f => !f.ShouldExcludeFromSql() && f.BaseField.IsReference).ToList();

            foreach (var field in referenceFields)
            {
                var fieldName = StringHelper.NormalizeName(field.BaseField.Name);
                var targetTable = StringHelper.NormalizeName(field.BaseField.ReferenceTarget);
                var cascadeType = GetCascadeType(field);

                cascadeGraph[tableName].Add(new CascadeRelation
                {
                    SourceTable = tableName,
                    TargetTable = targetTable,
                    FieldName = fieldName,
                    CascadeType = cascadeType
                });
            }
        }

        return cascadeGraph;
    }

    /// <summary>
    /// Get cascade type from field configuration
    /// </summary>
    private CascadeType GetCascadeType(MDDField field)
    {
        // Check M3L cascade behavior syntax
        if (!string.IsNullOrEmpty(field.BaseField.CascadeBehavior))
        {
            return field.BaseField.CascadeBehavior.ToUpperInvariant() switch
            {
                "CASCADE" => CascadeType.CASCADE,
                "NO ACTION" => CascadeType.NO_ACTION,
                "SET NULL" => CascadeType.SET_NULL,
                _ => CascadeType.CASCADE // Default
            };
        }

        // Check extended metadata
        if (field.ExtendedMetadata.ContainsKey("OnDelete"))
        {
            var onDelete = field.ExtendedMetadata["OnDelete"].ToString()?.ToUpperInvariant();
            return onDelete switch
            {
                "CASCADE" => CascadeType.CASCADE,
                "NO ACTION" => CascadeType.NO_ACTION,
                "SET NULL" => CascadeType.SET_NULL,
                _ => CascadeType.CASCADE
            };
        }

        return CascadeType.CASCADE; // Default behavior
    }

    /// <summary>
    /// Find all cascade paths from a source table using DFS
    /// </summary>
    private List<CascadePath> FindAllCascadePaths(string sourceTable, Dictionary<string, List<CascadeRelation>> cascadeGraph)
    {
        var paths = new List<CascadePath>();
        var visited = new HashSet<string>();

        FindCascadePathsRecursive(sourceTable, cascadeGraph, paths, visited, new List<CascadeRelation>());

        return paths;
    }

    /// <summary>
    /// Recursive helper for finding cascade paths
    /// </summary>
    private void FindCascadePathsRecursive(
        string currentTable,
        Dictionary<string, List<CascadeRelation>> cascadeGraph,
        List<CascadePath> paths,
        HashSet<string> visited,
        List<CascadeRelation> currentPath)
    {
        if (visited.Contains(currentTable))
            return; // Avoid infinite loops

        visited.Add(currentTable);

        if (cascadeGraph.ContainsKey(currentTable))
        {
            foreach (var relation in cascadeGraph[currentTable])
            {
                // Only follow CASCADE relationships for conflict detection
                if (relation.CascadeType == CascadeType.CASCADE)
                {
                    var newPath = new List<CascadeRelation>(currentPath) { relation };

                    // Add this path
                    paths.Add(new CascadePath
                    {
                        SourceTable = currentPath.FirstOrDefault()?.SourceTable ?? currentTable,
                        TargetTable = relation.TargetTable,
                        FieldName = relation.FieldName,
                        CascadeType = relation.CascadeType,
                        PathLength = newPath.Count,
                        Relations = newPath.ToList()
                    });

                    // Continue searching deeper
                    FindCascadePathsRecursive(relation.TargetTable, cascadeGraph, paths, visited, newPath);
                }
            }
        }

        visited.Remove(currentTable);
    }
}

/// <summary>
/// Represents a cascade relationship between two tables
/// </summary>
public class CascadeRelation
{
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public CascadeType CascadeType { get; set; }
}

/// <summary>
/// Represents a complete cascade path
/// </summary>
public class CascadePath
{
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public CascadeType CascadeType { get; set; }
    public int PathLength { get; set; }
    public List<CascadeRelation> Relations { get; set; } = new();
}

/// <summary>
/// Warning about cascade path conflicts
/// </summary>
public class CascadePathWarning
{
    public string TargetTable { get; set; } = string.Empty;
    public List<CascadePath> ConflictingPaths { get; set; } = new();
    public CascadePathSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    public string GetSuggestion()
    {
        if (ConflictingPaths.Count <= 1) return string.Empty;

        var suggestions = new List<string>();

        // Suggest keeping the most direct path as CASCADE and others as NO ACTION
        var directPath = ConflictingPaths.OrderBy(p => p.PathLength).First();
        var otherPaths = ConflictingPaths.Where(p => p != directPath);

        suggestions.Add($"Keep {directPath.SourceTable}.{directPath.FieldName} as CASCADE");

        foreach (var path in otherPaths)
        {
            suggestions.Add($"Change {path.SourceTable}.{path.FieldName} to @reference({path.TargetTable})! (NO ACTION)");
        }

        return "Suggestions:\n- " + string.Join("\n- ", suggestions);
    }
}

/// <summary>
/// Cascade behavior types
/// </summary>
public enum CascadeType
{
    CASCADE,
    NO_ACTION,
    SET_NULL
}

/// <summary>
/// Severity levels for cascade path warnings
/// </summary>
public enum CascadePathSeverity
{
    Warning,
    Error
}