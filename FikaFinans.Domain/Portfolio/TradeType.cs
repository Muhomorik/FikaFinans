using System.Text.Json.Serialization;

namespace FikaFinans.Domain.Portfolio;

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
