using System.Collections.Generic;
using System.Threading;

using Lavabird.Amplitude.Api;
using Xunit;

namespace Lavabird.Amplitude.Tests;

/// <summary>
/// End-to-end tests running against the production Amplitude API (via our test project).
/// </summary>
public class EndToEndTests
{
	/// <summary>
	/// Test API key used for integration tests to send real requests to the Amplitude API. This is only used for
	/// our unit tests and the key does not need to be private.
	/// </summary>
	private const string TestApiKey = "1543ff192fd0f70107e02fffbdd63645";
	
	/// <summary>
	/// Integration test of identifying a single user with the Amplitude API.
	/// </summary>
	[Fact]
	public void AmplitudeApi_Identify_ReturnsSuccess()
	{
		var identify = new AmplitudeIdentify(
			new AmplitudeIdentity("testUserId", "testDeviceId"),
			new Dictionary<string, object>
			{
				{ "TestProperty", "Test Value" }
			});
		
		var api = new AmplitudeApi(TestApiKey, null, true);
		var result = api.Identify(identify, CancellationToken.None);
		
		Assert.Equal(AmplitudeApiResult.Success, result.Result);
	}
	
	/// <summary>
	/// Integration test of sending a single event to the Amplitude API.
	/// </summary>
	[Fact]
	public void AmplitudeApi_Event_ReturnsSuccess()
	{
		var identity = new AmplitudeIdentity("testUserId", "testDeviceId");
		var testEvent = new AmplitudeEvent(identity, "Test.Event", new Dictionary<string, object>
		{
			{ "TestProperty", "Test Value" },
			{ "TestObject", new { Inner = 100 } },
			{ "TestArray", new [] { 1, 2, 3, 4} },
		});
		var api = new AmplitudeApi(TestApiKey, null, true);
		
		var result = api.SendEvents(new[] { testEvent }, CancellationToken.None);
		
		Assert.Equal(AmplitudeApiResult.Success, result.Result);
	}
}