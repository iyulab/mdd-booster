using MddBooster.Cli.Commands;

namespace MddBooster.Tests.Cli;

/// <summary>
/// A field declared <c>@system</c> — one the application's own code writes — carried through every target that has a
/// write or an authoring surface: the registration takes it off the generic write path, the form draws no control for
/// it, the field schema marks it read-only, and the entity says nobody edits it. The column and the read side are
/// untouched: the value is stored and shown like any other.
/// </summary>
[Collection(ConsoleCaptureCollection.Name)]
public sealed class SystemWrittenFieldBuildTests
{
    // WorkOrder: a state only a transition moves, the user who closed it (a reference), a hidden field that is also
    // system-written, and an ordinary one. Part: nothing declared — the control.
    private const string Model = """
# Namespace: X

## Timestampable ::interface
- created_at: timestamp = now()
- updated_at: timestamp = now()

## Account : Timestampable
- id: identifier @pk @generated
- name: string(50)

## WorkOrder : Timestampable
- id: identifier @pk @generated
- title: string(100)
- status: string(20) = "planned" @system
- closed_by_id: identifier? @reference(Account) @system
- audit_token: string(64)? @system @internal
- locked_note: string(200)? @immutable

## Part : Timestampable
- id: identifier @pk @generated
- name: string(50)
""";

    private sealed record Output(string Root) : IDisposable
    {
        public string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path])).ReplaceLineEndings("\n");

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Output Build(string apiExtra = "")
    {
        var root = Path.Combine(Path.GetTempPath(), $"mdd-system-field-{Guid.NewGuid():N}");
        var mddDir = Path.Combine(root, "mdd");
        Directory.CreateDirectory(mddDir);
        foreach (var d in new[] { "db", "entities", "api", "ui" })
            Directory.CreateDirectory(Path.Combine(root, d));

        File.WriteAllText(Path.Combine(mddDir, "model.m3l.md"), Model);
        File.WriteAllText(Path.Combine(mddDir, "mdd.json"), $$"""
{
  "sources": ["./model.m3l.md"],
  "targets": [
    { "type": "Sql", "dialect": "tsql", "projectPath": "../db", "emitSqlProj": false },
    { "type": "Model", "dialect": "tsql", "projectPath": "../entities", "namespace": "X.Entities", "dbContextName": "XDbContext" },
    { "type": "Api", "projectPath": "../api", "namespace": "X.Server"{{apiExtra}} },
    { "type": "TypeScript", "outputPath": "../ui/types", "formsOutputPath": "../ui/forms",
      "formLayoutImport": "@example/layout", "formControlsImport": "@example/controls",
      "formSelectOptionsImport": "@example/enum-options" }
  ]
}
""");

        var output = new Output(root);
        Assert.Equal(0, new BuildCommand().Run(mddDir));
        return output;
    }

    private static string Registration(Output o)
        => File.ReadAllText(Directory.GetFiles(o.Root, "ApiRegistration_gen.cs", SearchOption.AllDirectories).Single())
            .ReplaceLineEndings("\n");

    [Fact]
    public void The_registration_takes_the_system_fields_off_the_generic_write_path()
    {
        using var o = Build();
        var registration = Registration(o);

        // One declaration per model, naming the read type's properties in declaration order. The hidden field is not
        // on the read type, so there is nothing of it for a write to carry.
        Assert.Contains(
            "options.ODataModel.ExcludeFromWrite<X.Entities.WorkOrderExt>(x => x.Status, x => x.ClosedById);",
            registration);
        Assert.DoesNotContain("AuditToken", registration);
        Assert.DoesNotContain("LockedNote", registration);   // @immutable is a different statement
        Assert.DoesNotContain("ExcludeFromWrite<X.Entities.PartExt>", registration);
        Assert.DoesNotContain("ExcludeFromWrite<X.Entities.AccountExt>", registration);
    }

    [Fact]
    public void The_exclusion_follows_the_set_it_names()
    {
        using var o = Build();
        var registration = Registration(o);

        // The runtime resolves the read type against registered sets when the model is built; keeping the
        // declaration after every registration means the order never has to be reasoned about.
        Assert.True(
            registration.IndexOf("ExcludeFromWrite<", StringComparison.Ordinal)
            > registration.LastIndexOf("options.ODataModel.AddEntityPair<", StringComparison.Ordinal));
    }

    [Fact]
    public void A_registration_without_the_odata_surface_declares_nothing()
    {
        using var o = Build(""", "surfaces": ["graphql"]""");

        Assert.DoesNotContain("ExcludeFromWrite", Registration(o));
    }

    [Fact]
    public void The_form_draws_no_control_for_a_system_field_and_the_schema_marks_it_read_only()
    {
        using var o = Build();

        var form = o.Read("ui", "forms", "WorkOrderForm_gen.tsx");
        Assert.Contains("Title", form);
        Assert.Contains("LockedNote", form);          // @immutable is drawn, disabled
        Assert.DoesNotContain("Status", form);
        Assert.DoesNotContain("ClosedById", form);    // no slot either — a slot is a control the caller fills

        var schema = o.Read("ui", "types", "field_schema_gen.ts");
        var workOrder = schema[schema.IndexOf("  WorkOrder: {", StringComparison.Ordinal)..];
        workOrder = workOrder[..workOrder.IndexOf("\n  },", StringComparison.Ordinal)];
        var status = workOrder.Split('\n').Single(l => l.TrimStart().StartsWith("Status:", StringComparison.Ordinal));
        Assert.Contains("readOnly: true", status);
        // A required, bounded column — but those are statements about a value a person supplies.
        Assert.DoesNotContain("required", status);
        Assert.DoesNotContain("maxLength", status);

        // The type still carries the field: it is read like any other.
        Assert.Contains("Status: string", o.Read("ui", "types", "entities_gen.ts"));
    }

    [Fact]
    public void The_entities_say_who_may_supply_each_value_and_the_table_is_untouched()
    {
        using var o = Build();

        foreach (var file in new[] { "WorkOrder.cs", "WorkOrderExt.cs" })
        {
            var cs = o.Read("entities", "Entity_gen", file);
            Assert.Contains("    [Editable(false)]\n    public string Status { get; set; }", cs);
            Assert.Contains("    [Editable(false, AllowInitialValue = true)]\n    public string? LockedNote { get; set; }", cs);
        }

        var table = o.Read("db", "dbo", "Tables_gen", "WorkOrder.sql");
        Assert.Contains("[Status]", table);
        Assert.Contains("[ClosedById]", table);
        Assert.Contains("[AuditToken]", table);
    }
}
