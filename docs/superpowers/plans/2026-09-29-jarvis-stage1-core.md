# Jarvis Stage 1 — Text-Mode Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Цель:** Собрать рабочее консольное приложение `Jarvis.Core` на .NET 8, которое читает русские
текстовые команды из stdin, разбирает их двухуровневым NLU-конвейером (regex/алиасы, затем
embedding-сходство), выполняет один из 5 Windows-инструментов с подтверждением для опасных
действий, логирует каждый шаг и покрыто регрессионными тестами на 50+ фраз.

**Архитектура:** Слоёное консольное приложение: `Config` загружает YAML → `Nlu.CommandPipeline`
разбирает свободный текст в вызов инструмента через уровни `ICommandResolver` (`RuleBasedResolver`,
`EmbeddingResolver`) → реализации `Tools.ITool` выполняются через тонкие интерфейсы `OsActions`
(реальные Windows P/Invoke реализации в продакшене, фейки в тестах) → `Serilog` логирует результат
→ `Program.cs` крутит REPL и владеет машиной состояний подтверждения для опасных действий.

**Стек:** .NET 8, xUnit, YamlDotNet, Serilog (+ файловый sink), Microsoft.Extensions.Hosting/DI,
Microsoft.ML.OnnxRuntime, Microsoft.ML.Tokenizers (SentencePieceTokenizer), NAudio (CoreAudio).

**Спека:**
- `docs/superpowers/specs/2026-09-29-jarvis-roadmap.md`
- `docs/superpowers/specs/2026-09-29-jarvis-stage1-core-design.md`

## Глобальные ограничения

- Везде целевой фреймворк `net8.0` (SDK 10 установлен, net8.0 runtime есть — не переходить на net10.0).
- Никаких сетевых запросов во время работы, кроме однократного скрипта скачивания моделей; само
  приложение никогда не стучится в сеть.
- Каждый побочный эффект на уровне ОС (запуск/убийство процесса, выключение, громкость, инфо о
  системе) — за интерфейсом в `Jarvis.Core.Tools.OsActions`, чтобы юнит-тесты никогда не трогали
  реальную машину.
- Опасные действия (`system_control` shutdown/restart/sleep, принудительное закрытие в `close_app`)
  всегда требуют явного «да» на следующем вводе перед реальным выполнением — без исключений и
  автоподтверждения.
- `dotnet test` должен быть безопасен для многократного запуска на рабочей машине: он никогда не
  должен по-настоящему выключать, усыплять, блокировать компьютер, убивать реальный процесс или
  менять реальную громкость.
- Логи никогда не содержат аудио (в Этапе 1 его и так нет) и обязаны включать: время, фразу,
  уровень разбора, имя инструмента, результат.
- 50+ тестовых фраз на русском, покрывающих все 5 инструментов; ≥70% должны разрешаться суммарно
  уровнями 1-2 (согласно цели из roadmap).

## Фокус ревью

- **Шум в регистре/пробелах/пунктуации** («Открой ХРОМ!!», лишние пробелы, «пожалуйста» в конце) —
  разумный пользователь ожидает, что это всё равно разрешится уровнем 1, а не молча уйдёт в
  уровень 2/«не понял». Покрыто в Task 10 (нормализация в `RuleBasedResolver`) и Task 13
  (кейсы в фикстуре фраз).
- **Неоднозначное имя приложения, совпадающее с двумя алиасами** (например, имя — подстрока сразу
  двух записей в `apps.yaml`) — ожидаемое поведение «не найдено / уточни», а не тихий выбор не того
  приложения. Покрыто в Task 7 (`AppsCatalog.FindByNameOrAlias` ищет точное совпадение по алиасу,
  без нечёткого угадывания по подстроке).
- **Перехват подтверждения посторонней командой** — если есть ожидающее опасное действие, а
  пользователь говорит что-то не по теме вместо «да»/«отмена», по спеке ожидающее действие должно
  сбрасываться, а не выполниться незаметно позже. Покрыто в Task 12 (тест машины состояний
  подтверждения в `CommandPipeline`: посторонний ввод сбрасывает ожидание и обрабатывается как
  обычная команда).
- **Отсутствует файл embedding-модели или токенизатора** — по спеке нужна плавная деградация до
  одного уровня 1, а не падение. Покрыто в Task 12 (`Program.cs` вызывает
  `OnnxEmbeddingModel.FilesExist` перед созданием `EmbeddingResolver`; если файла нет, регистрируется
  только `RuleBasedResolver`, и один раз при старте пишется предупреждение в лог) и в Task 11
  (`EmbeddingResolver.IsAvailable` отдельно закрывает случай нулевых intent-образцов).
- **`close_app` вызван для приложения без запущенного процесса** — разумный пользователь ожидает
  «он и так не запущен», а не запрос на принудительное закрытие нуля процессов. Покрыто в Task 8
  (`CloseAppTool` возвращает обычный результат без подтверждения, если список процессов пуст).

---

## Task 1: Solution и структура проектов

**Files:**
- Create: `Jarvis.sln`
- Create: `src/Jarvis.Core/Jarvis.Core.csproj`
- Create: `src/Jarvis.Core/Program.cs`
- Create: `src/Jarvis.Core.Tests/Jarvis.Core.Tests.csproj`
- Create: `src/Jarvis.Core.Tests/UnitTest1.cs` (создаётся командой `dotnet new xunit`, удаляется в Task 4)

**Interfaces:**
- Даёт: собираемое консольное приложение `net8.0` (`Jarvis.Core`) и собираемый тестовый проект
  `net8.0` xUnit (`Jarvis.Core.Tests`), в которые следующие задачи будут добавлять файлы.

- [ ] **Шаг 1: Создать консольный проект**

```bash
dotnet new console -n Jarvis.Core -o src/Jarvis.Core --framework net8.0
```

- [ ] **Шаг 2: Создать тестовый проект и подключить ссылку**

```bash
dotnet new xunit -n Jarvis.Core.Tests -o src/Jarvis.Core.Tests --framework net8.0
dotnet add src/Jarvis.Core.Tests reference src/Jarvis.Core
```

- [ ] **Шаг 3: Создать solution и добавить оба проекта**

```bash
dotnet new sln -n Jarvis
dotnet sln Jarvis.sln add src/Jarvis.Core/Jarvis.Core.csproj src/Jarvis.Core.Tests/Jarvis.Core.Tests.csproj
```

- [ ] **Шаг 4: Добавить NuGet-пакеты, нужные для Этапа 1**

```bash
dotnet add src/Jarvis.Core package YamlDotNet
dotnet add src/Jarvis.Core package Serilog
dotnet add src/Jarvis.Core package Serilog.Sinks.Console
dotnet add src/Jarvis.Core package Serilog.Sinks.File
dotnet add src/Jarvis.Core package Microsoft.Extensions.Hosting
dotnet add src/Jarvis.Core package Microsoft.ML.OnnxRuntime
dotnet add src/Jarvis.Core package Microsoft.ML.Tokenizers --prerelease
```

- [ ] **Шаг 5: Заменить сгенерированный `Program.cs` минимальным баннером**

```csharp
Console.WriteLine("Jarvis.Core — этап 1 (текстовый режим). Ctrl+C для выхода.");
```

- [ ] **Шаг 6: Проверить сборку и прогон тестов**

Команда: `dotnet test`
Ожидается: PASS (тест-заглушка из шаблона проходит; удалим её в Task 4, когда появятся настоящие тесты).

- [ ] **Step 7: Закоммитить**

```bash
git add Jarvis.sln src/Jarvis.Core src/Jarvis.Core.Tests
git commit -m "chore: scaffold Jarvis.Core console app and test project"
```

---

## Task 2: Модели конфигов и загрузчик

**Files:**
- Create: `src/Jarvis.Core/Config/AppConfig.cs`
- Create: `src/Jarvis.Core/Config/AppEntry.cs`
- Create: `src/Jarvis.Core/Config/AppsCatalog.cs`
- Create: `src/Jarvis.Core/Config/ConfigLoader.cs`
- Create: `config/config.yaml`
- Create: `config/apps.yaml`
- Test: `src/Jarvis.Core.Tests/Config/ConfigLoaderTests.cs`

**Interfaces:**
- Даёт: `AppConfig` (record), `AppEntry` (record), `AppsCatalog.FindByNameOrAlias(string)`,
  `ConfigLoader.LoadAppConfig(string path)`, `ConfigLoader.LoadAppsCatalog(string path)`.
  Следующие задачи (Tools, Nlu) используют поля `AppConfig` и `AppsCatalog.FindByNameOrAlias`.

- [ ] **Step 1: Написать падающий тест на загрузку `AppConfig`**

```csharp
// src/Jarvis.Core.Tests/Config/ConfigLoaderTests.cs
using Jarvis.Core.Config;
using Xunit;

namespace Jarvis.Core.Tests.Config;

public class ConfigLoaderTests
{
    [Fact]
    public void LoadAppConfig_ParsesKnownFields()
    {
        var yaml = """
        language: ru
        embedding_threshold: 0.72
        volume_step_percent: 10
        shutdown_confirm_seconds: 20
        log_retention_days: 14
        embedding_model_path: models/e5-small-int8.onnx
        embedding_tokenizer_path: models/sentencepiece.bpe.model
        intents_directory: config/intents
        """;
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var config = ConfigLoader.LoadAppConfig(path);

        Assert.Equal("ru", config.Language);
        Assert.Equal(0.72, config.EmbeddingThreshold);
        Assert.Equal(10, config.VolumeStepPercent);
        Assert.Equal(20, config.ShutdownConfirmSeconds);
        Assert.Equal(14, config.LogRetentionDays);
    }

    [Fact]
    public void LoadAppsCatalog_FindsEntryByAlias()
    {
        var yaml = """
        apps:
          - name: Google Chrome
            aliases: [хром, гугл хром, chrome]
            path: C:\Program Files\Google\Chrome\Application\chrome.exe
        """;
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var catalog = ConfigLoader.LoadAppsCatalog(path);
        var found = catalog.FindByNameOrAlias("хром");

        Assert.NotNull(found);
        Assert.Equal("Google Chrome", found!.Name);
    }

    [Fact]
    public void LoadAppsCatalog_UnknownAlias_ReturnsNull()
    {
        var yaml = "apps: []";
        var path = Path.GetTempFileName();
        File.WriteAllText(path, yaml);

        var catalog = ConfigLoader.LoadAppsCatalog(path);

        Assert.Null(catalog.FindByNameOrAlias("несуществующее"));
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter ConfigLoaderTests`
Ожидается: FAIL (ошибка компиляции — пространства имён `Jarvis.Core.Config` ещё нет).

- [ ] **Шаг 3: Написать модели**

```csharp
// src/Jarvis.Core/Config/AppConfig.cs
namespace Jarvis.Core.Config;

public record AppConfig
{
    public string Language { get; init; } = "ru";
    public double EmbeddingThreshold { get; init; } = 0.72;
    public int VolumeStepPercent { get; init; } = 10;
    public int ShutdownConfirmSeconds { get; init; } = 20;
    public int LogRetentionDays { get; init; } = 14;
    public string EmbeddingModelPath { get; init; } = "models/e5-small-int8.onnx";
    public string EmbeddingTokenizerPath { get; init; } = "models/sentencepiece.bpe.model";
    public string IntentsDirectory { get; init; } = "config/intents";
}
```

```csharp
// src/Jarvis.Core/Config/AppEntry.cs
namespace Jarvis.Core.Config;

public record AppEntry
{
    public required string Name { get; init; }
    public List<string> Aliases { get; init; } = new();
    public string? Path { get; init; }
    public string? ShellCommand { get; init; }
}
```

```csharp
// src/Jarvis.Core/Config/AppsCatalog.cs
namespace Jarvis.Core.Config;

public class AppsCatalog
{
    private readonly List<AppEntry> _apps;

    public AppsCatalog(List<AppEntry> apps) => _apps = apps;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public AppEntry? FindByNameOrAlias(string text)
    {
        var needle = text.Trim().ToLowerInvariant();
        foreach (var app in _apps)
        {
            if (string.Equals(app.Name, needle, StringComparison.OrdinalIgnoreCase))
                return app;
            if (app.Aliases.Any(a => string.Equals(a, needle, StringComparison.OrdinalIgnoreCase)))
                return app;
        }
        return null;
    }
}
```

```csharp
// src/Jarvis.Core/Config/ConfigLoader.cs
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
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter ConfigLoaderTests`
Ожидается: PASS (3 теста)

- [ ] **Шаг 5: Написать настоящие `config/config.yaml` и `config/apps.yaml`**

```yaml
# config/config.yaml
language: ru
embedding_threshold: 0.72
volume_step_percent: 10
shutdown_confirm_seconds: 20
log_retention_days: 14
embedding_model_path: models/e5-small-int8.onnx
embedding_tokenizer_path: models/sentencepiece.bpe.model
intents_directory: config/intents
```

```yaml
# config/apps.yaml
apps:
  - name: Google Chrome
    aliases: [хром, гугл хром, chrome, браузер]
    path: C:\Program Files\Google\Chrome\Application\chrome.exe
  - name: Telegram
    aliases: [телеграм, телега, telegram]
    path: "%APPDATA%\\Telegram Desktop\\Telegram.exe"
  - name: Discord
    aliases: [дискорд, discord]
    path: "%LOCALAPPDATA%\\Discord\\Update.exe"
    shell_command: "--processStart Discord.exe"
  - name: Visual Studio Code
    aliases: [вс код, vs code, код, vscode]
    path: "%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe"
  - name: Проводник
    aliases: [проводник, файлы, explorer]
    path: explorer.exe
  - name: Блокнот
    aliases: [блокнот, notepad]
    path: notepad.exe
```

- [ ] **Step 6: Закоммитить**

```bash
git add src/Jarvis.Core/Config src/Jarvis.Core.Tests/Config config/config.yaml config/apps.yaml
git commit -m "feat: add config/apps.yaml models and YAML loader"
```

---

## Task 3: Структурированное логирование через Serilog

**Files:**
- Create: `src/Jarvis.Core/Logging/CommandLogEntry.cs`
- Create: `src/Jarvis.Core/Logging/LoggingSetup.cs`
- Test: `src/Jarvis.Core.Tests/Logging/LoggingSetupTests.cs`

**Interfaces:**
- Использует: ничего из предыдущих задач.
- Даёт: `LoggingSetup.CreateLogger(string logDirectory, int retentionDays)`, возвращающий
  `Serilog.ILogger`; `CommandLogEntry(DateTimeOffset Time, string Phrase, int Level, string?
  ToolName, string Result)` — далее `CommandPipeline`/`Program.cs` вызывают
  `logger.Information("{@Entry}", entry)`.

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Logging/LoggingSetupTests.cs
using Jarvis.Core.Logging;
using Serilog.Events;
using Serilog.Sinks.TestCorrelator;
using Xunit;

namespace Jarvis.Core.Tests.Logging;

public class LoggingSetupTests
{
    [Fact]
    public void CommandLogEntry_LogsAllRequiredFields()
    {
        using var context = TestCorrelator.CreateContext();
        var logger = new Serilog.LoggerConfiguration()
            .WriteTo.TestCorrelator()
            .CreateLogger();

        var entry = new CommandLogEntry(
            Time: DateTimeOffset.UtcNow,
            Phrase: "открой хром",
            Level: 1,
            ToolName: "open_app",
            Result: "success");

        logger.Information("{@Entry}", entry);

        var logEvent = Assert.Single(TestCorrelator.GetLogEventsFromContextGuid(context.Guid));
        var props = logEvent.Properties["Entry"].ToString();
        Assert.Contains("открой хром", props);
        Assert.Contains("open_app", props);
        Assert.Contains("success", props);
    }
}
```

- [ ] **Шаг 2: Добавить пакет test correlator и убедиться, что тест падает**

```bash
dotnet add src/Jarvis.Core.Tests package Serilog.Sinks.TestCorrelator
```

Команда: `dotnet test --filter LoggingSetupTests`
Ожидается: FAIL (пространства имён `Jarvis.Core.Logging` ещё нет).

- [ ] **Шаг 3: Реализовать `CommandLogEntry` и `LoggingSetup`**

```csharp
// src/Jarvis.Core/Logging/CommandLogEntry.cs
namespace Jarvis.Core.Logging;

public record CommandLogEntry(
    DateTimeOffset Time,
    string Phrase,
    int Level,
    string? ToolName,
    string Result);
```

```csharp
// src/Jarvis.Core/Logging/LoggingSetup.cs
using Serilog;

namespace Jarvis.Core.Logging;

public static class LoggingSetup
{
    public static Serilog.ILogger CreateLogger(string logDirectory, int retentionDays)
    {
        Directory.CreateDirectory(logDirectory);
        return new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, "jarvis-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retentionDays)
            .CreateLogger();
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter LoggingSetupTests`
Ожидается: PASS

- [ ] **Step 5: Закоммитить**

```bash
git add src/Jarvis.Core/Logging src/Jarvis.Core.Tests/Logging src/Jarvis.Core.Tests/Jarvis.Core.Tests.csproj
git commit -m "feat: add structured command logging via Serilog"
```

---

## Task 4: Базовые абстракции инструментов

**Files:**
- Create: `src/Jarvis.Core/Tools/ToolResult.cs`
- Create: `src/Jarvis.Core/Tools/PendingConfirmation.cs`
- Create: `src/Jarvis.Core/Tools/ToolContext.cs`
- Create: `src/Jarvis.Core/Tools/ITool.cs`
- Create: `src/Jarvis.Core/Tools/IToolRegistry.cs`
- Create: `src/Jarvis.Core/Tools/ToolRegistry.cs`
- Test: `src/Jarvis.Core.Tests/Tools/ToolRegistryTests.cs`
- Delete: `src/Jarvis.Core.Tests/UnitTest1.cs` (сгенерированная заглушка из Task 1)

**Interfaces:**
- Даёт: `ToolResult(bool Success, string Message, PendingConfirmation? Confirmation = null)`,
  `PendingConfirmation(string ToolName, IReadOnlyDictionary<string, object?> Args, string
  Prompt)`, `ToolContext(bool IsConfirmed)`, `ITool { string Name; Task<ToolResult>
  ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context); }`,
  `IToolRegistry { ITool? Find(string name); IReadOnlyList<ITool> All; }`. Каждая задача с
  инструментом (5-9) реализует `ITool` по этому контракту; `CommandPipeline` (Task 12) использует
  `IToolRegistry.Find` и читает `ToolResult.Confirmation`.

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Tools/ToolRegistryTests.cs
using Jarvis.Core.Tools;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeTool : ITool
{
    public string Name => "fake_tool";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
        => Task.FromResult(new ToolResult(true, "ok"));
}

public class ToolRegistryTests
{
    [Fact]
    public void Find_ReturnsRegisteredTool()
    {
        var registry = new ToolRegistry(new ITool[] { new FakeTool() });

        var tool = registry.Find("fake_tool");

        Assert.NotNull(tool);
        Assert.Equal("fake_tool", tool!.Name);
    }

    [Fact]
    public void Find_UnknownName_ReturnsNull()
    {
        var registry = new ToolRegistry(Array.Empty<ITool>());

        Assert.Null(registry.Find("does_not_exist"));
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter ToolRegistryTests`
Ожидается: FAIL (пространства имён `Jarvis.Core.Tools` ещё нет).

- [ ] **Шаг 3: Реализовать абстракции**

```csharp
// src/Jarvis.Core/Tools/PendingConfirmation.cs
namespace Jarvis.Core.Tools;

public record PendingConfirmation(
    string ToolName,
    IReadOnlyDictionary<string, object?> Args,
    string Prompt);
```

```csharp
// src/Jarvis.Core/Tools/ToolResult.cs
namespace Jarvis.Core.Tools;

public record ToolResult(bool Success, string Message, PendingConfirmation? Confirmation = null);
```

```csharp
// src/Jarvis.Core/Tools/ToolContext.cs
namespace Jarvis.Core.Tools;

public record ToolContext(bool IsConfirmed = false);
```

```csharp
// src/Jarvis.Core/Tools/ITool.cs
namespace Jarvis.Core.Tools;

public interface ITool
{
    string Name { get; }
    Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context);
}
```

```csharp
// src/Jarvis.Core/Tools/IToolRegistry.cs
namespace Jarvis.Core.Tools;

public interface IToolRegistry
{
    ITool? Find(string name);
    IReadOnlyList<ITool> All { get; }
}
```

```csharp
// src/Jarvis.Core/Tools/ToolRegistry.cs
namespace Jarvis.Core.Tools;

public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _byName;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ITool> All => _byName.Values.ToList();

    public ITool? Find(string name) => _byName.GetValueOrDefault(name);
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter ToolRegistryTests`
Ожидается: PASS

- [ ] **Шаг 5: Удалить сгенерированный тест-заглушку**

```bash
rm src/Jarvis.Core.Tests/UnitTest1.cs
```

- [ ] **Step 6: Закоммитить**

```bash
git add -A src/Jarvis.Core/Tools src/Jarvis.Core.Tests/Tools
git commit -m "feat: add ITool contract, ToolResult/PendingConfirmation, ToolRegistry"
```

---

## Task 5: Инструмент `get_system_info`

**Files:**
- Create: `src/Jarvis.Core/Tools/OsActions/ISystemInfoProvider.cs`
- Create: `src/Jarvis.Core/Tools/OsActions/SystemInfoSnapshot.cs`
- Create: `src/Jarvis.Core/Tools/OsActions/WmiSystemInfoProvider.cs`
- Create: `src/Jarvis.Core/Tools/GetSystemInfoTool.cs`
- Test: `src/Jarvis.Core.Tests/Tools/GetSystemInfoToolTests.cs`

**Interfaces:**
- Использует: `ITool`, `ToolResult`, `ToolContext` (Task 4).
- Даёт: `ISystemInfoProvider.GetSnapshot()` → `SystemInfoSnapshot(int BatteryPercent, bool
  IsCharging, double RamUsedGb, double RamTotalGb, double CpuLoadPercent, double FreeDiskGb)`;
  `GetSystemInfoTool` (имя `"get_system_info"`). Ни одна другая задача не зависит от внутренностей
  этого инструмента.

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Tools/GetSystemInfoToolTests.cs
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeSystemInfoProvider : ISystemInfoProvider
{
    public SystemInfoSnapshot GetSnapshot() => new(
        BatteryPercent: 73,
        IsCharging: true,
        RamUsedGb: 8.2,
        RamTotalGb: 16,
        CpuLoadPercent: 12.5,
        FreeDiskGb: 120.4);
}

public class GetSystemInfoToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSummaryWithBatteryAndRam()
    {
        var tool = new GetSystemInfoTool(new FakeSystemInfoProvider());

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>(), new ToolContext());

        Assert.True(result.Success);
        Assert.Contains("73", result.Message);
        Assert.Contains("8.2", result.Message);
        Assert.Null(result.Confirmation);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter GetSystemInfoToolTests`
Ожидается: FAIL (типов ещё не существует).

- [ ] **Шаг 3: Реализовать интерфейс, реальный провайдер и инструмент**

```csharp
// src/Jarvis.Core/Tools/OsActions/SystemInfoSnapshot.cs
namespace Jarvis.Core.Tools.OsActions;

public record SystemInfoSnapshot(
    int BatteryPercent,
    bool IsCharging,
    double RamUsedGb,
    double RamTotalGb,
    double CpuLoadPercent,
    double FreeDiskGb);
```

```csharp
// src/Jarvis.Core/Tools/OsActions/ISystemInfoProvider.cs
namespace Jarvis.Core.Tools.OsActions;

public interface ISystemInfoProvider
{
    SystemInfoSnapshot GetSnapshot();
}
```

```csharp
// src/Jarvis.Core/Tools/OsActions/WmiSystemInfoProvider.cs
using System.Diagnostics;
using System.Management;

namespace Jarvis.Core.Tools.OsActions;

public class WmiSystemInfoProvider : ISystemInfoProvider
{
    public SystemInfoSnapshot GetSnapshot()
    {
        int batteryPercent = 100;
        bool isCharging = true;
        using (var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery"))
        {
            foreach (var obj in searcher.Get())
            {
                batteryPercent = Convert.ToInt32(obj["EstimatedChargeRemaining"] ?? 100);
                var status = Convert.ToInt32(obj["BatteryStatus"] ?? 2);
                isCharging = status == 2 || status == 6;
            }
        }

        double ramTotalGb = 0, ramFreeGb = 0;
        using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
        {
            foreach (var obj in searcher.Get())
            {
                ramTotalGb = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024 / 1024;
                ramFreeGb = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024 / 1024;
            }
        }

        double cpuLoad;
        using (var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
        {
            counter.NextValue();
            Thread.Sleep(200);
            cpuLoad = counter.NextValue();
        }

        var systemDrive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        double freeDiskGb = systemDrive.AvailableFreeSpace / 1024.0 / 1024 / 1024;

        return new SystemInfoSnapshot(
            batteryPercent,
            isCharging,
            Math.Round(ramTotalGb - ramFreeGb, 1),
            Math.Round(ramTotalGb, 1),
            Math.Round(cpuLoad, 1),
            Math.Round(freeDiskGb, 1));
    }
}
```

```csharp
// src/Jarvis.Core/Tools/GetSystemInfoTool.cs
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class GetSystemInfoTool : ITool
{
    private readonly ISystemInfoProvider _provider;

    public GetSystemInfoTool(ISystemInfoProvider provider) => _provider = provider;

    public string Name => "get_system_info";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var s = _provider.GetSnapshot();
        var charging = s.IsCharging ? "заряжается" : "от батареи";
        var message = $"Батарея {s.BatteryPercent}% ({charging}), ОЗУ {s.RamUsedGb} из {s.RamTotalGb} ГБ, " +
                      $"CPU {s.CpuLoadPercent}%, свободно на диске {s.FreeDiskGb} ГБ.";
        return Task.FromResult(new ToolResult(true, message));
    }
}
```

> Для `System.Management` (WMI) на .NET 8 нужен NuGet-пакет `System.Management` — добавляется в Шаге 3.5 ниже.

- [ ] **Шаг 3.5: Добавить пакет для WMI**

```bash
dotnet add src/Jarvis.Core package System.Management
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter GetSystemInfoToolTests`
Ожидается: PASS

- [ ] **Step 5: Закоммитить**

```bash
git add src/Jarvis.Core/Tools src/Jarvis.Core.Tests/Tools src/Jarvis.Core/Jarvis.Core.csproj
git commit -m "feat: add get_system_info tool (battery/RAM/CPU/disk)"
```

---

## Task 6: Инструмент `volume_control`

**Files:**
- Create: `src/Jarvis.Core/Tools/OsActions/IVolumeController.cs`
- Create: `src/Jarvis.Core/Tools/OsActions/NAudioVolumeController.cs`
- Create: `src/Jarvis.Core/Tools/VolumeControlTool.cs`
- Test: `src/Jarvis.Core.Tests/Tools/VolumeControlToolTests.cs`

**Interfaces:**
- Использует: `ITool`, `ToolResult`, `ToolContext` (Task 4).
- Даёт: `IVolumeController { void Increase(int percent); void Decrease(int percent); void
  Mute(); void Unmute(); }`; `VolumeControlTool` (name `"volume_control"`, args `action:
  "up"|"down"|"mute"|"unmute"`, optional `amount: int`).

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Tools/VolumeControlToolTests.cs
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeVolumeController : IVolumeController
{
    public int IncreaseCalledWith = -1;
    public int DecreaseCalledWith = -1;
    public bool MuteCalled;
    public bool UnmuteCalled;

    public void Increase(int percent) => IncreaseCalledWith = percent;
    public void Decrease(int percent) => DecreaseCalledWith = percent;
    public void Mute() => MuteCalled = true;
    public void Unmute() => UnmuteCalled = true;
}

public class VolumeControlToolTests
{
    [Theory]
    [InlineData("up", 10)]
    [InlineData("down", 10)]
    public async Task ExecuteAsync_UsesDefaultStepWhenAmountMissing(string action, int expectedStep)
    {
        var fake = new FakeVolumeController();
        var tool = new VolumeControlTool(fake, defaultStepPercent: 10);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = action }, new ToolContext());

        if (action == "up") Assert.Equal(expectedStep, fake.IncreaseCalledWith);
        else Assert.Equal(expectedStep, fake.DecreaseCalledWith);
    }

    [Fact]
    public async Task ExecuteAsync_Mute_CallsMute()
    {
        var fake = new FakeVolumeController();
        var tool = new VolumeControlTool(fake, defaultStepPercent: 10);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "mute" }, new ToolContext());

        Assert.True(fake.MuteCalled);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_UpWithExplicitAmount_UsesAmount()
    {
        var fake = new FakeVolumeController();
        var tool = new VolumeControlTool(fake, defaultStepPercent: 10);

        await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "up", ["amount"] = 25 }, new ToolContext());

        Assert.Equal(25, fake.IncreaseCalledWith);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter VolumeControlToolTests`
Ожидается: FAIL (типов ещё не существует).

- [ ] **Шаг 3: Реализовать интерфейс, реальный NAudio-контроллер и инструмент**

```csharp
// src/Jarvis.Core/Tools/OsActions/IVolumeController.cs
namespace Jarvis.Core.Tools.OsActions;

public interface IVolumeController
{
    void Increase(int percent);
    void Decrease(int percent);
    void Mute();
    void Unmute();
}
```

```csharp
// src/Jarvis.Core/Tools/OsActions/NAudioVolumeController.cs
using NAudio.CoreAudioApi;

namespace Jarvis.Core.Tools.OsActions;

public class NAudioVolumeController : IVolumeController
{
    private MMDevice GetDefaultDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public void Increase(int percent)
    {
        var device = GetDefaultDevice();
        var current = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(current + percent / 100f, 0f, 1f);
    }

    public void Decrease(int percent)
    {
        var device = GetDefaultDevice();
        var current = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(current - percent / 100f, 0f, 1f);
    }

    public void Mute() => GetDefaultDevice().AudioEndpointVolume.Mute = true;

    public void Unmute() => GetDefaultDevice().AudioEndpointVolume.Mute = false;
}
```

```csharp
// src/Jarvis.Core/Tools/VolumeControlTool.cs
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class VolumeControlTool : ITool
{
    private readonly IVolumeController _controller;
    private readonly int _defaultStepPercent;

    public VolumeControlTool(IVolumeController controller, int defaultStepPercent)
    {
        _controller = controller;
        _defaultStepPercent = defaultStepPercent;
    }

    public string Name => "volume_control";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var action = args.GetValueOrDefault("action") as string ?? "";
        var amount = args.TryGetValue("amount", out var a) && a is int amt ? amt : _defaultStepPercent;

        switch (action)
        {
            case "up":
                _controller.Increase(amount);
                return Task.FromResult(new ToolResult(true, $"Громкость увеличена на {amount}%."));
            case "down":
                _controller.Decrease(amount);
                return Task.FromResult(new ToolResult(true, $"Громкость уменьшена на {amount}%."));
            case "mute":
                _controller.Mute();
                return Task.FromResult(new ToolResult(true, "Звук выключен."));
            case "unmute":
                _controller.Unmute();
                return Task.FromResult(new ToolResult(true, "Звук включён."));
            default:
                return Task.FromResult(new ToolResult(false, $"Неизвестное действие громкости: {action}"));
        }
    }
}
```

- [ ] **Шаг 3.5: Добавить пакет NAudio**

```bash
dotnet add src/Jarvis.Core package NAudio
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter VolumeControlToolTests`
Ожидается: PASS

- [ ] **Step 5: Закоммитить**

```bash
git add src/Jarvis.Core/Tools src/Jarvis.Core.Tests/Tools src/Jarvis.Core/Jarvis.Core.csproj
git commit -m "feat: add volume_control tool (NAudio CoreAudio)"
```

---

## Task 7: Инструмент `open_app`

**Files:**
- Create: `src/Jarvis.Core/Tools/OsActions/IProcessLauncher.cs`
- Create: `src/Jarvis.Core/Tools/OsActions/WindowsProcessLauncher.cs`
- Create: `src/Jarvis.Core/Tools/OpenAppTool.cs`
- Test: `src/Jarvis.Core.Tests/Tools/OpenAppToolTests.cs`

**Interfaces:**
- Использует: `ITool`/`ToolResult`/`ToolContext` (Task 4), `AppsCatalog`/`AppEntry` (Task 2).
- Даёт: `IProcessLauncher.Launch(AppEntry entry)`; `OpenAppTool` (name `"open_app"`, args
  `name: string`).

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Tools/OpenAppToolTests.cs
using Jarvis.Core.Config;
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeProcessLauncher : IProcessLauncher
{
    public AppEntry? LaunchedEntry;
    public void Launch(AppEntry entry) => LaunchedEntry = entry;
}

public class OpenAppToolTests
{
    private static AppsCatalog BuildCatalog() => new(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром", "chrome" }, Path = "chrome.exe" }
    });

    [Fact]
    public async Task ExecuteAsync_KnownAlias_LaunchesApp()
    {
        var fake = new FakeProcessLauncher();
        var tool = new OpenAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.True(result.Success);
        Assert.NotNull(fake.LaunchedEntry);
        Assert.Equal("Google Chrome", fake.LaunchedEntry!.Name);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownApp_ReturnsFailureWithoutLaunching()
    {
        var fake = new FakeProcessLauncher();
        var tool = new OpenAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "несуществующее" }, new ToolContext());

        Assert.False(result.Success);
        Assert.Null(fake.LaunchedEntry);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter OpenAppToolTests`
Ожидается: FAIL (типов ещё не существует).

- [ ] **Шаг 3: Реализовать**

```csharp
// src/Jarvis.Core/Tools/OsActions/IProcessLauncher.cs
using Jarvis.Core.Config;

namespace Jarvis.Core.Tools.OsActions;

public interface IProcessLauncher
{
    void Launch(AppEntry entry);
}
```

```csharp
// src/Jarvis.Core/Tools/OsActions/WindowsProcessLauncher.cs
using System.Diagnostics;
using Jarvis.Core.Config;

namespace Jarvis.Core.Tools.OsActions;

public class WindowsProcessLauncher : IProcessLauncher
{
    public void Launch(AppEntry entry)
    {
        var path = Environment.ExpandEnvironmentVariables(entry.Path ?? entry.Name);
        var psi = new ProcessStartInfo(path)
        {
            Arguments = entry.ShellCommand ?? "",
            UseShellExecute = true,
        };
        Process.Start(psi);
    }
}
```

```csharp
// src/Jarvis.Core/Tools/OpenAppTool.cs
using Jarvis.Core.Config;
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class OpenAppTool : ITool
{
    private readonly AppsCatalog _catalog;
    private readonly IProcessLauncher _launcher;

    public OpenAppTool(AppsCatalog catalog, IProcessLauncher launcher)
    {
        _catalog = catalog;
        _launcher = launcher;
    }

    public string Name => "open_app";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var name = args.GetValueOrDefault("name") as string ?? "";
        var entry = _catalog.FindByNameOrAlias(name);
        if (entry is null)
            return Task.FromResult(new ToolResult(false, $"Не знаю приложение «{name}». Уточните название."));

        _launcher.Launch(entry);
        return Task.FromResult(new ToolResult(true, $"Открываю {entry.Name}."));
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter OpenAppToolTests`
Ожидается: PASS

- [ ] **Step 5: Закоммитить**

```bash
git add src/Jarvis.Core/Tools src/Jarvis.Core.Tests/Tools
git commit -m "feat: add open_app tool"
```

---

## Task 8: Инструмент `close_app` (мягкое закрытие + подтверждение принудительного)

**Files:**
- Create: `src/Jarvis.Core/Tools/OsActions/IProcessCloser.cs`
- Create: `src/Jarvis.Core/Tools/OsActions/WindowsProcessCloser.cs`
- Create: `src/Jarvis.Core/Tools/CloseAppTool.cs`
- Test: `src/Jarvis.Core.Tests/Tools/CloseAppToolTests.cs`

**Interfaces:**
- Использует: `ITool`/`ToolResult`/`ToolContext`/`PendingConfirmation` (Task 4), `AppsCatalog`
  (Task 2).
- Даёт: `IProcessCloser { int SoftClose(string processName); int ForceKill(string
  processName); int CountRunning(string processName); }`; `CloseAppTool` (name `"close_app"`, args
  `name: string`). При `ToolContext.IsConfirmed == true` он убивает процесс принудительно вместо
  мягкого закрытия — `CommandPipeline` (Task 12) отвечает за повторный вызов с `IsConfirmed: true`
  и тем же аргументом `name` после того, как пользователь скажет «да».

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Tools/CloseAppToolTests.cs
using Jarvis.Core.Config;
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeProcessCloser : IProcessCloser
{
    public int RunningCount;
    public int SoftClosedCount;
    public bool ForceKillCalled;
    public string? LastProcessName;

    public int CountRunning(string processName) { LastProcessName = processName; return RunningCount; }
    public int SoftClose(string processName) { SoftClosedCount++; return RunningCount; }
    public int ForceKill(string processName) { ForceKillCalled = true; return 0; }
}

public class CloseAppToolTests
{
    private static AppsCatalog BuildCatalog() => new(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром" }, Path = "chrome.exe" }
    });

    [Fact]
    public async Task ExecuteAsync_NoRunningProcess_ReturnsPlainResultNoConfirmation()
    {
        var fake = new FakeProcessCloser { RunningCount = 0 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.True(result.Success);
        Assert.Null(result.Confirmation);
        Assert.Contains("не запущен", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessStillRunningAfterSoftClose_AsksForForceConfirmation()
    {
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.Equal(1, fake.SoftClosedCount);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("close_app", result.Confirmation!.ToolName);
        Assert.False(fake.ForceKillCalled);
    }

    [Fact]
    public async Task ExecuteAsync_Confirmed_ForceKillsWithoutSoftCloseAttempt()
    {
        var fake = new FakeProcessCloser { RunningCount = 1 };
        var tool = new CloseAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["name"] = "хром" },
            new ToolContext(IsConfirmed: true));

        Assert.True(fake.ForceKillCalled);
        Assert.Equal(0, fake.SoftClosedCount);
        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter CloseAppToolTests`
Ожидается: FAIL (типов ещё не существует).

- [ ] **Шаг 3: Реализовать**

```csharp
// src/Jarvis.Core/Tools/OsActions/IProcessCloser.cs
namespace Jarvis.Core.Tools.OsActions;

public interface IProcessCloser
{
    int CountRunning(string processName);
    int SoftClose(string processName);
    int ForceKill(string processName);
}
```

```csharp
// src/Jarvis.Core/Tools/OsActions/WindowsProcessCloser.cs
using System.Diagnostics;

namespace Jarvis.Core.Tools.OsActions;

public class WindowsProcessCloser : IProcessCloser
{
    private static string StripExtension(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    public int CountRunning(string processName) =>
        Process.GetProcessesByName(StripExtension(processName)).Length;

    public int SoftClose(string processName)
    {
        var procs = Process.GetProcessesByName(StripExtension(processName));
        foreach (var p in procs)
        {
            if (p.MainWindowHandle != IntPtr.Zero)
                p.CloseMainWindow();
        }
        Thread.Sleep(500);
        return CountRunning(processName);
    }

    public int ForceKill(string processName)
    {
        var procs = Process.GetProcessesByName(StripExtension(processName));
        foreach (var p in procs)
            p.Kill();
        return 0;
    }
}
```

```csharp
// src/Jarvis.Core/Tools/CloseAppTool.cs
using Jarvis.Core.Config;
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class CloseAppTool : ITool
{
    private readonly AppsCatalog _catalog;
    private readonly IProcessCloser _closer;

    public CloseAppTool(AppsCatalog catalog, IProcessCloser closer)
    {
        _catalog = catalog;
        _closer = closer;
    }

    public string Name => "close_app";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var name = args.GetValueOrDefault("name") as string ?? "";
        var entry = _catalog.FindByNameOrAlias(name);
        if (entry is null)
            return Task.FromResult(new ToolResult(false, $"Не знаю приложение «{name}»."));

        var processName = Path.GetFileNameWithoutExtension(entry.Path ?? entry.Name);

        if (context.IsConfirmed)
        {
            _closer.ForceKill(processName);
            return Task.FromResult(new ToolResult(true, $"{entry.Name} принудительно закрыт."));
        }

        if (_closer.CountRunning(processName) == 0)
            return Task.FromResult(new ToolResult(true, $"{entry.Name} и так не запущен."));

        var stillRunning = _closer.SoftClose(processName);
        if (stillRunning == 0)
            return Task.FromResult(new ToolResult(true, $"{entry.Name} закрыт."));

        var confirmation = new PendingConfirmation(
            ToolName: "close_app",
            Args: new Dictionary<string, object?> { ["name"] = name },
            Prompt: $"{entry.Name} не закрылся сам. Закрыть принудительно? (да/отмена)");
        return Task.FromResult(new ToolResult(false, confirmation.Prompt, confirmation));
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter CloseAppToolTests`
Ожидается: PASS

- [ ] **Step 5: Закоммитить**

```bash
git add src/Jarvis.Core/Tools src/Jarvis.Core.Tests/Tools
git commit -m "feat: add close_app tool with soft-close and force-kill confirmation"
```

---

## Task 9: Инструмент `system_control` (lock/shutdown/restart/sleep)

**Files:**
- Create: `src/Jarvis.Core/Tools/OsActions/ISystemPowerActions.cs`
- Create: `src/Jarvis.Core/Tools/OsActions/WindowsSystemPowerActions.cs`
- Create: `src/Jarvis.Core/Tools/SystemControlTool.cs`
- Test: `src/Jarvis.Core.Tests/Tools/SystemControlToolTests.cs`

**Interfaces:**
- Использует: `ITool`/`ToolResult`/`ToolContext`/`PendingConfirmation` (Task 4).
- Даёт: `ISystemPowerActions { void Lock(); void ShutdownWithDelay(int seconds); void
  RestartWithDelay(int seconds); void Sleep(); void CancelShutdown(); }`; `SystemControlTool`
  (имя `"system_control"`, args `action: "lock"|"shutdown"|"restart"|"sleep"`).
  `ISystemPowerActions.CancelShutdown` также напрямую используется в `Program.cs` (Task 12) для
  отдельной команды «отмена».

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Tools/SystemControlToolTests.cs
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeSystemPowerActions : ISystemPowerActions
{
    public bool LockCalled;
    public int? ShutdownDelay;
    public int? RestartDelay;
    public bool SleepCalled;
    public bool CancelCalled;

    public void Lock() => LockCalled = true;
    public void ShutdownWithDelay(int seconds) => ShutdownDelay = seconds;
    public void RestartWithDelay(int seconds) => RestartDelay = seconds;
    public void Sleep() => SleepCalled = true;
    public void CancelShutdown() => CancelCalled = true;
}

public class SystemControlToolTests
{
    [Fact]
    public async Task ExecuteAsync_Lock_ExecutesImmediatelyWithoutConfirmation()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "lock" }, new ToolContext());

        Assert.True(fake.LockCalled);
        Assert.True(result.Success);
        Assert.Null(result.Confirmation);
    }

    [Fact]
    public async Task ExecuteAsync_ShutdownNotConfirmed_AsksAndDoesNotShutdown()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "shutdown" }, new ToolContext());

        Assert.Null(fake.ShutdownDelay);
        Assert.NotNull(result.Confirmation);
        Assert.Equal("system_control", result.Confirmation!.ToolName);
    }

    [Fact]
    public async Task ExecuteAsync_ShutdownConfirmed_CallsShutdownWithConfiguredDelay()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object?> { ["action"] = "shutdown" },
            new ToolContext(IsConfirmed: true));

        Assert.Equal(20, fake.ShutdownDelay);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_SleepNotConfirmed_AsksAndDoesNotSleep()
    {
        var fake = new FakeSystemPowerActions();
        var tool = new SystemControlTool(fake, confirmSeconds: 20);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["action"] = "sleep" }, new ToolContext());

        Assert.False(fake.SleepCalled);
        Assert.NotNull(result.Confirmation);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter SystemControlToolTests`
Ожидается: FAIL (типов ещё не существует).

- [ ] **Шаг 3: Реализовать**

```csharp
// src/Jarvis.Core/Tools/OsActions/ISystemPowerActions.cs
namespace Jarvis.Core.Tools.OsActions;

public interface ISystemPowerActions
{
    void Lock();
    void ShutdownWithDelay(int seconds);
    void RestartWithDelay(int seconds);
    void Sleep();
    void CancelShutdown();
}
```

```csharp
// src/Jarvis.Core/Tools/OsActions/WindowsSystemPowerActions.cs
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jarvis.Core.Tools.OsActions;

public class WindowsSystemPowerActions : ISystemPowerActions
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public void Lock() => LockWorkStation();

    public void ShutdownWithDelay(int seconds) =>
        Process.Start(new ProcessStartInfo("shutdown", $"/s /t {seconds}") { CreateNoWindow = true, UseShellExecute = false });

    public void RestartWithDelay(int seconds) =>
        Process.Start(new ProcessStartInfo("shutdown", $"/r /t {seconds}") { CreateNoWindow = true, UseShellExecute = false });

    public void Sleep() => SetSuspendState(false, false, false);

    public void CancelShutdown() =>
        Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false });
}
```

```csharp
// src/Jarvis.Core/Tools/SystemControlTool.cs
using Jarvis.Core.Tools.OsActions;

namespace Jarvis.Core.Tools;

public class SystemControlTool : ITool
{
    private readonly ISystemPowerActions _power;
    private readonly int _confirmSeconds;

    public SystemControlTool(ISystemPowerActions power, int confirmSeconds)
    {
        _power = power;
        _confirmSeconds = confirmSeconds;
    }

    public string Name => "system_control";

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        var action = args.GetValueOrDefault("action") as string ?? "";

        if (action == "lock")
        {
            _power.Lock();
            return Task.FromResult(new ToolResult(true, "Экран заблокирован."));
        }

        if (action is not ("shutdown" or "restart" or "sleep"))
            return Task.FromResult(new ToolResult(false, $"Неизвестное действие: {action}"));

        if (!context.IsConfirmed)
        {
            var prompt = action switch
            {
                "shutdown" => $"Выключить ноутбук через {_confirmSeconds} секунд? (да/отмена)",
                "restart" => $"Перезагрузить ноутбук через {_confirmSeconds} секунд? (да/отмена)",
                _ => "Перевести ноутбук в сон? (да/отмена)",
            };
            var confirmation = new PendingConfirmation(
                ToolName: "system_control",
                Args: new Dictionary<string, object?> { ["action"] = action },
                Prompt: prompt);
            return Task.FromResult(new ToolResult(false, prompt, confirmation));
        }

        switch (action)
        {
            case "shutdown":
                _power.ShutdownWithDelay(_confirmSeconds);
                return Task.FromResult(new ToolResult(true, $"Выключаю через {_confirmSeconds} секунд. Скажите «отмена», чтобы остановить."));
            case "restart":
                _power.RestartWithDelay(_confirmSeconds);
                return Task.FromResult(new ToolResult(true, $"Перезагружаю через {_confirmSeconds} секунд. Скажите «отмена», чтобы остановить."));
            default:
                _power.Sleep();
                return Task.FromResult(new ToolResult(true, "Ухожу в сон."));
        }
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter SystemControlToolTests`
Ожидается: PASS

- [ ] **Step 5: Закоммитить**

```bash
git add src/Jarvis.Core/Tools src/Jarvis.Core.Tests/Tools
git commit -m "feat: add system_control tool (lock/shutdown/restart/sleep) with confirmation"
```

---

## Task 10: Основы Nlu + `RuleBasedResolver` (уровень 1)

**Files:**
- Create: `src/Jarvis.Core/Nlu/ResolveResult.cs`
- Create: `src/Jarvis.Core/Nlu/NluContext.cs`
- Create: `src/Jarvis.Core/Nlu/ICommandResolver.cs`
- Create: `src/Jarvis.Core/Nlu/RussianNumberParser.cs`
- Create: `src/Jarvis.Core/Nlu/RuleBasedResolver.cs`
- Test: `src/Jarvis.Core.Tests/Nlu/RussianNumberParserTests.cs`
- Test: `src/Jarvis.Core.Tests/Nlu/RuleBasedResolverTests.cs`

**Interfaces:**
- Использует: `AppsCatalog` (Task 2).
- Даёт: `ResolveResult(bool Resolved, string? ToolName, IReadOnlyDictionary<string, object?>
  Args, double Confidence, int Level)` со статическими `ResolveResult.Unresolved()` и
  `ResolveResult.For(...)`; `NluContext(AppsCatalog Apps)`; `ICommandResolver { int Level; bool
  IsAvailable; Task<ResolveResult> ResolveAsync(string utterance, NluContext context); }`;
  `RussianNumberParser.TryExtractPercent(string, out int)`,
  `RussianNumberParser.TryExtractMinutes(string, out int)`. `EmbeddingResolver` (Task 11) и
  `CommandPipeline` (Task 12) используют `ICommandResolver`/`ResolveResult`/`NluContext`.

- [ ] **Step 1: Написать падающий тест для парсера чисел**

```csharp
// src/Jarvis.Core.Tests/Nlu/RussianNumberParserTests.cs
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class RussianNumberParserTests
{
    [Theory]
    [InlineData("громкость на 30 процентов", 30)]
    [InlineData("прибавь звука на двадцать процентов", 20)]
    [InlineData("сделай пять процентов", 5)]
    public void TryExtractPercent_FindsValue(string text, int expected)
    {
        var ok = RussianNumberParser.TryExtractPercent(text, out var value);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryExtractPercent_NoNumber_ReturnsFalse()
    {
        Assert.False(RussianNumberParser.TryExtractPercent("сделай погромче", out _));
    }

    [Theory]
    [InlineData("поставь таймер на 5 минут", 5)]
    [InlineData("таймер на десять минут", 10)]
    public void TryExtractMinutes_FindsValue(string text, int expected)
    {
        var ok = RussianNumberParser.TryExtractMinutes(text, out var value);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter RussianNumberParserTests`
Ожидается: FAIL (пространства имён `Jarvis.Core.Nlu` ещё нет).

- [ ] **Шаг 3: Реализовать `RussianNumberParser`**

```csharp
// src/Jarvis.Core/Nlu/RussianNumberParser.cs
using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public static class RussianNumberParser
{
    private static readonly Dictionary<string, int> Words = new()
    {
        ["один"] = 1, ["два"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5,
        ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10,
        ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13, ["четырнадцать"] = 14,
        ["пятнадцать"] = 15, ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40,
        ["пятьдесят"] = 50, ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80,
        ["девяносто"] = 90, ["сто"] = 100,
    };

    private static bool TryFindNumber(string text, out int value)
    {
        var digitMatch = Regex.Match(text, @"\d+");
        if (digitMatch.Success)
        {
            value = int.Parse(digitMatch.Value);
            return true;
        }

        foreach (var word in text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Words.TryGetValue(word, out var wordValue))
            {
                value = wordValue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static bool TryExtractPercent(string text, out int value) => TryFindNumber(text, out value);

    public static bool TryExtractMinutes(string text, out int value) => TryFindNumber(text, out value);
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter RussianNumberParserTests`
Ожидается: PASS

- [ ] **Step 5: Написать падающий тест для `RuleBasedResolver`**

```csharp
// src/Jarvis.Core.Tests/Nlu/RuleBasedResolverTests.cs
using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class RuleBasedResolverTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром", "chrome" } }
    }));

    private readonly RuleBasedResolver _resolver = new();

    [Theory]
    [InlineData("открой хром")]
    [InlineData("Открой ХРОМ!!")]
    [InlineData("  открой   хром  ")]
    public async Task ResolveAsync_OpenApp_IsRobustToCaseAndPunctuation(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("open_app", result.ToolName);
        Assert.Equal("хром", result.Args["name"]);
    }

    [Fact]
    public async Task ResolveAsync_CloseApp_ExtractsName()
    {
        var result = await _resolver.ResolveAsync("закрой хром", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("close_app", result.ToolName);
        Assert.Equal("хром", result.Args["name"]);
    }

    [Theory]
    [InlineData("выключи ноут", "shutdown")]
    [InlineData("перезагрузи компьютер", "restart")]
    [InlineData("заблокируй экран", "lock")]
    [InlineData("усыпи ноутбук", "sleep")]
    public async Task ResolveAsync_SystemControl_MapsPhraseToAction(string phrase, string expectedAction)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("system_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
    }

    [Theory]
    [InlineData("громче", "up")]
    [InlineData("сделай тише", "down")]
    [InlineData("выключи звук", "mute")]
    [InlineData("включи звук", "unmute")]
    public async Task ResolveAsync_Volume_MapsPhraseToAction(string phrase, string expectedAction)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(expectedAction, result.Args["action"]);
    }

    [Fact]
    public async Task ResolveAsync_VolumeWithAmount_ExtractsAmount()
    {
        var result = await _resolver.ResolveAsync("громкость на 30 процентов", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(30, result.Args["amount"]);
    }

    [Theory]
    [InlineData("как там батарея")]
    [InlineData("сколько памяти свободно")]
    [InlineData("покажи информацию о системе")]
    public async Task ResolveAsync_SystemInfo_Resolves(string phrase)
    {
        var result = await _resolver.ResolveAsync(phrase, BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("get_system_info", result.ToolName);
    }

    [Fact]
    public async Task ResolveAsync_UnrecognizedPhrase_ReturnsUnresolved()
    {
        var result = await _resolver.ResolveAsync("расскажи анекдот про кота", BuildContext());

        Assert.False(result.Resolved);
    }
}
```

- [ ] **Step 6: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter RuleBasedResolverTests`
Ожидается: FAIL (`RuleBasedResolver`/`NluContext`/`ResolveResult`/`ICommandResolver` ещё не существуют).

- [ ] **Шаг 7: Реализовать `ResolveResult`, `NluContext`, `ICommandResolver`**

```csharp
// src/Jarvis.Core/Nlu/ResolveResult.cs
namespace Jarvis.Core.Nlu;

public record ResolveResult(
    bool Resolved,
    string? ToolName,
    IReadOnlyDictionary<string, object?> Args,
    double Confidence,
    int Level)
{
    public static ResolveResult Unresolved() =>
        new(false, null, new Dictionary<string, object?>(), 0, 0);

    public static ResolveResult For(string toolName, IReadOnlyDictionary<string, object?> args, double confidence, int level) =>
        new(true, toolName, args, confidence, level);
}
```

```csharp
// src/Jarvis.Core/Nlu/NluContext.cs
using Jarvis.Core.Config;

namespace Jarvis.Core.Nlu;

public record NluContext(AppsCatalog Apps);
```

```csharp
// src/Jarvis.Core/Nlu/ICommandResolver.cs
namespace Jarvis.Core.Nlu;

public interface ICommandResolver
{
    int Level { get; }
    bool IsAvailable { get; }
    Task<ResolveResult> ResolveAsync(string utterance, NluContext context);
}
```

- [ ] **Шаг 8: Реализовать `RuleBasedResolver`**

```csharp
// src/Jarvis.Core/Nlu/RuleBasedResolver.cs
using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public class RuleBasedResolver : ICommandResolver
{
    public int Level => 1;
    public bool IsAvailable => true;

    private static string Normalize(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant(), @"[!?.,;]+", "").Trim();

    private static readonly string[] ShutdownWords = { "выключи ноут", "выключи компьютер", "выключи комп", "выключи ноутбук" };
    private static readonly string[] RestartWords = { "перезагрузи компьютер", "перезагрузи комп", "перезагрузи ноутбук", "ребут" };
    private static readonly string[] LockWords = { "заблокируй экран", "заблокируй компьютер", "заблокируй ноутбук" };
    private static readonly string[] SleepWords = { "усыпи ноутбук", "усыпи компьютер", "спящий режим" };

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        var text = Normalize(utterance);

        var openMatch = Regex.Match(text, @"^(открой|запусти)\s+(?<name>.+)$");
        if (openMatch.Success)
            return Resolved("open_app", new() { ["name"] = openMatch.Groups["name"].Value.Trim() });

        var closeMatch = Regex.Match(text, @"^(закрой|вырубай|выруби)\s+(?<name>.+)$");
        if (closeMatch.Success)
            return Resolved("close_app", new() { ["name"] = closeMatch.Groups["name"].Value.Trim() });

        if (ShutdownWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "shutdown" });
        if (RestartWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "restart" });
        if (LockWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "lock" });
        if (SleepWords.Any(w => text.Contains(w)))
            return Resolved("system_control", new() { ["action"] = "sleep" });

        if (RussianNumberParser.TryExtractPercent(text, out var percent) &&
            (text.Contains("громк") || text.Contains("звук")))
        {
            return Resolved("volume_control", new() { ["action"] = "up", ["amount"] = percent });
        }

        if (text.Contains("громче") || text.Contains("прибавь звук") || text.Contains("прибавь громк"))
            return Resolved("volume_control", new() { ["action"] = "up" });
        if (text.Contains("тише") || text.Contains("убавь звук") || text.Contains("убавь громк"))
            return Resolved("volume_control", new() { ["action"] = "down" });
        if (text.Contains("выключи звук") || text.Contains("без звука") || text.Contains("замьють"))
            return Resolved("volume_control", new() { ["action"] = "mute" });
        if (text.Contains("включи звук"))
            return Resolved("volume_control", new() { ["action"] = "unmute" });

        if (text.Contains("батаре") || text.Contains("сколько памяти") || text.Contains("информаци") && text.Contains("систем"))
            return Resolved("get_system_info", new());

        return Task.FromResult(ResolveResult.Unresolved());
    }

    private Task<ResolveResult> Resolved(string tool, Dictionary<string, object?> args) =>
        Task.FromResult(ResolveResult.For(tool, args, confidence: 1.0, level: Level));
}
```

- [ ] **Step 9: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter "RuleBasedResolverTests|RussianNumberParserTests"`
Ожидается: PASS (все кейсы)

- [ ] **Step 10: Закоммитить**

```bash
git add src/Jarvis.Core/Nlu src/Jarvis.Core.Tests/Nlu
git commit -m "feat: add ResolveResult/ICommandResolver and level-1 RuleBasedResolver"
```

---

## Task 11: Каталог интентов + `EmbeddingResolver` (уровень 2)

**Files:**
- Create: `src/Jarvis.Core/Nlu/IntentSample.cs`
- Create: `src/Jarvis.Core/Nlu/IntentCatalog.cs`
- Create: `src/Jarvis.Core/Nlu/IEmbeddingModel.cs`
- Create: `src/Jarvis.Core/Nlu/OnnxEmbeddingModel.cs`
- Create: `src/Jarvis.Core/Nlu/EmbeddingResolver.cs`
- Create: `config/intents/open_app.yaml`
- Create: `config/intents/close_app.yaml`
- Create: `config/intents/system_control.yaml`
- Create: `config/intents/volume_control.yaml`
- Create: `config/intents/get_system_info.yaml`
- Test: `src/Jarvis.Core.Tests/Nlu/IntentCatalogTests.cs`
- Test: `src/Jarvis.Core.Tests/Nlu/EmbeddingResolverTests.cs`

**Interfaces:**
- Использует: `ICommandResolver`/`ResolveResult`/`NluContext` (Task 10).
- Даёт: `IntentSample(string Tool, string Phrase)`; `IntentCatalog.LoadFromDirectory(string
  dir)` → `IReadOnlyList<IntentSample>`; `IEmbeddingModel { float[] Embed(string text); }`;
  `EmbeddingResolver(IEmbeddingModel model, IReadOnlyList<IntentSample> samples, double
  threshold)`. `OnnxEmbeddingModel` — настоящая реализация на ONNX; юнит-тесты ниже её не трогают
  (они используют фейковый `IEmbeddingModel`), она подключается только в `Program.cs` (Task 12).

- [ ] **Step 1: Написать падающий тест для `IntentCatalog`**

```csharp
// src/Jarvis.Core.Tests/Nlu/IntentCatalogTests.cs
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class IntentCatalogTests
{
    [Fact]
    public void LoadFromDirectory_ReadsAllYamlFilesAsSamples()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "open_app.yaml"), """
        tool: open_app
        phrases:
          - открой хром
          - запусти браузер
        """);
        File.WriteAllText(Path.Combine(dir, "volume_control.yaml"), """
        tool: volume_control
        phrases:
          - сделай погромче
        """);

        var samples = IntentCatalog.LoadFromDirectory(dir);

        Assert.Equal(3, samples.Count);
        Assert.Contains(samples, s => s.Tool == "open_app" && s.Phrase == "открой хром");
        Assert.Contains(samples, s => s.Tool == "volume_control" && s.Phrase == "сделай погромче");
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter IntentCatalogTests`
Ожидается: FAIL (`IntentCatalog`/`IntentSample` ещё не существуют).

- [ ] **Шаг 3: Реализовать `IntentSample` и `IntentCatalog`**

```csharp
// src/Jarvis.Core/Nlu/IntentSample.cs
namespace Jarvis.Core.Nlu;

public record IntentSample(string Tool, string Phrase);
```

```csharp
// src/Jarvis.Core/Nlu/IntentCatalog.cs
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
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter IntentCatalogTests`
Ожидается: PASS

- [ ] **Шаг 5: Написать настоящие файлы `config/intents/*.yaml` (по 10+ фраз)**

```yaml
# config/intents/open_app.yaml
tool: open_app
phrases:
  - открой хром
  - запусти браузер
  - включи телеграм
  - открой дискорд пожалуйста
  - разверни вс код
  - запусти проводник
  - открой блокнот
  - подними хром
  - хочу открыть телеграм
  - включи дискорд
```

```yaml
# config/intents/close_app.yaml
tool: close_app
phrases:
  - закрой хром
  - выключи телеграм
  - вырубай дискорд
  - закрой это приложение
  - убери со экрана вс код
  - закрой браузер
  - выключи блокнот
  - вырубай проводник
  - закрой уже телеграм
  - убей процесс хрома
```

```yaml
# config/intents/system_control.yaml
tool: system_control
phrases:
  - выключи ноут
  - вырубай комп совсем
  - пора спать выключай ноутбук
  - перезапусти систему
  - ребутни комп
  - заблокируй ноутбук
  - поставь на паузу и заблокируй
  - отправь комп спать
  - усыпи систему
  - выключи компьютер полностью
```

```yaml
# config/intents/volume_control.yaml
tool: volume_control
phrases:
  - сделай погромче
  - прибавь звука процентов на 20
  - убавь громкость
  - потише пожалуйста
  - заглуши звук
  - выруби звук совсем
  - верни звук обратно
  - сделай звук тише
  - громкость на максимум
  - подними звук
```

```yaml
# config/intents/get_system_info.yaml
tool: get_system_info
phrases:
  - глянь что там с батарейкой
  - сколько заряда осталось
  - как дела с памятью
  - покажи загрузку процессора
  - сколько места на диске
  - расскажи о системе
  - какая сейчас нагрузка
  - проверь состояние батареи
  - сколько оперативки свободно
  - дай сводку по железу
```

- [ ] **Step 6: Написать падающий тест для `EmbeddingResolver`**

```csharp
// src/Jarvis.Core.Tests/Nlu/EmbeddingResolverTests.cs
using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class FakeEmbeddingModel : IEmbeddingModel
{
    // Возвращает почти one-hot вектор по "ключевому слову темы" во фразе,
    // чтобы косинусное сходство было детерминированным и предсказуемым в тестах.
    private static readonly string[] Topics = { "app", "volume", "battery", "unrelated" };

    public float[] Embed(string text)
    {
        var vector = new float[Topics.Length];
        var lower = text.ToLowerInvariant();
        if (lower.Contains("хром") || lower.Contains("открой") || lower.Contains("браузер")) vector[0] = 1f;
        else if (lower.Contains("звук") || lower.Contains("громк")) vector[1] = 1f;
        else if (lower.Contains("батаре")) vector[2] = 1f;
        else vector[3] = 1f;
        return vector;
    }
}

public class EmbeddingResolverTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    private static IReadOnlyList<IntentSample> Samples() => new List<IntentSample>
    {
        new("open_app", "открой хром"),
        new("volume_control", "сделай погромче"),
        new("get_system_info", "глянь что там с батарейкой"),
    };

    [Fact]
    public void IsAvailable_IsTrue_WhenConstructedWithSamples()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 0.5);
        Assert.True(resolver.IsAvailable);
    }

    [Fact]
    public async Task ResolveAsync_SimilarPhrase_MatchesClosestIntent()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 0.5);

        var result = await resolver.ResolveAsync("подними звук чуть-чуть", BuildContext());

        Assert.True(result.Resolved);
        Assert.Equal("volume_control", result.ToolName);
        Assert.Equal(2, result.Level);
    }

    [Fact]
    public async Task ResolveAsync_BelowThreshold_ReturnsUnresolved()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 1.5);

        var result = await resolver.ResolveAsync("подними звук чуть-чуть", BuildContext());

        Assert.False(result.Resolved);
    }

    [Fact]
    public async Task ResolveAsync_UnrelatedPhrase_ReturnsUnresolved()
    {
        var resolver = new EmbeddingResolver(new FakeEmbeddingModel(), Samples(), threshold: 0.5);

        var result = await resolver.ResolveAsync("расскажи анекдот", BuildContext());

        Assert.False(result.Resolved);
    }
}
```

- [ ] **Step 7: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter EmbeddingResolverTests`
Ожидается: FAIL (`IEmbeddingModel`/`EmbeddingResolver` ещё не существуют).

- [ ] **Шаг 8: Реализовать `IEmbeddingModel` и `EmbeddingResolver`**

```csharp
// src/Jarvis.Core/Nlu/IEmbeddingModel.cs
namespace Jarvis.Core.Nlu;

public interface IEmbeddingModel
{
    float[] Embed(string text);
}
```

```csharp
// src/Jarvis.Core/Nlu/EmbeddingResolver.cs
namespace Jarvis.Core.Nlu;

public class EmbeddingResolver : ICommandResolver
{
    private readonly IEmbeddingModel _model;
    private readonly double _threshold;
    private readonly List<(IntentSample Sample, float[] Vector)> _index;

    public EmbeddingResolver(IEmbeddingModel model, IReadOnlyList<IntentSample> samples, double threshold)
    {
        _model = model;
        _threshold = threshold;
        _index = samples.Select(s => (s, model.Embed("passage: " + s.Phrase))).ToList();
    }

    public int Level => 2;
    public bool IsAvailable => _index.Count > 0;

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        if (!IsAvailable)
            return Task.FromResult(ResolveResult.Unresolved());

        var queryVector = _model.Embed("query: " + utterance);

        var best = _index
            .Select(entry => (entry.Sample, Score: CosineSimilarity(queryVector, entry.Vector)))
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score < _threshold)
            return Task.FromResult(ResolveResult.Unresolved());

        var args = ArgExtraction.ExtractArgs(best.Sample.Tool, utterance);
        return Task.FromResult(ResolveResult.For(best.Sample.Tool, args, best.Score, Level));
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0;
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
```

`EmbeddingResolver` вызывает небольшой общий хелпер `ArgExtraction`, чтобы логика извлечения
параметров жила в одном месте для уровней 1 и 2 (по спеке уровень 2 должен «извлекать параметры
теми же правилами, что и уровень 1»). Создадим его прямо сейчас:

```csharp
// src/Jarvis.Core/Nlu/ArgExtraction.cs
using System.Text.RegularExpressions;

namespace Jarvis.Core.Nlu;

public static class ArgExtraction
{
    public static Dictionary<string, object?> ExtractArgs(string tool, string utterance)
    {
        var args = new Dictionary<string, object?>();
        var text = utterance.ToLowerInvariant();

        switch (tool)
        {
            case "open_app":
            case "close_app":
                var match = Regex.Match(text, @"(хром|телеграм|дискорд|вс код|код|проводник|блокнот|браузер)");
                if (match.Success) args["name"] = match.Value;
                break;
            case "volume_control":
                if (RussianNumberParser.TryExtractPercent(text, out var percent))
                    args["amount"] = percent;
                args["action"] = text.Contains("тиш") || text.Contains("убав") ? "down"
                    : text.Contains("глуш") || text.Contains("выруби") ? "mute"
                    : "up";
                break;
            case "system_control":
                args["action"] = text.Contains("блок") ? "lock"
                    : text.Contains("перезагру") || text.Contains("ребут") ? "restart"
                    : text.Contains("сп") ? "sleep"
                    : "shutdown";
                break;
        }
        return args;
    }
}
```

- [ ] **Step 9: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter "EmbeddingResolverTests|IntentCatalogTests"`
Ожидается: PASS

- [ ] **Шаг 10: Реализовать настоящий `OnnxEmbeddingModel` (не покрыт юнит-тестами — подключается вручную в Task 12/13)**

```csharp
// src/Jarvis.Core/Nlu/OnnxEmbeddingModel.cs
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Jarvis.Core.Nlu;

public class OnnxEmbeddingModel : IEmbeddingModel, IDisposable
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;

    public OnnxEmbeddingModel(string modelPath, string tokenizerPath)
    {
        _session = new InferenceSession(modelPath);
        using var tokenizerStream = File.OpenRead(tokenizerPath);
        _tokenizer = SentencePieceTokenizer.Create(tokenizerStream);
    }

    public static bool FilesExist(string modelPath, string tokenizerPath) =>
        File.Exists(modelPath) && File.Exists(tokenizerPath);

    public float[] Embed(string text)
    {
        var ids = _tokenizer.EncodeToIds(text).Select(i => (long)i).ToArray();
        var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });
        var attentionMask = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), new[] { 1, ids.Length });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
        };

        using var outputs = _session.Run(inputs);
        var lastHiddenState = outputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();

        // Усреднение по токенам (mean pooling), затем L2-нормализация — так задокументирована
        // стратегия пулинга для эмбеддингов multilingual-e5.
        var hidden = lastHiddenState.Dimensions[2];
        var tokens = lastHiddenState.Dimensions[1];
        var pooled = new float[hidden];
        for (var t = 0; t < tokens; t++)
            for (var h = 0; h < hidden; h++)
                pooled[h] += lastHiddenState[0, t, h];
        for (var h = 0; h < hidden; h++)
            pooled[h] /= tokens;

        var norm = (float)Math.Sqrt(pooled.Sum(v => v * v));
        if (norm > 0)
            for (var h = 0; h < hidden; h++)
                pooled[h] /= norm;

        return pooled;
    }

    public void Dispose() => _session.Dispose();
}
```

> Имена входных/выходных тензоров ONNX (`input_ids`, `attention_mask`, `last_hidden_state`)
> соответствуют стандартному экспорту `intfloat/multilingual-e5-small`. Если у скачанного файла
> модели имена другие, посмотрите их через `_session.InputMetadata.Keys` /
> `_session.OutputMetadata.Keys` (добавьте временный `Console.WriteLine` в конструктор) и
> поправьте строковые константы здесь — это правка на одну строку, не переделка дизайна.

- [ ] **Step 11: Закоммитить**

```bash
git add src/Jarvis.Core/Nlu src/Jarvis.Core.Tests/Nlu config/intents
git commit -m "feat: add intent catalog and level-2 EmbeddingResolver (ONNX + fake-tested)"
```

---

## Task 12: `CommandPipeline` + REPL-хост в Program.cs

**Files:**
- Create: `src/Jarvis.Core/Nlu/CommandPipeline.cs`
- Modify: `src/Jarvis.Core/Program.cs`
- Test: `src/Jarvis.Core.Tests/Nlu/CommandPipelineTests.cs`

**Interfaces:**
- Использует: `ICommandResolver`/`ResolveResult`/`NluContext` (Tasks 10-11), `IToolRegistry`/`ITool`/
  `ToolResult`/`ToolContext`/`PendingConfirmation` (Task 4).
- Даёт: `CommandPipeline(IReadOnlyList<ICommandResolver> resolvers, IToolRegistry tools, NluContext
  context)` с методом `Task<PipelineOutcome> ProcessAsync(string input)`, где `PipelineOutcome(string
  Message, int Level, string? ToolName, bool Resolved)` — то, что `Program.cs` печатает и логирует.
  `CommandPipeline` сама владеет машиной состояний подтверждения (приватное поле
  `PendingConfirmation? _pending`) — `Program.cs` просто вызывает `ProcessAsync` на каждой строке и
  не обязан знать о состоянии подтверждения, кроме отдельной команды «отмена», которая
  дополнительно вызывает `ISystemPowerActions.CancelShutdown()` напрямую.

- [ ] **Step 1: Написать падающий тест**

```csharp
// src/Jarvis.Core.Tests/Nlu/CommandPipelineTests.cs
using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Jarvis.Core.Tools;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class StubResolver : ICommandResolver
{
    private readonly ResolveResult _result;
    public StubResolver(int level, ResolveResult result) { Level = level; _result = result; }
    public int Level { get; }
    public bool IsAvailable => true;
    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context) => Task.FromResult(_result);
}

public class RecordingTool : ITool
{
    public List<(IReadOnlyDictionary<string, object?> Args, bool Confirmed)> Calls = new();
    public string Name => "system_control";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        Calls.Add((args, context.IsConfirmed));
        if (!context.IsConfirmed)
        {
            var confirmation = new PendingConfirmation(Name, args, "Точно? (да/отмена)");
            return Task.FromResult(new ToolResult(false, confirmation.Prompt, confirmation));
        }
        return Task.FromResult(new ToolResult(true, "Сделано."));
    }
}

public class CommandPipelineTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    [Fact]
    public async Task ProcessAsync_UnresolvedByAllLevels_ReturnsNotUnderstood()
    {
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.Unresolved()),
            new StubResolver(2, ResolveResult.Unresolved()),
        };
        var registry = new ToolRegistry(Array.Empty<ITool>());
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        var outcome = await pipeline.ProcessAsync("расскажи анекдот");

        Assert.False(outcome.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_DangerousTool_AsksThenExecutesOnDa()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        var first = await pipeline.ProcessAsync("выключи ноут");
        Assert.Contains("Точно", first.Message);
        Assert.Single(recordingTool.Calls);
        Assert.False(recordingTool.Calls[0].Confirmed);

        var second = await pipeline.ProcessAsync("да");
        Assert.Equal(2, recordingTool.Calls.Count);
        Assert.True(recordingTool.Calls[1].Confirmed);
        Assert.True(second.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_UnrelatedInputWhilePending_DropsPendingAndProcessesNormally()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        await pipeline.ProcessAsync("выключи ноут");
        var second = await pipeline.ProcessAsync("который час");

        // Был только исходный ожидающий вызов; "который час" не запускает подтверждённое выполнение.
        Assert.Single(recordingTool.Calls);
        Assert.False(second.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_Otmena_ClearsPendingWithoutExecuting()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        await pipeline.ProcessAsync("выключи ноут");
        await pipeline.ProcessAsync("отмена");

        Assert.Single(recordingTool.Calls); // всё ещё только исходный запрос — подтверждённого выполнения не было
    }
}

public class ThrowingTool : ITool
{
    public string Name => "open_app";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context) =>
        throw new InvalidOperationException("boom");
}

public class CommandPipelineErrorHandlingTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    [Fact]
    public async Task ProcessAsync_ToolThrows_ReturnsFriendlyMessageWithoutCrashing()
    {
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("open_app", new Dictionary<string, object?> { ["name"] = "хром" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { new ThrowingTool() });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        var outcome = await pipeline.ProcessAsync("открой хром");

        Assert.False(outcome.Resolved);
        Assert.Equal("Не получилось выполнить команду.", outcome.Message);
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Команда: `dotnet test --filter CommandPipelineTests`
Ожидается: FAIL (`CommandPipeline` ещё не существует).

- [ ] **Шаг 3: Реализовать `CommandPipeline`**

```csharp
// src/Jarvis.Core/Nlu/CommandPipeline.cs
using Jarvis.Core.Tools;

namespace Jarvis.Core.Nlu;

public record PipelineOutcome(string Message, int Level, string? ToolName, bool Resolved);

public class CommandPipeline
{
    private readonly IReadOnlyList<ICommandResolver> _resolvers;
    private readonly IToolRegistry _tools;
    private readonly NluContext _context;
    private PendingConfirmation? _pending;

    public CommandPipeline(IReadOnlyList<ICommandResolver> resolvers, IToolRegistry tools, NluContext context)
    {
        _resolvers = resolvers;
        _tools = tools;
        _context = context;
    }

    public async Task<PipelineOutcome> ProcessAsync(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();

        if (_pending is not null)
        {
            var pending = _pending;
            _pending = null;

            if (normalized is "да" or "да.")
                return await ExecuteTool(pending!.ToolName, pending.Args, confirmed: true, level: 0);

            // _pending уже обнулено выше — если это "отмена", просто сообщаем об отмене.
            if (normalized is "отмена" or "отмена.")
                return new PipelineOutcome("Отменено.", 0, null, true);

            // Не "да" и не "отмена" — ожидание сброшено, обрабатываем ввод как новую команду ниже.
        }

        foreach (var resolver in _resolvers.Where(r => r.IsAvailable).OrderBy(r => r.Level))
        {
            var result = await resolver.ResolveAsync(input, _context);
            if (result.Resolved && result.ToolName is not null)
                return await ExecuteTool(result.ToolName, result.Args, confirmed: false, level: result.Level);
        }

        return new PipelineOutcome("Не понял команду.", 0, null, false);
    }

    private async Task<PipelineOutcome> ExecuteTool(string toolName, IReadOnlyDictionary<string, object?> args, bool confirmed, int level)
    {
        var tool = _tools.Find(toolName);
        if (tool is null)
            return new PipelineOutcome($"Инструмент {toolName} не найден.", level, toolName, false);

        var result = await tool.ExecuteAsync(args, new ToolContext(IsConfirmed: confirmed));

        if (result.Confirmation is not null)
            _pending = result.Confirmation;

        return new PipelineOutcome(result.Message, level, toolName, result.Success);
    }
}
```

- [ ] **Step 4: Запустить тест и убедиться, что он проходит**

Команда: `dotnet test --filter CommandPipelineTests`
Ожидается: PASS

- [ ] **Шаг 5: Собрать всё вместе в `Program.cs`**

```csharp
// src/Jarvis.Core/Program.cs
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
    var samples = IntentCatalog.LoadFromDirectory(config.IntentsDirectory);
    var embeddingModel = new OnnxEmbeddingModel(config.EmbeddingModelPath, config.EmbeddingTokenizerPath);
    resolvers.Add(new EmbeddingResolver(embeddingModel, samples, config.EmbeddingThreshold));
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
```

- [ ] **Шаг 6: Ручная проверка запуском**

Команда: `dotnet run --project src/Jarvis.Core`
Ввод: `как там батарея`
Ожидается: русское предложение с цифрами по батарее/ОЗУ/CPU/диску и добавленная строка в
`logs/jarvis-<date>.log`.

- [ ] **Step 7: Закоммитить**

```bash
git add src/Jarvis.Core/Nlu/CommandPipeline.cs src/Jarvis.Core/Program.cs src/Jarvis.Core.Tests/Nlu/CommandPipelineTests.cs
git commit -m "feat: add CommandPipeline confirmation state machine and wire Program.cs REPL"
```

---

## Task 13: Фикстура из 50+ фраз, скрипт скачивания моделей, README

**Files:**
- Create: `src/Jarvis.Core.Tests/Resources/test-phrases.yaml`
- Create: `src/Jarvis.Core.Tests/Nlu/PhraseFixtureTests.cs`
- Create: `scripts/download-models.ps1`
- Create/Modify: `README.md`

**Interfaces:**
- Использует: `RuleBasedResolver`, `EmbeddingResolver` (с детерминированным тестовым дублёром вроде
  `FakeEmbeddingModel`, а не настоящей ONNX-моделью — CI не должен зависеть от скачанной модели),
  `CommandPipeline`.
- Даёт: ничего нового для последующих задач — это приёмочная задача, завершающая этап.

- [ ] **Шаг 1: Написать фикстуру фраз (50+ записей)**

```yaml
# src/Jarvis.Core.Tests/Resources/test-phrases.yaml
cases:
  # open_app — точные формулировки
  - phrase: "открой хром"
    expected_tool: open_app
  - phrase: "запусти телеграм"
    expected_tool: open_app
  - phrase: "открой дискорд"
    expected_tool: open_app
  - phrase: "запусти вс код"
    expected_tool: open_app
  - phrase: "открой проводник"
    expected_tool: open_app
  - phrase: "открой блокнот"
    expected_tool: open_app
  # open_app — разговорные формулировки (территория уровня 2)
  - phrase: "подними хром на экран"
    expected_tool: open_app
  - phrase: "хочу открыть телеграм"
    expected_tool: open_app
  - phrase: "включи дискорд"
    expected_tool: open_app
  # close_app
  - phrase: "закрой хром"
    expected_tool: close_app
  - phrase: "закрой телеграм через подтверждение да"
    expected_tool: close_app
  - phrase: "вырубай дискорд"
    expected_tool: close_app
  - phrase: "выключи вс код"
    expected_tool: close_app
  - phrase: "закрой это приложение"
    expected_tool: close_app
  - phrase: "убери со экрана блокнот"
    expected_tool: close_app
  # system_control
  - phrase: "выключи ноут"
    expected_tool: system_control
  - phrase: "выключи компьютер"
    expected_tool: system_control
  - phrase: "перезагрузи компьютер"
    expected_tool: system_control
  - phrase: "ребутни комп"
    expected_tool: system_control
  - phrase: "заблокируй экран"
    expected_tool: system_control
  - phrase: "заблокируй компьютер"
    expected_tool: system_control
  - phrase: "усыпи ноутбук"
    expected_tool: system_control
  - phrase: "переведи в спящий режим"
    expected_tool: system_control
  - phrase: "пора спать выключай ноутбук"
    expected_tool: system_control
  # volume_control
  - phrase: "громче"
    expected_tool: volume_control
  - phrase: "сделай громче"
    expected_tool: volume_control
  - phrase: "сделай тише"
    expected_tool: volume_control
  - phrase: "выключи звук"
    expected_tool: volume_control
  - phrase: "включи звук"
    expected_tool: volume_control
  - phrase: "громкость на 30"
    expected_tool: volume_control
  - phrase: "прибавь звука процентов на 20"
    expected_tool: volume_control
  - phrase: "потише пожалуйста"
    expected_tool: volume_control
  - phrase: "заглуши звук"
    expected_tool: volume_control
  - phrase: "верни звук обратно"
    expected_tool: volume_control
  # get_system_info
  - phrase: "как там батарея"
    expected_tool: get_system_info
  - phrase: "сколько памяти свободно"
    expected_tool: get_system_info
  - phrase: "глянь что там с батарейкой"
    expected_tool: get_system_info
  - phrase: "покажи информацию о системе"
    expected_tool: get_system_info
  - phrase: "сколько заряда осталось"
    expected_tool: get_system_info
  - phrase: "какая загрузка процессора"
    expected_tool: get_system_info
  - phrase: "сколько места на диске"
    expected_tool: get_system_info
  # устойчивость к регистру / пробелам / пунктуации
  - phrase: "Открой ХРОМ!!"
    expected_tool: open_app
  - phrase: "  открой   хром  "
    expected_tool: open_app
  - phrase: "открой хром, пожалуйста"
    expected_tool: open_app
  # нерешённые — НЕ должны сопоставляться ни с одним инструментом
  - phrase: "расскажи анекдот про кота"
    expected_tool: null
  - phrase: "какая погода завтра"
    expected_tool: null
  - phrase: "сколько будет дважды два"
    expected_tool: null
  - phrase: "спой песню"
    expected_tool: null
  - phrase: "который час в токио"
    expected_tool: null
  - phrase: "заведи будильник на завтра"
    expected_tool: null
```

- [ ] **Шаг 2: Написать падающий приёмочный тест**

```csharp
// src/Jarvis.Core.Tests/Nlu/PhraseFixtureTests.cs
using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Jarvis.Core.Tests.Nlu;

public class PhraseFixtureTests
{
    private record FixtureCase
    {
        public string Phrase { get; init; } = "";
        public string? ExpectedTool { get; init; }
    }

    private record FixtureFile
    {
        public List<FixtureCase> Cases { get; init; } = new();
    }

    private static List<FixtureCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "test-phrases.yaml");
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        var file = deserializer.Deserialize<FixtureFile>(File.ReadAllText(path));
        return file.Cases;
    }

    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром" } },
        new() { Name = "Telegram", Aliases = new() { "телеграм" } },
        new() { Name = "Discord", Aliases = new() { "дискорд" } },
    }));

    [Fact]
    public async Task Fixture_HasAtLeast50Cases()
    {
        Assert.True(LoadCases().Count >= 50, $"Expected 50+ cases, found {LoadCases().Count}");
    }

    [Fact]
    public async Task Fixture_AtLeast70PercentResolveViaLevel1Or2()
    {
        var samples = IntentCatalog.LoadFromDirectory(Path.Combine("config", "intents"));
        var resolvers = new List<ICommandResolver>
        {
            new RuleBasedResolver(),
            new EmbeddingResolver(new FakeEmbeddingModel(), samples, threshold: 0.3),
        };
        var context = BuildContext();
        var cases = LoadCases();
        var resolvedCount = 0;

        foreach (var testCase in cases)
        {
            ResolveResult? match = null;
            foreach (var resolver in resolvers.OrderBy(r => r.Level))
            {
                var result = await resolver.ResolveAsync(testCase.Phrase, context);
                if (result.Resolved) { match = result; break; }
            }

            if (testCase.ExpectedTool is null)
            {
                Assert.True(match is null, $"'{testCase.Phrase}' expected Unresolved but got '{match?.ToolName}'");
            }
            else
            {
                Assert.NotNull(match);
                Assert.Equal(testCase.ExpectedTool, match!.ToolName);
                resolvedCount++;
            }
        }

        var ratio = (double)resolvedCount / cases.Count(c => c.ExpectedTool is not null);
        Assert.True(ratio >= 0.70, $"Only {ratio:P0} of resolvable phrases matched (target >= 70%)");
    }
}
```

> Этот тест переиспользует `FakeEmbeddingModel` из `EmbeddingResolverTests.cs` (тот же тестовый
> проект, то же пространство имён) — фикстура доказывает, что *логика конвейера* достигает цели
> в 70%; реальная точность ONNX-модели — отдельное, ручное измерение (см. пункт 7 чек-листа
> Этапа 1 и честную переоценку в разделе рисков roadmap).

- [ ] **Шаг 3: Настроить копирование фикстуры и конфигов при сборке тестового проекта**

Добавьте в `src/Jarvis.Core.Tests/Jarvis.Core.Tests.csproj` внутрь существующего элемента `<Project>`:

```xml
<ItemGroup>
  <None Include="Resources\test-phrases.yaml" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 4: Запустить тест и убедиться, что он падает, then passes**

Команда: `dotnet test --filter PhraseFixtureTests`
Ожидается: сначала FAIL (фикстуры нет / путь `config/intents` не находится из рабочей директории
теста — поправьте `Path.Combine("config", "intents")` на абсолютный путь через подъём от
`Directory.GetCurrentDirectory()`, если рабочая директория тестового проекта не совпадает с корнем
репозитория), затем PASS, когда путь разрешится и выполнится условие ≥70%.

Если доля меньше 70%, чинить нужно добавлением новых паттернов в `RuleBasedResolver` или новых
intent-примеров — а не понижением порога или удалением неудачных кейсов.

- [ ] **Шаг 5: Написать `scripts/download-models.ps1`**

```powershell
# scripts/download-models.ps1
# Скачивает embedding-модель уровня 2 (multilingual-e5-small, ONNX int8) и её токенизатор.
# Хэши проверяются по принципу "доверяй при первом скачивании": при первом запуске
# вычисленный SHA256 сохраняется в models/CHECKSUMS.txt; при повторных запусках файл
# перекачивается только если существующий локальный файл не совпадает с сохранённым хэшем.

$ErrorActionPreference = "Stop"
$modelsDir = Join-Path $PSScriptRoot "..\models"
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null

$files = @(
    @{ Url = "https://huggingface.co/onnx-community/multilingual-e5-small/resolve/main/onnx/model_int8.onnx"; Name = "e5-small-int8.onnx" },
    @{ Url = "https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/sentencepiece.bpe.model"; Name = "sentencepiece.bpe.model" }
)

$checksumsPath = Join-Path $modelsDir "CHECKSUMS.txt"
$checksums = @{}
if (Test-Path $checksumsPath) {
    Get-Content $checksumsPath | ForEach-Object {
        $parts = $_ -split "  ", 2
        if ($parts.Length -eq 2) { $checksums[$parts[1]] = $parts[0] }
    }
}

foreach ($file in $files) {
    $destination = Join-Path $modelsDir $file.Name
    $needsDownload = -not (Test-Path $destination)

    if (-not $needsDownload -and $checksums.ContainsKey($file.Name)) {
        $currentHash = (Get-FileHash $destination -Algorithm SHA256).Hash
        if ($currentHash -ne $checksums[$file.Name]) {
            Write-Warning "$($file.Name) не совпадает с сохранённым хэшем, перекачиваю."
            $needsDownload = $true
        }
    }

    if ($needsDownload) {
        Write-Host "Скачиваю $($file.Name)..."
        Invoke-WebRequest -Uri $file.Url -OutFile $destination
        $hash = (Get-FileHash $destination -Algorithm SHA256).Hash
        $checksums[$file.Name] = $hash
        Write-Host "$($file.Name): SHA256 $hash"
    } else {
        Write-Host "$($file.Name) уже скачан и хэш совпадает, пропускаю."
    }
}

$checksums.GetEnumerator() | ForEach-Object { "$($_.Value)  $($_.Key)" } | Set-Content $checksumsPath
Write-Host "Готово. Хэши сохранены в $checksumsPath — при подмене файла на диске скрипт перекачает его заново."
```

- [ ] **Шаг 6: Написать раздел про Этап 1 в `README.md`**

```markdown
# Jarvis — локальный голосовой ассистент (в разработке)

Полностью локальный ассистент для Windows 10. Подробности архитектуры — в
`docs/superpowers/specs/`.

## Этап 1 — текстовое ядро

Требования: .NET 8 SDK (или новее, с установленным net8.0 runtime).

1. Скачайте embedding-модель для уровня 2 (опционально — без неё работает только уровень 1):
   ```powershell
   pwsh scripts/download-models.ps1
   ```
2. Запустите ядро:
   ```bash
   dotnet run --project src/Jarvis.Core
   ```
3. Введите команду, например `открой хром` или `как там батарея`.
4. Тесты: `dotnet test`
```

- [ ] **Step 7: Закоммитить**

```bash
git add src/Jarvis.Core.Tests/Resources src/Jarvis.Core.Tests/Nlu/PhraseFixtureTests.cs scripts/download-models.ps1 README.md src/Jarvis.Core.Tests/Jarvis.Core.Tests.csproj
git commit -m "test: add 50+ phrase acceptance fixture, model download script, README"
```

---

## Финальная проверка

- [ ] Пройти полный ручной чек-лист из `docs/superpowers/specs/2026-09-29-jarvis-stage1-core-design.md` (раздел «Ручной чек-лист приёмки этапа»).
- [ ] `dotnet test` — всё зелёное.
- [ ] `dotnet run --project src/Jarvis.Core` — REPL корректно отвечает хотя бы на одну фразу на каждый инструмент.
