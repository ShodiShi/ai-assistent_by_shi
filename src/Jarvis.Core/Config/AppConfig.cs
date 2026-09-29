namespace Jarvis.Core.Config;

public record AppConfig
{
    public string Language { get; init; } = "ru";
    // Эмпирически откалибровано на реальной скачанной модели (multilingual-e5-small int8) против
    // всех 50 фраз фикстуры Task 13: 0.72 (исходное предположение) давал 6/6 ложных срабатываний
    // на заведомо нераспознаваемых фразах; 0.90 (с симметричным "query:" префиксом на обеих
    // сторонах — см. EmbeddingResolver) даёт 0 ложных срабатываний при 40/44 верных совпадений —
    // лучший результат среди опробованных вариантов. См.
    // OnnxEmbeddingModelIntegrationTests.RealCatalog_EmbeddingResolver для деталей замера и
    // roadmap risk-секцию про смещение ID токенизатора, которое было причиной изначально
    // бессмысленных оценок (исправлено в OnnxEmbeddingModel.RemapToFairseqIds).
    public double EmbeddingThreshold { get; init; } = 0.90;
    public int VolumeStepPercent { get; init; } = 10;
    public int ShutdownConfirmSeconds { get; init; } = 20;
    public int ConfirmationTimeoutSeconds { get; init; } = 15;
    public int LogRetentionDays { get; init; } = 14;
    public string EmbeddingModelPath { get; init; } = "models/e5-small-int8.onnx";
    public string EmbeddingTokenizerPath { get; init; } = "models/sentencepiece.bpe.model";
    public string IntentsDirectory { get; init; } = "config/intents";
}
