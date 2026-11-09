using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Dustweave.Desktop;

internal sealed class FreeDistributionWindow : Window
{
    internal TextBox ConfirmationInput { get; } = new();
    internal Button ConfirmButton { get; } = new() { IsDefault = true, IsEnabled = false };
    internal TextBlock Feedback { get; } = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    internal bool Confirmed { get; private set; }

    internal FreeDistributionWindow(Window owner, DailyFreeDistributionAcknowledgement store)
    {
        var language = DailyLanguage.Current;
        string code = language.Code;
        string phrase = DailyFreeDistributionAcknowledgement.Phrase(code);
        Owner = owner; Width = 540; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        language.Bind(this, TitleProperty, "distribution.close_title");
        var body = new StackPanel { Margin = new Thickness(24, 16, 24, 24) };
        TextBlock Text(string key, bool bold = false, params object?[] args)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 12) };
            language.Bind(text, TextBlock.TextProperty, key, args); text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            body.Children.Add(text); return text;
        }
        Text("distribution.free_title", true);
        Text("distribution.refund");
        Text("distribution.local_confirmation");
        Text("distribution.confirm_prompt", false, phrase);
        language.Bind(ConfirmationInput, AutomationProperties.NameProperty, "distribution.confirm_prompt", phrase);
        body.Children.Add(ConfirmationInput);
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        Feedback.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(Feedback);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        language.Bind(cancel, ContentProperty, "common.cancel"); cancel.Click += (_, _) => Close();
        ConfirmButton.SetResourceReference(StyleProperty, "PrimaryButton");
        language.Bind(ConfirmButton, ContentProperty, "distribution.confirm_close");
        buttons.Children.Add(cancel); buttons.Children.Add(ConfirmButton); body.Children.Add(buttons);
        ConfirmationInput.TextChanged += (_, _) => {
            ConfirmButton.IsEnabled = DailyFreeDistributionAcknowledgement.Matches(ConfirmationInput.Text, code);
            Feedback.Visibility = Visibility.Collapsed;
        };
        ConfirmButton.Click += (_, _) => {
            try
            {
                store.Confirm(ConfirmationInput.Text, code);
                Confirmed = true; Close();
            }
            catch
            {
                language.Bind(Feedback, TextBlock.TextProperty, "distribution.save_failed");
                Feedback.Visibility = Visibility.Visible;
            }
        };
        Content = body; DailyDialogs.Prepare(this);
        Loaded += (_, _) => ConfirmationInput.Focus();
    }
}
