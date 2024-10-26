using System.Reflection;

namespace Lavabird.Amplitude;

public static class ObjectExtensions
{
	/// <summary>
	/// Converts an object to a dictionary with a key for each property defined on the object.
	/// </summary>
	public static Dictionary<string, object>? ToDictionary<T>(this T obj)
	{
		if (obj == null) return null;
		if (obj is Dictionary<string, object> dict) return dict;
		
		var dictionary = new Dictionary<string, object>();

		foreach (var property in obj.GetType().GetRuntimeProperties())
		{
			var val = property.GetValue(obj, null);
			if (val != null)
			{
				dictionary[property.Name] = val;
			}
		}

		return dictionary.Count != 0 ? dictionary : null;
	}
}