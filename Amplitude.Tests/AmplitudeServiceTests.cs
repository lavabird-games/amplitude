using System;
using System.Collections.Generic;
using System.IO;
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
	/// Waits for all events to be dispatched from the service.
	/// </summary>
	private static async Task AwaitEmptyQueue(AmplitudeService service)
	{
		// TODO: Not this. Plumb in an async signaller so the service can signal an empty queue
		await Task.Delay(1000);
	}
}