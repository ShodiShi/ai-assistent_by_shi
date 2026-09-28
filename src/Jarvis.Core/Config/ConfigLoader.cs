using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Jarvis.Core.Config;

public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static AppConfig LoadAppConfig(string path)
    {
        var yaml = File.ReadAllText(path);
        return Deserializer.Deserialize<AppConfig>(yaml) ?? new AppConfig();
    }

    private record AppsFile
    {
        public List<AppEntry> Apps { get; init; } = new();
    }

    public static AppsCatalog LoadAppsCatalog(string path)
    {
        var yaml = File.ReadAllText(path);
        var file = Deserializer.Deserialize<AppsFile>(yaml) ?? new AppsFile();
        return new AppsCatalog(file.Apps);
    }
}
