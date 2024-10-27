using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Lavabird.Amplitude.Api;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lavabird.Amplitude.Tests.Api;

public class AmplitudeApiTests
{
	/// <summary>
	/// Dummy key to use for fake API requests. This key is not actually valid.
	/// </summary>
	private const string DummyApiKey = "testApiKey";
	
	
	
	/// <summary>
	/// Tests the standard case of identifying a user.
	/// </summary>
	[Fact]
	public void Identify_SuccessResponse_ReturnsSuccess()
	{
		var mockMessageHandler = CreateMockHttpMessageHandler(
			HttpStatusCode.OK,
			"{\"code\":200 }");

		var identify = new AmplitudeIdentify(
			new AmplitudeIdentity("testUserId", "testDeviceId"),
			new Dictionary<string, object>
			{
				{ "TestProperty", "Test Value" }
			});
		
		var api = new AmplitudeApi(DummyApiKey, httpMessageHandler: mockMessageHandler.Object);
		var result = api.Identify(identify, CancellationToken.None);
		
		Assert.Equal(AmplitudeApiResult.Success, result.Result);
	}
	
	/// <summary>
	/// Tests the standard case of sending an event and receiving a success response.
	/// </summary>
	[Fact]
	public void SendEvents_SuccessResponse_ReturnsSuccess()
	{
		var mockMessageHandler = CreateMockHttpMessageHandler(
			HttpStatusCode.OK,
			"{\"code\":200, \"server_upload_time\":1396381378123 }");
		
		var api = new AmplitudeApi(DummyApiKey, httpMessageHandler: mockMessageHandler.Object);
		var result = api.SendEvents(new[] { CreateTestEvent() }, CancellationToken.None);
		
		Assert.Equal(AmplitudeApiResult.Success, result.Result);
	}
	
	/// <summary>
	/// Tests the case of serializing multiple events at once to the API.
	/// </summary>
	[Fact]
	public void SendEvents_MultipleEvents_ReturnsSuccess()
	{
		var mockMessageHandler = CreateMockHttpMessageHandler(
			HttpStatusCode.OK,
			"{\"code\":200, \"server_upload_time\":1396381378123 }");
		
		var api = new AmplitudeApi(DummyApiKey, httpMessageHandler: mockMessageHandler.Object);
		var result = api.SendEvents(new[]
		{
			CreateTestEvent(),
			CreateTestEvent(),
			CreateTestEvent(),
		}, CancellationToken.None);
		
		Assert.Equal(AmplitudeApiResult.Success, result.Result);
	}
	
	/// <summary>
	/// Tests that we correctly process invalid API key messages from the Amplitude API.
	/// </summary>
	[Fact]
	public void SendEvents_InvalidApiKeyResponse_ReturnsInvalidApiKey()
	{
		var mockMessageHandler = CreateMockHttpMessageHandler(
			HttpStatusCode.BadRequest,
			"{\"code\":400, \"error\":\"Invalid API key\" }");
		
		var api = new AmplitudeApi(DummyApiKey, httpMessageHandler: mockMessageHandler.Object);
		var result = api.SendEvents(new[] { CreateTestEvent() }, CancellationToken.None);
		
		Assert.Equal(AmplitudeApiResult.InvalidApiKey, result.Result);
	}
	
	/// <summary>
	/// Creates a single test event with data to send to the Amplitude API.
	/// </summary>
	/// <returns></returns>
	private static AmplitudeEvent CreateTestEvent()
	{
		var identity = new AmplitudeIdentity("testUserId", "testDeviceId");
		
		return new AmplitudeEvent(identity, "Test.Event", new Dictionary<string, object>
		{
			{ "TestProperty", "Test Value" }
		});
	}
	
	/// <summary>
	/// Creates a mocked HTTP message handler that will return the given status code and response.
	/// </summary>
	private static Mock<HttpMessageHandler> CreateMockHttpMessageHandler(HttpStatusCode statusCode, string response)
	{
		var mockMessageHandler = new Mock<HttpMessageHandler>();
		
		mockMessageHandler
			.Protected()
			.Setup<Task<HttpResponseMessage>>("SendAsync", 
				ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
			.ReturnsAsync(new HttpResponseMessage {
				StatusCode = statusCode,
				Content = new StringContent(response)
			});
		
		return mockMessageHandler;
	}
}