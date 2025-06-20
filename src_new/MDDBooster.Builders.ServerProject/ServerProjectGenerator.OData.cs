using MDDBooster.Extensions;

namespace MDDBooster.Builders.ServerProject;

/// <summary>
/// ServerProjectGenerator - OData generation methods
/// </summary>
public partial class ServerProjectGenerator
{
    /// <summary>
    /// Generate DataContext class for OData
    /// </summary>
    public string GenerateDataContext()
    {
        return ErrorHandling.ExecuteSafely(() =>
        {
            var sb = new StringBuilder();

            // Add file header
            sb.AppendLine("// # Code generated by \"MDD Booster\"; DO NOT EDIT.");
            sb.AppendLine("using Microsoft.Extensions.Logging;");
            sb.AppendLine();
            sb.AppendLine($"namespace {_config.Namespace}.Services;");
            sb.AppendLine();

            // Class declaration
            string classModifiers = _config.UsePartialClasses ? "public partial class" : "public class";
            sb.AppendLine($"{classModifiers} DataContext(IHttpContextAccessor httpContextAccessor, DbContextOptions options) : ODataContext(httpContextAccessor, options)");
            sb.AppendLine("{");

            // Generate DbSet properties for all models
            foreach (var model in Document.Models.Where(m => !m.BaseModel.IsAbstract))
            {
                string pluralName = model.BaseModel.Name.ToPlural();
                sb.AppendLine($"    public DbSet<{model.BaseModel.Name}> {pluralName} {{ get; set; }}");
            }

            sb.AppendLine();
            sb.AppendLine("#if DEBUG");
            sb.AppendLine("    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)");
            sb.AppendLine("    {");
            sb.AppendLine("        base.OnConfiguring(optionsBuilder);");
            sb.AppendLine();
            sb.AppendLine("        optionsBuilder.UseLoggerFactory(LoggerFactory.Create(builder => { builder.AddConsole(); }));");
            sb.AppendLine("        optionsBuilder.EnableSensitiveDataLogging();");
            sb.AppendLine("    }");
            sb.AppendLine("#endif");
            sb.AppendLine();

            // OnModelCreating method
            sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
            sb.AppendLine("    {");
            sb.AppendLine("        base.OnModelCreating(modelBuilder);");
            sb.AppendLine();

            // Generate relationship configurations
            GenerateRelationshipConfigurations(sb);

            // Generate triggers for models
            foreach (var model in Document.Models.Where(m => !m.BaseModel.IsAbstract))
            {
                sb.AppendLine($"        modelBuilder.Entity<{model.BaseModel.Name}>().ToTable(tb => tb.HasTrigger(\"{model.BaseModel.Name}Trigger\"));");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("        OnModelCreatingPartial(modelBuilder);");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);");
            sb.AppendLine("}");

            return sb.ToString();
        },
        string.Empty,
        "Failed to generate DataContext");
    }

    /// <summary>
    /// Generate EntitySetBuilder class for OData
    /// </summary>
    public string GenerateEntitySetBuilder()
    {
        return ErrorHandling.ExecuteSafely(() =>
        {
            var sb = new StringBuilder();

            // Add file header
            sb.AppendLine("// # Code generated by \"MDD Booster\"; DO NOT EDIT.");
            sb.AppendLine();
            sb.AppendLine($"namespace {_config.Namespace}.Services;");
            sb.AppendLine();

            // Class declaration
            string classModifiers = _config.UsePartialClasses ? "public partial class" : "public class";
            sb.AppendLine($"{classModifiers} EntitySetBuilder");
            sb.AppendLine("{");

            // AddCustom partial method
            sb.AppendLine("    partial void AddCustom(ODataConventionModelBuilder builder);");
            sb.AppendLine();

            // AddAll static method
            sb.AppendLine("    public static ODataConventionModelBuilder AddAll(ODataConventionModelBuilder builder)");
            sb.AppendLine("    {");
            sb.AppendLine("        var esBuilder = new EntitySetBuilder();");

            foreach (var model in Document.Models.Where(m => !m.BaseModel.IsAbstract))
            {
                sb.AppendLine($"        esBuilder.Add{model.BaseModel.Name}(builder);");
            }

            sb.AppendLine("        esBuilder.AddCustom(builder);");
            sb.AppendLine();
            sb.AppendLine("        return builder;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate individual Add methods for each model
            foreach (var model in Document.Models.Where(m => !m.BaseModel.IsAbstract))
            {
                sb.AppendLine($"    public virtual ODataModelBuilder Add{model.BaseModel.Name}(ODataModelBuilder builder)");
                sb.AppendLine("    {");
                string pluralName = model.BaseModel.Name.ToPlural();
                sb.AppendLine($"        builder.EntitySet<{model.BaseModel.Name}>(\"{pluralName}\");");
                sb.AppendLine("        return builder;");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#pragma warning restore CS8618, IDE1006");

            return sb.ToString();
        },
        string.Empty,
        "Failed to generate EntitySetBuilder");
    }

    /// <summary>
    /// Generate relationship configurations for Entity Framework
    /// </summary>
    private void GenerateRelationshipConfigurations(StringBuilder sb)
    {
        // Find models with follower-like relationships
        var followerPatterns = new[] { "Follower", "FollowRequest", "FollowActivity" };

        foreach (var pattern in followerPatterns)
        {
            var model = Document.Models.FirstOrDefault(m => m.BaseModel.Name == pattern);
            if (model != null)
            {
                GenerateFollowerRelationshipConfig(sb, model);
            }
        }
    }

    /// <summary>
    /// Generate follower relationship configurations
    /// </summary>
    private void GenerateFollowerRelationshipConfig(StringBuilder sb, MDDModel model)
    {
        var modelName = model.BaseModel.Name;
        var allFields = ModelUtilities.GetAllFields(Document, model);

        if (modelName == "Follower")
        {
            // Check if the model has FollowerId and FollowingId fields
            var followerField = allFields.FirstOrDefault(f => f.BaseField.Name.Contains("Follower") && f.BaseField.IsReference);
            var followingField = allFields.FirstOrDefault(f => f.BaseField.Name.Contains("Following") && f.BaseField.IsReference);

            if (followerField != null && followingField != null)
            {
                sb.AppendLine($"        modelBuilder.Entity<{modelName}>()");
                sb.AppendLine("            .HasOne(f => f.FollowerItem)");
                sb.AppendLine("            .WithMany(a => a.FollowerFollowers)");
                sb.AppendLine("            .HasForeignKey(f => f.FollowerId)");
                sb.AppendLine("            .OnDelete(DeleteBehavior.Cascade);");
                sb.AppendLine();
                sb.AppendLine($"        modelBuilder.Entity<{modelName}>()");
                sb.AppendLine("            .HasOne(f => f.Following)");
                sb.AppendLine("            .WithMany(a => a.FollowerFollowings)");
                sb.AppendLine("            .HasForeignKey(f => f.FollowingId)");
                sb.AppendLine("            .OnDelete(DeleteBehavior.NoAction);");
                sb.AppendLine();
            }
        }
        else if (modelName == "FollowRequest")
        {
            var requesterField = allFields.FirstOrDefault(f => f.BaseField.Name.Contains("Requester") && f.BaseField.IsReference);
            var requesteeField = allFields.FirstOrDefault(f => f.BaseField.Name.Contains("Requestee") && f.BaseField.IsReference);

            if (requesterField != null && requesteeField != null)
            {
                sb.AppendLine($"        modelBuilder.Entity<{modelName}>()");
                sb.AppendLine("            .HasOne(f => f.Requester)");
                sb.AppendLine("            .WithMany(a => a.FollowRequestRequesters)");
                sb.AppendLine("            .HasForeignKey(f => f.RequesterId)");
                sb.AppendLine("            .OnDelete(DeleteBehavior.Cascade);");
                sb.AppendLine();
                sb.AppendLine($"        modelBuilder.Entity<{modelName}>()");
                sb.AppendLine("            .HasOne(f => f.Requestee)");
                sb.AppendLine("            .WithMany(a => a.FollowRequestRequestees)");
                sb.AppendLine("            .HasForeignKey(f => f.RequesteeId)");
                sb.AppendLine("            .OnDelete(DeleteBehavior.NoAction);");
                sb.AppendLine();
            }
        }
        else if (modelName == "FollowActivity")
        {
            var userField = allFields.FirstOrDefault(f => f.BaseField.Name.Contains("User") && f.BaseField.IsReference && !f.BaseField.Name.Contains("Target"));
            var targetUserField = allFields.FirstOrDefault(f => f.BaseField.Name.Contains("TargetUser") && f.BaseField.IsReference);

            if (userField != null && targetUserField != null)
            {
                sb.AppendLine($"        modelBuilder.Entity<{modelName}>()");
                sb.AppendLine("            .HasOne(f => f.User)");
                sb.AppendLine("            .WithMany(a => a.FollowActivityUsers)");
                sb.AppendLine("            .HasForeignKey(f => f.UserId)");
                sb.AppendLine("            .OnDelete(DeleteBehavior.Cascade);");
                sb.AppendLine();
                sb.AppendLine($"        modelBuilder.Entity<{modelName}>()");
                sb.AppendLine("            .HasOne(f => f.TargetUser)");
                sb.AppendLine("            .WithMany(a => a.FollowActivityTargetUsers)");
                sb.AppendLine("            .HasForeignKey(f => f.TargetUserId)");
                sb.AppendLine("            .OnDelete(DeleteBehavior.NoAction);");
                sb.AppendLine();
            }
        }
    }
}