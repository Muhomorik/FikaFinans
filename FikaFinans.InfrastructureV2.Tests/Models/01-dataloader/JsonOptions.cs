using System.Text.Encodings.Web;
using System.Text.Json;

namespace FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = SnakeCaseWithDigitsPolicy.Instance,
        DictionaryKeyPolicy  = SnakeCaseWithDigitsPolicy.Instance,
        WriteIndented        = true,
        Encoder              = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };
}
