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
        app.MapPost("/api/push/register", RegisterPushAsync);
        app.MapPost("/api/push/unregister", UnregisterPushAsync);
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

    private async Task<IResult> RegisterPushAsync(MobilePushRegistrationRequest request, CancellationToken token)
    {
        var pushToken = request.Token?.Trim() ?? string.Empty;
        if (!(pushToken.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || pushToken.StartsWith("ExpoPushToken[", StringComparison.Ordinal)))
            return Results.BadRequest(new { error = "Некорректный Expo Push Token." });
        await AppServices.DataStore.MutateAsync(root =>
        {
            root.MobilePushSubscriptions ??= [];
            var existing = root.MobilePushSubscriptions.FirstOrDefault(value => value.Token == pushToken);
            if (existing is null)
            {
                root.MobilePushSubscriptions.Add(new Models.MobilePushSubscription
                {
                    Token = pushToken,
                    Platform = request.Platform?.Trim() ?? "android",
                    DeviceName = request.DeviceName?.Trim() ?? "Android",
                    UpdatedAt = DateTimeOffset.Now
                });
            }
            else
            {
                existing.Platform = request.Platform?.Trim() ?? existing.Platform;
                existing.DeviceName = request.DeviceName?.Trim() ?? existing.DeviceName;
                existing.UpdatedAt = DateTimeOffset.Now;
            }
        }, "register_mobile_push", token);
        return Results.Ok(new { registered = true });
    }

    private async Task<IResult> UnregisterPushAsync(MobilePushRegistrationRequest request, CancellationToken token)
    {
        var pushToken = request.Token?.Trim() ?? string.Empty;
        await AppServices.DataStore.MutateAsync(root =>
        {
            root.MobilePushSubscriptions ??= [];
            root.MobilePushSubscriptions.RemoveAll(value => value.Token == pushToken);
        }, "unregister_mobile_push", token);
        return Results.Ok(new { registered = false });
    }
}
