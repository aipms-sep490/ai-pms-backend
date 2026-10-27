using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIPMS.Application.Common.Serialization;

/// <summary>
/// Ensures all DateTime values are serialized as UTC ISO 8601 strings ending with 'Z'
/// and deserialized with DateTimeKind.Utc to prevent timezone drifting (BE-04).
/// Correctly converts Local times via ToUniversalTime and treats Unspecified as UTC.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dt = reader.GetDateTime();
        return NormalizeToUtc(dt);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(NormalizeToUtc(value));
    }

    public static DateTime NormalizeToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }
}

public sealed class NullableUtcDateTimeJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var dt = reader.GetDateTime();
        return UtcDateTimeJsonConverter.NormalizeToUtc(dt);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(UtcDateTimeJsonConverter.NormalizeToUtc(value.Value));
    }
}
