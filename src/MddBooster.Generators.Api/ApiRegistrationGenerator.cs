using MddBooster.Core.Generation;

namespace MddBooster.Generators.Api;

/// <summary>
/// <see cref="IArtifactGenerator"/> that produces a single
/// <c>Api_gen/ApiRegistration_gen.cs</c> file covering OData + GraphQL
/// entity pair registration for every model in the context. Paired with the
/// Model generator — running both produces a complete consumer-side wiring.
/// </summary>
public sealed class ApiRegistrationGenerator(ApiRegistrationGeneratorOptions options) : IArtifactGenerator
{
    private readonly ApiRegistrationGeneratorOptions _options = options
        ?? throw new ArgumentNullException(nameof(options));

    public string Name => "api";

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var apiDir = Path.Combine(projectRoot, "Api_gen");
        CleanDir(apiDir);

        // 타깃별 방출 범위. context.Models 는 해상도 범위(전 sources)이며 두 축은 별개다 —
        // 여기서 좁혀야 다른 타깃의 산출물이 영향받지 않는다.
        var models = _options.SurfaceFilter.Apply(context.Models).ToList();

        var rendered = ApiRegistrationRenderer.Render(
            models,
            _options.Namespace,
            _options.EntitiesNamespace);

        File.WriteAllText(Path.Combine(apiDir, "ApiRegistration_gen.cs"), rendered);

        // 권한 키는 opt-in 이다. 형식을 모르는 채로 내면 규약이 다른 소비자에게 틀린 상수가
        // 가므로, 형식을 준 소비자에게만 방출한다.
        if (!string.IsNullOrWhiteSpace(_options.PermissionKeyTemplate))
        {
            File.WriteAllText(
                Path.Combine(apiDir, "Permissions_gen.cs"),
                PermissionsRenderer.Render(models, _options.Namespace, _options.PermissionKeyTemplate));
        }

        var controllers = ODataControllerRenderer.Render(
            models,
            _options.Namespace,
            _options.EntitiesNamespace);

        File.WriteAllText(Path.Combine(apiDir, "Controllers_gen.cs"), controllers);
    }

    private string ResolveProjectRoot(string workingDirectory)
        => ConfiguredPathResolver.Resolve(workingDirectory, _options.ProjectPath, "projectPath");

    private static void CleanDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.cs"))
                File.Delete(f);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }
    }
}

public sealed class ApiRegistrationGeneratorOptions
{
    public required string ProjectPath { get; init; }
    public required string Namespace { get; init; }

    /// <summary>
    /// Namespace where generated entity types (Foo, FooExt) live. When different
    /// from <see cref="Namespace"/>, the renderer emits a <c>using</c> for it so
    /// the registration class can reference entities by short name.
    /// </summary>
    public string? EntitiesNamespace { get; init; }

    /// <summary>
    /// 이 타깃의 엔티티 부분집합 필터 (<c>includeEntities</c>/<c>excludeEntities</c>).
    /// 기본값은 전량 통과 — 미지정 소비자는 현행 동작 그대로다.
    /// </summary>
    public EntitySurfaceFilter SurfaceFilter { get; init; } = EntitySurfaceFilter.PassAll;

    /// <summary>
    /// 권한 키 형식(<c>permissionKeyTemplate</c>). 지정하면 <c>Permissions_gen.cs</c> 를 함께
    /// 방출하고, 생략하면(기본) 아무것도 내지 않는다 — 미지정 소비자는 현행 동작 그대로다.
    /// </summary>
    /// <remarks>
    /// 값이 «입력»인 것이 요점이다. 키 형식은 소비앱의 인가 규약이지 이 생성기가 아는 사실이
    /// 아니라, 여기서 발명하면 규약이 다른 소비자에게는 쓸모없는 것이 아니라 **틀린** 상수가 된다.
    /// </remarks>
    public string? PermissionKeyTemplate { get; init; }
}
