using System.Text.Json.Serialization;
using System.Threading;

namespace Lavabird.Amplitude.Api;

/// <summary>
/// Contains the data for a single event to be sent to the Amplitude API.
/// </summary>
public class AmplitudeEvent : AmplitudeBase
{
	/// <summary>
	/// Incrementing counter for each event created this session. Converted to an EventId and Used by Amplitude to
	/// preserve event order when events are generated with the same timestamp.
	/// </summary>
	private static long lastEventId = 0;
	
	/// <summary>
	/// The type (name) of this event. Must be specified when calling the Amplitude API.
	/// </summary>
	[JsonPropertyName("event_type")]
	public string EventType { get; set; }
	
	/// <summary>
	/// A dictionary of key-value pairs that represent data to send to Amplitude along with the event. You can store
	/// property values in an array and date values are transformed into string values.
	/// </summary>
	[JsonPropertyName("event_properties")]
	[JsonInclude] // Private setter
	public Dictionary<string, object> Properties { get; private set; } = new();
	
	/// <summary>
	/// Optional timestamp of the event. If time isn't sent with the event, then it's set to the time the API
	/// call was made.
	/// </summary>
	[JsonPropertyName("time")]
	public DateTimeOffset? Time { get; set; }
	
	/// <summary>
	/// Sequentially counter for each event to preserve event order when events are generated with the same timestamp.
	/// </summary>
	[JsonPropertyName("event_id")]
	[JsonInclude] // Private setter
	public long EventId { get; private set; }
	
	/// <summary>
	/// The start time of the session in milliseconds since epoch (Unix Timestamp), necessary if you want to associate
	/// events with a particular system. A session_id of -1 is the same as no session_id specified. Normally the
	/// AmplitudeService will generate these automatically.
	/// </summary>
	[JsonPropertyName("session_id")]
	public long? SessionId { get; set; }

	/// <summary>
	/// Unique identifier for this event. Amplitude de-duplicates subsequent events sent with the same DeviceId and
	/// InsertId within the past 7 days. Allows for events to be safely replayed in the case of network errors. An
	/// InsertId will be automatically generated for each event on creation.
	/// </summary>
	[JsonPropertyName("insert_id")]
	[JsonInclude] // Private setter
	public string? InsertId { get; private set; }

	/// <summary>
	/// The IP address of the user or device making this request. Used by Amplitude to determine geolocation.
	/// The default value of "$remote" tells Amplitude to use the source IP of the request.
	/// </summary>
	[JsonPropertyName("ip")]
	public string? IpAddress { get; set; } = "$remote";
	
	public AmplitudeEvent(AmplitudeIdentity identity, string eventName, 
		Dictionary<string, object>? eventProperties = null,
		Dictionary<string, object>? commonProperties = null)
		: base(identity)
	{
		EventId = Interlocked.Increment(ref lastEventId);
		InsertId = Guid.NewGuid().ToString("N");
		
		Time = DateTimeOffset.UtcNow;

		EventType = eventName;
		
		// Extra properties are merged with the event properties. If there are any conflicts, the event properties
		// have priority. Extra properties are used for adding common data to every event.

		if (eventProperties != null)
		{
			// We want to copy the keys rather than reassign as we don't control the input dictionary
			foreach (var property in eventProperties)
			{
				Properties[property.Key] = property.Value;
			}
		}
		
		if (commonProperties != null)
		{
			// Only add extra props that wouldn't overwrite event props
			foreach (var extraProp in commonProperties)
			{
				if (!Properties.ContainsKey(extraProp.Key))
				{
					Properties.Add(extraProp.Key, extraProp.Value);
				}
			}
		}
	}
	
	[JsonConstructor]
	public AmplitudeEvent()
	{
		// For deserialization
	}
}