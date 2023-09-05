using System.Text.Json;

namespace SerializableDictionaryPlugin;

public static class SerializableDictionaryHelper
{
    public static SerializableDictionary<dynamic, object> CreateFromFile(string filepath)
    {
        using FileStream fs = File.OpenRead(filepath);

        Dictionary<object, object>? baseDictionary = JsonSerializer.Deserialize<
            Dictionary<object, object>
        >(fs);

        if (baseDictionary is null || !baseDictionary.Any())
            throw new ArgumentException("Null or empty dictionary");

        object firstKey = baseDictionary.Keys.First();
        return firstKey switch
        {
            string s
                => new SerializableDictionary<object, object>(
                    baseDictionary,
                    DeterministicStringEqualityComparer.Instance
                ),
            _ => new SerializableDictionary<object, object>(baseDictionary),
        };
    }

    public static void WriteToFile(
        string filepath,
        IEnumerable<object> keys,
        IEnumerable<object> values
    )
    {
        Dictionary<object, object> dict = keys.Zip(values)
            .ToDictionary(x => x.First, x => x.Second);

        using FileStream fs = File.OpenWrite(filepath);
        JsonSerializer.Serialize(fs, dict, new JsonSerializerOptions() { WriteIndented = true });
    }
}
