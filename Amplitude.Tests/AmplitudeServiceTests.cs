using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Lavabird.Amplitude.Api;
using Moq;
using Xunit;

namespace Lavabird.Amplitude.Tests;

public class AmplitudeServiceTests
{
	/// <summary>
	/// Key to use for testing the Amplitude API. This key is not actually valid.
	/// </summary>
	private const string TestApiKey = "testApiKey";

	/// <summary>
	/// Identity to use when testing the event API.
	/// </summary>
	private static readonly AmplitudeIdentity TestIdentity = new AmplitudeIdentity("testUserId", "testDeviceId");

	/// <summary>
	/// Tests that a key exception is thrown when initializing the service using the API key copied from the Amplitude
	/// documentation.
	/// </summary>
	[Fact]
	public void Ctor_CopyPasteKeyFromDocs_ThrowsKeyException()
	{
		const string apiKeyFromDocs = "<YOUR_API_KEY>";

		Assert.Throws<ArgumentOutOfRangeException>(() => new AmplitudeService(apiKeyFromDocs));
	}
	
	/// <summary>
	/// Tests that sending an event before an identity is given will throw.
	/// </summary>
	[Fact]
	public void Event_EventBeforeIdentify_Throws()
	{
		var mockApi = new Mock<IAmplitudeApi>();
		var service = new AmplitudeService(TestApiKey, mockApi.Object);

		// Calling without identity already set
		Assert.Throws<InvalidOperationException>(() => service.Event("Test Event"));
	}
	
	/// <summary>
	/// Tests that sending an event after an identity is set will not throw.
	/// </summary>
	[Fact]
	public void Event_EventAfterIdentify_Succeeds()
	{
		var mockApi = new Mock<IAmplitudeApi>();
		var service = new AmplitudeService(TestApiKey, mockApi.Object);

		// Calling after setting identity should not throw anything
		service.Identify(TestIdentity);
		service.Event("Test Event");
	}
	
	/// <summary>
	/// Tests that when given a single event that the event is dispatched to the Amplitude API.
	/// </summary>
	[Fact]
	public async void Event_SingleEvent_EventIsDispatchedToApi()
	{
		var mockApi = new Mock<IAmplitudeApi>();
		var service = new AmplitudeService(TestApiKey, mockApi.Object);
		
		service.Event(TestIdentity, "Test Event");
		await AwaitEmptyQueue(service);
		
		mockApi.Verify(m => m.SendEvents(It.IsAny<IEnumerable<AmplitudeEvent>>(), It.IsAny<CancellationToken>()));
	}
	
	/// <summary>
	/// Tests that a group of API calls are batched together into a single outgoing call.
	/// </summary>
	[Fact]
	public async void Event_MultipleEvents_BatchedToApiInOneCall()
	{
		var mockApi = new Mock<IAmplitudeApi>();
		var service = new AmplitudeService(TestApiKey, mockApi.Object);
		
		service.Event(TestIdentity, "Test Event");
		service.Event(TestIdentity, "Test Event");
		service.Event(TestIdentity, "Test Event");
		await AwaitEmptyQueue(service);
		
		mockApi.Verify(
			m => m.SendEvents(It.IsAny<IEnumerable<AmplitudeEvent>>(), It.IsAny<CancellationToken>()), Times.Once);
	}
	
	/// <summary>
	/// Tests that the queue of outstanding events is saved to the persistence stream on shutdown.
	/// </summary>
	[Fact]
	public void Event_Persistence_SavedOnShutdown()
	{
		// Simulate throttling so events are forced to stay in the API queue unsent
		var mockApi = new Mock<IAmplitudeApi>();
		mockApi
			.Setup(m => m.SendEvents(It.IsAny<IEnumerable<AmplitudeEvent>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(AmplitudeApiResult.Throttled); 
		// Dummy stream needs to be seekable and writable for StreamWriter compatibility
		var mockStream = new Mock<Stream>();
		mockStream.Setup(m => m.Seek(It.IsAny<long>(), It.IsAny<SeekOrigin>())).Returns(0);
		mockStream.Setup(m => m.CanWrite).Returns(true);
		mockStream.Setup(m => m.CanSeek).Returns(true);
		var service = new AmplitudeService(TestApiKey, mockApi.Object, mockStream.Object);
		
		service.Event(TestIdentity, "Test Event", new { Foo = "Bar" });
		service.Shutdown();
		
		mockStream.Verify(m => m.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()));
		mockStream.Verify(m => m.Flush());
	}
	
	/// <summary>
	/// Tests that that the queue of persisted api data is restored back into the service on startup.
	/// </summary>
	[Fact]
	public void Event_Persistence_SavedApiCallsWereLoaded()
	{
		// Simulate throttling so events are forced to stay in the API queue unsent
		var mockApi = new Mock<IAmplitudeApi>();
		mockApi
			.Setup(m => m.SendEvents(It.IsAny<IEnumerable<AmplitudeEvent>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(AmplitudeApiResult.Throttled);
		// Actual memory stream so we can read and parse back
		var memoryStream = new MemoryStream();
		
		// Write events and shutdown so we save to the stream
		var service = new AmplitudeService(TestApiKey, mockApi.Object, memoryStream);
		service.Identify(TestIdentity, new { UserProp = "Foo" });
		service.Event(TestIdentity, "Test Event", new { Foo = "Bar", Fizz = 10 });
		service.Shutdown();
		
		// Reload the event queue and test the identity and event are restored
		memoryStream.Seek(0, SeekOrigin.Begin);
		var restore = new AmplitudeService(TestApiKey, mockApi.Object, memoryStream);
		
		Assert.Equal(2, restore.QueueSize);
	}
	
	/// <summary>
	/// Tests that that the data in a restored API call is the same as the input.
	/// </summary>
	[Fact]
	public async void Event_Persistence_SavedEventMatchesLoadedEvent()
	{
		var eventData = new { Foo = "Bar", Fizz = 10 };
		
		// Simulate throttling so events are forced to stay in the API queue unsent
		var mockApi = new Mock<IAmplitudeApi>();
		mockApi
			.Setup(m => m.SendEvents(It.IsAny<IEnumerable<AmplitudeEvent>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(AmplitudeApiResult.Throttled);
		// Actual memory stream so we can read and parse back
		var memoryStream = new MemoryStream();
		
		// Write events and shutdown so we save to the stream
		var service = new AmplitudeService(TestApiKey, mockApi.Object, memoryStream);
		service.Event(TestIdentity, "Test Event", eventData);
		service.Shutdown();
		
		// We want to save the event that got sent to the API this time
		AmplitudeEvent[]? sentEvents = null;
		mockApi = new Mock<IAmplitudeApi>();
		mockApi
			.Setup(m => m.SendEvents(It.IsAny<IEnumerable<AmplitudeEvent>>(), It.IsAny<CancellationToken>()))
			.Callback<IEnumerable<AmplitudeEvent>, CancellationToken>((events, _) => { sentEvents = events.ToArray(); })
			.ReturnsAsync(AmplitudeApiResult.Success);
		
		// Reload the event queue and test the identity and event are restored
		memoryStream.Seek(0, SeekOrigin.Begin);
		var restore = new AmplitudeService(TestApiKey, mockApi.Object, memoryStream);
		await AwaitEmptyQueue(restore);
		
		// Check the data sent and that we got the event back
		Assert.Equal(0, restore.QueueSize);
		Assert.NotNull(sentEvents);
		Assert.Equal(1, sentEvents!.Length);
		
		// Check the data in the event matched what we expected from the input
		var ev = sentEvents[0];
		Assert.Equal(ev.UserId, TestIdentity.UserId);
		Assert.Equal(ev.DeviceId, TestIdentity.DeviceId);
		Assert.Equal(ev.Properties["Foo"], eventData.Foo);
		Assert.Equal(ev.Properties["Fizz"], (long)eventData.Fizz); // JSON deserializes numbers to longs
	}
	
	/// <summary>
	/// Waits for all events to be dispatched from the service.
	/// </summary>
	private static async Task AwaitEmptyQueue(AmplitudeService service)
	{
		// This is a big ugly as we don't have a signal for when the queue empties - but works for now
		for (var n = 0; n < 100; n++)
		{
			if (service.QueueSize == 0) return;
			
			await Task.Delay(100);
		}
		
		// We took too long
		throw new Exception("Timeout waiting for empty queue");
	}
}