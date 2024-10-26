namespace Lavabird.Amplitude.Api;

/// <summary>
/// The result of sending data to the Amplitude API.
/// </summary>
public enum AmplitudeApiResult
{
	/// <summary>
	/// The API call was successful.
	/// </summary>
	Success,
	
	/// <summary>
	/// The data contained within the API request was bad or malformed.
	/// </summary>
	BadData,
	
	/// <summary>
	/// The API key used to authenticate the request was invalid.
	/// </summary>
	InvalidApiKey,
	
	/// <summary>
	/// The size of the payload was too large, or included too many properties.
	/// </summary>
	TooLarge,
	
	/// <summary>
	/// The API is throttling requests from this client due to a high number of requests in a short period.
	/// </summary>
	Throttled,
	
	/// <summary>
	/// The API server encountered an error while processing the request.
	/// </summary>
	ServerError,
	
	/// <summary>
	/// A network error occurred while trying to reach the Amplitude API.
	/// </summary>
	NetworkError,
}