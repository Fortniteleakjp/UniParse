using System.Windows;
using System.Windows.Threading;

namespace UnityFModel;

public partial class App : Application
{
    public App()
    {
        // Keep the app alive on a single unexpected error rather than crashing hard.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "UnityFModel", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
