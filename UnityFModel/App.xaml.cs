using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Threading;

namespace UnityFModel;

public partial class App : Application
{
    public App()
    {
        // AssetRipper.SourceGenerated (NuGet) is compiled against a slightly different
        // AssetRipper.Assets version than the one we build from source, so the default
        // loader throws FileNotFoundException for the exact version it asks for. Resolve
        // any version-mismatched assembly to the one already loaded, by simple name.
        AssemblyLoadContext.Default.Resolving += ResolveByName;

        // Keep the app alive on a single unexpected error rather than crashing hard.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private static Assembly? ResolveByName(AssemblyLoadContext context, AssemblyName name)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "UnityFModel", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
