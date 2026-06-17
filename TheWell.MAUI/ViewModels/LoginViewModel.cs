using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheWell.Core.DTOs;
using TheWell.Core.Entities;
using TheWell.MAUI.Services;
using TheWell.MAUI.Views;


namespace TheWell.MAUI.ViewModels;

public partial class LoginViewModel(ApiService api) : ObservableObject
{
    [ObservableProperty] private string _eNumber = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ForgotPasswordAsync() =>
        await Shell.Current.GoToAsync(nameof(OtpPage));

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(ENumber) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter your E-number and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";

        try
        {
            var result = await api.LoginAsync(new LoginRequest(ENumber, Password));
            if (result is null)
            {
                ErrorMessage = "Invalid credentials or could not reach the server.";
                return;
            }

            if (result.IsPasswordResetRequired)
            {
                await Shell.Current.GoToAsync(nameof(ForceResetPage));
                return;
            }

            if (result.AccountStatus == AccountStatuses.Graduation)
            {
                await Shell.Current.GoToAsync("//GraduationPage");
                return;
            }

            await Shell.Current.GoToAsync(result.IsIntakeComplete ? "//DashboardPage" : "//IntakePage");
        }
        catch (Exception)
        {
            ErrorMessage = "Could not connect to the server. Please try again.";
        }
        finally { IsBusy = false; }
    }

}
