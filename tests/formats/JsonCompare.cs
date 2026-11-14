using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Build-194: JSON trees equal as the Python writes them and the game reads them: objects by key in any order,
/// arrays in order, strings and booleans exactly, numbers by value. A number the Python writes as an integer must be an
/// integer here too (the game reads such fields with GetInt32); one it writes as a float may come out integral ("90").</summary>
public static class JsonCompare
{
    public static void Compare(JsonNode? expected,JsonNode? actual,string path,List<string> differences)
    {
        switch (expected,actual)
        {
            case (null,null): return;
            case (JsonObject e,JsonObject a):
                foreach (var (key,value) in e)
                    if (a.ContainsKey(key)) Compare(value,a[key],$"{path}.{key}",differences);
                    else differences.Add($"{path}.{key}: missing");
                differences.AddRange(a.Where(p => !e.ContainsKey(p.Key)).Select(p => $"{path}.{p.Key}: not in the Python's"));
                return;
            case (JsonArray e,JsonArray a):
                if (e.Count != a.Count) differences.Add($"{path}: {a.Count} items, expected {e.Count}");
                for (int i = 0; i < Math.Min(e.Count,a.Count); i++) Compare(e[i],a[i],$"{path}[{i}]",differences);
                return;
            case (JsonValue e,JsonValue a):
                if (e.GetValueKind() is JsonValueKind.Number && a.GetValueKind() is JsonValueKind.Number)
                {
                    string expectedText = e.ToJsonString(), actualText = a.ToJsonString();
                    static bool Integer(string text) => !text.Contains('.') && !text.Contains('e') && !text.Contains('E');
                    if (double.Parse(expectedText,CultureInfo.InvariantCulture) != double.Parse(actualText,CultureInfo.InvariantCulture))
                        differences.Add($"{path}: {actualText}, expected {expectedText}");
                    else if (Integer(expectedText) && !Integer(actualText)) differences.Add($"{path}: {actualText} is not an integer like the Python's {expectedText}");
                }
                else if (!JsonNode.DeepEquals(e,a)) differences.Add($"{path}: {a.ToJsonString()}, expected {e.ToJsonString()}");
                return;
            default:
                differences.Add($"{path}: {actual?.ToJsonString() ?? "null"}, expected {expected?.ToJsonString() ?? "null"}");
                return;
        }
    }
}
