using System;

namespace EmberTern.Firebird;

public sealed class ConnectionFailedException : Exception
{
    public ConnectionFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
