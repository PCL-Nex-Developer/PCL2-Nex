using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PCL.Core.Utils;
using YamlDotNet.Serialization;

namespace PCL.Core.App.Configuration.Storage;

public static class YamlToJsonConverter
{
    public static void Convert(Stream yamlInput, Stream jsonOutput, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(yamlInput);
        ArgumentNullException.ThrowIfNull(jsonOutput);
        if (!yamlInput.CanRead) throw new ArgumentException("must be readable", nameof(yamlInput));
        if (!jsonOutput.CanWrite) throw new ArgumentException("must be writable", nameof(jsonOutput));

        using var reader = new StreamReader(yamlInput, leaveOpen: leaveOpen);
        try
        {
            var value = new DeserializerBuilder()
                .WithAttemptingUnquotedStringTypeDeserialization()
                .Build()
                .Deserialize<object?>(reader);
            JsonSerializer.Serialize(jsonOutput, Normalize(value), JsonCompat.SerializerOptions);
        }
        finally
        {
            if (!leaveOpen) jsonOutput.Dispose();
        }
    }

    private static object? Normalize(object? value)
    {
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
                result[entry.Key?.ToString() ?? string.Empty] = Normalize(entry.Value);
            return result;
        }

        if (value is IEnumerable sequence and not string)
            return sequence.Cast<object?>().Select(Normalize).ToList();

        return value;
    }
}
