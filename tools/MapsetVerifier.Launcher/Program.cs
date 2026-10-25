using System.Reflection;
using System.Runtime.Loader;

try
{
    string path = Path.Combine(AppContext.BaseDirectory, "mapset-verifier", "FruitsAtelier.MapsetVerifier.dll");
    var context = new VerifierContext(path);
    var assembly = context.LoadFromAssemblyPath(path);
    var entry = assembly.EntryPoint ?? throw new MissingMethodException("Mapset Verifier entry point is missing.");
    return entry.Invoke(null, [args]) is int exitCode ? exitCode : 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error is TargetInvocationException { InnerException: { } inner } ? inner : error);
    return 1;
}

// Keep MV's package versions separate from the editor while sharing the bundled runtime.
sealed class VerifierContext(string path) : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver = new(path);
    protected override Assembly? Load(AssemblyName name) => resolver.ResolveAssemblyToPath(name) is { } assembly
        ? LoadFromAssemblyPath(assembly) : null;
    protected override nint LoadUnmanagedDll(string name) => resolver.ResolveUnmanagedDllToPath(name) is { } library
        ? LoadUnmanagedDllFromPath(library) : 0;
}
