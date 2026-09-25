using System.Windows.Input;
using DoViFixer.App.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class PresentationTests
{
    [TestMethod]
    public void PropertyChangesNotifyOnlyWhenValueChanges()
    {
        var model = new SampleModel();
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        model.Name = "Media";
        model.Name = "Media";
        model.Name = "Archive";

        CollectionAssert.AreEqual(new[] { "Name", "Name" }, notifications);
        Assert.AreEqual("Archive", model.Name);
    }

    [TestMethod]
    public void CommandExposesAvailabilityAndNotifiesBindings()
    {
        bool available = false;
        int executions = 0;
        int notifications = 0;
        var command = new RelayCommand(() => executions++, () => available);
        command.CanExecuteChanged += (_, _) => notifications++;
        ICommand binding = command;

        Assert.IsFalse(binding.CanExecute(null));
        available = true;
        command.RaiseCanExecuteChanged();
        Assert.IsTrue(binding.CanExecute(null));
        binding.Execute(null);

        Assert.AreEqual(1, executions);
        Assert.AreEqual(1, notifications);
    }

    [TestMethod]
    public void RowCommandRejectsMissingParametersAndPassesTheSelectedRow()
    {
        object? selected = null;
        var row = new object();
        var command = new RelayCommand<object>(value => selected = value, value => ReferenceEquals(value, row));
        ICommand binding = command;

        Assert.IsFalse(binding.CanExecute(null));
        Assert.IsFalse(binding.CanExecute(new object()));
        binding.Execute(null);
        Assert.IsNull(selected);
        Assert.IsTrue(binding.CanExecute(row));
        binding.Execute(row);
        Assert.AreSame(row, selected);
    }

    private sealed class SampleModel : ObservableObject
    {
        private string name = "";
        public string Name { get => name; set => SetProperty(ref name, value); }
    }
}
