namespace Sokoban.Model;

public sealed class InvalidLevelException : Exception
{
    public InvalidLevelException()
    {
    }

    public InvalidLevelException(string message)
        : base(message)
    {
    }

    public InvalidLevelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
