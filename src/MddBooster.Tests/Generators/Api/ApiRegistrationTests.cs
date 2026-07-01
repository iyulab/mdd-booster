using System.Text.Json;
using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Api;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MddBooster.Tests.Generators.Api;

public class ApiRegistrationTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Pluralizer_handles_common_english_cases()
    {
        Assert.Equal("Orders", Pluralizer.Pluralize("Order"));
        Assert.Equal("Categories", Pluralizer.Pluralize("Category"));
        Assert.Equal("Customers", Pluralizer.Pluralize("Customer"));
        Assert.Equal("Boys", Pluralizer.Pluralize("Boy"));       // vowel+y stays y
        Assert.Equal("Status", Pluralizer.Pluralize("Status"));  // single trailing s → unchanged
    }

    [Fact]
    public void Pluralizer_handles_sibilant_endings()
    {
        // -ss / -sh / -ch / -x / -z → +es (영어 규칙)
        Assert.Equal("Addresses", Pluralizer.Pluralize("Address"));
        Assert.Equal("OrderAddresses", Pluralizer.Pluralize("OrderAddress"));
        Assert.Equal("Classes", Pluralizer.Pluralize("Class"));
        Assert.Equal("Boxes", Pluralizer.Pluralize("Box"));
        Assert.Equal("Dishes", Pluralizer.Pluralize("Dish"));
        Assert.Equal("Benches", Pluralizer.Pluralize("Bench"));
        Assert.Equal("Quizzes".Replace("zzes", "zes"), Pluralizer.Pluralize("Quiz")); // 'Quizes' (double-z 규칙은 out of scope)
    }

    [Fact]
    public void ApiRegistration_emits_one_OData_and_one_GraphQL_line_per_model()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var rendered = ApiRegistrationRenderer.Render(models, "Test.Api");

        // Order → Orders
        Assert.Contains("options.ODataModel.AddEntityPair<OrderExt, Order>(\"Orders\");", rendered);
        Assert.Contains("options.GraphQL.AddEntityPair<OrderExt, Order>(\"orders\", \"order\");", rendered);

        // Customer → Customers
        Assert.Contains("options.ODataModel.AddEntityPair<CustomerExt, Customer>(\"Customers\");", rendered);
        Assert.Contains("options.GraphQL.AddEntityPair<CustomerExt, Customer>(\"customers\", \"customer\");", rendered);

        // OrderItem → OrderItems
        Assert.Contains("options.ODataModel.AddEntityPair<OrderItemExt, OrderItem>(\"OrderItems\");", rendered);
    }

    [Fact]
    public void ApiRegistration_output_is_valid_csharp()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ApiRegistrationRenderer.Render(models, "Test.Api");
        var tree = CSharpSyntaxTree.ParseText(src,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();

        Assert.True(errors.Count == 0,
            $"Syntax errors: {string.Join("; ", errors.Select(d => d.GetMessage()))}\n---\n{src}");
    }

    [Fact]
    public void ApiRegistration_emits_using_when_entities_namespace_differs()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ApiRegistrationRenderer.Render(models, "Yesung.Server", entitiesNamespace: "Yesung.Entities");

        Assert.Contains("using Yesung.Entities;", src);
        // using 이 namespace 선언보다 먼저 나와야 함
        var usingIndex = src.IndexOf("using Yesung.Entities;");
        var nsIndex = src.IndexOf("namespace Yesung.Server;");
        Assert.True(usingIndex < nsIndex);
    }

    [Fact]
    public void ApiRegistration_skips_using_when_entities_namespace_matches()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ApiRegistrationRenderer.Render(models, "Yesung.Entities", entitiesNamespace: "Yesung.Entities");

        Assert.DoesNotContain("using Yesung.Entities;", src);
    }

    [Fact]
    public void ODataControllerRenderer_emits_one_concrete_subclass_per_model()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ODataControllerRenderer.Render(models, "Yesung.Server", "Yesung.Entities");

        // 반드시 포함: Orders/OrderItems/Customers 각 controller
        Assert.Contains("public sealed partial class OrdersController", src);
        Assert.Contains("IyuODataController<OrderExt, Order>", src);
        Assert.Contains("public sealed partial class OrderItemsController", src);
        Assert.Contains("public sealed partial class CustomersController", src);
        // using 지시자
        Assert.Contains("using Yesung.Entities;", src);
        // Controllers 서브네임스페이스
        Assert.Contains("namespace Yesung.Server.Controllers;", src);
    }

    [Fact]
    public void ODataControllerRenderer_output_is_valid_csharp()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ODataControllerRenderer.Render(models, "Test.Api");
        var tree = CSharpSyntaxTree.ParseText(src,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();

        Assert.True(errors.Count == 0,
            $"Syntax errors: {string.Join("; ", errors.Select(d => d.GetMessage()))}\n---\n{src}");
    }

    // --- @internal 엔티티 제외 (보안: 아이덴티티 인프라를 데이터 API에 노출하지 않음) ---

    private static ResolvedModel ModelWith(string name, params FieldAttribute[] attrs) => new()
    {
        Name = name,
        Fields = [new FieldNode
        {
            Name = "key", Type = "string", Kind = FieldKind.Stored,
            Nullable = false, Loc = new SourceLocation { File = "t.m3l.md", Line = 1 }
        }],
        Source = new ModelNode
        {
            Name = name, Type = ModelType.Model,
            Loc = new SourceLocation { File = "t.m3l.md" }, Attributes = [.. attrs],
        },
    };

    private static FieldAttribute Attr(string name, params string[] args) => new()
    {
        Name = name,
        Args = [.. args.Select(a => JsonSerializer.SerializeToElement(a))],
    };

    [Fact]
    public void ApiRegistration_skips_internal_entity_but_keeps_normal()
    {
        var models = new List<ResolvedModel>
        {
            ModelWith("Order"),                            // 일반 → 노출
            ModelWith("ServiceClient", Attr("internal")),  // @internal → 미노출
        };

        var src = ApiRegistrationRenderer.Render(models, "Test.Api");

        // 일반 엔티티는 OData/GraphQL 모두 등록
        Assert.Contains("options.ODataModel.AddEntityPair<OrderExt, Order>(\"Orders\");", src);
        Assert.Contains("options.GraphQL.AddEntityPair<OrderExt, Order>(\"orders\", \"order\");", src);
        // @internal 엔티티는 어떤 등록 라인도 방출하지 않음
        Assert.DoesNotContain("ServiceClient", src);
    }

    [Fact]
    public void ODataController_skips_internal_entity()
    {
        var models = new List<ResolvedModel>
        {
            ModelWith("Order"),
            ModelWith("ServiceClient", Attr("internal")),
        };

        var src = ODataControllerRenderer.Render(models, "Test.Api", "Test.Entities");

        Assert.Contains("public sealed partial class OrdersController", src);
        Assert.DoesNotContain("ServiceClient", src);
    }

    [Fact]
    public void ApiRegistration_contains_method_signature_with_IyuMainServerOptions()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("bank-account.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ApiRegistrationRenderer.Render(models, "Test.Api");

        Assert.Contains("public static partial class ApiRegistration", src);
        Assert.Contains("public static void RegisterGeneratedEntities(global::Iyu.MainServer.IyuMainServerOptions options)", src);
    }
}
