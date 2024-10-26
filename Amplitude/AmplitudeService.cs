using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using Lavabird.Amplitude.Api;

namespace Lavabird.Amplitude;

public class AmplitudeService : IAsyncDisposable
{
	/// <summary>
	/// The API used to send data to Amplitude's HTTP endpoint.
	/// </summary>
	private readonly IAmplitudeApi api;
	
	/// <summary>
	/// The queue of API data still to be sent to the Amplitude API.
	/// </summary>
	private readonly List<AmplitudeBase> apiQueue = new();
	
	/// <summary>
	/// The configuration settings object for this AmplitudeService.
	/// </summary>
	private readonly AmplitudeServiceSettings settings;
	
	/// <summary>
	/// When this API session was started. Amplitude uses this to group events into a single session.
	/// </summary>
	private DateTimeOffset sessionStart = DateTimeOffset.UtcNow;
	
	/// <summary>
	/// The identity of the user or device that is responsible for the event. This will be saved from the last
	/// call to Identify. API calls still have the option to specify a different identity for each event (such as
	/// when used on a server to dispatch multiple events for different users).
	/// </summary>
	private AmplitudeIdentity? lastIdentity;
	
	/// <summary>
	/// Optional stream used to persist events to storage. This enables better tracking for mobile devices
	/// which may not have a stable internet connection.
	/// </summary>
	private readonly Stream? persistenceStream;
	
	/// <summary>
	/// The settings used to serialize entities to JSON for persistence.
	/// </summary>
	private readonly JsonSerializerOptions? persistenceSerializerOptions;
	
	/// <summary>
	/// Optional logger for this service. If not specified, Debug.WriteLine is used.
	/// </summary>
	private readonly Action<LogLevel, string>? logger;
	
	/// <summary>
	/// Additional properties to send with every Event (will be merged with properties defined in the Event call).
	/// </summary>
	public Dictionary<string, object> GlobalEventProperties { get; } = new();
	
	/// <summary>
	/// The maximum number of events we send in a single batch. We start with the recommended value from Amplitude
	/// but will reduce it for the session if we hit payload size limits. This is rare, and only happens when
	/// sending events with very large amounts of custom data.
	/// </summary>
	private int maxEventBatchSize = AmplitudeApi.MaxEventBatchSize;

	/// <summary>
	/// Whether queue dispatching has been disabled. This should only happen if we have an invalid API key. If so
	/// we can't send any data, all we can do is store it up for the future.
	/// </summary>
	private bool disableQueueDispatch;

	/// <summary>
	/// Lock used for concurrent access to the data queue.
	/// </summary>
	private readonly object queueLock = new();
	
	/// <summary>
	/// Whether we already have a call to the Amplitude API in progress. We don't dispatch multiple calls concurrently
	/// as we'd get throttled. Instead we batch them together when possible.
	/// </summary>
	private int dispatchInterlock = 0;

	/// <summary>
	/// The last task started to dispatch events to the Amplitude API. We only have one running at a time.
	/// </summary>
	private Task? dispatchTask;

	/// <summary>
	/// Timer used to periodically save the event queue to the persistence stream.
	/// </summary>
	private readonly System.Timers.Timer? persistenceTimer;
	
	/// <summary>
	/// Cancellation token so we can abort the dispatch and persistence tasks to gracefully shut down.
	/// </summary>
	private readonly CancellationTokenSource cancellationToken = new CancellationTokenSource();
	
	/// <summary>
	///		Creates a new AmplitudeService instance to send data and events to the Amplitude API.
	/// </summary>
	/// <param name="apiKey">
	///		Amplitude API key for the project to send event data to.
	/// </param>
	/// <param name="persistenceStream">
	///		An optional Stream to persist/restore saved event data. This is written to periodically and should exclusive
	///		to the this service. If not provided then no data will be persisted (or restored).
	/// </param>
	/// <param name="logger">
	///		Action delegate for logging purposes, if none is specified
	///		<see cref="System.Diagnostics.Debug.WriteLine(object)"/> is used.</param>
	/// <param name="settings">
	///		Configuration settings for the AmplitudeService. Default settings will be used if not provided.
	/// </param>
	public AmplitudeService(string apiKey, 
		Stream? persistenceStream = null, 
		Action<LogLevel, string>? logger = null, 
		AmplitudeServiceSettings? settings = null) : 
		this(apiKey, null, persistenceStream, logger, settings)
	{
		
	}

	/// <summary>
	/// Internal version of the constructor that allows for dependency injection of the API.
	/// </summary>
	internal AmplitudeService(
		string apiKey,
		IAmplitudeApi? api,
		Stream? persistenceStream = null,
		Action<LogLevel, string>? logger = null,
		AmplitudeServiceSettings? settings = null)
	{
		// Deliberately catch the example key from the docs to stop copy/paste errors
		if (apiKey == "<YOUR_API_KEY>")
		{
			throw new ArgumentOutOfRangeException(nameof(apiKey), "Please specify Amplitude API key");
		}
		
		this.settings = settings ?? new AmplitudeServiceSettings();
		this.logger ??= (level, message) => { Debug.WriteLine($"Amplitude: [{level}] {message}"); };

		this.api = api ?? new AmplitudeApi(apiKey, logger, this.settings.UseEuResidency);
		
		if (persistenceStream != null)
		{
			this.persistenceStream = persistenceStream;
			persistenceSerializerOptions = new JsonSerializerOptions()
			{
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
			};
			RestoreFromStream();

			// We can have a stream but configure without periodic saving still
			if (this.settings.BackgroundWritePeriodSeconds > 0)
			{
				persistenceTimer = new System.Timers.Timer(
					TimeSpan.FromSeconds(this.settings.BackgroundWritePeriodSeconds).TotalMilliseconds);
				persistenceTimer.Elapsed += OnSaveQueueTimer;
			}
		}
	}

	/// <summary>
	/// Gracefully shuts down the service, saving any remaining events to the persistence stream (if set).
	/// </summary>
	public void Shutdown()
	{
		cancellationToken.Cancel();
		
		// This is similar to Dispose, but we don't null everything out. We're in a state where we would be
		// reactivated if more events came in.
		
		if (dispatchTask is { IsCompleted: false, IsCanceled: false})
		{
			dispatchTask.Wait();
		}
		
		if (persistenceStream != null)
		{
			persistenceTimer?.Stop();
			SaveQueue();	
		}
	}

	/// <summary>
	/// Begin new user session. Amplitude groups events into a single session if they have the same session identifier.
	/// Normally, you don't have to call this, but you can if you want to force a new session (e.g. you are building a
	/// plugin or component rather than an app).
	/// </summary>
	public void NewSession()
	{
		sessionStart = DateTimeOffset.UtcNow;
	}

	/// <summary>
	/// Set user identification for session. Future events will be associated with this identity unless
	/// manually specified with each event. 
	/// </summary>
	/// <param name="identity">The identity of the user or device being tracked.</param>
	/// <param name="properties">Optional additional parameters to set for the user.</param>
	public void Identify(AmplitudeIdentity identity, Dictionary<string, object>? properties = null)
	{
		var identify = new AmplitudeIdentify(identity, properties);

		lastIdentity = identity;
		
		QueueApi(identify);
	}

	/// <summary>
	/// Set user identification for session. Future events will be associated with this identity unless
	/// manually specified with each event. 
	/// </summary>
	/// <param name="identity">The identity of the user or device being tracked.</param>
	/// <param name="properties">Optional additional parameters to set for the user.</param>
	public void Identify(AmplitudeIdentity identity, object? properties)
	{
		Identify(identity, properties.ToDictionary());
	}

	/// <summary>
	/// Log an event with the given parameters for a specific identity.
	/// </summary>
	/// <param name="identity">The identity of the source of this event.</param>
	/// <param name="eventName">The name of the event to track.</param>
	/// <param name="properties">Optional additional parameters for the event.</param>
	public void Event(AmplitudeIdentity identity, string eventName, Dictionary<string, object>? properties = null)
	{
		var ev = new AmplitudeEvent(identity, eventName, properties, GlobalEventProperties)
		{
			SessionId = sessionStart.ToUnixTimeMilliseconds()
		};

		QueueApi(ev);
	}

	/// <summary>
	/// Log an event with the given parameters for a specific identity.
	/// </summary>
	/// <param name="identity">The identity of the source of this event.</param>
	/// <param name="eventName">The name of the event to track.</param>
	/// <param name="properties">Optional additional parameters for the event.</param>
	public void Event(AmplitudeIdentity identity, string eventName, object properties)
	{
		Event(identity, eventName, properties.ToDictionary());
	}

	/// <summary>
	/// Log an event with the given parameters using the identity of the last call to Identify.
	/// </summary>
	/// <param name="eventName">The name of the event to track.</param>
	/// <param name="properties">Optional additional parameters for the event.</param>
	public void Event(string eventName, Dictionary<string, object>? properties = null)
	{
		if (lastIdentity == null)
		{
			throw new InvalidOperationException(
				"Must call Identify() before logging events or specify the identity in the Event call.");
		}

		Event(lastIdentity, eventName, properties);	
	}

	/// <summary>
	/// Log an event with the given parameters using the identity of the last call to Identify.
	/// </summary>
	/// <param name="eventName">The name of the event to track.</param>
	/// <param name="properties">Optional additional parameters for the event.</param>
	public void Event(string eventName, object properties)
	{
		Event(eventName, properties.ToDictionary());
	}

	/// <summary>
	/// Adds the given event to the queue of events to be sent to the Amplitude API.
	/// </summary>
	private void QueueApi(AmplitudeBase ev)
	{
		lock (queueLock)
		{
			apiQueue.Add(ev);
		}

		Dispatch();
	}
	
	/// <summary>
	/// Removes the given number of API calls from the pending event queue (in a thread safe way).
	/// </summary>
	private void RemoveFromQueueApi(int numberToRemove)
	{
		lock(queueLock)
		{
			apiQueue.RemoveRange(0, numberToRemove);
		}
	}
	
	/// <summary>
	/// Handles the creating of a new dispatch task if one is not already running.
	/// </summary>
	private void Dispatch()
	{
		// We only have 1 running task a time. If we queued additional events whilst it was still running then it 
		// will pick up those events before completing anyway. The task will unset this flag when it completes.
		if (!disableQueueDispatch && Interlocked.CompareExchange(ref dispatchInterlock, 1, 0) == 0)
		{
			// We have the potential to run quite long with larger batch wait times or if Amplitude throttles us
			dispatchTask = Task.Factory.StartNew(DispatchQueueTask, TaskCreationOptions.LongRunning);
		}
	}
	
	/// <summary>
	/// The background task for dispatching queued events to the Amplitude API.
	/// </summary>
	private async Task<bool> DispatchQueueTask()
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				// We have some events to dispatch. We might want to wait for more to group together before sending.
				// This saves making many requests to the Amplitude API and reduces bandwidth.
				// ReSharper disable once InconsistentlySynchronizedField
				if (settings.DispatchBatchPeriodSeconds > 0 && apiQueue.Any())
				{
					await Task.Delay(TimeSpan.FromSeconds(settings.DispatchBatchPeriodSeconds),
						cancellationToken.Token);
				}

				var apiCallsToSend = new List<AmplitudeBase>();
				var backOff = false;

				lock (queueLock)
				{
					// If we don't have anything left to send we can exit the Task and wait until we're called again
					if (!apiQueue.Any()) return true;

					// Remove any events that have been queued for too long. This would normally only happen for
					// events which have been previously persisted but are now outside the time limit. 

					var expireBefore = DateTime.UtcNow - TimeSpan.FromSeconds(settings.QueuedApiCallsTtlSeconds);
					var removed = apiQueue.RemoveAll(q => q is AmplitudeEvent ev && ev.Time < expireBefore);

					if (removed > 0)
					{
						logger?.Invoke(LogLevel.Information, $"Removed {removed} expired events from event queue");
					}

					// Build batch of data to send at once
					foreach (var ev in apiQueue)
					{
						// Only Event calls can be batched together, but the queue can contain a mix
						if (apiCallsToSend.Count > 0 &&
						    (ev is not AmplitudeEvent || apiCallsToSend[0] is not AmplitudeEvent)) break;
						if (apiCallsToSend.Count >= maxEventBatchSize) break;

						apiCallsToSend.Add(ev);
					}
				}

				if (apiCallsToSend.Count > 0)
				{
					// A little ugly, but we need to call different parts of the API, yet handle the response the same way
					AmplitudeApiResult result;
					if (apiCallsToSend[0] is AmplitudeIdentify identify)
					{
						result = await api.Identify(identify, cancellationToken.Token);
					}
					else
					{
						result = await api.SendEvents(apiCallsToSend.Cast<AmplitudeEvent>(), cancellationToken.Token);
					}

					switch (result)
					{
						case AmplitudeApiResult.Success:
							// Success. Can now remove those events from queue
							RemoveFromQueueApi(apiCallsToSend.Count);
							break;

						case AmplitudeApiResult.BadData:
							// TODO: For now, we also remove these from the list. In future we want to get the index of the
							// events in the batch which failed and then only remove those from the queue.
							RemoveFromQueueApi(apiCallsToSend.Count);
							break;

						case AmplitudeApiResult.InvalidApiKey:
							// We cannot recover from this. The best we can do is save any events to the queue and hope for a
							// new key API next time. We log the event, and then skip processing all future events.
							logger?.Invoke(LogLevel.Error,
								$"Amplitude API returned invalid API key. Further API calls will not be sent");
							disableQueueDispatch = true;
							return false;

						// Events only. For identity we cant reduce the batch size
						case AmplitudeApiResult.TooLarge when apiCallsToSend[0] is AmplitudeEvent:
						{
							// If our payload was too large, we assume that future payloads might also be too large and 
							// hit the cap. We reduce the batch size if possible and try again.
							if (maxEventBatchSize > 1)
							{
								maxEventBatchSize /= 2;
							}
							else
							{
								// Single event was still too large. Remove the problematic event and continue
								logger?.Invoke(LogLevel.Error,
									$"Event data was too large for Amplitude (EventId = {((AmplitudeEvent)apiCallsToSend[0]).EventId})");
								RemoveFromQueueApi(apiCallsToSend.Count);
							}

							break;
						}

						case AmplitudeApiResult.TooLarge:
							logger?.Invoke(LogLevel.Error, $"Api data (non-Event) was too large for Amplitude");
							RemoveFromQueueApi(apiCallsToSend.Count);
							break;

						case AmplitudeApiResult.Throttled:
							logger?.Invoke(LogLevel.Warning, $"Amplitude is throttling API calls");
							backOff = true;
							break;

						case AmplitudeApiResult.NetworkError:
						case AmplitudeApiResult.ServerError:
						default:
							// We treat replayable server errors in the same way as a throttle. Retry in a bit
							backOff = true;
							break;
					}
				}

				if (backOff && !cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(TimeSpan.FromSeconds(settings.BackOffDelaySeconds), cancellationToken.Token);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception e)
		{
			logger?.Invoke(LogLevel.Error,
				$"{nameof(AmplitudeService)} caught exception during {nameof(DispatchQueueTask)}: " + e);
		}
		finally
		{
			Interlocked.Exchange(ref dispatchInterlock, 0);
		}

		return true;
	}

	/// <summary>
	/// Saves any data that has not yet been sent to the API. This is called periodically automatically, and in the
	/// event of a graceful shutdown.
	/// </summary>
	private void SaveQueue()
	{
		if (persistenceStream == null) return;

		try
		{
			// We don't need to write anything in the common case that the event has been sent to the API before our 
			// save interval has expired. However, if there are already existing events in the store, we would need to
			// clear them. We can check this by seeing if our stream position is > 0 (i.e. it had some content previously).

			lock (persistenceStream)
			{
				if (apiQueue.Any() || persistenceStream.Position > 0)
				{
					string persistedData;
					lock (queueLock)
					{
						persistedData = JsonSerializer.Serialize(apiQueue, persistenceSerializerOptions);
					}

					// Reset us back to the start and truncate content (in case new data is shorter)
					persistenceStream.Seek(0, SeekOrigin.Begin);
					persistenceStream.SetLength(0);

					using (var writer = new StreamWriter(persistenceStream, Encoding.UTF8, 1024, true))
					{
						writer.Write(persistedData);
					}

					persistenceStream.Flush();
				}
			}
		}
		catch (Exception e)
		{
			logger?.Invoke(LogLevel.Error, $"Failed to persist events: {e}");
		}
	}
	
	/// <summary>
	/// Handles when the background timer indicates that we should save the event queue to the persistence stream.
	/// </summary>
	private void OnSaveQueueTimer(object? sender, ElapsedEventArgs args)
	{
		SaveQueue();
	}

	/// <summary>
	/// Loads any previously persisted data from the persistence stream.
	/// </summary>
	private void RestoreFromStream()
	{
		if (persistenceStream == null) return;
		
		try
		{
			lock (persistenceStream)
			{
				using var reader = new StreamReader(persistenceStream, Encoding.UTF8, false, 1024, true);
				var persistedData = reader.ReadLine();
				
				if (!string.IsNullOrEmpty(persistedData))
				{
					var data = JsonSerializer.Deserialize<List<AmplitudeBase>>(persistedData, persistenceSerializerOptions);
					if (data != null && data.Any())
					{ 
						lock (queueLock)
						{
							apiQueue.InsertRange(0, data);

							Dispatch();
						}

						logger?.Invoke(LogLevel.Information, $"Restored {data.Count} events from previous session");
					}
				}
			}
		}
		catch (Exception e)
		{
			// We are safe to continue, but we won't have any of the previously saved data
			logger?.Invoke(LogLevel.Error, $"Failed to load persisted events: {e}");
		}
	}
	
	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		cancellationToken.Cancel();
		
		if (dispatchTask is { IsCompleted: false, IsCanceled: false })
		{
			await dispatchTask;
			dispatchTask = null;
		}
		
		if (persistenceStream != null)
		{
			persistenceTimer?.Stop();
			SaveQueue();
		}
	}
}