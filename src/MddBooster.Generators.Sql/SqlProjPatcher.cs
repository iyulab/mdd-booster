using System.Xml.Linq;

namespace MddBooster.Generators.Sql;

public static class SqlProjPatcher
{
    public static void Patch(string sqlProjPath, string generatedFolderRelative, IEnumerable<string> generatedFileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlProjPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedFolderRelative);
        ArgumentNullException.ThrowIfNull(generatedFileNames);

        var targetEntries = generatedFileNames
            .Select(name => Path.Combine(generatedFolderRelative, name).Replace('/', '\\'))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var doc = XDocument.Load(sqlProjPath, LoadOptions.PreserveWhitespace);
        var root = doc.Root ?? throw new InvalidOperationException($"유효하지 않은 sqlproj: {sqlProjPath}");
        var ns = root.GetDefaultNamespace();

        // 1. 기존의 generatedFolderRelative 아래 Build Include 노드 전부 제거.
        //    각 요소의 직전 whitespace 텍스트 노드도 함께 제거하여 호출이
        //    거듭되어도 공백이 축적되지 않게 한다(idempotent).
        var managedBuildNodes = root.Descendants(ns + "Build")
            .Where(b =>
            {
                var include = (string?)b.Attribute("Include");
                return include is not null &&
                    include.Replace('/', '\\').StartsWith(generatedFolderRelative + "\\", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        XElement? itemGroupToUse = null;
        foreach (var node in managedBuildNodes)
        {
            itemGroupToUse ??= node.Parent;
            if (node.PreviousNode is XText leadingWs
                && string.IsNullOrWhiteSpace(leadingWs.Value))
            {
                leadingWs.Remove();
            }
            node.Remove();
        }

        // 2. 삽입할 ItemGroup 확보 (없으면 신규 생성)
        if (itemGroupToUse is null)
        {
            itemGroupToUse = root.Elements(ns + "ItemGroup").FirstOrDefault();
            if (itemGroupToUse is null)
            {
                itemGroupToUse = new XElement(ns + "ItemGroup");
                root.Add(itemGroupToUse);
            }
        }

        // 3. 신규 엔트리 삽입 (정렬 순서로).
        //    LoadOptions.PreserveWhitespace + SaveOptions.None 은 기존 공백만
        //    보존할 뿐 새로 추가되는 요소 사이의 개행을 만들지 않는다. 사람이
        //    diff/리뷰할 수 있도록 각 Build 요소 앞에 명시적으로 개행+들여쓰기
        //    텍스트 노드를 삽입한다. 들여쓰기는 ItemGroup의 기존 자식/꼬리
        //    공백에서 추정하며, 없으면 4-space 기본값.
        var indent = DetectChildIndent(itemGroupToUse) ?? "    ";
        var newline = Environment.NewLine;

        // ItemGroup 마지막 자식이 요소라면(공백 없음) 닫는 태그 직전 꼬리
        // 공백이 없는 상태. 그대로 끝에 Add 하면 새 노드가 마지막 요소와
        // 한 줄에 붙는다. 꼬리 텍스트가 이미 있으면(예: "\n  ") 그 앞에
        // 요소를 끼워 넣는다.
        var tailText = itemGroupToUse.LastNode as XText;
        foreach (var include in targetEntries)
        {
            var prefix = new XText(newline + indent);
            var element = new XElement(ns + "Build", new XAttribute("Include", include));
            if (tailText is not null)
            {
                tailText.AddBeforeSelf(prefix);
                tailText.AddBeforeSelf(element);
            }
            else
            {
                itemGroupToUse.Add(prefix);
                itemGroupToUse.Add(element);
            }
        }

        doc.Save(sqlProjPath, SaveOptions.None);
    }

    private static string? DetectChildIndent(XElement itemGroup)
    {
        // 기존 자식 앞의 공백 텍스트 노드를 찾아 들여쓰기 문자열만 추출.
        foreach (var node in itemGroup.Nodes())
        {
            if (node is XText text)
            {
                var s = text.Value;
                var lastNewline = s.LastIndexOfAny(['\n', '\r']);
                if (lastNewline >= 0 && lastNewline + 1 < s.Length)
                {
                    var tail = s[(lastNewline + 1)..];
                    if (tail.Length > 0 && tail.All(char.IsWhiteSpace))
                        return tail;
                }
            }
        }
        return null;
    }
}
