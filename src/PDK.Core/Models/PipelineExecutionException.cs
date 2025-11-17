namespace PDK.Core.Models;

public class PipelineExecutionException : PdkException
{
    public PipelineExecutionException(string message) : base(message) { }
    public PipelineExecutionException(string message, Exception innerException) : base(message, innerException) { }
}