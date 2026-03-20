using AzureChat.Services;

namespace AzureChat.Tests.Services;

public class ChatServiceDeploymentExceptionTests
{
    [Fact]
    public void AzureDeploymentNotFoundException_StoresDeploymentName()
    {
        var ex = new AzureDeploymentNotFoundException("my-gpt4", "deployment not found");
        Assert.Equal("my-gpt4", ex.DeploymentName);
    }

    [Fact]
    public void AzureDeploymentNotFoundException_StoresMessage()
    {
        const string msg = "Azure OpenAI deployment 'gpt-4o-2' was not found (HTTP 404).";
        var ex = new AzureDeploymentNotFoundException("gpt-4o-2", msg);
        Assert.Equal(msg, ex.Message);
    }

    [Fact]
    public void AzureDeploymentNotFoundException_StoresInnerException()
    {
        var inner = new Exception("original 404");
        var ex = new AzureDeploymentNotFoundException("gpt-4o", "not found", inner);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void AzureDeploymentNotFoundException_IsInvalidOperationException()
    {
        var ex = new AzureDeploymentNotFoundException("gpt-4o", "msg");
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void AzureDeploymentNotFoundException_CanBeDetectedByIsCheck()
    {
        Exception ex = new AzureDeploymentNotFoundException("gpt-4o", "not found");

        // Simulate the Program.cs detection pattern.
        bool detected = ex is AzureDeploymentNotFoundException dnf && dnf.DeploymentName == "gpt-4o";
        Assert.True(detected);
    }

    [Fact]
    public void AzureDeploymentNotFoundException_NotTriggeredForOtherExceptions()
    {
        Exception ex = new InvalidOperationException("some other error");

        bool detected = ex is AzureDeploymentNotFoundException;
        Assert.False(detected);
    }
}
