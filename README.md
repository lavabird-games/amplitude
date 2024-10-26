


# Amplitude

This is a .Net library to track events and user data via the [Amplitude](https://amplitude.com/) analytics platform. 

### Features

* Batching of events to minimize network traffic.
* Automatic retry in case of temporary network loss - as is typical on mobile.
* Background persistence for unsent events in case of network loss on on exit or application crash.
* Simple logging harness with no external dependencies. Easily connect with your existing logging setup.

### Usage

#### Initialization

Create a new instance of the `AmplitudeService` using the API key for the Amplitude project you are sending data to.

```cs
var amplitude = new AmplitudeService("<YOUR_API_KEY>");
```
The service includes a default configuration with behavior suitable for standard use cases. For more specific requirements the behavior can be customized using an `AmplitudeServiceSettings` instance.

```cs
var settings = new AmplitudeServiceSettings()
{
	DispatchBatchPeriodSeconds = 10,
	UseEuResidency = true,
};
var amplitude = new AmplitudeService("<YOUR_API_KEY>", settings: settings);
```
#### Identify
Events in Amplitude are tied to an identity in the form of a user or device identifier (or both). These are represented by an `AmplitudeIdentity`. If only a user identity is specified, then Amplitude will automatically generate a device identifier based on a hash of the user identifier.  

A call to `Identify` will set the identity in use for the current session. This identity will apply to events sent after this call.

```cs
var identity = new AmplitudeIdentity("user_id", "device_id");
amplitude.Identify(identity);
amplitude.Event("Dummy Event"); // Will use the previous identity
```

Amplitude supports storing custom data about each identity. This can be sent with the `Identify` call to be made available in the Amplitude dashboard.

```cs
var identity = new AmplitudeIdentity("user_id", "device_id");
amplitude.Identify(identity, new()
{
    UserProperty = "Foo",
    AnotherProperty = 20,
});
```

#### Sending Events

Events in Amplitude consist of an event name, and a set of optional parameters to send with that event. These optional parameters are extracted as key-value pairs to display in the Amplitude dashboard.

Sending a simple event:

```cs
amplitude.Event("Dummy Event");
```

Sending custom event data via anonymous object:

```cs
amplitude.Event("Dummy Event", new {
	DummyProperty = "Foo",
	AnotherProperty = 20,
});
```

Alternatively custom data can be sent using a `Dictionary` instead:

```cs
var data = new Dictionary<string, object>()  
{  
    ["DummyProperty"] = "Foo",  
    ["AnotherProperty"] = 10,  
};
amplitude.Event("Dummy Event", data);
```
In most cases, the library will be used on a client and the identity can be set once with an `Identify` call. However, if being used server side you may want to tie each event to a specific user each time an event is created. All of the `Event` methods include an extra overload to also pass an identity for that specific event.

```cs
var identity = new AmplitudeIdentity("user_id", "device_id");
amplitude.Event(identity, "Dummy Event", data);
```

#### Persistence

A `Stream` (normally a `FileStream`) can be passed during initialization to persist unsent events on `Shutdown()` or `Dispose()`.

```cs
var stream = File.Open("path/to/file", FileMode.OpenOrCreate);
var amplitude = new AmplitudeService("<YOUR_API_KEY>", persistenceStream: stream);
```

The persistence stream will also be written to periodically in the background in case the parent application crashes with unsent data. The write frequency can be controlled with an `AmplitudeServiceSettings` object during initialization.

During initialization, the stream will be checked for existing data from a previous session. Any unsent data will be added to the queue to be retried. The `AmplitudeService` will have generated a unique insert ID with each event, so if an event was previously sent but still saved (e.g. due to an application crash or a network error when confirming the event) the event will not be duplicated by Amplitude.

Persisted events older than Amplitude's max timeout of 7 days will not be replayed by default.

### Installation

Simply add a reference to the [Lavabird.Amplitude NuGet package](https://www.nuget.org/packages/Lavabird.Amplitude) to your project.

##### Method 1: Package Manager (Recommended)

[Install](https://docs.microsoft.com/en-us/nuget/tools/ps-ref-install-package)  the package in the project:

```
Install-Package Lavabird.Amplitude
```

### Compatibility

 Supports .Net Framework 4.7, Net 6, or .Net 8.

### License

Licensed under the [MIT license](LICENSE). Based on the original [AmplitudeSharp](https://github.com/marchello2000/AmplitudeSharp) by Mark Vulfson.
