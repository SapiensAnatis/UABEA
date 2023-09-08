using AssetsTools.NET;
using AssetsTools.NET.Extra;
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
            .Select(GetPrimitiveFieldValue)
            .Where(IsNonEmptyKey);

        IEnumerable<object> values = dict["entriesValue.Array"].Children.Select(
            x => x.Children.ToDictionary(c => c.FieldName, GetPrimitiveFieldValue)
        );

        Dictionary<object, object> newDict = keys.Zip(values)
            .ToDictionary(x => x.First, x => x.Second);

        using FileStream fs = File.Open(filepath, FileMode.Create, FileAccess.ReadWrite);
        JsonSerializer.Serialize(fs, newDict, new JsonSerializerOptions() { WriteIndented = true });
    }

    private static void UpdateFromStringDictionary(
        AssetTypeValueField dict,
        Dictionary<string, JsonElement> source
    )
    {
        SerializableDictionary<string, JsonElement> serializedDict =
            new(source, DeterministicStringEqualityComparer.Instance);

        UpdateFromDictionary(dict, serializedDict);
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

        UpdateFromDictionary(dict, serializedDict);
    }

    private static void UpdateFromDictionary<TKey>(
        AssetTypeValueField dict,
        SerializableDictionary<TKey, JsonElement> serializedDict
    )
    {
        UpdateFromArray(dict["buckets.Array"], serializedDict.buckets);
        UpdateFromArray(dict["entriesHashCode.Array"], serializedDict.entriesHashCode);
        UpdateFromArray(dict["entriesKey.Array"], serializedDict.entriesKey);
        UpdateFromArray(dict["entriesNext.Array"], serializedDict.entriesNext);
        UpdateFromObjects(dict["entriesValue.Array"], serializedDict.entriesValue);

        dict["count"].AsInt = serializedDict.Count;
        dict["freeCount"].AsInt = serializedDict.freeCount;
        dict["freeList"].AsInt = serializedDict.freeList;
    }

    private static void UpdateFromArray<TValue>(
        AssetTypeValueField array,
        IEnumerable<TValue> newValues
    )
    {
        array.Children = newValues
            .Select(
                x =>
                {
                    AssetTypeValueField newChild = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);

                    if (x is string stringValue)
                        newChild.AsString = stringValue;
                    else if (x is not null)
                        newChild.AsObject = x;

                    return newChild;
                }
                   
            )
            .ToList();
    }

    private static void UpdateFromObjects(
        AssetTypeValueField array,
        IEnumerable<JsonElement> newValues
    )
    {
        array.Children = newValues
            .Select(
                jsonObject =>
                {
                    AssetTypeValueField newChild = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
                    if (jsonObject.ValueKind == JsonValueKind.Undefined)
                        return newChild;

                    foreach (AssetTypeValueField grandChild in newChild)
                    {
                        if (!jsonObject.TryGetProperty(grandChild.FieldName, out JsonElement property))
                            throw new InvalidOperationException($"Missing JSON property: {grandChild.FieldName}");

                        object jsonProperty = DeserializeToValueType(property, grandChild.Value.ValueType);
                        grandChild.Value.AsObject = jsonProperty;
                    }

                    return newChild;
                }
            )
            .ToList();
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

    private static object GetPrimitiveFieldValue(AssetTypeValueField field)
    {
        return field.Value.ValueType switch
        {
            AssetValueType.Bool => field.AsBool,
            AssetValueType.Int32 => field.AsInt,
            AssetValueType.String => field.AsString,
            AssetValueType.Float => field.AsFloat,
            AssetValueType.Double => field.AsDouble,
            _ => throw new NotImplementedException($"Unrecognized type {field.Value.ValueType}"),
        };
    }

    private static object DeserializeToValueType(JsonElement element, AssetValueType fieldType)
    {
        return fieldType switch
        {
            AssetValueType.Bool => element.Deserialize<bool>(),
            AssetValueType.Int32 => element.Deserialize<int>(),
            AssetValueType.String => element.Deserialize<string>() ?? string.Empty,
            AssetValueType.Float => element.Deserialize<float>(),
            AssetValueType.Double => element.Deserialize<double>(),
            _ => throw new NotSupportedException($"Unexpected AssetValueType {fieldType}")
        };
    }
}
