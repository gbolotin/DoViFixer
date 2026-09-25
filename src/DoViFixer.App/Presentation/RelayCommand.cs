using System.Windows.Input;

namespace DoViFixer.App.Presentation;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute() => canExecute?.Invoke() ?? true;
    public void Execute() => execute();
    bool ICommand.CanExecute(object? parameter) => CanExecute();
    void ICommand.Execute(object? parameter) => Execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(T value) => canExecute?.Invoke(value) ?? true;
    public void Execute(T value) => execute(value);
    bool ICommand.CanExecute(object? parameter) => parameter is T value && CanExecute(value);
    void ICommand.Execute(object? parameter)
    {
        if (parameter is T value)
        {
            Execute(value);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
