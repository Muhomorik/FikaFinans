using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FikaFinans.Infrastructure.Serialization;

namespace FikaFinans.Infrastructure.Pipeline.Json;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy   = SnakeCaseWithDigitsPolicy.Instance,
        DictionaryKeyPolicy    = SnakeCaseWithDigitsPolicy.Instance,
        WriteIndented          = true,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters =
        {
            new IsinJsonConverter(),
            new JsonStringEnumConverter(),
        },
    };
}
