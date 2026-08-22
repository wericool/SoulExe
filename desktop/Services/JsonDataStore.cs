using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class JsonDataStore
{
    private const int CurrentSchemaVersion = 8;
    private readonly DataPaths _paths;
    private readonly string _dataFile;
    private readonly string _backupDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private SoulDataRoot? _root;

    public JsonDataStore(DataPaths paths)
    {
        _paths = paths;
        _dataFile = Path.Combine(paths.Root, "soulexe.json");
        _backupDirectory = Path.Combine(paths.Root, "backups");
    }

    public DataPaths Paths => _paths;
    public string DataFile => _dataFile;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_root is not null) return;
            Directory.CreateDirectory(_backupDirectory);
            _root = await LoadOrCreateAsync(cancellationToken);
            if (_root.SchemaVersion > CurrentSchemaVersion)
                throw new InvalidOperationException($"Данные были созданы новой версией SoulExe ({_root.SchemaVersion}).");

            while (_root.SchemaVersion < CurrentSchemaVersion)
                ApplyMigration(_root);

            _root.Personas ??= [];
            RefreshBundledJarvis(_root);
            EnsureBundledPromptPresets(_root);
            await PersistUnsafeAsync("initialise", cancellationToken, createBackup: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> ReadAsync<T>(Func<SoulDataRoot, T> reader, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = GetRoot();
            return reader(DeepClone(root));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MutateAsync(Action<SoulDataRoot> mutation, string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = GetRoot();
            mutation(root);
            await PersistUnsafeAsync(reason, cancellationToken, createBackup: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> MutateAsync<T>(Func<SoulDataRoot, T> mutation, string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var root = GetRoot();
            var result = mutation(root);
            await PersistUnsafeAsync(reason, cancellationToken, createBackup: true);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_dataFile))
                await BackupUnsafeAsync(reason, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SoulDataRoot> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_dataFile))
        {
            return CreateInitialRoot();
        }

        try
        {
            await using var stream = File.OpenRead(_dataFile);
            var root = await JsonSerializer.DeserializeAsync<SoulDataRoot>(stream, _json, cancellationToken);
            if (root is null) throw new InvalidDataException("Файл данных пуст.");
            return root;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
        {
            var corruptPath = Path.Combine(_backupDirectory, $"corrupt_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json");
            try { File.Copy(_dataFile, corruptPath, overwrite: true); } catch { }
            AppLog.Write("Data file could not be parsed; a new data root was created.", exception);
            return CreateInitialRoot();
        }
    }

    private static SoulDataRoot CreateInitialRoot()
    {
        var character = CreateJarvis();
        var chat = new SoulChat { Name = "Основной чат" };
        character.Chats.Add(chat);
        character.CurrentChatId = chat.Id;
        return new SoulDataRoot { Characters = [character], PromptPresets = CreateBundledPromptPresets() };
    }


    private static List<SoulPromptPreset> CreateBundledPromptPresets() =>
    [
        new SoulPromptPreset
        {
            Id = Guid.Parse("1138ca8b-18c5-4d2c-b42b-771d2f7c0001"),
            Name = "Стандартный RP",
            Description = "Обычный ролевой режим: персонаж остаётся в образе, поддерживает логику сцены и реагирует естественно, не решая за вас.",
            IsBuiltIn = true,
            PromptText = """
                [ROLEPLAY PRESET — STANDARD]
                This is an ongoing roleplay between {{user}}, {{char}}, and any characters naturally present in the scene. Stay in character and preserve temporal, emotional and logical coherence. React naturally to the current scene and use sensory or environmental detail only when it improves immersion. Vary response length and phrasing; do not repeat character traits or formulas mechanically. Never write, choose, think, feel, speak, or act for {{user}}. Use the character card, lore, summary and memory as the source of established facts.
                """
        },
        new SoulPromptPreset
        {
            Id = Guid.Parse("1138ca8b-18c5-4d2c-b42b-771d2f7c0002"),
            Name = "Стандартный RP — русский",
            Description = "Тот же ролевой режим, но модель всегда продолжает диалог по-русски, даже если часть запроса написана на другом языке.",
            IsBuiltIn = true,
            PromptText = """
                [ROLEPLAY PRESET — STANDARD RUSSIAN]
                This is an ongoing roleplay between {{user}}, {{char}}, and any characters naturally present in the scene. Stay in character and preserve temporal, emotional and logical coherence. React naturally to the current scene and use sensory or environmental detail only when it improves immersion. Vary response length and phrasing; do not repeat character traits or formulas mechanically. Never write, choose, think, feel, speak, or act for {{user}}. Use the character card, lore, summary and memory as the source of established facts. Always write the final roleplay reply in clear, natural Russian regardless of the language used in the latest message.
                """
        },
        new SoulPromptPreset
        {
            Id = Guid.Parse("1138ca8b-18c5-4d2c-b42b-771d2f7c0003"),
            Name = "Приключение / RPG",
            Description = "Режим ведущего приключения: больше мира, событий, NPC, последствий и сюжетных возможностей, но выбор и действия остаются за вами.",
            IsBuiltIn = true,
            PromptText = """
                [ROLEPLAY PRESET — ADVENTURE RPG]
                Guide {{user}} through a living, coherent world together with {{char}}. Enrich appropriate replies with scene atmosphere, relevant NPC reactions, danger, mystery, opportunity and believable consequences. Let the world respond dynamically to established facts, lore, summary and memory. Present natural openings for action rather than repetitive menu-like choices. Keep {{char}} in character. Never decide, narrate, speak, think, feel, or act for {{user}}; the user alone chooses their actions.
                """
        },
        new SoulPromptPreset
        {
            Id = Guid.Parse("1138ca8b-18c5-4d2c-b42b-771d2f7c0004"),
            Name = "Короткий диалог",
            Description = "Быстрый разговорный режим: преимущественно прямая речь и короткие реакции, минимум описаний без потери характера.",
            IsBuiltIn = true,
            PromptText = """
                [ROLEPLAY PRESET — CONCISE DIALOGUE]
                Keep {{char}} in character and prioritise natural, responsive conversation with {{user}}. Use concise replies, usually one to four sentences, unless the user explicitly asks for detail. Keep narration minimal and include it only when it adds a meaningful reaction. Preserve established facts from the character card, lore, summary and memory. Never write, choose, think, feel, speak, or act for {{user}}.
                """
        }
    ];

    private static void EnsureBundledPromptPresets(SoulDataRoot root)
    {
        root.PromptPresets ??= [];
        foreach (var bundled in CreateBundledPromptPresets())
        {
            var existing = root.PromptPresets.FirstOrDefault(preset => preset.Id == bundled.Id);
            if (existing is null)
            {
                root.PromptPresets.Add(bundled);
                continue;
            }
            if (!existing.IsBuiltIn) continue;
            existing.Name = bundled.Name;
            existing.Description = bundled.Description;
            existing.PromptText = bundled.PromptText;
            existing.IsBuiltIn = true;
        }
    }

    private static SoulCharacter CreateJarvis() => new()
    {
        Name = "Джарвис",
        Title = "Интеллектуальный AI-помощник",
        Description = "Джарвис — умный, собранный и тактичный искусственный интеллект. Он помогает разбираться в вопросах, поддерживает естественный диалог и умеет объяснять сложное простыми словами.",
        Personality = "Спокойный, наблюдательный, вежливый и уверенный в себе. Джарвис мыслит структурно, задаёт уточняющие вопросы, если это необходимо, и не делает вид, что знает то, чего не знает. Его лёгкая ирония уместна только там, где она не мешает делу.",
        Scenario = "Джарвис — персональный интеллектуальный помощник пользователя в локальном приложении SoulExe. Между ним и пользователем нет заранее заданного сюжета: диалог может быть дружеским, практическим, творческим или ролевым, если пользователь сам этого захочет.",
        SystemPrompt = "Ты — Джарвис, умный, внимательный и тактичный искусственный интеллект-помощник. Всегда отвечай на том языке, на котором написал последнее сообщение пользователь; если язык смешанный или неясный, используй русский. Держи нить текущего разговора, отвечай по существу и объясняй сложное понятным языком. Не выдумывай факты, личный опыт, доступ к интернету или действия, которых не выполнял. Когда информации недостаточно, прямо скажи об этом и при необходимости задай один уместный уточняющий вопрос. Не называй себя моделью без необходимости и не выходи из образа Джарвиса.",
        CreatorNotes = "Стандартный персонаж SoulExe. Его можно свободно изменять, экспортировать или удалить после создания других персонажей.",
        ExampleDialogue = "<START>\nПользователь: Можешь объяснить это проще?\nДжарвис: Разумеется. Сначала коротко сформулирую главную мысль, затем разберём её по шагам.\n<START>\nПользователь: На каком языке ты отвечаешь?\nДжарвис: На языке вашего последнего сообщения. Сейчас — на русском.",
        FolderName = "Джарвис",
        SourceType = "soulexe_default",
        Greetings = [new SoulGreeting { Text = "Здравствуйте. Я Джарвис, ваш локальный интеллектуальный помощник. Чем могу быть полезен?", IsPrimary = true, Position = 0 }]
    };

    private static void RefreshBundledJarvis(SoulDataRoot root)
    {
        // Upgrade only the untouched legacy starter card; user-created or edited cards stay intact.
        var legacy = root.Characters.FirstOrDefault(character =>
            character.SourceType == "local" &&
            character.Name == "Ассистент" &&
            character.Title == "Локальный AI-персонаж" &&
            character.Description == "Первый персонаж SoulExe.");
        if (legacy is null) return;

        var jarvis = CreateJarvis();
        legacy.Name = jarvis.Name;
        legacy.Title = jarvis.Title;
        legacy.Description = jarvis.Description;
        legacy.Personality = jarvis.Personality;
        legacy.Scenario = jarvis.Scenario;
        legacy.SystemPrompt = jarvis.SystemPrompt;
        legacy.CreatorNotes = jarvis.CreatorNotes;
        legacy.ExampleDialogue = jarvis.ExampleDialogue;
        legacy.FolderName = jarvis.FolderName;
        legacy.SourceType = jarvis.SourceType;
        legacy.Greetings = jarvis.Greetings;
        legacy.UpdatedAt = DateTimeOffset.Now;
    }

    private static void ApplyMigration(SoulDataRoot root)
    {
        if (root.SchemaVersion == 1)
        {
            // Version 2 makes Cognitive Architecture opt-in. Existing user cards keep all text and chats,
            // but automatic memory and summary processing are disabled until explicitly enabled per character.
            foreach (var character in root.Characters)
            {
                character.CognitiveArchitectureEnabled = false;
                character.SoulMemoryEnabled = false;
                character.AutoSummaryEnabled = false;
            }
        }
        if (root.SchemaVersion == 4)
        {
            // Version 5 adds per-chat starting context. Existing chats stay unchanged and start with an empty context.
            foreach (var character in root.Characters)
            {
                character.DefaultUserProfile ??= "";
                character.DefaultRelationshipContext ??= "";
                foreach (var chat in character.Chats)
                {
                    chat.InitialUserProfile ??= "";
                    chat.InitialRelationshipContext ??= "";
                }
            }
        }
        if (root.SchemaVersion == 5)
        {
            // Version 6 schedules automatic cognitive work locally instead of competing with foreground chat generation.
            root.Preferences.CognitiveBackgroundMode = string.Equals(root.Preferences.CognitiveBackgroundMode, "immediate", StringComparison.OrdinalIgnoreCase) ? "immediate" : "idle";
            root.Preferences.CognitiveBackgroundIdleSeconds = Math.Clamp(root.Preferences.CognitiveBackgroundIdleSeconds, 10, 30);
        }
        if (root.SchemaVersion == 6)
        {
            // Version 7 introduces isolated two-character scenes.
            root.Scenes ??= [];
        }
        if (root.SchemaVersion == 7)
        {
            // Version 8 surfaces the already supported reusable user personas in the library.
            // Clear only links to personas that are no longer present, never touch chat history.
            root.Personas ??= [];
            var personaIds = root.Personas.Select(persona => persona.Id).ToHashSet();
            foreach (var character in root.Characters)
            {
                if (character.SelectedPersonaId is Guid personaId && !personaIds.Contains(personaId))
                    character.SelectedPersonaId = null;
            }
        }
        root.SchemaVersion++;
    }

    private async Task PersistUnsafeAsync(string reason, CancellationToken cancellationToken, bool createBackup)
    {
        if (createBackup && File.Exists(_dataFile))
            await BackupUnsafeAsync(reason, cancellationToken);

        var temporary = _dataFile + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, GetRoot(), _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporary, _dataFile, overwrite: true);
        CleanupBackups();
    }

    private async Task BackupUnsafeAsync(string reason, CancellationToken cancellationToken)
    {
        var safeReason = string.Concat(reason.Select(x => char.IsLetterOrDigit(x) ? x : '_'));
        var target = Path.Combine(_backupDirectory, $"soultext_{DateTimeOffset.Now:yyyyMMdd_HHmmss}_{safeReason}.json");
        await using var source = File.OpenRead(_dataFile);
        await using var destination = File.Create(target);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private void CleanupBackups()
    {
        try
        {
            foreach (var file in new DirectoryInfo(_backupDirectory)
                         .EnumerateFiles("soultext_*.json")
                         .OrderByDescending(x => x.LastWriteTimeUtc)
                         .Skip(12))
            {
                file.Delete();
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Backup cleanup failed.", exception);
        }
    }

    private SoulDataRoot GetRoot() => _root ?? throw new InvalidOperationException("Хранилище SoulText не инициализировано.");

    private SoulDataRoot DeepClone(SoulDataRoot source)
    {
        var serialized = JsonSerializer.Serialize(source, _json);
        return JsonSerializer.Deserialize<SoulDataRoot>(serialized, _json) ?? throw new InvalidOperationException("Не удалось скопировать данные SoulText.");
    }
}
