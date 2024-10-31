using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lavabird.Amplitude.Api;

/// <summary>
/// JSON converter for serializing <see cref="DateTimeOffset"/> objects to the format expected by the Amplitude API.
/// Amplitude can only process times as a millisecond the Unix epoch.
/// </summary>
public class AmplitudeDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
	/// <inheritdoc />
	public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		// We only use this for writing to the Amplitude API, not our own persistence
		throw new NotSupportedException();
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
	{
		writer.WriteNumberValue(value.ToUnixTimeMilliseconds());
	}
}

/// <summary>
/// JSON converter to re-read native types back from JSON when deserializing. JSON doesn't have type information
/// so we do a sensible guess (in the same way the Amplitude API will do it).
/// </summary>
public class ObjectNativeTypeConverter : JsonConverter<object>
{
	/// <inheritdoc />
	public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => true,
			JsonTokenType.False => false,
			JsonTokenType.Number when reader.TryGetInt64(out long l) => l,
			JsonTokenType.Number => reader.GetDouble(),
			JsonTokenType.String when reader.TryGetDateTime(out DateTime datetime) => datetime,
			JsonTokenType.String => reader.GetString()!,
			
			JsonTokenType.StartArray => JsonSerializer.Deserialize<object[]>(ref reader, options),
			JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options),
			
			_ => reader.GetString()
		};
	}

	/// <inheritdoc />
	public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
	{
		// We only need this for reading data back in
		throw new NotSupportedException();
	}

}