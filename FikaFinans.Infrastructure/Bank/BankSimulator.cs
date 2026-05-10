using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace FikaFinans.Infrastructure.Bank;

/// <summary>
/// Virtual clock for the bank emulator. Extends <see cref="HistoricalScheduler"/> so tests
/// can inject a <c>BankSimulator(testStartTime)</c> and drive time with the same
/// <c>AdvanceTo</c> / <c>AdvanceBy</c> API used by <c>TestScheduler</c>.
/// </summary>
public sealed class BankSimulator : HistoricalScheduler, IDisposable
{
    private const int NightHour      = 23;
    private const int MarketOpenHour = 9;
    private const int MarketCloseHour = 17;

    private readonly Subject<DateTimeOffset> _timeChanged = new();
    private readonly Subject<DateTimeOffset> _nightTick   = new();

    public IObservable<DateTimeOffset> TimeChanged => _timeChanged.AsObservable();
    public IObservable<DateTimeOffset> NightTick   => _nightTick.AsObservable();
    public bool IsMarketOpen => Now.Hour >= MarketOpenHour && Now.Hour < MarketCloseHour;

    public BankSimulator()
        : base(new DateTimeOffset(2025, 1, 2, 8, 0, 0, TimeSpan.FromHours(1))) { }

    public BankSimulator(DateTimeOffset startTime) : base(startTime) { }

    // Hide base AdvanceTo so every path emits observables.
    public new void AdvanceTo(DateTimeOffset target)
    {
        var from = Now;
        base.AdvanceTo(target);
        Emit(from, Now);
    }

    // Hide base AdvanceBy so every path emits observables.
    public new void AdvanceBy(TimeSpan duration)
    {
        var from = Now;
        base.AdvanceBy(duration);
        Emit(from, Now);
    }

    /// <summary>Advance to 23:00 tonight (or tomorrow night if already past 23:00).</summary>
    public void AdvanceToNight()
    {
        var night = new DateTimeOffset(Now.Year, Now.Month, Now.Day, NightHour, 0, 0, Now.Offset);
        if (Now.Hour >= NightHour)
            night = night.AddDays(1);
        AdvanceTo(night);
    }

    /// <summary>Pass through tonight's settlement window, then jump to next-morning open.</summary>
    public void AdvanceToNextDay()
    {
        if (Now.Hour < NightHour)
        {
            var tonight = new DateTimeOffset(Now.Year, Now.Month, Now.Day, NightHour, 0, 0, Now.Offset);
            AdvanceTo(tonight);
        }

        var nextMorning = new DateTimeOffset(Now.Year, Now.Month, Now.Day, MarketOpenHour, 0, 0, Now.Offset)
            .AddDays(1);
        AdvanceTo(nextMorning);
    }

    private void Emit(DateTimeOffset from, DateTimeOffset to)
    {
        _timeChanged.OnNext(to);
        CheckNightCrossing(from, to);
    }

    private void CheckNightCrossing(DateTimeOffset from, DateTimeOffset to)
    {
        if (from.Date == to.Date)
        {
            if (from.Hour < NightHour && to.Hour >= NightHour)
                _nightTick.OnNext(to);
            return;
        }

        // Multi-day span — fire one NightTick per crossed midnight.
        if (from.Hour < NightHour)
            _nightTick.OnNext(new DateTimeOffset(from.Year, from.Month, from.Day, NightHour, 0, 0, from.Offset));

        var d = from.Date.AddDays(1);
        while (d < to.Date)
        {
            _nightTick.OnNext(new DateTimeOffset(d.Year, d.Month, d.Day, NightHour, 0, 0, from.Offset));
            d = d.AddDays(1);
        }

        if (to.Hour >= NightHour)
            _nightTick.OnNext(to);
    }

    public void Dispose()
    {
        _timeChanged.Dispose();
        _nightTick.Dispose();
    }
}
