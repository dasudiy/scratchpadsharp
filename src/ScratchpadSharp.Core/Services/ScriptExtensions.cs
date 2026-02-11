using System;

namespace ScratchpadSharp.Core.Services;

public static class ScriptExtensions
{
    public static T Dump<T>(this T obj, string? label = null)
    {
        DumpDispatcher.Dispatch(obj, label);
        return obj;
    }
}
