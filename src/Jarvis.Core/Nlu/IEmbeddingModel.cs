namespace Jarvis.Core.Nlu;

public interface IEmbeddingModel
{
    float[] Embed(string text);
}
