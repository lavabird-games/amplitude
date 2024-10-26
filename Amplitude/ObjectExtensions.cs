using System.Reflection;

namespace Lavabird.Amplitude;

public static class ObjectExtensions
{
	/// <summary>
	/// Converts an object to a dictionary with a key for each property defined on the object.
	/// </summary>
	public static Dictionary<string, object>? ToDictionary<T>(this T obj)
	{
		if (obj != null)
		{
			var dictionary = new Dictionary<string, object>();

			foreach (var property in obj.GetType().GetRuntimeProperties())
			{
				var val = property.GetValue(obj, null);
				if (val != null)
				{
					dictionary[property.Name] = val;
				}
			}

			if (dictionary.Count != 0)
			{
				return dictionary;
			}
		}

		return null;
	}
}