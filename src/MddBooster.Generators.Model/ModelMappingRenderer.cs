using MddBooster.Core.Ast;
using MddBooster.Core.Generation;
using MddBooster.Core.Semantic;

namespace MddBooster.Generators.Model;

/// <summary>A model set's EF mapping: the context and one configuration per entity type.</summary>
public sealed class ModelMapping(string context, IReadOnlyList<EntityConfiguration> configurations)
{
    public string Context { get; } = context;

    public IReadOnlyList<EntityConfiguration> Configurations { get; } = configurations;

    /// <summary>The configuration source of <paramref name="entityType"/> (e.g. <c>OrderExt</c>).</summary>
    public string this[string entityType] =>
        Configurations.SingleOrDefault(c => c.EntityType == entityType)?.Source
        ?? throw new KeyNotFoundException($"No configuration for '{entityType}'.");

    /// <summary>Every source of the mapping — the context, then the configurations in application order.</summary>
    public IEnumerable<string> Sources => Configurations.Select(c => c.Source).Prepend(Context);
}

/// <summary>Renders a model set's context and entity configurations together — the Model target's mapping half.</summary>
public static class ModelMappingRenderer
{
    public static ModelMapping Render(
        IReadOnlyList<ResolvedModel> models,
        string contextName,
        string ns,
        IReadOnlySet<string>? customExtViewModels = null,
        bool postgresNaming = false,
        IReadOnlyList<OneToOneRelationships.Relationship>? oneToOne = null,
        IReadOnlyList<ManyToOneNavigations.Navigation>? manyToOne = null,
        IReadOnlyList<ResolvedView>? readViews = null,
        IReadOnlyList<ReverseCollections.Collection>? collections = null)
        => new(
            DbContextRenderer.Render(models, contextName, ns, readViews, sqlServer: !postgresNaming),
            EntityConfigurationRenderer.Render(
                models, ns, customExtViewModels, postgresNaming, oneToOne, manyToOne, readViews, collections));
}
