using System.Windows.Input;

namespace DoViFixer.App.Tests;

/// <summary>
/// Invokes commands the way a bound button does: only when CanExecute allows it.
/// The Toolkit's Execute and ExecuteAsync run without checking CanExecute.
/// </summary>
internal static class CommandInvocation
{
    public static Task InvokeAsync(this IAsyncRelayCommand command, object? parameter = null) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;

    public static void Invoke(this ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
}
