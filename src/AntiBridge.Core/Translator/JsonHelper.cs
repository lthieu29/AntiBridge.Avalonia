using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace AntiBridge.Core.Translator;

/// <summary>
/// Helper class for JSON manipulation similar to gjson/sjson in Go
/// </summary>
public static class JsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>
    /// Get a value from JSON using dot notation path
    /// </summary>
    public static JsonNode? GetPath(JsonNode? root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path)) return root;
        
        var parts = path.Split('.');
        var current = root;
        
        foreach (var part in parts)
        {
            if (current == null) return null;
            
            // Handle array index
            if (int.TryParse(part, out var index))
            {
                if (current is JsonArray arr && index >= 0 && index < arr.Count)
                    current = arr[index];
                else
                    return null;
            }
            else if (current is JsonObject obj)
            {
                current = obj[part];
            }
            else
            {
                return null;
            }
        }
        
        return current;
    }

    /// <summary>
    /// Get string value from path
    /// </summary>
    public static string? GetString(JsonNode? root, string path)
    {
        var node = GetPath(root, path);
        return node?.GetValue<string>();
    }

    /// <summary>
    /// Get int value from path
    /// </summary>
    public static int? GetInt(JsonNode? root, string path)
    {
        var node = GetPath(root, path);
        if (node == null) return null;
        try { return node.GetValue<int>(); }
        catch { return null; }
    }

    /// <summary>
    /// Get long value from path
    /// </summary>
    public static long? GetLong(JsonNode? root, string path)
    {
        var node = GetPath(root, path);
        if (node == null) return null;
        try { return node.GetValue<long>(); }
        catch { return null; }
    }

    /// <summary>
    /// Get double value from path
    /// </summary>
    public static double? GetDouble(JsonNode? root, string path)
    {
        var node = GetPath(root, path);
        if (node == null) return null;
        try { return node.GetValue<double>(); }
        catch { return null; }
    }

    /// <summary>
    /// Get bool value from path
    /// </summary>
    public static bool? GetBool(JsonNode? root, string path)
    {
        var node = GetPath(root, path);
        if (node == null) return null;
        try { return node.GetValue<bool>(); }
        catch { return null; }
    }

    /// <summary>
    /// Check if path exists
    /// </summary>
    public static bool Exists(JsonNode? root, string path)
    {
        return GetPath(root, path) != null;
    }

    /// <summary>
    /// Set a value at path, creating intermediate objects as needed
    /// </summary>
    public static JsonNode SetPath(JsonNode root, string path, JsonNode? value)
    {
        if (string.IsNullOrEmpty(path)) return value ?? root;
        
        var parts = path.Split('.');
        var current = root;
        
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            
            if (part == "-1" && current is JsonArray arr)
            {
                // Append to array
                var newObj = new JsonObject();
                arr.Add(newObj);
                current = newObj;
            }
            else if (int.TryParse(part, out var index))
            {
                if (current is JsonArray array)
                {
                    while (array.Count <= index)
                        array.Add(new JsonObject());
                    current = array[index]!;
                }
            }
            else
            {
                if (current is JsonObject obj)
                {
                    if (obj[part] == null)
                    {
                        // Check if next part is array index or "-1"
                        var nextPart = parts[i + 1];
                        if (nextPart == "-1" || int.TryParse(nextPart, out _))
                            obj[part] = new JsonArray();
                        else
                            obj[part] = new JsonObject();
                    }
                    current = obj[part]!;
                }
            }
        }
        
        var lastPart = parts[^1];
        if (lastPart == "-1" && current is JsonArray finalArr)
        {
            finalArr.Add(value?.DeepClone());
        }
        else if (int.TryParse(lastPart, out var lastIndex) && current is JsonArray arr2)
        {
            while (arr2.Count <= lastIndex)
                arr2.Add(null);
            arr2[lastIndex] = value?.DeepClone();
        }
        else if (current is JsonObject finalObj)
        {
            finalObj[lastPart] = value?.DeepClone();
        }
        
        return root;
    }

    /// <summary>
    /// Set string value at path
    /// </summary>
    public static JsonNode SetString(JsonNode root, string path, string? value)
    {
        return SetPath(root, path, value != null ? JsonValue.Create(value) : null);
    }

    /// <summary>
    /// Set int value at path
    /// </summary>
    public static JsonNode SetInt(JsonNode root, string path, int value)
    {
        return SetPath(root, path, JsonValue.Create(value));
    }

    /// <summary>
    /// Set long value at path
    /// </summary>
    public static JsonNode SetLong(JsonNode root, string path, long value)
    {
        return SetPath(root, path, JsonValue.Create(value));
    }

    /// <summary>
    /// Set double value at path
    /// </summary>
    public static JsonNode SetDouble(JsonNode root, string path, double value)
    {
        return SetPath(root, path, JsonValue.Create(value));
    }

    /// <summary>
    /// Set bool value at path
    /// </summary>
    public static JsonNode SetBool(JsonNode root, string path, bool value)
    {
        return SetPath(root, path, JsonValue.Create(value));
    }

    /// <summary>
    /// Set raw JSON at path
    /// </summary>
    public static JsonNode SetRaw(JsonNode root, string path, string json)
    {
        var node = JsonNode.Parse(json);
        return SetPath(root, path, node);
    }

    /// <summary>
    /// Delete a path from JSON
    /// </summary>
    public static JsonNode Delete(JsonNode root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        
        var parts = path.Split('.');
        var current = root;
        
        for (int i = 0; i < parts.Length - 1; i++)
        {
            current = GetPath(current, parts[i]);
            if (current == null) return root;
        }
        
        var lastPart = parts[^1];
        if (current is JsonObject obj && obj.ContainsKey(lastPart))
        {
            obj.Remove(lastPart);
        }
        else if (int.TryParse(lastPart, out var index) && current is JsonArray arr && index < arr.Count)
        {
            arr.RemoveAt(index);
        }
        
        return root;
    }

    /// <summary>
    /// Parse JSON string to JsonNode
    /// </summary>
    public static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse JSON bytes to JsonNode
    /// </summary>
    public static JsonNode? Parse(byte[] json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convert JsonNode to string
    /// </summary>
    public static string Stringify(JsonNode? node)
    {
        if (node == null) return "null";
        return node.ToJsonString(Options);
    }

    /// <summary>
    /// Deep clone a JsonNode
    /// </summary>
    public static JsonNode? DeepClone(this JsonNode? node)
    {
        if (node == null) return null;
        return JsonNode.Parse(node.ToJsonString());
    }

    /// <summary>
    /// Check if node is array
    /// </summary>
    public static bool IsArray(JsonNode? node) => node is JsonArray;

    /// <summary>
    /// Check if node is object
    /// </summary>
    public static bool IsObject(JsonNode? node) => node is JsonObject;

    /// <summary>
    /// Get array from node
    /// </summary>
    public static JsonArray? AsArray(JsonNode? node) => node as JsonArray;

    /// <summary>
    /// Get object from node
    /// </summary>
    public static JsonObject? AsObject(JsonNode? node) => node as JsonObject;

    /// <summary>
    /// Recursively removes all "cache_control" fields from a JSON structure.
    /// This is necessary because VS Code and other clients may send back historical messages
    /// with cache_control fields that the Anthropic API does not accept.
    /// Ported from Rust: deep_clean_cache_control()
    /// </summary>
    /// <param name="node">The JSON node to clean</param>
    /// <returns>The number of cache_control fields removed</returns>
    public static int DeepCleanCacheControl(JsonNode? node)
    {
        if (node == null) return 0;

        int removed = 0;

        if (node is JsonObject obj)
        {
            // Remove cache_control from this object
            if (obj.Remove("cache_control"))
            {
                removed++;
            }

            // Recursively clean all child values
            // We need to iterate over a copy of keys since we're modifying during iteration
            var keys = obj.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                var child = obj[key];
                if (child != null)
                {
                    removed += DeepCleanCacheControl(child);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            // Recursively clean all array elements
            foreach (var item in arr)
            {
                removed += DeepCleanCacheControl(item);
            }
        }

        return removed;
    }

    /// <summary>
    /// Recursively removes all "[undefined]" string values from a JSON structure.
    /// This handles a common issue where clients like Cherry Studio inject "[undefined]" strings.
    /// Ported from Rust: deep_clean_undefined()
    /// </summary>
    /// <param name="node">The JSON node to clean</param>
    /// <returns>The number of "[undefined]" values removed or replaced</returns>
    public static int DeepCleanUndefined(JsonNode? node)
    {
        if (node == null) return 0;

        int cleaned = 0;

        if (node is JsonObject obj)
        {
            // Find keys with "[undefined]" values
            var keysToRemove = new List<string>();
            var keysToRecurse = new List<string>();

            foreach (var kvp in obj)
            {
                if (kvp.Value is JsonValue val)
                {
                    try
                    {
                        var strVal = val.GetValue<string>();
                        if (strVal == "[undefined]")
                        {
                            keysToRemove.Add(kvp.Key);
                        }
                    }
                    catch
                    {
                        // Not a string value, skip
                    }
                }
                else if (kvp.Value != null)
                {
                    keysToRecurse.Add(kvp.Key);
                }
            }

            // Remove keys with "[undefined]" values
            foreach (var key in keysToRemove)
            {
                obj.Remove(key);
                cleaned++;
            }

            // Recursively clean child objects/arrays
            foreach (var key in keysToRecurse)
            {
                var child = obj[key];
                if (child != null)
                {
                    cleaned += DeepCleanUndefined(child);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            // Find indices with "[undefined]" values (remove in reverse order)
            var indicesToRemove = new List<int>();

            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];
                if (item is JsonValue val)
                {
                    try
                    {
                        var strVal = val.GetValue<string>();
                        if (strVal == "[undefined]")
                        {
                            indicesToRemove.Add(i);
                        }
                    }
                    catch
                    {
                        // Not a string value, recurse
                        cleaned += DeepCleanUndefined(item);
                    }
                }
                else if (item != null)
                {
                    cleaned += DeepCleanUndefined(item);
                }
            }

            // Remove in reverse order to preserve indices
            for (int i = indicesToRemove.Count - 1; i >= 0; i--)
            {
                arr.RemoveAt(indicesToRemove[i]);
                cleaned++;
            }
        }

        return cleaned;
    }
}
