using System.Globalization;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="LoggerAnalyticsTracker"/>. Verifica que el
/// serializer del Track output es culture-invariant — bug detectado
/// post-cap-310 cuando el smoke log mostró "durationMs=0,7629" con
/// coma decimal (host CurrentCulture=es-CO). Ingestion pipelines
/// (Logstash/Vector/Splunk) parsean asumiendo punto decimal.
/// </summary>
public sealed class LoggerAnalyticsTrackerTests
{
    private static (LoggerAnalyticsTracker tracker, ILogger<LoggerAnalyticsTracker> logger) BuildSut()
    {
        var logger = Substitute.For<ILogger<LoggerAnalyticsTracker>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var tracker = new LoggerAnalyticsTracker(logger);
        return (tracker, logger);
    }

    private static string CapturedFormattedValue(ILogger<LoggerAnalyticsTracker> logger)
    {
        // ILogger.Log es invocado con un state que ToStringea al formato
        // final del template. El último argumento del último call es el
        // formatter Func<state,exception,string>; lo ejecutamos.
        var calls = logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .ToList();
        Assert.NotEmpty(calls);
        var last = calls[^1];
        var args = last.GetArguments();
        var state = args[2]!;
        var formatter = (Delegate)args[4]!;
        return (string)formatter.DynamicInvoke(state, null)!;
    }

    [Fact]
    public void Track_DoublePropertyValue_FormatsWithInvariantDecimalPoint()
    {
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            // Force es-CO — el bug ocurre cuando CurrentCulture usa coma decimal.
            CultureInfo.CurrentCulture = new CultureInfo("es-CO");
            var (sut, logger) = BuildSut();

            sut.Track("retention.swept", new Dictionary<string, object?>
            {
                ["policy"] = "audit",
                ["durationMs"] = 0.7629,
                ["success"] = true,
            });

            var rendered = CapturedFormattedValue(logger);
            Assert.Contains("durationMs=0.7629", rendered);
            Assert.DoesNotContain("0,7629", rendered);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void Track_DecimalAndIntValues_AlsoCultureInvariant()
    {
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var (sut, logger) = BuildSut();

            sut.Track("event.test", new Dictionary<string, object?>
            {
                ["amount"] = 1234.56m,
                ["count"] = 42,
            });

            var rendered = CapturedFormattedValue(logger);
            Assert.Contains("amount=1234.56", rendered);
            Assert.Contains("count=42", rendered);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void Track_DateTime_UsesInvariantRoundTripFormat()
    {
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-CO");
            var (sut, logger) = BuildSut();
            var ts = new DateTime(2026, 4, 29, 15, 57, 18, DateTimeKind.Utc);

            sut.Track("event.test", new Dictionary<string, object?>
            {
                ["at"] = ts,
            });

            var rendered = CapturedFormattedValue(logger);
            Assert.Contains("at=2026-04-29T15:57:18", rendered);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void Track_StringValue_QuotedAndUnchanged()
    {
        var (sut, logger) = BuildSut();

        sut.Track("event.test", new Dictionary<string, object?>
        {
            ["policy"] = "audit",
        });

        var rendered = CapturedFormattedValue(logger);
        Assert.Contains("policy=\"audit\"", rendered);
    }

    [Fact]
    public void Track_NullValue_RendersLiteralNull()
    {
        var (sut, logger) = BuildSut();

        sut.Track("event.test", new Dictionary<string, object?>
        {
            ["maybe"] = null,
        });

        var rendered = CapturedFormattedValue(logger);
        Assert.Contains("maybe=null", rendered);
    }

    [Fact]
    public void Track_EmptyEventName_DoesNotEmit()
    {
        var (sut, logger) = BuildSut();

        sut.Track("", new Dictionary<string, object?> { ["k"] = "v" });

        Assert.DoesNotContain(
            logger.ReceivedCalls(),
            c => c.GetMethodInfo().Name == nameof(ILogger.Log));
    }
}
