using System.Threading;
using System.Threading.Tasks;

namespace Lavabird.Amplitude.Api;

public interface IAmplitudeApi
{
	/// <summary>
	/// Sends the given data about the current user or device to the Amplitude HTTP API.
	/// </summary>
	/// <param name="identification">The identification data to send.</param>
	/// <returns></returns>
	public Task<AmplitudeApiResult> Identify(AmplitudeIdentify identification, CancellationToken ct);

	/// <summary>
	/// Sends the given batch of events to the Amplitude HTTP API
	/// </summary>
	/// <param name="events">
	/// The batch of events to send. Amplitude recommends sending no more than 10 events in a single batch. Their API
	/// supports more in bursts, but the client risks being rate limited. 
	/// </param>
	public Task<AmplitudeApiResult> SendEvents(IEnumerable<AmplitudeEvent> events, CancellationToken ct);
}