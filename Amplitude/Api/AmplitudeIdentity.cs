﻿namespace Lavabird.Amplitude.Api;

/// <summary>
/// Represents the identity of a user or device for an event. Enforces that at least one must be specified.
/// </summary>
public class AmplitudeIdentity
{
	/// <summary>
	/// ID of the user responsible for this event. Can be a generated anonymous ID. One of either a UserID or DeviceID
	/// must be set when calling the Amplitude API.
	/// </summary>
	public string? UserId { get; private set; }
    
	/// <summary>
	/// The unique ID for the device responsible for this event. One of either a UserID or DeviceID must be set when
	/// calling the Amplitude API. If a DeviceId isn't sent with the event, then a hashed version of the UserId will
	/// be generated by Amplitude.
	/// </summary>
	public string? DeviceId { get; private set; }

	public AmplitudeIdentity(string? userId, string? deviceId)
	{
		if (userId == null && deviceId == null)
		{
			throw new ArgumentException("At least one of UserId or DeviceId must be specified.");
		}
		UserId = userId;
		DeviceId = deviceId;
	}
}