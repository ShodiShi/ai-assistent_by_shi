namespace Jarvis.Core.Config;

public record AppConfig
{
    public string Language { get; init; } = "ru";
    public double EmbeddingThreshold { get; init; } = 0.72;
    public int VolumeStepPercent { get; init; } = 10;
    public int ShutdownConfirmSeconds { get; init; } = 20;
    public int ConfirmationTimeoutSeconds { get; init; } = 15;
    public int LogRetentionDays { get; init; } = 14;
    public string EmbeddingModelPath { get; init; } = "models/e5-small-int8.onnx";
    public string EmbeddingTokenizerPath { get; init; } = "models/sentencepiece.bpe.model";
    public string IntentsDirectory { get; init; } = "config/intents";
}
