namespace LateHan.Game.Domain;

public enum TenDayPeriod
{
    Early,
    Middle,
    Late,
}

public readonly record struct GameDate
{
    public GameDate(int year, int month, int day)
    {
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        if (day is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(day));
        }

        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }

    public int Month { get; }

    public int Day { get; }

    public TenDayPeriod Period => Day <= 10 ? TenDayPeriod.Early : Day <= 20 ? TenDayPeriod.Middle : TenDayPeriod.Late;

    public GameDate AddDays(int days)
    {
        if (days < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days));
        }

        var absoluteDay = ((Year - 1) * 360) + ((Month - 1) * 30) + (Day - 1) + days;
        return new GameDate((absoluteDay / 360) + 1, ((absoluteDay % 360) / 30) + 1, (absoluteDay % 30) + 1);
    }

    public int DaysUntil(GameDate other) => other.ToOrdinal() - ToOrdinal();

    public override string ToString() => $"{Year}年{Month}月{Day}日（{Period.ToChinese()}）";

    private int ToOrdinal() => ((Year - 1) * 360) + ((Month - 1) * 30) + Day;
}

public static class TenDayPeriodExtensions
{
    public static string ToChinese(this TenDayPeriod period) => period switch
    {
        TenDayPeriod.Early => "上旬",
        TenDayPeriod.Middle => "中旬",
        TenDayPeriod.Late => "下旬",
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };
}
