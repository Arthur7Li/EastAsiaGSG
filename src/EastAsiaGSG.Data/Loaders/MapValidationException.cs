namespace EastAsiaGSG.Data.Loaders;

/// <summary>
/// Exception thrown when map configuration data fails structural, relational, or invariant validation.
/// </summary>
public sealed class MapValidationException : Exception
{
    public MapValidationException(string message) : base(message)
    {
    }

    public MapValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
