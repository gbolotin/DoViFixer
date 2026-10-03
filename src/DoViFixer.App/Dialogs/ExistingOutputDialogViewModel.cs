using CommunityToolkit.Mvvm.Input;
using DoViFixer.Application.Abstractions;
using WpfFoundation.Dialogs;

namespace DoViFixer.App.Dialogs;

/// <summary>Asks whether to replace an existing conversion output, keep both files, or cancel the conversion.</summary>
public sealed class ExistingOutputDialogViewModel : DialogViewModel
{
    public ExistingOutputDialogViewModel(string existingPath) : base("File already exists")
    {
        ExistingPath = existingPath;
        Message = $"{Path.GetFileName(existingPath)} already exists in {Path.GetDirectoryName(existingPath)}.\n\n"
            + "Replace it after the new file is verified, or save the new file under a numbered name.";
        Buttons =
        [
            new("Replace", new RelayCommand(() => Choose(ExistingOutputHandling.Replace))),
            new("Save as new file", new RelayCommand(() => Choose(ExistingOutputHandling.KeepBoth))) { IsDefault = true, IsPrimary = true },
            new("Cancel", CancelCommand) { IsCancel = true }
        ];
    }

    public string ExistingPath { get; }

    public string Message { get; }

    /// <summary>The accepted choice; <see cref="ExistingOutputHandling.Skip"/> until the dialog is accepted.</summary>
    public ExistingOutputHandling Choice { get; private set; }

    public override IReadOnlyList<DialogButton> Buttons { get; }

    public void Choose(ExistingOutputHandling choice)
    {
        Choice = choice;
        if (!Accept())
        {
            Choice = ExistingOutputHandling.Skip;
        }
    }
}
