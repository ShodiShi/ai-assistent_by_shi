using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Jarvis.Core.Nlu;

public static class IntentCatalog
{
    private record IntentFile
    {
        public string Tool { get; init; } = "";
        public List<string> Phrases { get; init; } = new();
    }

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static IReadOnlyList<IntentSample> LoadFromDirectory(string directory)
    {
        var samples = new List<IntentSample>();
        if (!Directory.Exists(directory))
            return samples;

        foreach (var file in Directory.GetFiles(directory, "*.yaml"))
        {
            var content = Deserializer.Deserialize<IntentFile>(File.ReadAllText(file));
            foreach (var phrase in content.Phrases)
                samples.Add(new IntentSample(content.Tool, phrase));
        }
        return samples;
    }
}
