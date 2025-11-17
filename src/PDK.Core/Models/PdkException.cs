namespace PDK.Core.Models;

public class PdkException : Exception
{
    public PdkException(string message) : base(message) { }
    public PdkException(string message, Exception innerException) : base(message, innerException) { }
}