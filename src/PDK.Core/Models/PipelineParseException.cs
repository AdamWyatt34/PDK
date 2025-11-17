namespace PDK.Core.Models;

public class PipelineParseException : PdkException
{
    public PipelineParseException(string message) : base(message) { }
    public PipelineParseException(string message, Exception innerException) : base(message, innerException) { }
}