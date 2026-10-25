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
}
