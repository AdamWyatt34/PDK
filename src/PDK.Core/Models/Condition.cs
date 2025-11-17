namespace PDK.Core.Models;

public class Condition
{
    public string Expression { get; set; } = string.Empty;
    public ConditionType Type { get; set; }
}