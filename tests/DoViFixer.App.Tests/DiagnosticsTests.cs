using DoViFixer.App.Presentation.Application;
using DoViFixer.App.ViewModels;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class DiagnosticsTests
{
    [TestMethod]
    public async Task ExportCommandReportsSuccessAndHonorsDialogCancellation()
    {
        using var runtime = new TestRuntime();
        var writer = new Writer();
        runtime.Services.AddSingleton<IDiagnosticBundleWriter>(writer);
        var vm = runtime.Container.GetRequiredService<SettingsViewModel>();
        await vm.ExportDiagnosticsCommand.InvokeAsync();
        Assert.AreEqual(0, writer.Calls);
        runtime.SaveFilePath = "support.zip";
        await vm.ExportDiagnosticsCommand.InvokeAsync();
        Assert.AreEqual(1, writer.Calls);
        Assert.AreEqual(ViewStatus.Result, vm.Status);
        StringAssert.Contains(vm.StatusMessage, "support.zip");
        Assert.IsTrue(vm.ExportDiagnosticsCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task BusyExportDisablesAnotherExportAndCanBeCancelled()
    {
        using var runtime = new TestRuntime { SaveFilePath = "support.zip" };
        var writer = new Writer { Block = true };
        runtime.Services.AddSingleton<IDiagnosticBundleWriter>(writer);
        var vm = runtime.Container.GetRequiredService<SettingsViewModel>();
        Task exporting = vm.ExportDiagnosticsCommand.InvokeAsync();
        await writer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(vm.ExportDiagnosticsCommand.CanExecute(null));
        vm.CancelCommand.Invoke();
        await exporting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(ViewStatus.Cancelled, vm.Status);
        Assert.IsTrue(vm.IsIdle);
    }

    [TestMethod]
    public async Task ExportFailureIsVisibleAndCommandIsEnabledAgain()
    {
        using var runtime = new TestRuntime { SaveFilePath = "support.zip" };
        runtime.Services.AddSingleton<IDiagnosticBundleWriter>(new Writer { Fail = true });
        var vm = runtime.Container.GetRequiredService<SettingsViewModel>();
        await vm.ExportDiagnosticsCommand.InvokeAsync();
        Assert.AreEqual(ViewStatus.Error, vm.Status);
        StringAssert.Contains(vm.StatusMessage, "destination exists");
        Assert.IsTrue(vm.ExportDiagnosticsCommand.CanExecute(null));
    }

    private sealed class Writer : IDiagnosticBundleWriter
    {
        public int Calls { get; private set; }
        public bool Block { get; init; }
        public bool Fail { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DiagnosticExportResult> WriteAsync(string destination, DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            if (Fail)
            {
                throw new IOException("destination exists");
            }
            if (Block)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return new(destination, 3, 0, 0);
        }
    }
}
