using AssetsTools.NET;
using Avalonia.Platform.Storage;
using SerializableDictionaryPlugin.Options;
using System.Diagnostics;
using System.Text.Json;

namespace SerializableDictionaryPlugin;

public static class SerializableDictionaryHelper
{
    public static void UpdateFromFile(string filepath, AssetTypeValueField baseField)
    {
        using FileStream fs = File.OpenRead(filepath);

        Dictionary<string, JsonElement>? baseDictionary = JsonSerializer.Deserialize<
            Dictionary<string, JsonElement>
        >(fs);

        if (baseDictionary is null || !baseDictionary.Any())
            throw new ArgumentException("Null or empty dictionary");

        AssetTypeValueField dict = baseField["dict"];
        AssetValueType keyType = dict["entriesKey.Array"][0].TemplateField.ValueType;

        switch (keyType)
        {
            case AssetValueType.Int32:
                UpdateFromIntDictionary(dict, baseDictionary);
                break;
            case AssetValueType.String:
                UpdateFromStringDictionary(dict, baseDictionary);
                break;
            default:
                throw new NotSupportedException($"Keys of type {keyType} are not supported.");
        }
    }

    public static void WriteToFile(string filepath, AssetTypeValueField baseField)
    {
        AssetTypeValueField dict = baseField["dict"];

        IEnumerable<object> keys = dict["entriesKey.Array"].Children
            .Select(x => x.AsObject)
            .Where(IsNonEmptyKey);

        IEnumerable<object> values = dict["entriesValue.Array"].Children.Select(
            x => x.Children.ToDictionary(c => c.FieldName, c => c.AsObject)
        );

        Dictionary<object, object> newDict = keys.Zip(values)
            .ToDictionary(x => x.First, x => x.Second);

        using FileStream fs = File.OpenWrite(filepath);
        JsonSerializer.Serialize(fs, newDict, new JsonSerializerOptions() { WriteIndented = true });
    }

    private static void UpdateFromIntDictionary(
        AssetTypeValueField dict,
        Dictionary<string, JsonElement> source
    )
    {
        SerializableDictionary<int, JsonElement> serializedDict =
            new(
                source.Select(x =>
                {
                    if (!int.TryParse(x.Key, out int intKey))
                    {
                        Debugger.Break();
                        throw new ArgumentException(nameof(x));
                    }

                    return new KeyValuePair<int, JsonElement>(intKey, x.Value);
                })
            );

        dict["count"].AsInt = serializedDict.Count;
        dict["buckets.Array"].Children.UpdateWithArray(serializedDict.buckets);
        dict["entriesHashCode.Array"].Children.UpdateWithArray(serializedDict.entriesHashCode);
        dict["entriesNext.Array"].Children.UpdateWithArray(serializedDict.entriesNext);
        dict["entriesKey.Array"].Children.UpdateWithArray(serializedDict.entriesKey);

        foreach (
            (AssetTypeValueField field, JsonElement newValue) in dict["entriesValue.Array"].Zip(
                serializedDict.entriesValue.Where(x => x.ValueKind != JsonValueKind.Undefined)
            )
        )
        {
            foreach (AssetTypeValueField subField in field.Children)
            {
                if (!newValue.TryGetProperty(subField.FieldName, out JsonElement newValueField))
                {
                    throw new InvalidOperationException(
                        $"NewValue was missing property: {subField.FieldName}. "
                            + $"NewValue: ${JsonSerializer.Serialize(newValue, new JsonSerializerOptions { WriteIndented = true })}"
                    );
                }

                subField.Value.AsObject = subField.Value.ValueType switch
                {
                    AssetValueType.Bool => newValueField.Deserialize<bool>(),
                    AssetValueType.Int32 => newValueField.Deserialize<int>(),
                    AssetValueType.String => newValueField.Deserialize<string>(),
                    AssetValueType.Float => newValueField.Deserialize<float>(),
                    AssetValueType.Double => newValueField.Deserialize<double>(),
                    _
                        => throw new NotSupportedException(
                            $"Unexpected AssetValueType {subField.Value.ValueType}"
                        )
                };
            }
        }

        dict["freeCount"].AsInt = serializedDict.freeCount;
        dict["freeList"].AsInt = serializedDict.freeList;
    }

    private static void UpdateFromStringDictionary(
        AssetTypeValueField dict,
        Dictionary<string, JsonElement> source
    )
    {
        SerializableDictionary<string, JsonElement> serializedDict =
            new(source, DeterministicStringEqualityComparer.Instance);

        dict["buckets.Array"].Children.UpdateWithArray(serializedDict.buckets);
        dict["count"].AsInt = serializedDict.Count;
        dict["entriesHashCode.Array"].Children.UpdateWithArray(serializedDict.entriesHashCode);
        dict["entriesNext.Array"].Children.UpdateWithArray(serializedDict.entriesNext);
        dict["entriesKey.Array"].Children.UpdateWithArray(serializedDict.entriesKey);
        //  dict["entriesValue.Array"].Children = serializedDict.entriesValue.AsArrayChildren();
        dict["freeCount"].AsInt = serializedDict.freeCount;
        dict["freeList"].AsInt = serializedDict.freeList;
    }

    private static bool IsNonEmptyKey(object key)
    {
        return key switch
        {
            int intValue => intValue is not 0,
            string stringValue => !string.IsNullOrEmpty(stringValue),
            _ => throw new NotImplementedException(),
        };
    }
}

file static class EnumerableExtensions
{
    public static void UpdateWithArray(
        this List<AssetTypeValueField> fields,
        IEnumerable<int> newValues
    )
    {
        foreach ((AssetTypeValueField field, int newValue) in fields.Zip(newValues))
        {
            field.AsInt = newValue;
        }
    }

    public static void UpdateWithArray(
        this List<AssetTypeValueField> fields,
        IEnumerable<string> newValues
    )
    {
        foreach ((AssetTypeValueField field, string newValue) in fields.Zip(newValues))
        {
            field.AsString = newValue;
        }
    }

    public static List<AssetTypeValueField> AsArrayChildren(this IEnumerable<string> values)
    {
        return values
            .Select(
                x =>
                    new AssetTypeValueField
                    {
                        Value = new(x),
                        TemplateField = new() { Name = "data" }
                    }
            )
            .ToList();
    }
}
