using Meitou.Engine.Time;

namespace Meitou.Tests.Engine;

public class GameClockTests
{
    const double Hour = GameClock.SecondsPerGameHour;

    [Fact]
    public void Start_values_and_wrapping()
    {
        var game = new GameClock();
        Assert.Equal(13, game.HourOfDay, 9);
        Assert.Equal(1, game.Day);
        Assert.Equal(0, game.HoursSinceStart);
        Assert.Equal("13:00", game.TimeText);
        Assert.Equal("Day: 1", game.DayText);
        game.Advance(11 * Hour + 0.01);   // 13:00 + 11 h = 00:00 of day 2
        Assert.Equal(2, game.Day);
        Assert.Equal("00:00", game.TimeText);
        game.Advance(1.5 * Hour);
        Assert.Equal("01:30", game.TimeText);
        game.SetHourOfDay(6);
        Assert.Equal(6, game.HourOfDay, 9);
        Assert.Equal(2, game.Day);
        Assert.Equal(7, new GameClock(7.99).Hour);
        Assert.Equal(59, new GameClock(7.9999).Minute);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(3, 7, 45)]
    [InlineData(2, 13, 7)]
    [InlineData(9, 23, 59)]
    public void Setting_the_time_of_a_loaded_save_reads_back_as_the_same_day_hour_and_minute(long day, int hour, int minute)
    {
        var game = new GameClock(13, 1);
        game.Advance(5 * Hour);
        game.SetTime(day, hour + minute / 60.0);
        Assert.Equal((day, hour, minute), (game.Day, game.Hour, game.Minute));
        game.Advance(30);   // 30 game seconds is 16 game minutes (a game hour is 109 s)
        Assert.True(game.Day * 24 * 60 + game.Hour * 60 + game.Minute > day * 24 * 60 + hour * 60 + minute);
        // A clock whose own start is another day and hour lands on the same time.
        var other = new GameClock(2, 5);
        other.SetTime(day, hour + minute / 60.0);
        Assert.Equal((day, hour, minute), (other.Day, other.Hour, other.Minute));
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(5.5, 0.25)]
    [InlineData(6, 0.5)]
    [InlineData(7, 1)]
    [InlineData(12, 1)]
    [InlineData(21, 1)]
    [InlineData(22, 0.5)]
    [InlineData(22.5, 0.25)]
    [InlineData(23, 0)]
    [InlineData(23.5, 0)]
    public void Daylight_ramps_over_two_hours_after_sunrise_and_before_sunset(double hour, double factor)
    {
        Assert.Equal(factor, new GameClock(startHour: hour).DaylightFactor, 9);
    }
}
