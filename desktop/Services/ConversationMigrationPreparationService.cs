using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Provides an opt-in preflight/backup operation only. No production code invokes this service
/// automatically until schema v9 storage and round-trip tests exist.
/// </summary>
public sealed class ConversationMigrationPreparationService
{
    private readonly JsonDataStore _store;

    public ConversationMigrationPreparationService(JsonDataStore store) => _store = store;

    public Task<ConversationMigrationPreflightReport> AnalyzeAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => ConversationMigrationPreflight.Analyze(root), token);

    public async Task<ConversationMigrationPreflightReport> CreateVerifiedBackupAsync(CancellationToken token = default)
    {
        var report = await AnalyzeAsync(token);
        if (!report.IsSafeToPrepareBackup)
            throw new InvalidOperationException("Резервная копия для миграции не создана: preflight обнаружил нарушения инвариантов.");
        await _store.CreateBackupAsync("conversation_schema_v9_preflight", token);
        return report;
    }
}
