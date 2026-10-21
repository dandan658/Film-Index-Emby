using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace StrmAssistant.Common
{
    internal static class DependencyLoader
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            var own = typeof(DependencyLoader).Assembly;
            var context = AssemblyLoadContext.GetLoadContext(own);
            if (context == null) return;
            context.Resolving += (_, name) =>
            {
                if (!string.Equals(name.Name, "0Harmony", StringComparison.Ordinal)) return null;
                lock (own)
                {
                    foreach (var assembly in context.Assemblies)
                        if (assembly.GetName().Name == name.Name && assembly.GetName().Version == name.Version) return assembly;
                    using var stream = own.GetManifestResourceStream("FilmIndex.Dependencies.0Harmony.dll");
                    return stream == null ? null : context.LoadFromStream(stream);
                }
            };
        }
    }
}
