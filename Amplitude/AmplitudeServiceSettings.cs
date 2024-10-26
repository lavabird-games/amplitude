namespace Lavabird.Amplitude;

public class AmplitudeServiceSettings
{
	/// <summary>
	/// The number of seconds to wait before a retry if the Amplitude API returns an error or throttles us.
	/// Default value of 30s recommended by Amplitude docs.
	/// </summary>
	public uint BackOffDelaySeconds { get; set; } = 30;

	/// <summary>
	/// The number of seconds to wait after receiving an event before we dispatch it. This allows the
	/// service to group multiple calls in quick succession together. Recommended 1 for desktop and 10s
	/// for mobile devices.
	/// </summary>
	public uint DispatchBatchPeriodSeconds { get; set; } = 1;

	/// <summary>
	/// The maximum amount of time a call can remain in the event queue (includes events that have been
	/// persisted after a session). Max recommended time is 7 days because after that we can't guarantee an
	/// event is unique if it gets replayed (unique event ID validity is 7 days for the Amplitude events API).
	/// </summary>
	public uint QueuedApiCallsTtlSeconds { get; set; } = 60 * 60 * 24 * 7;

	/// <summary>
	/// The time period between background saves of the event queue. Ensures we persist any outstanding
	/// events in the event of a crash, or on environments where we may not have full control of the
	/// application lifecycle. Will not write if there is no new data to write. Setting to 0 will disable
	/// background writes (the persistence store will still be used on startup and graceful exit).
	/// </summary>
	public uint BackgroundWritePeriodSeconds { get; set; } = 2;

	/// <summary>
	/// Whether to use the EU residency endpoint for Amplitude or the standard one.
	/// </summary>
	public bool UseEuResidency { get; set; } = false;
}