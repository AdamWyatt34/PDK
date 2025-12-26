namespace DotNetConsole;

public class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("PDK Console Application Example");
        Console.WriteLine("================================");

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: DotNetConsole <number1> <number2>");
            Console.WriteLine("Example: DotNetConsole 5 3");
            return 1;
        }

        if (!int.TryParse(args[0], out int a) || !int.TryParse(args[1], out int b))
        {
            Console.WriteLine("Error: Both arguments must be valid integers");
            return 1;
        }

        var calculator = new Calculator();

        Console.WriteLine($"\nInputs: {a} and {b}");
        Console.WriteLine($"Add:      {a} + {b} = {calculator.Add(a, b)}");
        Console.WriteLine($"Subtract: {a} - {b} = {calculator.Subtract(a, b)}");
        Console.WriteLine($"Multiply: {a} * {b} = {calculator.Multiply(a, b)}");

        if (b != 0)
        {
            Console.WriteLine($"Divide:   {a} / {b} = {calculator.Divide(a, b):F2}");
        }
        else
        {
            Console.WriteLine("Divide:   Cannot divide by zero");
        }

        return 0;
    }
}

public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
    public int Multiply(int a, int b) => a * b;
    public double Divide(int a, int b) => b != 0 ? (double)a / b : throw new DivideByZeroException();
}
