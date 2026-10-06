namespace Dustweave.Accounts;

internal sealed class SessionManagerException : Exception
{
    internal SessionManagerException(string message)
        : base(message)
    {
    }
}
