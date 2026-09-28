namespace Jarvis.Core.Nlu;

public interface ICommandResolver
{
    int Level { get; }
    bool IsAvailable { get; }
    Task<ResolveResult> ResolveAsync(string utterance, NluContext context);
}
