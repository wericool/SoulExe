using System.Threading;
using System.Threading.Tasks;

namespace SoulTextWpf.Services;

public static class AppServices
{
    private static readonly SemaphoreSlim InitGate = new(1, 1);
    private static bool _initialized;

    public static DataPaths Paths { get; } = new();
    public static JsonDataStore DataStore { get; } = new(Paths);
    public static CharacterLibraryService CharacterLibrary { get; } = new(DataStore);
    public static ModelsHubService ModelsHub { get; } = new(DataStore);
    public static RecommendedModelsService RecommendedModels { get; } = new(Paths);
    public static LorebookService Lorebooks { get; } = new(DataStore);
    public static PersonaService Personas { get; } = new(DataStore);
    public static PromptEngine PromptEngine { get; } = new();
    public static StateVariableService StateVariables { get; } = new(DataStore);
    public static SoulMemoryService SoulMemory { get; } = new(DataStore);
    public static ChatSummaryService Summaries { get; } = new(DataStore);
    public static CharacterCardImportService CharacterCards { get; } = new(DataStore);
    public static CharactersGatewayService CharactersGateway { get; } = new(DataStore);
    public static SoulOfWaifuImportService SoulOfWaifuImporter { get; } = new(DataStore);
    public static CharacterCardExportService CharacterCardExporter { get; } = new();
    public static SceneService Scenes { get; } = new(DataStore);
    public static ScenePromptEngine ScenePromptEngine { get; } = new();
    public static ConversationService Conversations { get; } = new(DataStore, CharacterLibrary, Scenes);

    public static async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await InitGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Paths.EnsureDirectories();
            await DataStore.InitializeAsync(cancellationToken);
            AppLog.Write($"Data store initialised at {Paths.Root}.");
            _initialized = true;
        }
        finally
        {
            InitGate.Release();
        }
    }
}
