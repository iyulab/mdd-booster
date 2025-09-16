using MDDBooster.Models;
using MDDBooster.Processors;

namespace MDDBooster.Builders.MsSql.Processors;

public class MsSqlFrameworkAttributeProcessor : IModelProcessor
{
    public void Process(MDDDocument document)
    {
        foreach (var model in document.Models)
        {
            foreach (var field in model.Fields)
            {
                ProcessFieldAttributes(field);
            }
        }

        foreach (var interface_ in document.Interfaces)
        {
            foreach (var field in interface_.Fields)
            {
                ProcessFieldAttributes(field);
            }
        }
    }

    private void ProcessFieldAttributes(MDDField field)
    {
        // Process SQL Server-specific framework attributes
        foreach (var attr in field.FrameworkAttributes)
        {
            if (attr.Name.Equals("Insert", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["InsertValue"] = attr.Parameters.FirstOrDefault();
            }
            else if (attr.Name.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["UpdateValue"] = attr.Parameters.FirstOrDefault();
            }
            else if (attr.Name.Equals("Without", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["ExcludeFromGeneration"] = true;
            }
            else if (attr.Name.Equals("DataType", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["DataType"] = attr.Parameters.FirstOrDefault();
            }
            else if (attr.Name.Equals("JsonIgnore", StringComparison.OrdinalIgnoreCase))
            {
                field.ExtendedMetadata["JsonIgnore"] = true;
            }
        }
    }
}