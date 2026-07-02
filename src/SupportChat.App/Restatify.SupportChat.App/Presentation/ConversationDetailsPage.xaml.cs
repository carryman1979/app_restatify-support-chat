namespace Restatify.SupportChat.Presentation;

public sealed partial class ConversationDetailsPage : Page
{
	private const double SubtleArrowOpacity = 0.55;
	private const double ActiveArrowOpacity = 1.0;

	public ConversationDetailsPage()
	{
		this.InitializeComponent();
	}

	private void OnBackClick(object sender, RoutedEventArgs e)
	{
		if (Frame?.CanGoBack is true)
		{
			Frame.GoBack();
		}
	}

	private void OnLoadMoreArrowPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		SetLoadMoreArrowOpacity(ActiveArrowOpacity);
	}

	private void OnLoadMoreArrowPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		SetLoadMoreArrowOpacity(SubtleArrowOpacity);
	}

	private void OnLoadMoreArrowGotFocus(object sender, RoutedEventArgs e)
	{
		SetLoadMoreArrowOpacity(ActiveArrowOpacity);
	}

	private void OnLoadMoreArrowLostFocus(object sender, RoutedEventArgs e)
	{
		SetLoadMoreArrowOpacity(SubtleArrowOpacity);
	}

	private void SetLoadMoreArrowOpacity(double opacity)
	{
		if (LoadMoreArrowButton is not null)
		{
			LoadMoreArrowButton.Opacity = opacity;
		}
	}

	private async void OnReplyMessageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
	{
		if (e.Key != global::Windows.System.VirtualKey.Enter)
		{
			return;
		}

		var shiftState = global::Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Shift);
		var isShiftPressed = (shiftState & global::Windows.UI.Core.CoreVirtualKeyStates.Down) == global::Windows.UI.Core.CoreVirtualKeyStates.Down;
		if (isShiftPressed)
		{
			e.Handled = true;

			if (sender is TextBox textBox)
			{
				var currentText = textBox.Text ?? string.Empty;
				var start = Math.Clamp(textBox.SelectionStart, 0, currentText.Length);
				var length = Math.Clamp(textBox.SelectionLength, 0, currentText.Length - start);
				var updated = currentText.Remove(start, length).Insert(start, Environment.NewLine);
				textBox.Text = updated;
				textBox.SelectionStart = start + Environment.NewLine.Length;
				textBox.SelectionLength = 0;
			}

			return;
		}

		e.Handled = true;

		if (DataContext is not ConversationDetailsViewModel model || !model.CanInteractWithConversation)
		{
			return;
		}

		if (sender is TextBox replyTextBox)
		{
			model.ReplyMessage = replyTextBox.Text ?? string.Empty;
		}

		if (model.SendReplyCommand.CanExecute(null))
		{
			await model.SendReplyCommand.ExecuteAsync(null);
		}
	}

	private async void OnDeleteConversationClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is not ConversationDetailsViewModel model)
		{
			return;
		}

		var dialog = new ContentDialog
		{
			Title = model.DeleteDialogTitle,
			Content = model.DeleteDialogContent,
			PrimaryButtonText = model.DeleteDialogConfirm,
			CloseButtonText = model.DeleteDialogCancel,
			DefaultButton = ContentDialogButton.Close,
			XamlRoot = this.XamlRoot,
		};

		var result = await dialog.ShowAsync();
		if (result != ContentDialogResult.Primary)
		{
			return;
		}

		var deleted = await model.DeleteConversationAsync();
		var frame = Frame;
		if (deleted && frame is { CanGoBack: true })
		{
			frame.GoBack();
		}
	}
}
