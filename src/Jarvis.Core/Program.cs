using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Jarvis.Core.Logging;

var configPath = Path.Combine("config", "config.yaml");
var appsPath = Path.Combine("config", "apps.yaml");

AppConfig config;
AppsCatalog appsCatalog;
try
{
    config = ConfigLoader.LoadAppConfig(configPath);
    appsCatalog = ConfigLoader.LoadAppsCatalog(appsPath);
}
catch (Exception ex)
{
    Console.WriteLine($"Не удалось загрузить конфиги ({ex.Message}), использую значения по умолчанию.");
    config = new AppConfig();
    appsCatalog = new AppsCatalog(new List<AppEntry>());
}

var logger = LoggingSetup.CreateLogger("logs", config.LogRetentionDays);

var powerActions = new WindowsSystemPowerActions();

var tools = new ITool[]
{
    new OpenAppTool(appsCatalog, new WindowsProcessLauncher()),
    new CloseAppTool(appsCatalog, new WindowsProcessCloser()),
    new SystemControlTool(powerActions, config.ShutdownConfirmSeconds),
    new VolumeControlTool(new NAudioVolumeController(), config.VolumeStepPercent),
    new GetSystemInfoTool(new WmiSystemInfoProvider()),
};
var registry = new ToolRegistry(tools);

var resolvers = new List<ICommandResolver> { new RuleBasedResolver() };

if (OnnxEmbeddingModel.FilesExist(config.EmbeddingModelPath, config.EmbeddingTokenizerPath))
{
    try
    {
        var samples = IntentCatalog.LoadFromDirectory(config.IntentsDirectory);
        var embeddingModel = new OnnxEmbeddingModel(config.EmbeddingModelPath, config.EmbeddingTokenizerPath);
        resolvers.Add(new EmbeddingResolver(embeddingModel, samples, config.EmbeddingThreshold));
    }
    catch (Exception ex)
    {
        // Файлы модели есть, но конструктор всё равно упал (повреждённый файл, несовместимый
        // формат токенизатора, ошибка создания сессии ONNX Runtime и т.п.) — деградация до
        // уровня 1, а не падение процесса REPL.
        logger.Warning(ex, "Не удалось инициализировать модель уровня 2 ({ModelPath}), работают только правила уровня 1.", config.EmbeddingModelPath);
    }
}
else
{
    logger.Warning("Модель уровня 2 не найдена ({ModelPath}), работают только правила уровня 1.", config.EmbeddingModelPath);
}

var context = new NluContext(appsCatalog);
var pipeline = new CommandPipeline(resolvers, registry, context);

Console.WriteLine("Jarvis.Core — этап 1 (текстовый режим). Ctrl+C для выхода.");

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (line is null) break;
    if (line.Trim().Length == 0) continue;

    if (line.Trim().Equals("отмена", StringComparison.OrdinalIgnoreCase))
        powerActions.CancelShutdown();

    var outcome = await pipeline.ProcessAsync(line);

    logger.Information("{@Entry}", new CommandLogEntry(
        DateTimeOffset.Now, line, outcome.Level, outcome.ToolName, outcome.Resolved ? "success" : "unresolved"));

    Console.WriteLine(outcome.Message);
}
