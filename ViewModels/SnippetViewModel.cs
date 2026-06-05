using System;
using CommunityToolkit.Mvvm.ComponentModel;
using RemoteManager.Models;

namespace RemoteManager.ViewModels;

public partial class SnippetViewModel : ObservableObject
{
    private readonly Snippet _snippet;

    public SnippetViewModel(Snippet snippet)
    {
        _snippet = snippet;
        _name = snippet.Name;
        _command = snippet.Command;
    }

    public Guid Id => _snippet.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _command;

    public Snippet ToModel()
    {
        _snippet.Name = Name;
        _snippet.Command = Command;
        return _snippet;
    }
}
