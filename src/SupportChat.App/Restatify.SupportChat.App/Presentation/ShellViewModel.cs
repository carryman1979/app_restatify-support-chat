using Restatify.SupportChat.ViewModels;

namespace Restatify.SupportChat.Presentation;

public class ShellViewModel
{
    private readonly IAuthenticationService _authentication;
    private readonly INavigator _navigator;

    public ShellViewModel(
		IAuthenticationService authentication,
        INavigator navigator)
    {
        _navigator = navigator;
        _authentication = authentication;
        _authentication.LoggedOut += LoggedOut;

        App.TraceStartup("ShellViewModel: constructed.");
    }

    private async void LoggedOut(object? sender, EventArgs e)
    {
        await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
    }
}
