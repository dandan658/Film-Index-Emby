using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace FilmIndex.IntegrationTests;

internal static class DependencyLoader
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(DependencyLoader).Assembly);
        if (context != null) context.Resolving += (_, name) => name.Name == "FilmIndex.Emby" || name.Name == "0Harmony"
            ? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name.Name) : null;
    }
}
