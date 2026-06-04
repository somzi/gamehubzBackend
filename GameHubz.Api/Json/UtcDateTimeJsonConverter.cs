using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHubz.Api.Json
{
    /// <summary>
    /// Serializes all DateTime values with an explicit UTC marker ("Z"), so JS clients
    /// reliably parse them as UTC (and then can render in the user's local timezone).
    ///
    /// We store DateTime in UTC throughout the backend (DateTimeProvider, DateTime.UtcNow),
    /// but Npgsql reads timestamp columns back with Kind=Unspecified. Without an explicit
    /// marker in the JSON string ("...T12:38:00"), JavaScript's Date constructor treats
    /// the value as local time, which causes the wrong wall-clock time on the client.
    /// </summary>
    public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = reader.GetDateTime();
            return raw.Kind == DateTimeKind.Utc
                ? raw
                : DateTime.SpecifyKind(raw, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            DateTime utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc) // Unspecified → assume UTC (our convention)
            };
            writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }
    }

    public class UtcNullableDateTimeJsonConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var raw = reader.GetDateTime();
            return raw.Kind == DateTimeKind.Utc
                ? raw
                : DateTime.SpecifyKind(raw, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is null) { writer.WriteNullValue(); return; }
            DateTime v = value.Value;
            DateTime utc = v.Kind switch
            {
                DateTimeKind.Utc => v,
                DateTimeKind.Local => v.ToUniversalTime(),
                _ => DateTime.SpecifyKind(v, DateTimeKind.Utc)
            };
            writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }
    }
}
