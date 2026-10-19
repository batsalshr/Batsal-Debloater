using CommunityToolkit.Mvvm.ComponentModel;

namespace UltimatePCOptimizerPro.ViewModels;

/// <summary>Base for every sidebar page.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Subtitle { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";

    private bool _loadedOnce;

    /// <summary>Called whenever the page becomes visible. First call triggers <see cref="LoadAsync"/>.</summary>
    public async Task OnNavigatedToAsync()
    {
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            await LoadAsync();
        }
        else await OnRevisitedAsync();
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected virtual Task OnRevisitedAsync() => Task.CompletedTask;

    protected async Task RunBusy(string message, Func<Task> work)
    {
        IsBusy = true;
        StatusMessage = message;
        try { await work(); }
        catch (Exception ex) { StatusMessage = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}

public sealed class NavItem
{
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public Type? PageType { get; init; }
    public bool IsHeader { get; init; }

    /// <summary>Used as the accessible name of the sidebar item (screen readers, UI Automation).</summary>
    public override string ToString() => Title;
}

/// <summary>Simple label/value row used by the specs and info panels.</summary>
public sealed record InfoRow(string Label, string Value, bool Unavailable = false, string? Tone = null);

public sealed class InfoSection
{
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public List<InfoRow> Rows { get; init; } = new();
}
