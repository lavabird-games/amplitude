using System.Text.Json.Serialization;

namespace Lavabird.Amplitude.Api;

public class AmplitudeIdentify : AmplitudeBase
{
	/// <summary>
	/// A dictionary of key-value pairs that represent data tied to the user. Each distinct value appears as a user
	/// segment on the Amplitude dashboard. Object depth may not exceed 40 layers. You can store property values in an
	/// array and date values are transformed into string values.
	/// </summary>
	[JsonPropertyName("user_properties")]
	[JsonInclude]
	public Dictionary<string, object> Properties { get; private set; } = new();

	public AmplitudeIdentify(AmplitudeIdentity identity, Dictionary<string, object>? userProperties = null) : base(identity)
	{
		if (userProperties != null)
		{
			// We want to copy the keys rather than reassign as we don't control the input dictionary
			foreach (var property in userProperties)
			{
				Properties[property.Key] = property.Value;
			}
		}
	}
	
	[JsonConstructor]
	private AmplitudeIdentify()
	{
		// For deserialization
	}
}
