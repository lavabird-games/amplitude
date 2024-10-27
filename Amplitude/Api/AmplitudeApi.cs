using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Lavabird.Amplitude.Api;

internal class AmplitudeApi : IAmplitudeApi
{
	/// <summary>
	/// The API key we are using to make requests to the Amplitude API.
	/// </summary>
	private readonly string apiKey;
	
	/// <summary>
	/// The HTTP client used to make requests to the Amplitude API.
	/// </summary>
	private readonly HttpClient httpClient;
	
	/// <summary>
	/// The settings used to serialize JSON in a format for the Amplitude API.
	/// </summary>
	private readonly JsonSerializerOptions jsonSerializerOptions;
	
	/// <summary>
	/// Optional logger for this service.
	/// </summary>
	private readonly Action<LogLevel, string>? logger;

	/// <summary>
	/// Whether to use the EU residency endpoint for Amplitude or the standard one
	/// </summary>
	private readonly bool euResidency;

	/// <summary>
	/// The maximum number of events that can be batched together (as recommended by Amplitude docs).
	/// </summary>
	public const int MaxEventBatchSize = 10;

	public AmplitudeApi(string apiKey, Action<LogLevel, string>? logger = null, 
		bool euResidency = false, HttpMessageHandler? httpMessageHandler = null)
	{
		this.apiKey = apiKey;
		this.logger = logger;
		this.euResidency = euResidency;
		
		var httpHandler = httpMessageHandler ?? new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
			Proxy = WebRequest.GetSystemWebProxy(),
			UseProxy = true,
		};
		httpClient = new HttpClient(httpHandler);

		jsonSerializerOptions = new JsonSerializerOptions()
		{
			Converters =
			{
				new AmplitudeDateTimeOffsetConverter(),
			},
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			MaxDepth = 42, // Matches Amplitude max property depth plus 2 for outer wrappers
		};
	}

	/// <inheritdoc />
	public async Task<AmplitudeApiResult> Identify(AmplitudeIdentify identification, CancellationToken ct)
	{
		// Identify API format from https://developers.amplitude.com/docs/identify-api
		// It uses a different format to the events (2) API. It is similar to the original
		// v1 HTTP API - we use HTTP post params, one of which contains the id data as JSON.

		var json = JsonSerializer.Serialize(identification, jsonSerializerOptions);
		
		var boundary = "----" + DateTime.Now.Ticks;
		using var content = new MultipartFormDataContent(boundary);

		content.Add(new StringContent(apiKey, Encoding.UTF8, "text/plain"), "api_key");
		content.Add(new StringContent(json, Encoding.UTF8, "application/json"), "identification");

		try
		{
			using var response = await httpClient
				.PostAsync($"https://{GetAmplitudeHost(euResidency)}/identify", content, ct)
				.ConfigureAwait(false);
				
			// Fortunately the response codes at least match (mostly) the v2 event API
			return await ResultFromAmplitudeHttpResponse(response);
		}
		catch (HttpRequestException ex)
		{
			return ResultFromHttpRequestException(ex);
		}
	}

	/// <inheritdoc />
	public async Task<AmplitudeApiResult> SendEvents(IEnumerable<AmplitudeEvent> events, CancellationToken ct)
	{
		// Events API format v2 from https://developers.amplitude.com/docs/http-api-v2
		// Multiple events can be combined into a single call, but docs recommend 10 per batch.
		// JSON body with format as follows:
		//
		// {
		//   "api_key": "foo",
		//   "events: [
		//     /* ... */
		//   ],
		//   "options": {
		//       "min_id_length": 5
		//   }
		// }

		var payload = new
		{
			api_key = apiKey,
			events = events,
			options = new
			{
				// Amplitude puts a default min length of 5 on user IDs. We want to be able to send numeric ID's < 10k
				min_id_length = events.Any(e => e.UserId is { Length: < 5 }) ? 1 : 5
			}
		};

		using var ms = new MemoryStream();
		SerializeJsonIntoStream(payload, ms);
		
		using var content = new StreamContent(ms);
		using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{GetAmplitudeHost(euResidency)}/2/httpapi");
		
		content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
		request.Content = content;

		try
		{
			using var response = await httpClient
				.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
				.ConfigureAwait(false);
					
			return await ResultFromAmplitudeHttpResponse(response);
		}
		catch (HttpRequestException ex)
		{
			return ResultFromHttpRequestException(ex);
		}
	}

	/// <summary>
	/// Assigns one of our internal AmplitudeApiResult statuses from the API response. 
	/// </summary>
	/// <param name="response">The HTTP response from an API call to Amplitude</param>
	private async Task<AmplitudeApiResult> ResultFromAmplitudeHttpResponse(HttpResponseMessage response)
	{
		// Amplitude documented HTTP API response codes as follows
		switch (response.StatusCode)
		{
			// 200
			case HttpStatusCode.OK:
				return AmplitudeApiResult.Success;

			// 400
			case HttpStatusCode.BadRequest:
				// Error message detailing what was wrong with the request will be in body
				var responseContent = await response.Content.ReadAsStringAsync();
				// We want to catch API key messages during integration, so we do some extra work to catch them
				try
				{
					// Response can come as a JSON payload from the events API, or simple a string from the others
					if (responseContent == "invalid_api_key")
					{
						return AmplitudeApiResult.InvalidApiKey;
					}
					// Try parse response as JSON and extract error message instead
					// { "code":400, "error":"Invalid API key: 1234567890" }
					var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
					if(payload != null && payload.TryGetValue("error", out var errorMessage) && 
					   errorMessage is JsonElement { ValueKind: JsonValueKind.String } jsonValue)
					{
						if(jsonValue.ToString().StartsWith("Invalid API key", StringComparison.InvariantCultureIgnoreCase))
						{
							return AmplitudeApiResult.InvalidApiKey;
						}
					}
				}
				catch (JsonException)
				{
					// We failed to decode the object. Format changed? Something else? Just treat as error and log.
					// We can swallow the exception as we're logging the entire API response anyway.
				}
				// If we get here, there was something wrong with the data, but its not our API key
				logger?.Invoke(LogLevel.Error, $"Amplitude API returned 400 (Bad Request): {responseContent}");
				return AmplitudeApiResult.BadData;

			// 413
			case HttpStatusCode.RequestEntityTooLarge:
				logger?.Invoke(LogLevel.Error, $"Event data sent to Amplitude exceeded size limit");
				return AmplitudeApiResult.TooLarge;

			// 429
			case (HttpStatusCode)429:
				return AmplitudeApiResult.Throttled;

			// 500, 502, 504
			case HttpStatusCode.InternalServerError:
			case HttpStatusCode.NotImplemented:
			case HttpStatusCode.BadGateway:
				// Amplitude had an error when handling the request. State unknown. Not guaranteed to be processed,
				// but also not guaranteed to be not. Need to resend request using same insert_id as before.
				return AmplitudeApiResult.ServerError;

			// 503
			case HttpStatusCode.ServiceUnavailable:
				// Failed, but guaranteed not commit event. We retry again as if it was a server error
				return AmplitudeApiResult.ServerError;
			
			// 408, 504 - Not officially returned by API, but could happen depending on client network
			case HttpStatusCode.RequestTimeout:
			case HttpStatusCode.GatewayTimeout:
				return AmplitudeApiResult.NetworkError;

			default:
				// If we are getting something not defined in the docs then treat as a server error (can retry)
				return AmplitudeApiResult.ServerError;
		}
	}

	/// <summary>
	/// Returns a result matching the network exception that occurred.
	/// </summary>
	private AmplitudeApiResult ResultFromHttpRequestException(HttpRequestException ex)
	{
		// Most web exceptions we can retry, but an authentication error will never succeed
		if (ex.InnerException is WebException { Status: WebExceptionStatus.TrustFailure } wex)
		{
			logger?.Invoke(LogLevel.Error, $"Trust failure connecting to Amplitude API: {wex}");
			return AmplitudeApiResult.UnrecoverableError;
		}
			
		return AmplitudeApiResult.NetworkError;
	}
	
	/// <summary>
	/// Gets the host to use for when making API requests to Amplitude.
	/// </summary>
	private static string GetAmplitudeHost(bool euResidency)
	{
		return euResidency ? "api.eu.amplitude.com" : "api2.amplitude.com";
	}

	/// <summary>
	/// Serializes the given object into a steam.
	/// </summary>
	/// <param name="value">The object to serialize</param>
	/// <param name="stream">The stream to serialize the object into</param>
	private void SerializeJsonIntoStream(object value, Stream stream)
	{
		// On high throughput apps we can serialize a lot. It's slightly friendlier to do as stream not string.
		// Useful if we have many thousands of events queued. See https://johnthiriet.com/efficient-post-calls/

		using var sw = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
		JsonSerializer.Serialize(stream, value, jsonSerializerOptions);
		
		// Must flush and move back to start so the HTTP client can read from there
		sw.Flush();
		stream.Seek(0, SeekOrigin.Begin);
	}
}