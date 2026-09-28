namespace Jarvis.Core.Tools;

public interface IToolRegistry
{
    ITool? Find(string name);
    IReadOnlyList<ITool> All { get; }
}
