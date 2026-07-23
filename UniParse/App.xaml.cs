using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UniParse.Services;

namespace UniParse;

public partial class App : Application
{
    public App()
    {
        ApplicationLogger.Initialize();
        // AssetRipper.SourceGenerated (NuGet) is compiled against a slightly different
        // AssetRipper.Assets version than the one we build from source, so the default
        // loader throws FileNotFoundException for the exact version it asks for. Resolve
        // any version-mismatched assembly to the one already loaded, by simple name.
        AssemblyLoadContext.Default.Resolving += ResolveByName;

        // Keep the app alive on a single unexpected error rather than crashing hard.
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) => ApplicationLogger.Info("Application", "UniParse exited.");
    }

    private static Assembly? ResolveByName(AssemblyLoadContext context, AssemblyName name)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ApplicationLogger.Error("UnhandledException", "Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            $"予期しないエラーが発生しました:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "UniParse", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        ApplicationLogger.Error(
            "UnhandledException",
            $"Unhandled non-UI exception. IsTerminating={e.IsTerminating}",
            e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ApplicationLogger.Error("UnobservedTaskException", "An unobserved task exception was raised.", e.Exception);
        e.SetObserved();
    }
}
