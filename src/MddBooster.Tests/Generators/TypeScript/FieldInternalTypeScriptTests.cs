using M3L.Native;
using MddBooster.Core.Ast;
using MddBooster.Core.Semantic;
using MddBooster.Generators.TypeScript;
using static MddBooster.Tests.Generators.TypeScript.FormImportFixtures;

namespace MddBooster.Tests.Generators.TypeScript;

/// <summary>
/// A field marked <c>@internal</c> is stored but carried by neither OData wire shape — it is absent
/// from the read type, so the data API neither returns it nor accepts it in a write body. The
/// TypeScript target mirrors that wire shape (<c>entities_gen.ts</c>, <c>field_schema_gen.ts</c>,
/// generated forms), so the field is absent there too: a type that declares it promises a value
/// no response carries, and a form that edits it posts a property the server rejects.
/// <para>
/// A model-level <c>@internal</c> is a different statement (no data API set at all) and keeps its
/// TypeScript type — that half is pinned below so the two axes are not conflated.
/// </para>
/// </summary>
public class FieldInternalTypeScriptTests
{
    private static IReadOnlyList<ResolvedModel> LoadInline(string body)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mdd-tsfi-{Guid.NewGuid():N}.m3l.md");
        File.WriteAllText(tmp, "# Namespace: test\n\n" + body);
        try
        {
            var ast = new M3lLoader().LoadFile(tmp);
            return new InterfaceResolver(ast).ResolveAll();
        }
        finally { File.Delete(tmp); }
    }

    private const string Account = """
        ## HashScheme ::enum
        - pbkdf2: "PBKDF2"
        - bcrypt: "bcrypt"

        ## Account

        - id: identifier @pk @generated
        - username: string(50) @not_null "사용자명"
        - password_hash: string(255) @not_null @internal "비밀번호 해시"
        - hash_scheme: HashScheme @not_null @internal "해시 방식"

        ## ServiceClient @internal

        - id: identifier @pk @generated
        - name: string(50) @not_null "이름"
        - secret_hash: string(255) @not_null "시크릿 해시"
        """;

    [Fact]
    public void The_entity_interface_does_not_declare_an_internal_field()
    {
        var models = LoadInline(Account);

        var result = TsInterfaceRenderer.RenderAll(models, new HashSet<string> { "HashScheme" });

        Assert.Contains("Username", result);
        Assert.DoesNotContain("PasswordHash", result);
        Assert.DoesNotContain("HashScheme:", result);
        // The enum was reachable only through the internal field, so importing it would be dead.
        Assert.DoesNotContain("import type { HashScheme }", result);
    }

    [Fact]
    public void The_field_schema_does_not_describe_an_internal_field()
    {
        var models = LoadInline(Account);

        var result = TsFieldSchemaRenderer.RenderAll(models);

        Assert.Contains("Username:", result);
        Assert.DoesNotContain("PasswordHash", result);
    }

    [Fact]
    public void The_generated_form_does_not_offer_an_internal_field()
    {
        var models = LoadInline(Account);

        var form = TsFormRenderer.RenderAll(models, [], TestImports)["Account"];

        Assert.Contains("Username", form);
        Assert.DoesNotContain("PasswordHash", form);
    }

    [Fact]
    public void A_model_level_internal_entity_keeps_its_type_and_its_unmarked_fields()
    {
        var models = LoadInline(Account);

        var result = TsInterfaceRenderer.RenderAll(models, new HashSet<string> { "HashScheme" });

        Assert.Contains("export interface ServiceClient", result);
        Assert.Contains("SecretHash", result);
    }
}
