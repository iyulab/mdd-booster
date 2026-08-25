using M3L.Native;
using MddBooster.Core.Naming;

namespace MddBooster.Generators.Sql;

public sealed class SqlGenerator : IArtifactGenerator
{
    private readonly SqlGeneratorOptions _options;

    public SqlGenerator(SqlGeneratorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "sql";

    public void Generate(GeneratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var projectRoot = ResolveProjectRoot(context.WorkingDirectory);
        var tablesGenDir = Path.Combine(projectRoot, "dbo", "Tables_gen");
        var viewsGenDir = Path.Combine(projectRoot, "dbo", "Views_gen");

        CleanSqlDir(tablesGenDir);
        CleanSqlDir(viewsGenDir);

        var enumLookup = context.Enums.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var planner = new ViewPlanner();

        // 1. Tables
        var tableFileNames = new List<string>();
        foreach (var model in context.Models)
        {
            var sql = TableRenderer.Render(model, _options.Schema, enumLookup, _options.EmitEnumCheckConstraints, _options.EmitForeignKeyIndexes);
            var fileName = $"{model.Name}.sql";
            File.WriteAllText(Path.Combine(tablesGenDir, fileName), sql);
            tableFileNames.Add(fileName);
        }

        // 2. Views
        // First pass: plan all models and collect each FullView model's own derived
        // (Lookup/Rollup/Computed) column names, so a Lookup/Rollup elsewhere can tell
        // whether the specific column it reads needs {Name}FullView or the base table
        // suffices — redirecting on "target merely has a FullView" instead of "the exact
        // column requested is derived" can chain two models into a cycle (SQL72009).
        var allPlans = context.Models.Select(m => planner.Plan(m)).ToList();
        var derivedFieldsByModel = allPlans
            .Where(p => p.NeedsFullView)
            .ToDictionary(
                p => p.Model.Name,
                p => (IReadOnlySet<string>)new HashSet<string>(
                    p.Lookups.Concat(p.Rollups).Concat(p.Computeds).Select(f => NameCasing.ToPascalCase(f.Name))),
                StringComparer.Ordinal);

        // Fail the build, not the deployment: two models can each redirect a JOIN/subquery to
        // the other's FullView for independently valid reasons and still add up to a cycle
        // neither one's own render step can see on its own (docket #101).
        var cycle = FullViewCycleDetector.Detect(allPlans, derivedFieldsByModel);
        if (cycle != null)
        {
            throw new InvalidOperationException(
                $"Circular FullView dependency detected: {string.Join(" -> ", cycle)}. " +
                "SQL Server cannot deploy views that reference each other (SQL72009). This happens " +
                "when a chained Lookup passes through a derived (Lookup/Rollup/Computed) column on a " +
                "model that also Rollups an aggregate sourced from a derived column back on the " +
                "originating model. Break the cycle by pointing the chained Lookup at a raw base " +
                "column, or by sourcing the Rollup's aggregated field from a raw column instead.");
        }

        var viewFileNames = new List<string>();

        foreach (var plan in allPlans)
        {
            if (!plan.NeedsAnyView) continue;

            if (plan.NeedsUdView)
            {
                var sql = UdViewRenderer.Render(plan.Model, _options.Schema);
                var fileName = $"{plan.Model.Name}UdView.sql";
                File.WriteAllText(Path.Combine(viewsGenDir, fileName), sql);
                viewFileNames.Add(fileName);
            }

            if (plan.NeedsFullView)
            {
                var sql = FullViewRenderer.Render(plan, _options.Schema, derivedFieldsByModel);
                var fileName = $"{plan.Model.Name}FullView.sql";
                File.WriteAllText(Path.Combine(viewsGenDir, fileName), sql);
                viewFileNames.Add(fileName);
            }
        }

        // 3. Post-deployment refresh script (Scripts_gen)
        // Scans Views_gen/ and Views/ to emit sp_refreshview for every view in dependency order.
        if (_options.EmitRefreshScript)
        {
            var scriptsGenDir = Path.Combine(projectRoot, "dbo", "Scripts_gen");
            if (!Directory.Exists(scriptsGenDir))
                Directory.CreateDirectory(scriptsGenDir);

            var viewsDir = Path.Combine(projectRoot, "dbo", "Views");
            var refreshScript = PostDeploymentScriptRenderer.Render(viewsGenDir, viewsDir);
            File.WriteAllText(
                Path.Combine(scriptsGenDir, "Script.PostDeployment.RefreshViews.sql"),
                refreshScript);
        }

        // 4. .sqlproj patch — tables + views + scripts_gen
        // When EmitSqlProj is false the consumer drives schema via a desired-state tool
        // (e.g. Schemorph) and the .sqlproj is being retired, so we neither locate nor patch it.
        if (_options.EmitSqlProj)
        {
            var sqlProjPath = FindSqlProj(projectRoot);
            SqlProjPatcher.Patch(
                sqlProjPath,
                generatedFolderRelative: Path.Combine("dbo", "Tables_gen"),
                generatedFileNames: tableFileNames);
            if (viewFileNames.Count > 0)
            {
                SqlProjPatcher.Patch(
                    sqlProjPath,
                    generatedFolderRelative: Path.Combine("dbo", "Views_gen"),
                    generatedFileNames: viewFileNames);
            }
            // The Scripts_gen reference is only valid when the RefreshViews.sql was actually
            // emitted; otherwise it would dangle in the .sqlproj.
            if (_options.EmitRefreshScript)
            {
                SqlProjPatcher.Patch(
                    sqlProjPath,
                    generatedFolderRelative: Path.Combine("dbo", "Scripts_gen"),
                    generatedFileNames: ["Script.PostDeployment.RefreshViews.sql"],
                    itemType: "None");
            }
        }
    }

    private static void CleanSqlDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.sql"))
                File.Delete(file);
        }
        else
        {
            Directory.CreateDirectory(dir);
        }
    }

    private string ResolveProjectRoot(string workingDirectory)
        => ConfiguredPathResolver.Resolve(workingDirectory, _options.ProjectPath, "projectPath");

    private static string FindSqlProj(string projectRoot)
    {
        var candidates = Directory.GetFiles(projectRoot, "*.sqlproj", SearchOption.TopDirectoryOnly);
        if (candidates.Length == 0)
        {
            throw new FileNotFoundException($"'{projectRoot}' 폴더에서 .sqlproj을 찾을 수 없습니다.");
        }
        if (candidates.Length > 1)
        {
            throw new InvalidOperationException($"'{projectRoot}' 폴더에 .sqlproj이 여러 개 있습니다: {string.Join(", ", candidates)}");
        }
        return candidates[0];
    }
}
