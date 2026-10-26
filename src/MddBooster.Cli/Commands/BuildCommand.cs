using MddBooster.Core.Generation;
using MddBooster.Core.Naming;
using MddBooster.Cli.Config;
using MddBooster.Generators.Api;
using MddBooster.Generators.TypeScript;

namespace MddBooster.Cli.Commands;

public sealed class BuildCommand
{
    public int Run(string configDirectory)
    {
        configDirectory = Path.GetFullPath(configDirectory);
        if (!Directory.Exists(configDirectory))
        {
            Console.Error.WriteLine($"설정 디렉터리를 찾을 수 없습니다: {configDirectory}");
            return 2;
        }

        var cfgPath = Path.Combine(configDirectory, "mdd.json");
        MddJsonConfig cfg;
        try
        {
            cfg = ConfigLoader.Load(cfgPath);
        }
        catch (ConfigException ex)
        {
            Console.Error.WriteLine($"[config] {ex.Message}");
            return 4;
        }

        // 경고 정책은 진단이 하나라도 찍히기 «전»에 성립해야 한다 — 잘못 적힌 코드는 조용히 발화하지
        // 않는 규칙이 되므로 다른 설정 오류와 같은 종료 코드(4)로 먼저 세운다.
        var warningPolicy = WarningPolicy.From(cfg, out var policyViolations);
        if (policyViolations.Count > 0)
        {
            Console.Error.WriteLine($"[config] 설정 오류 {policyViolations.Count}건:");
            foreach (var v in policyViolations)
                Console.Error.WriteLine("  " + v);
            return 4;
        }
        var promotedWarnings = 0;
        void ReportWarning(string channel, string code, string text)
        {
            switch (warningPolicy.For(code))
            {
                case WarningPolicy.Disposition.Suppress:
                    return;
                case WarningPolicy.Disposition.Error:
                    promotedWarnings++;
                    Console.Error.WriteLine($"[{channel}] 에러(경고 승격) {text}");
                    return;
                default:
                    Console.Error.WriteLine($"[{channel}] 경고 {text}");
                    return;
            }
        }

        // 설정 자신의 경고가 먼저다 — 모르는 키가 곧 «비어 있는 sources» 의 원인일 수 있고, 그 경우
        // 뒤따르는 오류보다 이 한 줄이 원인을 말한다. 코드가 있어 treatWarningsAsErrors 로 CI 에서 세울 수 있다.
        foreach (var w in ConfigLoader.UnknownKeyWarnings(cfg))
            ReportWarning("config", "MDD024", $"[MDD024] {w}");

        // 소스 없는 설정은 설정 오류다 — 로더 깊숙이에서 예외로 새어 나가던 것을 다른 설정 오류와
        // 같은 자리·같은 종료 코드로. 바로 위 MDD024 가 있다면 그것이 원인을 말한다.
        if (cfg.Sources.Count == 0)
        {
            Console.Error.WriteLine("[config] mdd.json 에 sources 가 없습니다 — M3L 파일 경로 목록을 \"sources\": [\"./tables.m3l.md\"] 처럼 적으십시오.");
            return 4;
        }

        // 1. M3L 소스 로드 — 전체 sources를 하나의 resolve 단위로 병합 파싱한다.
        // 파일별 독립 파싱은 cross-file 상속·인터페이스 참조를 E007로 오탐한다 (스펙 §2.1 Rule 3).
        var loader = new M3lLoader();
        var sourcePaths = cfg.Sources
            .Select(srcRel => Path.GetFullPath(Path.Combine(configDirectory, srcRel)))
            .ToList();
        // 없는 소스는 설정 오류다 — 가리킨 것은 mdd.json 이다. 한 번에 전부 말한다: 하나씩 고치며
        // 다시 돌리게 하지 않는다.
        var missingSources = cfg.Sources
            .Where(rel => !File.Exists(Path.GetFullPath(Path.Combine(configDirectory, rel))))
            .ToList();
        if (missingSources.Count > 0)
        {
            Console.Error.WriteLine($"[config] sources 의 파일을 찾을 수 없습니다 ({missingSources.Count}건 — 경로는 mdd.json 이 있는 디렉터리 기준):");
            foreach (var rel in missingSources)
                Console.Error.WriteLine($"  {rel} → {Path.GetFullPath(Path.Combine(configDirectory, rel))}");
            return 4;
        }

        // 참조 원문 — 함께 읽어 검증하되 어느 타깃도 내지 않는다. 설정이 가리킨 파일이 없거나, 생성할 원문과
        // 같은 파일이거나, 네임스페이스가 없으면 설정 오류다(조용히 한쪽으로 접지 않는다).
        var referenceProblems = new List<string>();
        var namespaceOfReference = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var referenceEntries = cfg.References ?? [];
        for (var i = 0; i < referenceEntries.Count; i++)
        {
            var entry = referenceEntries[i];
            if (string.IsNullOrWhiteSpace(entry.Namespace)) referenceProblems.Add($"references[{i}]: namespace 가 없습니다.");
            if (entry.Sources.Count == 0) referenceProblems.Add($"references[{i}]: sources 가 비어 있습니다.");
            foreach (var rel in entry.Sources)
            {
                var abs = Path.GetFullPath(Path.Combine(configDirectory, rel));
                if (!File.Exists(abs)) referenceProblems.Add($"references[{i}]: {rel} → {abs} 가 없습니다.");
                else if (sourcePaths.Contains(abs, StringComparer.OrdinalIgnoreCase)) referenceProblems.Add($"references[{i}]: {rel} 는 sources 에도 있습니다 — 한 파일은 생성하거나(sources) 참조하거나(references) 둘 중 하나입니다.");
                else if (!namespaceOfReference.TryAdd(abs, entry.Namespace)) referenceProblems.Add($"references[{i}]: {rel} 가 두 번 참조됩니다.");
            }
        }
        if (referenceProblems.Count > 0)
        {
            Console.Error.WriteLine("[config] references:");
            foreach (var p in referenceProblems) Console.Error.WriteLine($"  {p}");
            return 4;
        }
        var references = new ReferenceSet(namespaceOfReference);
        foreach (var refAbs in namespaceOfReference.Keys) sourcePaths.Add(refAbs);

        foreach (var srcAbs in sourcePaths)
        {
            Console.WriteLine(references.ContainsSource(srcAbs) ? $"[m3l] 로딩(참조 — 생성 안 함): {srcAbs}" : $"[m3l] 로딩: {srcAbs}");
        }

        M3L.Native.M3lAst mergedAst;
        try
        {
            mergedAst = loader.LoadFiles(sourcePaths);
        }
        catch (M3lLoadException ex) when (ex.Diagnostics.Count > 0)
        {
            // 파서 에러는 모델 오류다 — 검증기 에러와 같은 모양·같은 종료 코드(3). 한 줄로 이어 붙인
            // 예외 메시지가 아니라 진단마다 한 줄.
            Console.Error.WriteLine($"[m3l] 에러 {ex.Diagnostics.Count}건:");
            foreach (var d in ex.Diagnostics)
                Console.Error.WriteLine($"  {d}");
            return 3;
        }

        // 파서 경고 표면화 — 조용히 삼키지 않는다.
        // 검증기 결과도 파서가 낸 경고를 다시 담아 온다(M3L-W010 등). 같은 진단(코드·위치·문장)은
        // 두 채널 중 먼저 온 쪽에서 한 번만 보고한다 — 두 번 찍히면 승격 집계도 두 번 센다.
        var reportedM3l = new HashSet<string>(StringComparer.Ordinal);
        // 참조 원문의 경고는 그 원문을 생성하는 빌드의 몫이다 — 여기서 보고하면(그리고 승격하면) 남이 고칠 원문이
        // 이 빌드를 세운다. 조용히 버리지 않는다: 건수를 한 줄로 알린다. 참조 원문의 «에러»는 그대로 빌드를 세운다
        // (검증할 수 없는 원문 위에 짓지 않는다).
        var referenceWarnings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in mergedAst.Warnings)
        {
            var line = $"[{w.Code}] {w.File}:{w.Line}:{w.Col} {w.Message}";
            if (references.ContainsSource(w.File)) referenceWarnings.Add(line);
            else if (reportedM3l.Add(line)) ReportWarning("m3l", w.Code, line);
        }

        // 검증기 진단 표면화 — 파서와 «다른 층»이다. 등록된 `::attribute` 의 오용
        // (M3L-W005~W008) 같은 규칙은 검증기에만 있어서, 파서 경고만 찍던 동안 mdd 소비자는
        // `::attribute` 를 등록해도 오용을 볼 방법이 없었다. 같은 파일 집합을 한 단위로 넘긴다
        // — 다른 파일에 선언된 레지스트리를 그 파일 혼자서는 알 수 없다.
        var m3lDiagnostics = loader.ValidateFiles(sourcePaths);
        foreach (var w in m3lDiagnostics.Warnings)
        {
            var line = $"[{w.Code}] {w.File}:{w.Line}:{w.Col} {w.Message}";
            if (references.ContainsSource(w.File)) referenceWarnings.Add(line);
            else if (reportedM3l.Add(line)) ReportWarning("m3l", w.Code, line);
        }
        // 검증기 «에러»는 빌드를 세운다 — 의미 분석 에러와 같은 종료 코드(3). 모델이 언어
        // 규칙을 어긴 채로 생성을 진행하면, 그 위반(예: 어디에도 정의되지 않은 타입 이름)이
        // 생성물 어딘가에서 조용히 다른 무엇으로 바뀌어 나온다.
        if (m3lDiagnostics.Errors.Count > 0)
        {
            Console.Error.WriteLine($"[m3l] 에러 {m3lDiagnostics.Errors.Count}건:");
            foreach (var e in m3lDiagnostics.Errors)
                Console.Error.WriteLine($"  [{e.Code}] {e.File}:{e.Line}:{e.Col} {e.Message}");
            return 3;
        }

        var allUnconsumed = new List<string>(AstAccounting.ListUnconsumed(mergedAst));

        // MDD053 — 한 빌드에서 같은 짧은 이름의 선언 둘. M3L 은 네임스페이스마다 이름 공간이 따로라 그것을 허용하지만(§2.1.3),
        // 이 생성기의 산출물 이름 — C# 형 · 테이블 · 데이터 셋 · TypeScript 형 — 은 짧은 이름에서 나와 둘이 한 이름이 된다.
        // 네임스페이스별 출력은 아직 없으므로 생성 전에 세운다 — 조용히 하나가 다른 하나를 덮게 두지 않는다.
        var shortNameClashes = mergedAst.Models.Select(m => (m.Name, Qualified: m.QualifiedName ?? m.Name, Kind: "model"))
            .Concat(mergedAst.Views.Select(v => (v.Name, Qualified: v.QualifiedName ?? v.Name, Kind: "view")))
            .Concat(mergedAst.Interfaces.Select(i => (i.Name, Qualified: i.QualifiedName ?? i.Name, Kind: "interface")))
            .Concat(mergedAst.Enums.Select(e => (e.Name, Qualified: e.QualifiedName ?? e.Name, Kind: "enum")))
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();
        if (shortNameClashes.Count > 0)
        {
            Console.Error.WriteLine($"[m3l] 에러 {shortNameClashes.Count}건:");
            foreach (var clash in shortNameClashes)
                Console.Error.WriteLine(
                    $"  [MDD053] '{clash.Key}' 를 {string.Join(", ", clash.Select(d => $"{d.Kind} {d.Qualified}"))} 가 함께 선언합니다 — "
                    + "생성되는 형 · 테이블 이름이 짧은 이름에서 나와 부딪히므로 한쪽의 이름을 바꾸십시오(네임스페이스마다 따로 내는 출력은 아직 없습니다).");
            return 3;
        }

        // owners — 소유자마다 이 빌드가 정한 성질. 모델에 없는 소유자를 적은 것은 설정 오류다(modules 의 소유자와 같다):
        // 오타 난 소유자를 조용히 무시하면 그 성질이 걸리지 않은 채 빌드가 지나간다.
        var declaredOwners = mergedAst.Models.Select(m => m.Prefix)
            .Concat(mergedAst.Enums.Select(e => e.Prefix))
            .Concat(mergedAst.Interfaces.Select(i => i.Prefix))
            .Concat(mergedAst.Views.Select(v => v.Prefix))
            .OfType<string>().Where(p => p.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var unknownOwners = (cfg.Owners ?? []).Keys.Where(o => !declaredOwners.Contains(o)).ToList();
        if (unknownOwners.Count > 0)
        {
            Console.Error.WriteLine(
                $"[config] owners 의 소유자 {string.Join(", ", unknownOwners.Select(o => $"'{o}'"))} 를 선언한 파일(# Prefix:)이 없습니다"
                + (declaredOwners.Count > 0 ? $" — 있는 소유자: {string.Join(", ", declaredOwners.Order(StringComparer.Ordinal))}." : " — 이 빌드에는 접두를 선언한 파일이 없습니다."));
            return 4;
        }
        var ownersWithoutNamePrefix = (cfg.Owners ?? [])
            .Where(o => !o.Value.NamePrefix).Select(o => o.Key)
            .ToHashSet(StringComparer.Ordinal);

        var allModels = new List<ResolvedModel>(new InterfaceResolver(mergedAst, ownersWithoutNamePrefix).ResolveAll());
        var allEnums = new List<M3L.Native.EnumNode>(mergedAst.Enums);

        Console.WriteLine($"[m3l] 모델 {allModels.Count}개, enum {allEnums.Count}개 로드됨: {string.Join(", ", allModels.Select(m => m.Name))}");

        foreach (var line in ExtensionCoverage.Describe(allModels))
            Console.WriteLine($"[m3l] 확장: {line}");

        // A declared one-to-one is reported the same way composition is: silently producing a unique
        // index and nothing else is how the author finds out from the output instead of the build.
        // Conflicting reverse names stop the build rather than letting one of them win invisibly.
        // In a layered build an inverse that would point up is not generated, so it collides with nothing — the same
        // layers the Model target generates with (the references, and a single Model target's modules).
        var layers = BuildLayers(cfg, allModels, allEnums, references);
        var oneToOne = OneToOneRelationships.Describe(allModels, layers is null ? null : layers.HasInverse);
        foreach (var line in OneToOneRelationships.Describe(oneToOne))
            Console.WriteLine($"[m3l] 1:1 {line}");
        var conflicts = oneToOne.Where(r => r.ReverseNameConflict is not null).ToList();
        if (conflicts.Count > 0)
        {
            Console.Error.WriteLine(
                $"[m3l] 에러 {conflicts.Count}건: 같은 대상에 대한 1:1 참조가 역방향 이름을 공유한다 — "
                + "한쪽의 필드명을 바꾸거나 대상을 나누십시오.");
            foreach (var r in conflicts)
                Console.Error.WriteLine(
                    $"  {r.DependentModel}.{r.ForeignKeyField} 와 {r.DependentModel}.{r.ReverseNameConflict} "
                    + $"가 {r.TargetModel}.{r.ReverseName} 을 함께 요구한다");
            return 3;
        }

        // 로더 회계 — 파싱은 되지만 생성 파이프라인이 소비하지 않는 요소를 가시화한다.
        // (standalone ::view / ::flow / extension, 그리고 언어가 정의하지 않은 ### 섹션은
        //  현재 어떤 타깃도 산출하지 않는다.)
        if (allUnconsumed.Count > 0)
        {
            Console.Error.WriteLine(
                $"[m3l] 경고: 소비되지 않는 요소 {allUnconsumed.Count}개 — 어떤 타깃도 산출물을 생성하지 않습니다: " +
                string.Join(", ", allUnconsumed));
        }

        // 1.5. 의미 분석 — Warning은 표면화만 하고 진행, Error는 빌드 중단.
        // standalone ::view 의 해석도 같은 층이다 — 모델을 가리키는 참조가 맞는지는 생성 전에 판정한다.
        var viewResolution = ViewResolver.Resolve(mergedAst.Views, allModels);
        // 이름 검사는 이 빌드가 실제로 이름을 쓰는 방언에서만 — Sql 타깃(스키마)과 Model 타깃(HasDatabaseName).
        var builtDialects = cfg.Targets
            .Where(t => t.Type is "Sql" or "Model")
            .Select(t => IsPostgresDialect(t) ? SqlDialect.PostgreSql : SqlDialect.SqlServer)
            .Distinct()
            .ToList();
        var diagnostics = new SemanticAnalyzer(allModels, allEnums, builtDialects).Analyze()
            .Concat(viewResolution.Diagnostics)
            .ToList();
        var warnings = diagnostics.Where(d => d.Severity == SemanticSeverity.Warning).ToList();
        var errors = diagnostics.Where(d => d.Severity == SemanticSeverity.Error).ToList();
        foreach (var w in warnings)
        {
            if (w.Location is { } at && references.ContainsSource(at.File)) referenceWarnings.Add(w.Format());
            else ReportWarning("semantic", w.Code, w.Format());
        }
        if (referenceWarnings.Count > 0)
            Console.WriteLine($"[m3l] 정보: 참조 원문(references)의 경고 {referenceWarnings.Count}건은 그 원문을 생성하는 빌드의 몫이라 보고하지 않습니다.");

        // standalone ::view 의 CREATE VIEW 는 Sql 타깃이 생성한다. 그 타깃이 없으면 뷰 자체는 이 빌드가 만들지 않는다 —
        // 로더 회계와 같은 자리에서 알린다(읽기 모델은 그래도 나오고, 그 이름의 뷰가 데이터베이스에 있다고 전제한다).
        if (viewResolution.Views.Count > 0)
        {
            var sqlTargets = cfg.Targets.Where(t => t.Type == "Sql").ToList();
            if (sqlTargets.Count == 0)
            {
                Console.Error.WriteLine(
                    $"[m3l] 경고: standalone 뷰 {viewResolution.Views.Count}개의 CREATE VIEW 를 생성하는 타깃이 없습니다(Sql 타깃이 생성한다): "
                    + string.Join(", ", viewResolution.Views.Select(v => v.Name)));
            }
            foreach (var view in viewResolution.Views.Where(v => !string.IsNullOrWhiteSpace(v.Source.SourceDef?.OrderBy)))
                Console.WriteLine($"[m3l] 정보: 뷰 '{view.Name}' 의 order_by 는 방출하지 않는다 — 뷰에는 순서가 없다(조회하는 쪽이 정렬한다).");

            // 읽기 모델(엔티티 · 읽기 전용 셋)은 행을 키로 가리킬 수 있는 뷰에만 낸다. 키 없는 뷰는 정당한 뷰다(집계에는
            // 행의 신원이 없다) — 경고가 아니라, 왜 그 뷰가 Model·Api 산출물에 없는지를 한 줄로 알린다.
            if (cfg.Targets.Any(t => t.Type is "Model" or "Api" or "TypeScript"))
            {
                foreach (var view in viewResolution.Views.Where(v =>
                             !ViewReadModels.IsInternal(v) && ViewReadModels.KeyOf(v) is null))
                    Console.WriteLine(
                        $"[m3l] 정보: 뷰 '{view.Name}' 은(는) 읽기 모델로 내지 않는다(SQL 만 생성) — 행을 가리킬 키가 없다. "
                        + "읽기 셋으로 서빙하려면 `- id: identifier @from(<관계>.id)` 열을 투영한다.");
            }
        }
        var readViews = viewResolution.Views
            .Where(ViewReadModels.IsReadModel)
            .ToList();
        // 읽기 모델이 내는 클래스·셋 이름이 모델이 내는 이름과 같으면 생성물이 한 이름을 두 번 선언한다 — 고르지 않고
        // 멈춘다(MDD038). 그 이름을 내는 타깃이 있을 때만 문제다.
        if (cfg.Targets.Any(t => t.Type is "Model" or "Api" or "TypeScript"))
        {
            foreach (var (view, reason) in ViewReadModels.NameClashes(
                         readViews, allModels.Select(m => m.Name), allEnums.Select(e => e.Name)))
            {
                errors.Add(new SemanticDiagnostic("MDD038",
                    $"뷰 '{view.Name}' 을(를) 읽기 모델로 낼 수 없다 — {reason}. 뷰의 이름을 바꾸거나, 읽기 모델로 내지 않으려면 "
                    + "`@internal` 을 붙인다.",
                    view.Source.Loc));
            }
            // 이름이 비어 있어도 생성 클래스가 그 모양을 가질 수 없으면 소비 앱의 컴파일이 멈춘다 — 여기서 멈춘다.
            foreach (var view in readViews)
            {
                foreach (var reason in ViewReadModels.ShapeProblems(view))
                {
                    errors.Add(new SemanticDiagnostic("MDD038",
                        $"뷰 '{view.Name}' 을(를) 읽기 모델로 낼 수 없다 — {reason}. 읽기 모델로 내지 않으려면 `@internal` 을 붙인다.",
                        view.Source.Loc));
                }
            }
        }
        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"[semantic] 에러 {errors.Count}건:");
            foreach (var d in errors)
                Console.Error.WriteLine("  " + d.Format());
            return 3;
        }
        // 승격된 경고는 m3l·의미 분석 두 층을 «다 모은 뒤» 한 번에 세운다 — 첫 층에서 멈추면
        // 뒤 층의 경고를 고쳐 가며 재실행하게 된다.
        if (promotedWarnings > 0)
        {
            Console.Error.WriteLine(
                $"[config] 경고 {promotedWarnings}건을 에러로 올렸습니다(treatWarningsAsErrors/warningsAsErrors) — 빌드를 중단합니다.");
            return 3;
        }

        // 참조 원문은 이 빌드가 내는 모든 것의 아래 층이다 — 참조된 선언이 생성할 선언을 가리키면, 그 원문을 낸 쪽은
        // 이 빌드 없이 컴파일된다. 모델 오류(3), 가리키는 선언과 대상을 둘 다 적는다.
        if (!references.IsEmpty)
        {
            var upward = LayerDirection.Of(references).Violations(allModels, allEnums, viewResolution.Views);
            if (upward.Count > 0)
            {
                Console.Error.WriteLine($"[m3l] 참조 원문(references)이 생성할 원문(sources)의 선언을 가리킵니다 ({upward.Count}건):");
                foreach (var line in upward) Console.Error.WriteLine($"  {line}");
                return 3;
            }
        }

        var context = new GeneratorContext
        {
            Models = allModels,
            WorkingDirectory = configDirectory,
            Enums = allEnums,
            Views = viewResolution.Views,
            ReadViews = readViews,
            References = references,
        };

        // 1.6. 설정 위반 수집 — 전부 모아 한 번에 실패시킨다(한 건씩 고쳐가며 재실행하게 만들지 않는다).
        var configViolations = new List<string>();

        // 같은 종류·같은 경로의 타깃이 둘이면 둘째가 첫째를 **조용히 덮어쓴다**.
        // 같은 종류라도 경로가 다르면 정상(복수 서버 시나리오) — 경로까지 같을 때만 오류다.
        foreach (var dup in cfg.Targets
            .GroupBy(t => (t.Type, Path: ResolveTargetPath(configDirectory, t)))
            .Where(g => g.Count() > 1))
        {
            configViolations.Add(
                $"{dup.Key.Type} 타깃이 같은 경로({dup.Key.Path})에 {dup.Count()}개 있습니다 "
                + "— 나중 것이 앞선 것의 산출물을 덮어씁니다. 경로를 분리하거나 중복을 제거하세요.");
        }

        // 폼 디렉터리는 한 TypeScript 타깃만 소유한다 — 각 타깃은 자기가 이번에 내지 않은 생성 폼을
        // 지우므로(TypeScriptGenerator.RemoveStaleForms), 두 타깃이 한 디렉터리를 나누면 서로의 폼을
        // 지운다. 타입 출력 경로가 같은 경우는 위 검사가 이미 거른다.
        foreach (var shared in cfg.Targets
            .Where(t => t.Type == "TypeScript" && !string.IsNullOrWhiteSpace(t.FormsOutputPath))
            .GroupBy(t => Path.GetFullPath(Path.IsPathRooted(t.FormsOutputPath!)
                ? t.FormsOutputPath!
                : Path.Combine(configDirectory, t.FormsOutputPath!)))
            .Where(g => g.Count() > 1))
        {
            configViolations.Add(
                $"TypeScript 타깃 {shared.Count()}개가 같은 formsOutputPath({shared.Key})를 씁니다 "
                + "— 각 타깃은 이번 빌드가 내지 않은 생성 폼을 지우므로 서로의 폼을 지웁니다. "
                + "폼 디렉터리를 타깃마다 분리하세요.");
        }

        // Api 타깃이 entity 타입을 참조할 수 있도록 entity namespace를 결정한다.
        // 과거에는 `FirstOrDefault(Model)?.Namespace` 였다 — Model 타깃이 둘이면 둘째의 namespace가
        // **조용히 무시**되고 Api 타깃이 잘못된 `using`을 방출해 소비자 빌드가 깨졌다.
        // 이제: Api 타깃의 명시 `entitiesNamespace`가 최우선, 없으면 Model 타깃이 유일할 때만 추론,
        // 후보가 둘 이상이면 추론하지 않고 **오류로 명시를 요구**한다.
        var modelNamespaces = cfg.Targets
            .Where(t => t.Type == "Model" && !string.IsNullOrWhiteSpace(t.Namespace))
            .Select(t => t.Namespace!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var api in cfg.Targets.Where(t => t.Type == "Api"
                                                  && string.IsNullOrWhiteSpace(t.EntitiesNamespace)))
        {
            if (modelNamespaces.Count > 1)
            {
                configViolations.Add(
                    $"Api 타깃({TargetPathOf(api)}): Model 타깃의 namespace 후보가 {modelNamespaces.Count}개입니다 "
                    + $"({string.Join(", ", modelNamespaces)}) — 어느 것을 참조할지 추론하지 않습니다. "
                    + "이 Api 타깃에 entitiesNamespace 를 명시하세요.");
            }
        }

        // 방언 정합 검사 — Sql 타깃이 snake DB를 만드는데 Model 타깃이 기본 매핑이면
        // (또는 그 역이면) 런타임 DB 오류가 되어서야 드러난다. 빌드 시점 경고로 표면화.
        var sqlPg = cfg.Targets.Any(t => t.Type == "Sql" && IsPostgresDialect(t));
        foreach (var modelTarget in cfg.Targets.Where(t => t.Type == "Model"))
        {
            if (sqlPg != IsPostgresDialect(modelTarget)
                && cfg.Targets.Any(t => t.Type == "Sql"))
            {
                Console.Error.WriteLine(
                    "[config] 경고: Sql 타깃과 Model 타깃의 dialect가 다릅니다 — 생성 DDL과 " +
                    "EF 매핑이 서로 다른 네이밍을 전제하게 됩니다. 두 타깃에 같은 dialect를 지정하세요.");
            }
        }

        // 1.7. 타깃별 엔티티 부분집합 검증 — 조용한 드롭·조용한 무시 금지:
        // 오타로 표면이 텅 비는 것이 가장 나쁜 실패다.
        var filters = new Dictionary<MddJsonTarget, EntitySurfaceFilter>();
        var formsFilters = new Dictionary<MddJsonTarget, EntitySurfaceFilter>();
        foreach (var target in cfg.Targets)
        {
            var label = $"{target.Type} 타깃({TargetPathOf(target)})";
            // Model 타깃 전용 — 엔티티 어셈블리를 나누는 것은 엔티티를 내는 타깃뿐이다. 다른 타깃에서 조용히 무시하지 않는다.
            if (target.Modules is not null && target.Type != "Model")
                configViolations.Add($"{label}: modules 는 Model 타깃에만 지정할 수 있습니다.");
            var hasFilter = target.IncludeEntities?.Count > 0 || target.ExcludeEntities?.Count > 0
                || target.IncludeOwners?.Count > 0 || target.ExcludeOwners?.Count > 0;

            // 표면 타깃 전용 — Sql·Model 에 걸면 FK/상속 무결성이 깨진다. 조용히 무시하지 않는다.
            if (hasFilter && target.Type is not ("Api" or "TypeScript"))
            {
                configViolations.Add(
                    $"{label}: includeEntities/excludeEntities/includeOwners/excludeOwners 는 표면 타깃(Api·TypeScript)에만 "
                    + "지정할 수 있습니다 — Sql·Model 을 부분집합으로 만들면 FK/상속 무결성이 깨집니다.");
                continue;
            }

            filters[target] = EntitySurfaceFilter.Validate(
                target.IncludeEntities, target.ExcludeEntities, target.IncludeOwners, target.ExcludeOwners,
                allModels, label, out var violations, readViews);
            configViolations.AddRange(violations);

            if (!string.IsNullOrWhiteSpace(target.SharedTypesImport))
            {
                if (target.Type != "TypeScript")
                    configViolations.Add($"{label}: sharedTypesImport 는 TypeScript 타깃에만 지정할 수 있습니다.");
                else if (!(target.IncludeOwners?.Count > 0 || target.ExcludeOwners?.Count > 0))
                    configViolations.Add(
                        $"{label}: sharedTypesImport 는 includeOwners/excludeOwners 가 있는 타깃에만 뜻이 있습니다 "
                        + "— 무엇을 다른 소유자의 타입으로 가져올지를 소유자 축이 정합니다.");
            }

            // 1.7a. 권한 키 형식 — 자리표시자 오타는 어떤 정책과도 매칭 안 되는 리터럴 키를
            // 조용히 방출한다. 통과시키지 않는다.
            if (!string.IsNullOrWhiteSpace(target.PermissionKeyTemplate))
            {
                if (target.Type != "Api")
                {
                    configViolations.Add(
                        $"{label}: permissionKeyTemplate 는 Api 타깃에만 지정할 수 있습니다 "
                        + "— 권한 상수를 방출하는 타깃이 그것뿐입니다.");
                }
                else
                {
                    foreach (var bad in PermissionKeyTemplate.Validate(target.PermissionKeyTemplate))
                    {
                        configViolations.Add(
                            $"{label}: permissionKeyTemplate 의 '{{{bad}}}' 은(는) 알 수 없는 자리표시자입니다 "
                            + $"— 쓸 수 있는 것: {string.Join(", ", PermissionKeyTemplate.Placeholders.Select(p => "{" + p + "}"))}.");
                    }
                }
            }

            // 1.7a'. 방출 표면 — 빈 목록·오타가 «아무것도 등록하지 않는» 또는 «한 표면이 조용히 빠진»
            // 산출을 만들게 두지 않는다.
            if (target.Surfaces is not null)
            {
                if (target.Type != "Api")
                {
                    configViolations.Add(
                        $"{label}: surfaces 는 Api 타깃에만 지정할 수 있습니다 — 런타임 표면을 등록하는 타깃이 그것뿐입니다.");
                }
                else
                {
                    foreach (var problem in MddBooster.Generators.Api.ApiSurfaceNames.Validate(target.Surfaces))
                        configViolations.Add($"{label}: surfaces 가 {problem}");
                }
            }

            // 1.7b. 폼이 임포트할 «이 생성기가 쓰지 않는» 모듈 — 소비자가 정한다. 기본값은 두지 않는다:
            // 남의 디렉터리 배치나 특정 패키지에 대한 추측이 되고, 추측이 틀리면 tsc 가 처음 말한다.
            configViolations.AddRange(FormImportViolations(target, label));

            // 1.7c. 폼 전용 필터 — 같은 술어를 재사용하되 적용 범위가 좁다.
            var hasFormsFilter = target.FormsInclude?.Count > 0 || target.FormsExclude?.Count > 0;
            if (!hasFormsFilter) continue;

            if (target.Type != "TypeScript")
            {
                configViolations.Add(
                    $"{label}: formsInclude/formsExclude 는 TypeScript 타깃에만 지정할 수 있습니다 "
                    + "— 폼을 방출하는 타깃이 그것뿐입니다.");
                continue;
            }

            if (target.FormsOutputPath is null)
            {
                configViolations.Add(
                    $"{label}: formsInclude/formsExclude 를 지정했지만 formsOutputPath 가 없습니다 "
                    + "— 폼을 아예 내지 않는 설정이라 이 필터가 아무것도 하지 않습니다. "
                    + "폼을 내려면 formsOutputPath 를, 안 내려면 이 필터를 지우세요.");
                continue;
            }

            var formsFilter = EntitySurfaceFilter.Validate(
                target.FormsInclude, target.FormsExclude, allModels,
                $"{label} 폼", out var formsViolations);
            configViolations.AddRange(formsViolations);

            // 폼 집합 ⊆ 타깃 집합. 생성 폼은 자기 타입을 entities_gen.ts 에서 임포트하므로,
            // 타깃이 내지 않는 엔티티의 폼은 임포트가 깨진 파일이 된다. 조용히 드롭하면
            // 소비자는 자기가 적은 이름이 왜 폼으로 안 나오는지 알 수 없다 — 말하게 한다.
            if (formsViolations.Count == 0 && target.FormsInclude is { Count: > 0 })
            {
                var inTargetSurface = filters[target].Apply(allModels)
                    .Select(m => m.Name)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var name in target.FormsInclude.Where(n => !inTargetSurface.Contains(n)))
                {
                    configViolations.Add(
                        $"{label}: formsInclude 의 '{name}' 은(는) 이 타깃의 엔티티 범위 밖입니다 "
                        + "(includeEntities/excludeEntities 가 이미 제외했습니다) — 폼은 자기 타입을 "
                        + "entities_gen.ts 에서 임포트하므로 타입 없이 폼만 낼 수 없습니다.");
                }
            }

            formsFilters[target] = formsFilter;
        }

        // 1.8. TypeScript 타깃의 EntitySetName 이 **어떤 Api 타깃도 등록하지 않는** 셋을 광고하는지.
        // `entity_names_gen.ts` 는 OData entity set 이름의 미러로 계약돼 있고 소비자가 이걸로 URL 을
        // 만든다. 타깃별 필터가 도입되면서 Api 와 TypeScript 의 범위가 어긋날 수 있게 됐는데,
        // 어긋나도 아무 신호가 없다 — @internal 을 목록에서 빼서 없앤 것과 같은 결함 계열이다.
        //
        // 판정은 **전 Api 타깃의 합집합**과 비교한다. 공유 UI 하나가 여러 서버를 담당하는 구성이
        // 정상이기 때문이다(어떤 셋이든 어느 한 서버가 등록하면 그 이름은 유효하다).
        // Api 타깃이 아예 없는 설정에서는 판정 근거가 없으므로 검사하지 않는다.
        // 오류가 아니라 경고다 — 서버가 다른 mdd.json 에 설정돼 있을 수 있다.
        var apiTargets = cfg.Targets.Where(t => t.Type == "Api").ToList();
        if (apiTargets.Count > 0 && configViolations.Count == 0)
        {
            var registered = apiTargets
                .SelectMany(t => (filters.TryGetValue(t, out var af) ? af : EntitySurfaceFilter.PassAll)
                    .Apply(allModels))
                .Where(m => !EntitySurface.IsInternal(m))
                .Select(m => m.Name)
                .ToHashSet(StringComparer.Ordinal);
            // 읽기 모델인 뷰도 같은 계약이다 — `ReadOnlySetName` 은 Api 타깃이 읽기 전용으로 등록하는 셋의 미러다.
            var registeredViews = apiTargets
                .SelectMany(t => readViews.Where(
                    (filters.TryGetValue(t, out var af) ? af : EntitySurfaceFilter.PassAll).Admits))
                .Select(v => v.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var ts in cfg.Targets.Where(t => t.Type == "TypeScript"))
            {
                var tsFilter = filters.TryGetValue(ts, out var tf) ? tf : EntitySurfaceFilter.PassAll;
                var viewExtras = readViews
                    .Where(v => tsFilter.Admits(v) && !registeredViews.Contains(v.Name))
                    .Select(v => v.Name)
                    .ToList();
                if (viewExtras.Count > 0)
                {
                    Console.Error.WriteLine(
                        $"[config] 경고: TypeScript 타깃({TargetPathOf(ts)})의 ReadOnlySetName 이 "
                        + $"어떤 Api 타깃도 등록하지 않는 뷰 {viewExtras.Count}개를 포함합니다 — "
                        + $"이 TypeScript 타깃에 같은 필터를 지정하세요: {string.Join(", ", viewExtras.Take(10))}"
                        + (viewExtras.Count > 10 ? $" … (+{viewExtras.Count - 10})" : ""));
                }

                var extras = tsFilter
                    .Apply(allModels)
                    .Where(m => !EntitySurface.IsInternal(m) && !registered.Contains(m.Name))
                    .Select(m => m.Name)
                    .ToList();

                if (extras.Count > 0)
                {
                    Console.Error.WriteLine(
                        $"[config] 경고: TypeScript 타깃({TargetPathOf(ts)})의 EntitySetName 이 "
                        + $"어떤 Api 타깃도 등록하지 않는 엔티티 {extras.Count}개를 포함합니다 — "
                        + $"소비자가 그 이름으로 OData URL 을 만들면 존재하지 않는 경로가 됩니다. "
                        + $"이 TypeScript 타깃에 같은 필터를 지정하세요: {string.Join(", ", extras.Take(10))}"
                        + (extras.Count > 10 ? $" … (+{extras.Count - 10})" : ""));
                }
            }
        }

        // 1.8b. 공유 키 선언(`::aspect`)은 aspect 셋을 등록하는 Api 타깃이 낸다 — Base 셋은 같은 빌드의 다른
        // Api 타깃이 등록할 수 있다(소유자로 가른 타깃에서 모듈의 aspect 가 베이스의 Base 에 붙는 모양). 그래서
        // 타깃마다 «다른 타깃이 OData 로 등록하는 모델»을 넘긴다. Base 를 어느 타깃도 등록하지 않으면 선언할
        // 곳이 없다 — 공유 키 거절이 꺼진다는 것을 조용히 두지 않는다(MDD033).
        var odataRegisteredBy = new Dictionary<MddJsonTarget, HashSet<string>>();
        if (configViolations.Count == 0)
        {
            foreach (var api in apiTargets.Where(t =>
                ApiSurfaceNames.Parse(t.Surfaces).HasFlag(ApiSurfaces.OData)))
            {
                odataRegisteredBy[api] = (filters.TryGetValue(api, out var af) ? af : EntitySurfaceFilter.PassAll)
                    .Apply(allModels)
                    .Where(m => !EntitySurface.IsInternal(m))
                    .Select(m => m.Name)
                    .ToHashSet(StringComparer.Ordinal);
            }
            var odataRegisteredAnywhere = odataRegisteredBy.Values.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);
            foreach (var aspect in allModels
                .Where(m => odataRegisteredAnywhere.Contains(m.Name))
                .Where(m => m.Source.Base is { Kind: "aspect" })
                .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var principal = allModels.FirstOrDefault(m => m.Name == aspect.Source.Base!.Model);
                if (principal is null || EntitySurface.IsInternal(principal)
                    || odataRegisteredAnywhere.Contains(principal.Name)) continue;

                ReportWarning("config", "MDD033", new SemanticDiagnostic("MDD033",
                    $"'{aspect.Name}' 은(는) '{principal.Name}' 의 aspect 인데, 어떤 Api 타깃도 '{principal.Name}' 을(를) OData 로 "
                    + "등록하지 않는다 — 공유 키 선언(DeclareSharedKey)을 낼 곳이 없어 그 셋의 공유 키 거절이 꺼진다. "
                    + $"'{principal.Name}' 을(를) 등록하는 Api 타깃을 두거나, aspect 도 함께 빼라.",
                    aspect.Source.Loc, SemanticSeverity.Warning).Format());
            }

            // 1.8c. `@searchable` 참조 키는 읽기 navigation 의 [Searchable] 이 된다 — 런타임 $search 는 그 navigation 을
            // 따라 대상 셋의 텍스트를 찾는데, 대상 셋이 등록돼 있지 않으면 조용히 건너뛴다. 의존 셋은 OData 로 서빙되는데
            // 대상은 같은 빌드의 어떤 Api 타깃도 등록하지 않으면, 그 선언은 아무것도 매칭하지 않는다(MDD035).
            var searchNavigated = ManyToOneNavigations.Describe(allModels, oneToOne);
            foreach (var model in allModels
                .Where(m => odataRegisteredAnywhere.Contains(m.Name))
                .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var navigated = SearchableReferences.ReadNavigatedKeys(model.Name, oneToOne, searchNavigated);
                foreach (var field in model.Fields.Where(f => f.Kind == M3L.Native.FieldKind.Stored
                    && SearchableReferences.IsSearchableReference(f) && navigated.Contains(f.Name)))
                {
                    var target = MddBooster.Core.Ast.FieldAttributes.FirstArg(field, "reference")!;
                    if (odataRegisteredAnywhere.Contains(target)) continue;

                    ReportWarning("config", "MDD035", new SemanticDiagnostic("MDD035",
                        $"'{model.Name}.{field.Name}' @searchable @reference({target}): 어떤 Api 타깃도 '{target}' 을(를) OData 로 "
                        + "등록하지 않는다 — 런타임 $search 는 등록되지 않은 대상의 navigation 을 건너뛰어, 이 선언은 아무것도 매칭하지 않는다. "
                        + $"'{target}' 을(를) 등록하는 Api 타깃을 두거나, 이 키의 @searchable 을 빼라.",
                        field.Loc, SemanticSeverity.Warning).Format());
                }
            }
        }

        // 1.8d. 모델이 선언한 개명(`@renamed_from`)은 Sql 타깃이 사전 스크립트로 낸다 — 그 자리(`preScriptsPath`)가 없는 Sql
        // 타깃은 개명을 실행하지 않는다. 그 타깃의 산출물을 반영하면 옛 이름은 제거, 새 이름은 빈 채로 생성으로 읽힌다(MDD051).
        var renameSteps = RenameSteps.Of(allModels);
        if (renameSteps.Count > 0)
        {
            foreach (var sql in cfg.Targets.Where(t => t.Type == "Sql" && t.PreScriptsPath is null))
            {
                var first = renameSteps[0];
                var model = allModels.First(m => m.Name == first.Model);
                ReportWarning("config", "MDD051", new SemanticDiagnostic("MDD051",
                    $"Sql 타깃({TargetPathOf(sql)})에 `preScriptsPath` 가 없어 `@renamed_from` 개명 {renameSteps.Count}건을 실행하지 않는다 "
                    + $"(첫째: {first.Model} {first.From} → {first.To}). 이 타깃의 산출물을 반영하면 옛 이름은 제거, 새 이름은 빈 채로 "
                    + "생성으로 읽힌다 — 사전 스크립트 디렉터리(`projectPath` 밖)를 지정하라.",
                    model.Source.Loc, SemanticSeverity.Warning).Format());
            }
        }

        if (configViolations.Count > 0)
        {
            Console.Error.WriteLine($"[config] 설정 오류 {configViolations.Count}건:");
            foreach (var v in configViolations)
                Console.Error.WriteLine("  " + v);
            return 4;
        }
        // 설정을 모델에 비춰 본 경고(MDD033 · MDD035 · MDD051)는 앞의 승격 검사 «뒤»에 나온다 — 여기서 다시 센다.
        // 이 검사가 없던 동안 그 셋은 승격돼도 «에러(경고 승격)»으로 찍히기만 하고 빌드는 계속됐다.
        if (promotedWarnings > 0)
        {
            Console.Error.WriteLine(
                $"[config] 경고 {promotedWarnings}건을 에러로 올렸습니다(treatWarningsAsErrors/warningsAsErrors) — 빌드를 중단합니다.");
            return 3;
        }

        // 1.9. 「의도적으로 구현하지 않은 것」을 생성물이 스스로 말하지 않는 자리 — 안내 레벨.
        //
        // 결함이 아니라 «설계»인데, 소비자 입장에서는 결함과 구분되지 않는다: 폼이 그냥 텍스트
        // 상자를 낸다. 실제로 한 소비자가 근거 절(README 「타입 → 컨트롤 매핑」, 실측 표까지 있다)을
        // 못 찾고 스스로 스파이크를 짰다. 문서를 더 쓰는 것은 답이 아니었다 — 그 자리에 이미 있었다.
        // 닿지 않은 것은 **문서가 아니라 시점**이라, 소비자가 반드시 보는 출력(빌드)에 건다.
        //
        // 경고가 아니라 «안내»인 이유: 고칠 것이 없다. 경고로 내면 소비자가 결함으로 읽고
        // 없애려 들며, 그러면 이 줄은 다시 소음이 된다. 문면에 「의도된 것」과 「근거 위치」가
        // 둘 다 들어가야 하는 이유도 같다.
        //
        // 🔴 그래서 stdout 이다. 이 CLI 는 스트림으로 층위를 나눈다 — 정보는 stdout(`[m3l] 로딩`·
        // `[ts] 완료`), 진단은 stderr(경고·오류). 접두사만으로 나누고 stderr 에 실으면
        // `LargeModelAcceptanceTests.Building_the_acceptance_fixture_reports_nothing_on_stderr`
        // 가 깨진다 — 그 게이트는 「깨끗한 모델은 stderr 가 비어 있다」를 재는 것이고, 안내를
        // 그쪽에 실으면 게이트를 «약화시켜» 맞춰야 한다. 약화시키는 대신 스트림을 맞췄다:
        // 이 줄은 빌드가 못 한 일이 아니라 한 일에 대한 설명이므로 애초에 진단이 아니다.
        //
        // 대상은 **조용한** 축 하나뿐이다. `timestamp`/`datetime` 은 `type="datetime"` 토큰을
        // 방출해 이미 말하고 있고, `money`/`percentage` 는 빌드가 실패한다. 남는 것은 `time` —
        // 네이티브 피커가 소수 초를 조용히 버리기 때문에 자유 텍스트로 두는 축이다
        // (근거: `TsFormRenderer` 의 remarks 실측 표). 새로 조용한 축이 생기면 여기 더한다.
        var tsTargets = cfg.Targets.Where(t => t.Type == "TypeScript").ToList();
        if (tsTargets.Count > 0)
        {
            var plainTextTimeFields = tsTargets
                .SelectMany(t => (filters.TryGetValue(t, out var tf) ? tf : EntitySurfaceFilter.PassAll)
                    .Apply(allModels))
                .SelectMany(m => m.Fields
                    .Where(f => string.Equals(f.Type, "time", StringComparison.OrdinalIgnoreCase))
                    .Select(f => $"{m.Name}.{f.Name}"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (plainTextTimeFields.Count > 0)
            {
                Console.WriteLine(
                    $"[typescript] 안내: 'time' 필드 {plainTextTimeFields.Count}개가 생성 폼에서 "
                    + "자유 텍스트 입력으로 나옵니다 — 결함이 아니라 의도된 것입니다. "
                    + "네이티브 시각 피커는 소수 초(SQL TIME(7))를 조용히 버려서, 일부 값만 "
                    + "저장되고 나머지는 사라집니다. 근거와 실측 표: README 「타입 → 컨트롤 매핑」. "
                    + $"대상: {string.Join(", ", plainTextTimeFields.Take(10))}"
                    + (plainTextTimeFields.Count > 10 ? $" … (+{plainTextTimeFields.Count - 10})" : ""));
            }
        }

        // 2. 타깃별 생성기 준비 — 그리고 «모든» 타깃의 검증을 어느 타깃이든 쓰기 전에.
        //
        // 타깃은 차례로 쓴다. 검증이 각 타깃의 Generate 안에만 있으면, 뒤 타깃이 모델을 거절할 때
        // 앞 타깃은 이미 새 모델로 다시 써진 뒤다 — 실패한 빌드가 «반쯤 갱신된» 트리를 남기고,
        // 생성물을 커밋하는 소비자에게는 멱등 검사(재생성 후 git diff)가 그 혼합을 변경으로 본다.
        var entityNamespaces = EntityNamespaceMap(cfg, context);
        var prepared = cfg.Targets.Select(target =>
        {
            var filter = filters.TryGetValue(target, out var f) ? f : EntitySurfaceFilter.PassAll;
            // 명시 > 유일 추론 > null. 후보가 둘 이상인 경우는 위에서 이미 오류로 걸렀다.
            var entitiesNamespace = target.EntitiesNamespace
                ?? (modelNamespaces.Count == 1 ? modelNamespaces[0] : null);
            var formsFilter = formsFilters.TryGetValue(target, out var ff) ? ff : EntitySurfaceFilter.PassAll;
            var registeredElsewhere = odataRegisteredBy
                .Where(kv => !ReferenceEquals(kv.Key, target))
                .SelectMany(kv => kv.Value)
                .ToHashSet(StringComparer.Ordinal);
            return (Target: target, Filter: filter, FormsFilter: formsFilter,
                Generator: ResolveGenerator(target, entitiesNamespace, filter, formsFilter, registeredElsewhere, entityNamespaces));
        }).ToList();

        // 거절은 그 종류의 종료 코드로 — 모델을 이 타깃이 표현할 수 없으면 의미 분석 에러와 같은 3, 타깃의 설정이나
        // 그 설정이 가리키는 파일이 틀렸으면 다른 설정 오류와 같은 4. 예상 밖 실패만 최상위 `error:` 로 간다.
        foreach (var p in prepared)
        {
            try
            {
                p.Generator.Validate(context);
            }
            catch (GeneratorRefusalException ex)
            {
                var configuration = ex.Kind == GeneratorRefusalKind.Configuration;
                Console.Error.WriteLine($"[{p.Generator.Name}] {(configuration ? "설정 오류" : "오류")} {ex.Message}");
                return configuration ? 4 : 3;
            }
        }

        // 3. 타깃별 생성기 실행
        //
        // 파일 시스템이 쓰기를 거절하면(읽기 전용·다른 프로그램이 연 파일·권한) 그 타깃과 원인, 그리고
        // «이 빌드가 이미 새로 쓴 타깃»을 말하고 멈춘다. 검증은 위에서 끝났으니 여기서의 실패는 모델이 아니라
        // 환경 쪽이다 — 그리고 앞 타깃은 이미 새 모델로 써졌으므로, 그 사실을 말하지 않으면 사용자는 섞인
        // 트리를 멀쩡한 것으로 읽는다.
        var written = new List<string>();
        foreach (var (target, filter, formsFilter, generator) in prepared)
        {
            var targetPath = TargetPathOf(target);
            Console.WriteLine($"[{generator.Name}] 생성 시작 (target: {targetPath})");
            // 커버리지 회계 — 화이트리스트는 정본에 새 엔티티가 들어와도 조용히 빠지므로
            // 무엇이 제외됐는지 매 빌드에서 보이게 한다.
            if (!filter.IsPassAll)
                Console.WriteLine($"[{generator.Name}] {filter.DescribeCoverage(allModels)}");
            // 폼 필터도 같은 성질을 갖는다 — 화이트리스트면 새 엔티티가 조용히 폼에서 빠진다.
            // 기준은 allModels 가 아니라 **타깃이 이미 좁힌 집합**이다(폼 ⊆ 타깃).
            if (!formsFilter.IsPassAll)
                Console.WriteLine($"[{generator.Name}] 폼 {formsFilter.DescribeCoverage(filter.Apply(allModels), "forms")}");
            try
            {
                generator.Generate(context);
            }
            catch (Exception ex) when (OutputWriteFailure.IsWriteFailure(ex))
            {
                Console.Error.WriteLine($"[{generator.Name}] 출력을 쓰지 못했습니다 (target: {targetPath}) — {OutputWriteFailure.Describe(ex)}");
                Console.Error.WriteLine(written.Count > 0
                    ? $"[{generator.Name}] 이미 새로 쓴 타깃: {string.Join(", ", written)} — 이 타깃도 일부는 써졌을 수 있습니다. 이 빌드의 출력은 섞여 있으니 원인을 고친 뒤 다시 빌드하십시오."
                    : $"[{generator.Name}] 이 타깃의 일부 파일은 써졌을 수 있습니다 — 원인을 고친 뒤 다시 빌드하십시오.");
                return 1;
            }
            written.Add($"{generator.Name}({targetPath})");
            Console.WriteLine($"[{generator.Name}] 완료");
        }

        Console.WriteLine("build 완료.");
        return 0;
    }

    /// <summary>로그·오류 메시지에 쓰는 타깃 경로 (TypeScript 는 outputPath, 그 외는 projectPath).</summary>
    private static string TargetPathOf(MddJsonTarget target)
        => !string.IsNullOrEmpty(target.OutputPath) ? target.OutputPath : target.ProjectPath;

    /// <summary>중복 타깃 판정용 정규화 경로. 상대 경로는 mdd.json 위치 기준으로 절대화한다.</summary>
    private static string ResolveTargetPath(string configDirectory, MddJsonTarget target)
    {
        var raw = TargetPathOf(target);
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(configDirectory, raw));
    }

    /// <summary>Model·Sql 타깃 공용 dialect 판정. 미지 값은 각 분기에서 오류.</summary>
    private static bool IsPostgresDialect(MddJsonTarget target)
        => target.Dialect?.ToLowerInvariant() is "postgres" or "postgresql" or "pg";

    /// <summary>
    /// Sql 타깃의 방언 분기. 기본(tsql)은 현행 SSDT/T-SQL 경로 그대로 — 기존 소비자 무영향.
    /// postgres는 SSDT 개념 노브(emitSqlProj/emitRefreshScript)와 양립하지 않으므로
    /// 명시 설정 시 조용히 무시하지 않고 오류로 알린다.
    /// </summary>
    private static IArtifactGenerator CreateSqlGenerator(MddJsonTarget target)
    {
        switch (target.Dialect?.ToLowerInvariant())
        {
            case null or "tsql":
                return new SqlGenerator(new SqlGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Schema = target.Schema ?? "dbo",
                    EmitSqlProj = target.EmitSqlProj ?? true,
                    EmitRefreshScript = target.EmitRefreshScript ?? true,
                    EmitEnumCheckConstraints = target.EmitEnumCheckConstraints ?? false,
                    EmitForeignKeyIndexes = target.EmitForeignKeyIndexes ?? true,
                    PreScriptsPath = target.PreScriptsPath,
                });

            case "postgres" or "postgresql" or "pg":
                if (target.EmitSqlProj == true)
                {
                    throw new InvalidOperationException(
                        "dialect 'postgres'는 emitSqlProj를 지원하지 않습니다 (.sqlproj는 SSDT/T-SQL 개념) — 옵션을 제거하세요.");
                }
                if (target.EmitRefreshScript == true)
                {
                    throw new InvalidOperationException(
                        "dialect 'postgres'는 emitRefreshScript를 지원하지 않습니다 (sp_refreshview는 T-SQL 개념) — 옵션을 제거하세요.");
                }
                return new MddBooster.Generators.Sql.Postgres.PostgresSqlGenerator(
                    new MddBooster.Generators.Sql.Postgres.PostgresSqlGeneratorOptions
                    {
                        ProjectPath = target.ProjectPath,
                        Schema = target.Schema ?? "public",
                        EmitEnumCheckConstraints = target.EmitEnumCheckConstraints ?? false,
                        EmitForeignKeyIndexes = target.EmitForeignKeyIndexes ?? true,
                        PreScriptsPath = target.PreScriptsPath,
                    });

            default:
                throw new NotSupportedException(
                    $"지원하지 않는 Sql dialect: '{target.Dialect}' (지원: tsql, postgres)");
        }
    }

    /// <summary>The three form-import keys, by the name a consumer writes in <c>mdd.json</c>.</summary>
    private static IEnumerable<(string Key, string? Value)> FormImportKeys(MddJsonTarget target) =>
    [
        ("formLayoutImport", target.FormLayoutImport),
        ("formControlsImport", target.FormControlsImport),
        ("formSelectOptionsImport", target.FormSelectOptionsImport),
    ];

    /// <summary>
    /// A target that emits forms names all three modules; a target that does not
    /// names none. Either way round, the build says which key is the problem.
    /// </summary>
    private static IEnumerable<string> FormImportViolations(MddJsonTarget target, string label)
    {
        var declared = FormImportKeys(target)
            .Where(k => !string.IsNullOrWhiteSpace(k.Value))
            .Select(k => k.Key)
            .ToList();
        var emitsForms = target.Type == "TypeScript" && !string.IsNullOrWhiteSpace(target.FormsOutputPath);

        if (!emitsForms)
        {
            if (declared.Count > 0)
            {
                yield return $"{label}: {string.Join(", ", declared)} 를 지정했지만 이 타깃은 폼을 내지 않습니다 "
                    + "— 폼은 TypeScript 타깃이 formsOutputPath 를 가질 때만 생성됩니다.";
            }
            yield break;
        }

        var missing = FormImportKeys(target)
            .Where(k => string.IsNullOrWhiteSpace(k.Value))
            .Select(k => k.Key)
            .ToList();
        if (missing.Count > 0)
        {
            yield return $"{label}: formsOutputPath 로 폼을 내려면 {string.Join(", ", missing)} 가 필요합니다 "
                + "— 생성 폼이 임포트하는 레이아웃·컨트롤·옵션 헬퍼는 이 생성기가 쓰지 않는 모듈이라, "
                + "어디서 가져올지는 소비자가 정합니다(README 「소비 프로젝트 계약 (TypeScript 타깃)」).";
        }
    }

    /// <summary>
    /// The form-import modules a target declared, or <c>null</c> when it emits no
    /// forms. Only called after <see cref="FormImportViolations"/> found none, so
    /// a forms-emitting target has all three.
    /// </summary>
    private static TsFormModuleImports? FormModulesFor(MddJsonTarget target) =>
        string.IsNullOrWhiteSpace(target.FormsOutputPath)
            ? null
            : new TsFormModuleImports
            {
                Layout = target.FormLayoutImport!,
                Controls = target.FormControlsImport!,
                SelectOptions = target.FormSelectOptionsImport!,
            };

    /// <summary>
    /// The layers of this build for the one-to-one inverses (<see cref="LayerDirection"/>): a single Model target's modules
    /// over the references, or the references alone — null when the build has neither. A module list that does not
    /// assign cleanly falls back to the references here; the Model target refuses it, with every reason, when it validates.
    /// </summary>
    private static LayerDirection? BuildLayers(
        MddJsonConfig cfg, IReadOnlyList<ResolvedModel> allModels, IReadOnlyList<M3L.Native.EnumNode> allEnums, ReferenceSet references)
    {
        var modular = cfg.Targets.Where(t => t.Type == "Model" && t.Modules is { Count: > 0 }).ToList();
        if (modular.Count == 1)
        {
            try
            {
                var modules = MddBooster.Generators.Model.ModelModules.Assign(
                    modular[0].Modules!.Select(m => new MddBooster.Generators.Model.ModelModule(m.Owners, m.ProjectPath, m.Namespace, m.DataInterface)).ToList(),
                    allModels.Where(m => !references.Contains(m)).ToList(),
                    allEnums.Where(e => !references.Contains(e)).ToList(),
                    []);
                return MddBooster.Generators.Model.ModuleDirection.For(modules, references);
            }
            catch (GeneratorRefusalException)
            {
                // Reported by the Model target's validation.
            }
        }
        return references.IsEmpty ? null : LayerDirection.Of(references);
    }

    /// <summary>
    /// The entity types (PascalCase model and view names) that do not live in the one entities namespace a surface
    /// target is given: a referenced build's types (their reference's namespace), and the types of a Model target split
    /// into modules (their module's). Modules count only when one Model target declares them — with several, which
    /// split a surface target names is not knowable, and the target's <c>entitiesNamespace</c> rule applies as before.
    /// </summary>
    private static IReadOnlyDictionary<string, string> EntityNamespaceMap(MddJsonConfig cfg, GeneratorContext context)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var model in context.Models)
            if (context.References.NamespaceOf(model) is { } ns) map[NameCasing.ToPascalCase(model.Name)] = ns;
        foreach (var view in context.ReadViews)
            if (context.References.NamespaceOf(view) is { } ns) map[NameCasing.ToPascalCase(view.Name)] = ns;

        var modular = cfg.Targets.Where(t => t.Type == "Model" && t.Modules is { Count: > 0 }).ToList();
        if (modular.Count != 1) return map;
        var moduleOfOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in modular[0].Modules!)
            foreach (var owner in module.Owners)
                moduleOfOwner.TryAdd(owner, module.Namespace);
        foreach (var model in context.GeneratedModels)
            if (moduleOfOwner.TryGetValue(EntitySurfaceFilter.OwnerOf(model), out var ns)) map[NameCasing.ToPascalCase(model.Name)] = ns;
        foreach (var view in context.GeneratedReadViews)
            if (moduleOfOwner.TryGetValue(view.Source.Prefix ?? "", out var ns)) map[NameCasing.ToPascalCase(view.Name)] = ns;
        return map;
    }

    private IArtifactGenerator ResolveGenerator(
        MddJsonTarget target, string? modelNamespace, EntitySurfaceFilter surfaceFilter,
        EntitySurfaceFilter formsSurfaceFilter, IReadOnlySet<string> registeredElsewhere,
        IReadOnlyDictionary<string, string> entityNamespaces)
    {
        return target.Type switch
        {
            "Sql" => CreateSqlGenerator(target),
            "Model" => new MddBooster.Generators.Model.ModelGenerator(
                new MddBooster.Generators.Model.ModelGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Namespace = target.Namespace
                        ?? throw new InvalidOperationException("Model target requires 'namespace'."),
                    DbContextName = target.DbContextName
                        ?? throw new InvalidOperationException("Model target requires 'dbContextName'."),
                    SqlProjectPath = target.SqlProjectPath,
                    PostgresNaming = IsPostgresDialect(target),
                    Modules = target.Modules?
                        .Select(m => new MddBooster.Generators.Model.ModelModule(m.Owners, m.ProjectPath, m.Namespace, m.DataInterface))
                        .ToList(),
                }),
            "Api" => new MddBooster.Generators.Api.ApiRegistrationGenerator(
                new MddBooster.Generators.Api.ApiRegistrationGeneratorOptions
                {
                    ProjectPath = target.ProjectPath,
                    Namespace = target.Namespace
                        ?? throw new InvalidOperationException("Api target requires 'namespace'."),
                    EntitiesNamespace = modelNamespace,
                    EntityNamespaces = entityNamespaces,
                    SurfaceFilter = surfaceFilter,
                    PermissionKeyTemplate = target.PermissionKeyTemplate,
                    Surfaces = MddBooster.Generators.Api.ApiSurfaceNames.Parse(target.Surfaces),
                    RegisteredElsewhere = registeredElsewhere,
                }),
            "TypeScript" => new TypeScriptGenerator(
                new TypeScriptGeneratorOptions
                {
                    OutputPath = target.OutputPath
                        ?? throw new InvalidOperationException("TypeScript target requires 'outputPath'."),
                    FormsOutputPath = target.FormsOutputPath,
                    FormModules = FormModulesFor(target),
                    SurfaceFilter = surfaceFilter,
                    FormsSurfaceFilter = formsSurfaceFilter,
                    SharedTypesImport = string.IsNullOrWhiteSpace(target.SharedTypesImport) ? null : target.SharedTypesImport,
                }),
            _ => throw new NotSupportedException(
                $"지원하지 않는 target type: '{target.Type}' (지원: Sql, Model, Api, TypeScript)"),
        };
    }
}
