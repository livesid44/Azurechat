namespace AzureChat.Services;

/// <summary>
/// Thrown when Azure OpenAI returns HTTP 404 because the configured deployment name
/// does not exist in the target Azure OpenAI resource.
/// </summary>
public sealed class AzureDeploymentNotFoundException : InvalidOperationException
{
    public string DeploymentName { get; }

    public AzureDeploymentNotFoundException(string deploymentName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        DeploymentName = deploymentName;
    }
}
