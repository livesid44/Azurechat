using Azure.Core;

namespace AzureChat.Services;

/// <summary>
/// Wraps a static Entra ID access token into an <see cref="Azure.Core.TokenCredential"/>
/// so the Azure SDK sends <c>Authorization: Bearer &lt;token&gt;</c> instead of the
/// <c>api-key</c> header used by <see cref="System.ClientModel.ApiKeyCredential"/>.
/// Use this when <c>AzureOpenAI:AuthType</c> is set to <c>"Bearer"</c>.
/// </summary>
internal sealed class StaticBearerTokenCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => new(token, DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(new AccessToken(token, DateTimeOffset.MaxValue));
}
