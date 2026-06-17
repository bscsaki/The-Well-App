using TheWell.MAUI.Views;

namespace TheWell.MAUI;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(ForceResetPage), typeof(ForceResetPage));
        Routing.RegisterRoute(nameof(LogEntryPage), typeof(LogEntryPage));
        Routing.RegisterRoute(nameof(OtpPage), typeof(OtpPage));
    }
}
