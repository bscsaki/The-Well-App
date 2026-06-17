using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheWell.MAUI.Services;

namespace TheWell.MAUI.ViewModels;

/// <summary>
/// Handles two distinct flows:
/// - First-login: no Token query param, uses the JWT from login to authenticate the reset
/// - OTP forgot-password: Token query param set from VerifyOtp, no JWT required
/// </summary>
[QueryProperty(nameof(Token), "Token")]
public partial class ForceResetViewModel(ApiService api) : ObservableObject
{
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isBusy;

    private bool IsOtpFlow => !string.IsNullOrEmpty(Token);

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (NewPassword != ConfirmPassword) { ErrorMessage = "Passwords do not match."; return; }
        if (NewPassword.Length < 8) { ErrorMessage = "Password must be at least 8 characters."; return; }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            bool ok = IsOtpFlow
                ? await api.PasswordResetAsync(Token, NewPassword)
                : await api.ForceResetAsync(NewPassword);

            if (!ok) { ErrorMessage = "Reset failed. Please try again."; return; }
            await Shell.Current.GoToAsync("//LoginPage");
        }
        finally { IsBusy = false; }
    }
}
