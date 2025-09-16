using MDDBooster.Models;
using MDDBooster.Parsers;
using MDDBooster.Processors;

namespace MDDBooster;

public class MDDBoosterParser
{
    private readonly M3LParser.M3LParser _baseParser;
    private readonly MDDParserOptions _options;

    public MDDBoosterParser(MDDParserOptions options = null)
    {
        // Configure the base parser to NOT apply default inheritance
        var parserOptions = new M3LParser.M3LParserOptions
        {
            ResolveInheritance = false // Disable built-in inheritance resolution
        };

        _baseParser = new M3LParser.M3LParser(parserOptions);
        _options = options ?? new MDDParserOptions();

        // Always add the InheritanceProcessor
        if (!_options.ModelProcessors.Any(p => p is InheritanceProcessor))
        {
            _options.ModelProcessors.Add(new InheritanceProcessor());
        }

        // Add default framework attribute parsers if none are provided
        if (!_options.FrameworkAttributeParsers.Any())
        {
            _options.FrameworkAttributeParsers.Add(new DefaultFrameworkAttributeParser());
        }

        AppLog.Debug("MDDBoosterParser initialized with {ParserCount} attribute parsers, {ProcessorCount} model processors, {RawProcessorCount} raw text processors",
            _options.FrameworkAttributeParsers.Count,
            _options.ModelProcessors.Count,
            _options.RawTextProcessors.Count);
    }

    public MDDDocument Parse(string filePath)
    {
        AppLog.Information("Parsing file with MDDBoosterParser: {FilePath}", filePath);
        var content = File.ReadAllText(filePath);
        return ParseContent(content);
    }

    public MDDDocument ParseContent(string content)
    {
        AppLog.Debug("Beginning content parsing with MDDBoosterParser");

        // Apply raw text processors
        var processedContent = content;
        foreach (var processor in _options.RawTextProcessors)
        {
            AppLog.Debug("Applying raw text processor: {ProcessorType}", processor.GetType().Name);
            processedContent = processor.Process(processedContent);
        }

        // Parse with base parser
        AppLog.Debug("Calling base M3L parser to parse content");
        var baseDocument = _baseParser.ParseContent(processedContent);

        // Create MDDDocument
        var document = new MDDDocument
        {
            BaseDocument = baseDocument,
            RawText = content
        };

        // Convert base models to MDDModels
        AppLog.Debug("Converting {ModelCount} base models to MDDModels", baseDocument.Models.Count);
        foreach (var baseModel in baseDocument.Models)
        {
            var model = new MDDModel
            {
                BaseModel = baseModel,
                RawText = ExtractModelText(content, baseModel.Name)
            };

            AppLog.Debug("Processing model: {ModelName}", baseModel.Name);

            // Convert fields
            foreach (var baseField in baseModel.Fields)
            {
                var field = new MDDField
                {
                    BaseField = baseField,
                    RawText = ExtractFieldText(model.RawText, baseField.Name)
                };

                // Parse framework attributes
                foreach (var attrText in baseField.FrameworkAttributes)
                {
                    foreach (var parser in _options.FrameworkAttributeParsers)
                    {
                        if (parser.CanParse(attrText))
                        {
                            AppLog.Debug("Parsing framework attribute in field {FieldName}: {Attribute} with {ParserType}",
                                baseField.Name, attrText, parser.GetType().Name);
                            var attr = parser.Parse(attrText);
                            if (attr != null)
                            {
                                field.FrameworkAttributes.Add(attr);
                            }
                            break;
                        }
                    }
                }

                model.Fields.Add(field);
            }

            document.Models.Add(model);
        }

        // Convert interfaces
        AppLog.Debug("Converting {InterfaceCount} base interfaces to MDDInterfaces", baseDocument.Interfaces.Count);
        foreach (var baseInterface in baseDocument.Interfaces)
        {
            var interface_ = new MDDInterface
            {
                BaseInterface = baseInterface,
                RawText = ExtractModelText(content, baseInterface.Name + " ::interface")
            };

            AppLog.Debug("Processing interface: {InterfaceName}", baseInterface.Name);

            // Convert fields
            foreach (var baseField in baseInterface.Fields)
            {
                var field = new MDDField
                {
                    BaseField = baseField,
                    RawText = ExtractFieldText(interface_.RawText, baseField.Name)
                };

                // Parse framework attributes
                foreach (var attrText in baseField.FrameworkAttributes)
                {
                    foreach (var parser in _options.FrameworkAttributeParsers)
                    {
                        if (parser.CanParse(attrText))
                        {
                            AppLog.Debug("Parsing framework attribute in interface field {FieldName}: {Attribute} with {ParserType}",
                                baseField.Name, attrText, parser.GetType().Name);
                            var attr = parser.Parse(attrText);
                            if (attr != null)
                            {
                                field.FrameworkAttributes.Add(attr);
                            }
                            break;
                        }
                    }
                }

                interface_.Fields.Add(field);
            }

            document.Interfaces.Add(interface_);
        }

        // Convert enums
        AppLog.Debug("Converting {EnumCount} base enums to MDDEnums", baseDocument.Enums.Count);
        foreach (var baseEnum in baseDocument.Enums)
        {
            var enum_ = new MDDEnum
            {
                BaseEnum = baseEnum,
                RawText = ExtractModelText(content, baseEnum.Name + " ::enum")
            };

            AppLog.Debug("Processing enum: {EnumName}", baseEnum.Name);

            document.Enums.Add(enum_);
        }

        // Apply model processors
        foreach (var processor in _options.ModelProcessors)
        {
            AppLog.Debug("Applying model processor: {ProcessorType}", processor.GetType().Name);
            processor.Process(document);
        }

        AppLog.Information("MDDBoosterParser completed parsing: {ModelCount} models, {InterfaceCount} interfaces, {EnumCount} enums",
            document.Models.Count, document.Interfaces.Count, document.Enums.Count);
        return document;
    }

    private string ExtractModelText(string content, string modelName)
    {
        AppLog.Debug("Extracting text for model: {ModelName}", modelName);

        // Simple implementation - can be improved for robustness
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var modelStartPattern = $"## {Regex.Escape(modelName)}";
        var modelEndPattern = @"^##\s";

        var sb = new StringBuilder();
        bool inModel = false;

        foreach (var line in lines)
        {
            if (!inModel)
            {
                if (Regex.IsMatch(line, modelStartPattern))
                {
                    inModel = true;
                    sb.AppendLine(line);
                }
            }
            else
            {
                if (Regex.IsMatch(line, modelEndPattern) && !line.Contains(modelName))
                {
                    break;
                }
                sb.AppendLine(line);
            }
        }

        var result = sb.ToString();
        AppLog.Debug("Extracted {Length} characters for model: {ModelName}", result.Length, modelName);
        return result;
    }

    private string ExtractFieldText(string modelText, string fieldName)
    {
        AppLog.Debug("Extracting text for field: {FieldName}", fieldName);

        // Simple implementation - can be improved for robustness
        var lines = modelText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var fieldPattern = $@"^\s*-\s+{Regex.Escape(fieldName)}(\s|:)";

        var sb = new StringBuilder();
        bool inField = false;
        int currentIndent = 0;

        foreach (var line in lines)
        {
            if (!inField)
            {
                if (Regex.IsMatch(line, fieldPattern))
                {
                    inField = true;
                    sb.AppendLine(line);
                    currentIndent = line.TakeWhile(c => char.IsWhiteSpace(c)).Count();
                }
            }
            else
            {
                var leadingSpaces = line.TakeWhile(c => char.IsWhiteSpace(c)).Count();
                if (line.Trim().StartsWith("-") && leadingSpaces <= currentIndent)
                {
                    break;
                }
                else if (leadingSpaces > currentIndent || line.Trim().StartsWith(">"))
                {
                    sb.AppendLine(line);
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                }
                else
                {
                    break;
                }
            }
        }

        var result = sb.ToString();
        AppLog.Debug("Extracted {Length} characters for field: {FieldName}", result.Length, fieldName);
        return result;
    }
}