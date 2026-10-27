using System.Text.Json;
using AIPMS.Application.Common.Serialization;
using Xunit;

namespace AIPMS.UnitTests.Serialization;

public sealed class UtcDateTimeJsonConverterTests
{
    private sealed record TestModel(DateTime Timestamp, DateTime? OptionalTimestamp);

    private readonly JsonSerializerOptions _options;

    public UtcDateTimeJsonConverterTests()
    {
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _options.Converters.Add(new UtcDateTimeJsonConverter());
        _options.Converters.Add(new NullableUtcDateTimeJsonConverter());
    }

    [Fact]
    public void Serializes_Unspecified_DateTime_As_Utc_With_Z()
    {
        var model = new TestModel(
            new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Unspecified),
            new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Unspecified));

        var json = JsonSerializer.Serialize(model, _options);

        Assert.Contains("\"timestamp\":\"2026-09-01T08:30:00Z\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"optionalTimestamp\":\"2026-09-01T10:00:00Z\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serializes_Local_DateTime_Converts_To_Universal_Time_With_Z()
    {
        var localNow = DateTime.Now;
        var expectedUtc = localNow.ToUniversalTime();

        var model = new TestModel(localNow, null);
        var json = JsonSerializer.Serialize(model, _options);

        var roundTripped = JsonSerializer.Deserialize<TestModel>(json, _options);
        Assert.NotNull(roundTripped);
        Assert.Equal(DateTimeKind.Utc, roundTripped.Timestamp.Kind);
        Assert.Equal(expectedUtc.Year, roundTripped.Timestamp.Year);
        Assert.Equal(expectedUtc.Month, roundTripped.Timestamp.Month);
        Assert.Equal(expectedUtc.Day, roundTripped.Timestamp.Day);
        Assert.Equal(expectedUtc.Hour, roundTripped.Timestamp.Hour);
        Assert.Equal(expectedUtc.Minute, roundTripped.Timestamp.Minute);
        Assert.Equal(expectedUtc.Second, roundTripped.Timestamp.Second);
    }

    [Fact]
    public void Serializes_Null_OptionalTimestamp_Correctly()
    {
        var model = new TestModel(
            new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            null);

        var json = JsonSerializer.Serialize(model, _options);

        Assert.Contains("\"timestamp\":\"2026-09-01T08:30:00Z\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"optionalTimestamp\":null", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserializes_String_To_Utc_Kind()
    {
        var json = """{"timestamp":"2026-09-01T08:30:00Z","optionalTimestamp":"2026-09-01T10:00:00Z"}""";

        var model = JsonSerializer.Deserialize<TestModel>(json, _options);

        Assert.NotNull(model);
        Assert.Equal(DateTimeKind.Utc, model.Timestamp.Kind);
        Assert.True(model.OptionalTimestamp.HasValue);
        Assert.Equal(DateTimeKind.Utc, model.OptionalTimestamp.Value.Kind);
    }

    [Fact]
    public void Deserializes_Offset_String_Converts_To_Utc_Correctly()
    {
        // 15:30:00 at UTC+7 should be 08:30:00 UTC
        var json = """{"timestamp":"2026-09-01T15:30:00+07:00","optionalTimestamp":null}""";

        var model = JsonSerializer.Deserialize<TestModel>(json, _options);

        Assert.NotNull(model);
        Assert.Equal(DateTimeKind.Utc, model.Timestamp.Kind);
        Assert.Equal(new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc), model.Timestamp);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NormalizeToUtc_Always_Returns_Utc_Kind(DateTimeKind kind)
    {
        var dt = kind == DateTimeKind.Local ? DateTime.Now : new DateTime(2026, 10, 26, 12, 0, 0, kind);
        var normalized = UtcDateTimeJsonConverter.NormalizeToUtc(dt);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }
}
