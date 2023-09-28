using System.Text.Json;

namespace AtelierTool;

internal class UnderscoreNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name) || 2 > name.Length || (name[0] == '_' && !char.IsUpper(name[1])))
        {
            return name;
        }

        var arr = name.ToCharArray();
        arr[0] = char.ToLowerInvariant(arr[0]);
        return "_" + new string(arr);
    }
}