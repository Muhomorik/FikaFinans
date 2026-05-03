using System.Text.Json.Serialization;

namespace FikaFinans.InfrastructureV2.Tests.Models.PortfolioConstructor;

[JsonConverter(typeof(JsonStringEnumConverter<TradeType>))]
public enum TradeType
{
    [JsonStringEnumMemberName("Buy")]         Buy,
    [JsonStringEnumMemberName("TopUp")]       TopUp,
    [JsonStringEnumMemberName("Trim")]        Trim,
    [JsonStringEnumMemberName("Sell")]        Sell,
    [JsonStringEnumMemberName("PartialSell")] PartialSell,
    [JsonStringEnumMemberName("Hold")]        Hold,
    [JsonStringEnumMemberName("NoOp")]        NoOp,
}
