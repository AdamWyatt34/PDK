namespace DotNetSample;

/// <summary>
/// Simple .NET console application for integration testing PDK executors.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello from PDK test project!");
        Console.WriteLine($"Running on .NET {Environment.Version}");

        if (args.Length > 0)
        {
            Console.WriteLine($"Arguments: {string.Join(", ", args)}");
        }
    }
}
