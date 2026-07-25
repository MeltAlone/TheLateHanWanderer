using LateHan.Game.Domain;

namespace LateHan.Game.Domain.Tests;

public sealed class GameDateTests
{
    [Theory]
    [InlineData(1, TenDayPeriod.Early)]
    [InlineData(10, TenDayPeriod.Early)]
    [InlineData(11, TenDayPeriod.Middle)]
    [InlineData(20, TenDayPeriod.Middle)]
    [InlineData(21, TenDayPeriod.Late)]
    [InlineData(30, TenDayPeriod.Late)]
    public void PeriodUsesTenDayBoundaries(int day, TenDayPeriod expected)
    {
        Assert.Equal(expected, new GameDate(189, 8, day).Period);
    }

    [Fact]
    public void AddDaysCrossesMonthAndYearBoundaries()
    {
        Assert.Equal(new GameDate(190, 1, 2), new GameDate(189, 12, 29).AddDays(3));
    }

    [Fact]
    public void InvalidDatesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(189, 13, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(189, 1, 31));
    }
}
