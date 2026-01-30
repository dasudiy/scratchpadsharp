namespace ScratchpadSharp.Core.Isolation;

/// <summary>
/// AssemblyLoadContext for isolating script execution and enabling unloading.
/// Phase 2: Implementation with isCollectible=true and WeakReference unloading.
/// </summary>
public class ScriptAssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext
{
    public ScriptAssemblyLoadContext() : base(isCollectible: true)
    {
        // TODO: Phase 2 - Implement full ALC isolation
        // - Add AssemblyDependencyResolver
        // - Implement Load override
        // - Handle transitive dependencies
    }
}
