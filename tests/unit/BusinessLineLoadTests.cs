using System.Text.Json;
using WePilot.Agent.Tools;
using Xunit;

namespace WePilot.Tests;
public class BusinessLineLoadTests
{
    private static object Calendar(string date, string holiday = "F", double? hours = 8, double? rate = 50) => new {
        lineId = "L1", date, holiday, shift1 = "03", shift2 = "", hours1 = hours, hours2 = 0, rate1 = rate, rate2 = 0
    };
    private static object Plan(string date, string order = "O1", double? made = 2, double? change = 1) => new {
        lineId = "L1", date, orderId = order, plannedHours = made, changeHours = change
    };
    private static BusinessLineLoad.Result Build(object[] calendar, params object[] tasks)
    {
        var snapshot = JsonSerializer.SerializeToElement(new { calendar, loadTasks = tasks });
        var order = JsonSerializer.SerializeToElement(new { lineId = "L1", lineName = "Line 1", date = "2026-09-16" });
        return BusinessLineLoad.Build(snapshot, "O1", new[] { order });
    }
    [Fact]
    public void FullLineIncludesOtherOrdersAndAppliesCalendarRate()
    {
        var result = Build(new[] { Calendar("2026-09-16") }, Plan("2026-09-16"), Plan("2026-09-16", "OTHER", 3, 0.5));
        var day = result.Periods.Single(x => x.Grain == "day" && x.PeriodKey == "2026-09-16");
        Assert.Equal(4, day.CapacityHours);
        Assert.Equal(6.5, day.PlannedHours);
        Assert.Equal(3, day.OrderHours);
        Assert.Equal(162.5, day.LoadPercent);
        Assert.Equal(-2.5, day.RemainingHours);
        Assert.Equal(1, day.OverloadedDays);
        var month = result.Periods.Single(x => x.Grain == "month" && x.PeriodKey == "2026-09");
        Assert.Null(month.CapacityHours);
        Assert.Null(month.LoadPercent);
        Assert.Equal(29, month.MissingCalendarDays);
        Assert.Equal(6.5, month.PlannedHours);
    }
    [Fact]
    public void HolidaysMissingDaysAndInvalidCalendarAreDistinct()
    {
        var result = Build(new[] { Calendar("2026-09-16", "T"), Calendar("2026-09-18", rate: null) }, Plan("2026-09-16"));
        var holiday = result.Periods.Single(x => x.Grain == "day" && x.PeriodKey == "2026-09-16");
        Assert.Equal(0, holiday.CapacityHours);
        Assert.Null(holiday.LoadPercent);
        Assert.Equal(1, holiday.ZeroCapacityPlanDays);
        Assert.Equal(1, holiday.NonworkingDays);
        var missing = result.Periods.Single(x => x.Grain == "day" && x.PeriodKey == "2026-09-17");
        Assert.Null(missing.CapacityHours);
        Assert.Equal(0, missing.NonworkingDays);
        var invalid = result.Periods.Single(x => x.Grain == "day" && x.PeriodKey == "2026-09-18");
        Assert.Equal(1, invalid.InvalidCalendarDays);
    }
    [Fact]
    public void MissingHoursAndDuplicateCalendarsDoNotProduceFalseRates()
    {
        var result = Build(new[] { Calendar("2026-09-16"), Calendar("2026-09-16") }, Plan("2026-09-16", made: null));
        var day = result.Periods.Single(x => x.Grain == "day" && x.PeriodKey == "2026-09-16");
        Assert.Null(day.LoadPercent);
        Assert.Null(day.PlannedHours);
        Assert.Equal(1, day.InvalidCalendarDays);
        Assert.Equal(1, day.MissingPlanHours);
    }
    [Fact]
    public void MonthlyAndAnnualRatesUseSumOfHoursNotAverageOfDailyRates()
    {
        var start = new DateOnly(2026, 1, 1);
        var calendar = Enumerable.Range(0, 365).Select(i => Calendar(start.AddDays(i).ToString("yyyy-MM-dd"), hours: i == 0 ? 2 : 8, rate: 100)).ToArray();
        var result = Build(calendar, Plan("2026-01-01", made: 2, change: 0), Plan("2026-01-02", made: 4, change: 0));
        var month = result.Periods.Single(x => x.Grain == "month" && x.PeriodKey == "2026-01");
        Assert.Equal(242, month.CapacityHours);
        Assert.Equal(6d / 242 * 100, month.LoadPercent);
        var year = result.Periods.Single(x => x.Grain == "year");
        Assert.Equal(2914, year.CapacityHours);
        Assert.Equal(6d / 2914 * 100, year.LoadPercent);
        Assert.Equal(0, year.MissingCalendarDays);
    }
}
