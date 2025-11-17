namespace PDK.Core.Models;

public interface IContainerManager
{
    Task<string> StartContainer(string image, Dictionary<string, string> environment);
    Task<int> ExecuteCommand(string containerId, string command, string shell = "bash");
    Task<string> GetContainerOutput(string containerId);
    Task StopContainer(string containerId);
    Task<bool> IsDockerAvailable();
}