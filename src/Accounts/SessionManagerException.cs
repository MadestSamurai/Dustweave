namespace BD2AccountSessionManager;

internal sealed class SessionManagerException : Exception
{
    internal SessionManagerException(string message)
        : base(message)
    {
    }
}
