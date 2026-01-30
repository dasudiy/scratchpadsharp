using System;

namespace ScratchpadSharp.Shared.Exceptions;

public class PackageException : Exception
{
    public PackageException(string message) : base(message) { }
    public PackageException(string message, Exception inner) : base(message, inner) { }
}

public class CorruptPackageException : PackageException
{
    public CorruptPackageException(string message) : base(message) { }
    public CorruptPackageException(string message, Exception inner) : base(message, inner) { }
}

public class UnsupportedFormatException : PackageException
{
    public string FileVersion { get; set; } = string.Empty;
    public string AppVersion { get; set; } = "1.0";

    public UnsupportedFormatException(string message) : base(message) { }
    public UnsupportedFormatException(string message, Exception inner) : base(message, inner) { }
}
