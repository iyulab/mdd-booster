using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.Model;

namespace MddBooster.Tests.Generators.Model;

public class ReferenceAttributeTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Render_emits_reference_attribute_on_fk_property()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Entities");

        Assert.Contains("[Reference(\"Customer\")]", rendered.Write);
        Assert.Contains("public Guid CustomerId", rendered.Write);
    }

    [Fact]
    public void Reference_field_generates_nav_property_on_write_entity()
    {
        // EF Core needs navigation properties to infer INSERT order for parent/child
        // pairs added in the same SaveChanges call. Without nav props, FK violations
        // occur when the child is inserted before the parent.
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Entities");

        // Nav property on Write class: type = reference target, name = field without _id
        Assert.Contains("public Customer Customer { get; set; } = null!;", rendered.Write);
        // FK property still present alongside nav
        Assert.Contains("public Guid CustomerId { get; set; }", rendered.Write);
        // Nav property NOT on Ext (read-only, no writes)
        Assert.DoesNotContain("public Customer Customer", rendered.Read);
        // Nav property NOT in Interface (interface is for stored field contract only)
        Assert.DoesNotContain("Customer Customer", rendered.Interface);
    }

    [Fact]
    public void Nullable_reference_field_generates_nullable_nav_property()
    {
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-nullable-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Entities");

        // Nullable FK → nullable nav property, no null! initializer
        Assert.Contains("public Customer? Customer { get; set; }", rendered.Write);
        Assert.DoesNotContain("public Customer? Customer { get; set; } = null!", rendered.Write);
    }

    [Fact]
    public void Ext_read_type_stays_flat_so_the_api_surface_can_be_scoped()
    {
        // ⚠️ TRIPWIRE — 이 단언이 깨지면 API 표면 격리 보장이 함께 깨진다. 고치기 전에 읽을 것.
        //
        // 런타임(iyu-framework)은 **read 타입만** 스키마에 등록한다:
        //   OData    IyuEdmModelBuilder.AddEntityPair → _modelBuilder.EntitySet<TRead>(setName)
        //   GraphQL  IyuGraphQLSchemaBuilder.AddEntityPair → Field(queryName).Type<ListType<ObjectType<TRead>>>()
        // write 타입은 어느 스키마에도 들어가지 않는다(레지스트리 전용).
        //
        // 그래서 `@internal`(및 향후 타깃별 엔티티 필터)이 AddEntityPair 를 방출하지 않으면
        // 그 엔티티 타입은 **정말로** 스키마에서 사라진다 — 단, read 타입이 다른 엔티티 타입을
        // 프로퍼티로 들고 있지 않을 때만. Ext 에 내비게이션을 방출하기 시작하면 컨벤션 기반
        // 타입 발견이 제외한 엔티티를 되살려, 제외가 "루트 필드만 숨김"으로 조용히 약화된다.
        //
        // 즉 `ApiRegistrationRenderer` 의 "시크릿 컬럼 노출 금지" 주장은 **이 불변식에 기대고 있다.**
        var ast = new M3lLoader().LoadFile(FixturePath("order-with-ref.m3l.md"));
        var resolved = new InterfaceResolver(ast).ResolveAll().Single(m => m.Name == "Order");

        var rendered = EntityPairRenderer.Render(resolved, "Test.Api");

        // FK 는 스칼라로만 노출된다.
        Assert.Contains("public Guid CustomerId { get; set; }", rendered.Read);
        // 참조 대상 엔티티 타입을 프로퍼티로 들지 않는다 (nullable 표기까지 함께 봉인).
        Assert.DoesNotContain("public Customer ", rendered.Read);
        Assert.DoesNotContain("public Customer? ", rendered.Read);
    }
}
