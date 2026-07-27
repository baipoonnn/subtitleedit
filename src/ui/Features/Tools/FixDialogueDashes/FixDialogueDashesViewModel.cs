using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Logic.Config;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public partial class FixDialogueDashesViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<FixDialogueDashesCandidate> _candidates;
    [ObservableProperty] private FixDialogueDashesCandidate? _selectedCandidate;
    [ObservableProperty] private string _candidatesInfo;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }
    public List<SubtitleLineViewModel> AllSubtitlesFixed { get; private set; }

    private List<SubtitleLineViewModel> _allSubtitles;

    public FixDialogueDashesViewModel()
    {
        Candidates = new ObservableCollection<FixDialogueDashesCandidate>();
        _allSubtitles = new List<SubtitleLineViewModel>();
        AllSubtitlesFixed = new List<SubtitleLineViewModel>();
        CandidatesInfo = string.Empty;
    }

    public void Initialize(List<SubtitleLineViewModel> subtitles)
    {
        _allSubtitles = subtitles;

        Dispatcher.UIThread.Post(() =>
        {
            Candidates.Clear();
            var detected = FixDialogueDashesHelper.Detect(_allSubtitles);
            foreach (var c in detected)
            {
                Candidates.Add(c);
            }

            CandidatesInfo = detected.Count == 0
                ? Se.Language.Tools.FixDialogueDashes.NoCandidatesFound
                : string.Format(Se.Language.Tools.FixDialogueDashes.CandidatesFoundX, detected.Count);
        });
    }

    [RelayCommand]
    private void Ok()
    {
        AllSubtitlesFixed = FixDialogueDashesHelper.Apply(_allSubtitles, Candidates);
        OkPressed = true;
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Window?.Close();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var c in Candidates)
        {
            c.IsSelected = true;
        }
    }

    [RelayCommand]
    private void InverseSelection()
    {
        foreach (var c in Candidates)
        {
            c.IsSelected = !c.IsSelected;
        }
    }

    internal void KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Window?.Close();
        }
    }
}
