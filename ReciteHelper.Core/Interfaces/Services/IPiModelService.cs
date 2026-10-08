namespace ReciteHelper.Core.Interfaces.Services;

public sealed record PiOAuthProvider(string Id, string Name, bool LoggedIn);
public sealed record PiChatModel(string Id, string Name);
public sealed record PiAuthOption(string Id, string Label, string? Description);
public sealed record PiAuthPrompt(string Type, string Message, string? Placeholder, PiAuthOption[]? Options);
public sealed record PiAuthLink(string Url, string? Label);
public sealed record PiAuthEvent(
    string Type, string? Message, string? Url, string? Instructions,
    string? UserCode, string? VerificationUri, PiAuthLink[]? Links);

public interface IPiModelService
{
    Task<IReadOnlyList<PiOAuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PiChatModel>> GetModelsAsync(string provider, CancellationToken cancellationToken = default);
    Task LoginAsync(string provider, Func<PiAuthPrompt, CancellationToken, Task<string>> prompt,
        Action<PiAuthEvent> notify, CancellationToken cancellationToken = default);
    Task LogoutAsync(string provider, CancellationToken cancellationToken = default);
    Task<string> RunChatAsync(string provider, string model, string prompt, string? instructions,
        CancellationToken cancellationToken = default);
}
