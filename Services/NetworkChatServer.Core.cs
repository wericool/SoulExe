using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SoulExe.Services;

/// <summary>Registration boundary for the local client entry point and authentication.</summary>
public sealed partial class NetworkChatServer
{
    private void MapCoreRoutes(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(MobileStyleWebClient.Content, "text/html; charset=utf-8"));
        app.MapGet("/api/health", () => Results.Ok(new { service = "SoulExe", mobileDiscovery = true }));
        app.MapGet("/api/diagnostics/prompt", GetLatestPromptDiagnostic);
        app.MapPost("/api/auth/login", LoginAsync);
    }

    private static IResult GetLatestPromptDiagnostic() => PromptDiagnosticSnapshotStore.Latest() is { } snapshot
        ? Results.Ok(new { generationId = snapshot.GenerationId, createdAt = snapshot.CreatedAt, trace = snapshot.Trace })
        : Results.NotFound(new { error = "Диагностика промпта пока отсутствует." });

    private IResult LoginAsync(MobileLoginRequest request)
    {
        var configured = _credentials();
        var username = request.Username?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (!FixedEquals(username, configured.Username) || !MobileAccessPasswordHasher.Verify(password, configured.PasswordHash))
            return Results.Unauthorized();
        var session = CreateToken();
        _sessions.Add(session, DateTimeOffset.UtcNow);
        return Results.Ok(new { session });
    }
}
