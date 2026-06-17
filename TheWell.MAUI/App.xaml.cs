using Microsoft.Extensions.DependencyInjection;
using TheWell.MAUI.Services;
using TheWell.MAUI.Views;

namespace TheWell.MAUI;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		ApiService.SessionExpired += OnSessionExpired;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	private void OnSessionExpired()
	{
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			if (Current?.Windows.FirstOrDefault()?.Page is not Shell shell) return;
			await shell.DisplayAlert("Session Expired", "Please log in again.", "OK");
			await shell.GoToAsync($"///{nameof(LoginPage)}");
		});
	}
}
