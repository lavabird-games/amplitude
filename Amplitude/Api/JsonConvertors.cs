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