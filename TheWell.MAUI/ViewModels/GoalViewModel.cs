using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheWell.Core.DTOs;
using TheWell.MAUI.Services;

namespace TheWell.MAUI.ViewModels;

public partial class GoalViewModel(ApiService api) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IntakeResponse? _intake;

    [ObservableProperty] private bool _isBusy;

    public bool IsEmpty => !IsBusy && Intake is null;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        OnPropertyChanged(nameof(IsEmpty));
        try { Intake = await api.GetIntakeAsync(); }
        catch { }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
