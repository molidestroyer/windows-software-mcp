namespace SoftwareDock.Application.Abstractions;

/// <summary>The package manager is not installed or cannot be started.</summary>
public sealed class PackageManagerUnavailableException : Exception
{
    public PackageManagerUnavailableException()
    {
    }

    public PackageManagerUnavailableException(string message)
        : base(message)
    {
    }

    public PackageManagerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
