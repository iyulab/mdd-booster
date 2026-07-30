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

        var src = ApiRegistrationRenderer.Render(models, "Sample.Server", entitiesNamespace: "Sample.Entities");

        Assert.Contains("using Sample.Entities;", src);
        // using 이 namespace 선언보다 먼저 나와야 함
        var usingIndex = src.IndexOf("using Sample.Entities;");
        var nsIndex = src.IndexOf("namespace Sample.Server;");
        Assert.True(usingIndex < nsIndex);
    }

    [Fact]
    public void ApiRegistration_skips_using_when_entities_namespace_matches()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ApiRegistrationRenderer.Render(models, "Sample.Entities", entitiesNamespace: "Sample.Entities");

        Assert.DoesNotContain("using Sample.Entities;", src);
    }

    [Fact]
    public void ODataControllerRenderer_emits_one_concrete_subclass_per_model()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-derived.m3l.md"));
        var models = new InterfaceResolver(ast).ResolveAll().ToList();

        var src = ODataControllerRenderer.Render(models, "Sample.Server", "Sample.Entities");

        // 반드시 포함: Orders/OrderItems/Customers 각 controller
        Assert.Contains("public sealed partial class OrdersController", src);
        Assert.Contains("IyuODataController<OrderExt, Order>", src);
        Assert.Contains("public sealed partial class OrderItemsController", src);
        Assert.Contains("public sealed partial class CustomersController", src);
        // using 지시자
        Assert.Contains("using Sample.Entities;", src);
        // Controllers 서브네임스페이스
        Assert.Contains("namespace Sample.Server.Controllers;", src);
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

    // --- 선행 약어 엔티티의 camelCase (GraphQL 필드명) ---

    [Theory]
    // 엔티티 → (OData set, GraphQL query, GraphQL mutation prefix)
    [InlineData("QRScanLog", "QRScanLogs", "qrScanLogs", "qrScanLog")]
    [InlineData("QRCode", "QRCodes", "qrCodes", "qrCode")]
    [InlineData("OrderQR", "OrderQRs", "orderQRs", "orderQR")]  // 후행 약어 보존
    [InlineData("Order", "Orders", "orders", "order")]           // 회귀
    public void ApiRegistration_camelCases_leading_acronyms(
        string entity, string odataSet, string queryName, string mutationPrefix)
    {
        var src = ApiRegistrationRenderer.Render([ModelWith(entity)], "Test.Api");

        // OData set 이름은 PascalCase 복수 — DbSet 이름과 맞춰야 하므로 이 변경의 영향 밖이다.
        Assert.Contains($"options.ODataModel.AddEntityPair<{entity}Ext, {entity}>(\"{odataSet}\");", src);
        Assert.Contains(
            $"options.GraphQL.AddEntityPair<{entity}Ext, {entity}>(\"{queryName}\", \"{mutationPrefix}\");", src);
    }

    [Fact]
    public void ApiRegistration_pluralizes_after_camelCasing_not_before()
    {
        // 순서 반전을 고정하는 케이스. 복수화를 먼저 하면 Pluralize("QR")="QRs" 의 'R' 뒤에
        // 소문자 's' 가 붙어 약어 연쇄가 끊기고 camelCase 가 "qRs" 를 낸다.
        var src = ApiRegistrationRenderer.Render([ModelWith("QR")], "Test.Api");

        Assert.Contains("options.GraphQL.AddEntityPair<QRExt, QR>(\"qrs\", \"qr\");", src);
        Assert.DoesNotContain("qRs", src);
    }

    [Fact]
    public void ApiRegistration_allcaps_acronym_ending_in_S_keeps_bare_plural()
    {
        // Pluralizer 의 bare trailing "s" 검사는 Ordinal 이다 — camelCase 를 먼저 적용하면
        // "SMS" → "sms" 가 그 분기에 걸려 복수형이 붙지 않는다. **의도된 동작**이며
        // Status·News 가 이미 같은 성질을 갖는다. query 와 mutation prefix 가 같아지지만
        // 둘은 서로 다른 루트 타입이라 스키마 충돌이 아니다.
        //
        // 같은 Ordinal 검사 때문에 **OData set 은 대문자 'S' 라서 그 분기를 타지 않고** "SMSs" 가
        // 된다. 그 비대칭은 의도다 — PascalCase 복수는 DbSet 이름과 맞춰야 하므로 이 변경의
        // 영향 밖이며, 두 표면이 서로 다른 규칙을 쓴다는 사실 자체를 여기서 고정한다.
        var src = ApiRegistrationRenderer.Render([ModelWith("SMS")], "Test.Api");

        Assert.Contains("options.GraphQL.AddEntityPair<SMSExt, SMS>(\"sms\", \"sms\");", src);
        Assert.Contains("options.ODataModel.AddEntityPair<SMSExt, SMS>(\"SMSs\");", src);
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
