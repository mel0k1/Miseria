using SteamKit2.Authentication;

namespace Miseria.Core;

internal sealed class InteractiveAuthenticator(Func<GuardPrompt, Task<string?>> prompt) : IAuthenticator
{
    public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
        await prompt(new GuardPrompt(GuardKind.DeviceCode, null, previousCodeWasIncorrect))
            ?? throw new GuardCancelledException();

    public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
        await prompt(new GuardPrompt(GuardKind.EmailCode, email, previousCodeWasIncorrect))
            ?? throw new GuardCancelledException();

    public Task<bool> AcceptDeviceConfirmationAsync() => Task.FromResult(false);
}
