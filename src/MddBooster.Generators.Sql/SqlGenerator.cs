using M3L.Native;

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
        var sqlProjPath = FindSqlProj(projectRoot);
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
            var sql = TableRenderer.Render(model, _options.Schema, enumLookup);
            var fileName = $"{model.Name}.sql";
            File.WriteAllText(Path.Combine(tablesGenDir, fileName), sql);
            tableFileNames.Add(fileName);
        }

        // 2. Views (conditional — only models with derived fields)
        // First pass: plan all models and collect which ones have ext views,
        // so rollup subqueries can reference {Name}ExtView when the target has computed fields.
        var allPlans = context.Models.Select(m => planner.Plan(m)).ToList();
        var extViewModels = new HashSet<string>(
            allPlans.Where(p => p.NeedsExtView).Select(p => p.Model.Name));

        var viewFileNames = new List<string>();
        foreach (var plan in allPlans)
        {
            if (!plan.NeedsAnyView) continue;

            if (plan.NeedsFullView)
            {
                var sql = FullViewRenderer.Render(plan, _options.Schema);
                var fileName = $"{plan.Model.Name}FullView.sql";
                File.WriteAllText(Path.Combine(viewsGenDir, fileName), sql);
                viewFileNames.Add(fileName);
            }
            if (plan.NeedsExtView)
            {
                var sql = ExtViewRenderer.Render(plan, _options.Schema, extViewModels);
                var fileName = $"{plan.Model.Name}ExtView.sql";
                File.WriteAllText(Path.Combine(viewsGenDir, fileName), sql);
                viewFileNames.Add(fileName);
            }
        }

        // 3. .sqlproj 패치 — tables + views
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
    {
        if (Path.IsPathRooted(_options.ProjectPath))
        {
            return Path.GetFullPath(_options.ProjectPath);
        }
        return Path.GetFullPath(Path.Combine(workingDirectory, _options.ProjectPath));
    }

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
