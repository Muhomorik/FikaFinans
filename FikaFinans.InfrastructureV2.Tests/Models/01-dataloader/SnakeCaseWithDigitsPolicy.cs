using System.Text;
using System.Text.Json;

namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public sealed class SnakeCaseWithDigitsPolicy : JsonNamingPolicy
{
    public static readonly SnakeCaseWithDigitsPolicy Instance = new();

    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0)
            {
                var prev = name[i - 1];
                if (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev)))
                    sb.Append('_');
                else if (char.IsDigit(c) && char.IsLower(prev))
                    sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
