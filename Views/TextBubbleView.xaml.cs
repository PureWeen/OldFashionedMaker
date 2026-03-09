namespace OldFashionedMaker.Views;

public partial class TextBubbleView : ContentView
{
    public TextBubbleView(string message, bool isUser)
    {
        InitializeComponent();
        MessageLabel.Text = message;

        if (isUser)
        {
            Bubble.BackgroundColor = Color.FromArgb("#D4A574");
            MessageLabel.TextColor = Colors.White;
            Bubble.HorizontalOptions = LayoutOptions.End;
        }
        else
        {
            Bubble.BackgroundColor = Color.FromArgb("#F0E6D8");
            MessageLabel.TextColor = Color.FromArgb("#2C1810");
            Bubble.HorizontalOptions = LayoutOptions.Start;
        }
    }

    public void AppendText(string text)
    {
        MessageLabel.Text += text;
    }

    public void SetText(string text)
    {
        MessageLabel.Text = text;
    }

    public string? GetText()
    {
        return MessageLabel.Text;
    }
}
