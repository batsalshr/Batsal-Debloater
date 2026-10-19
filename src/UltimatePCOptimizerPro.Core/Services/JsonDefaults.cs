using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Services;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Indented = Create(true);
    public static readonly JsonSerializerOptions Compact = Create(false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static T? ReadFile<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Indented);
    }

    /// <summary>Atomic write: write to temp file then replace, so a crash never leaves a truncated backup.</summary>
    public static void WriteFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Indented));
        File.Move(tmp, path, overwrite: true);
    }

    public static T? ReadEmbedded<T>(string resourceSuffix)
    {
        var asm = typeof(JsonDefaults).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (name is null) return default;
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<T>(stream, Indented);
    }

    public static IEnumerable<string> EmbeddedNames(string prefix)
        => typeof(JsonDefaults).Assembly.GetManifestResourceNames().Where(n => n.Contains(prefix, StringComparison.OrdinalIgnoreCase));
}
