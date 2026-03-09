using OldFashionedMaker.Services;
using OldFashionedMaker.Views;

namespace OldFashionedMaker.Pages;

public partial class ChatPage : ContentPage
{
    private readonly ChatOrchestrator _orchestrator;
    private readonly DrinkService _drinkService;
    private bool _isSending;

    public ChatPage(ChatOrchestrator orchestrator, DrinkService drinkService)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _drinkService = drinkService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (MessageStack.Children.Count == 0)
        {
            // Welcome message
            var welcome = new TextBubbleView(
                "Welcome! I'm your Old Fashioned bartender. 🥃\n\n" +
                "Tell me about a drink you made, ask for advice, " +
                "search your history by flavor, or just chat about cocktails.\n\n" +
                "Try: \"I just made one with Buffalo Trace and it was too sweet\"",
                isUser: false);
            MessageStack.Children.Add(welcome);
            await ScrollToBottom();
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var message = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message) || _isSending)
            return;

        _isSending = true;
        MessageEntry.Text = string.Empty;

        // Add user bubble
        MessageStack.Children.Add(new TextBubbleView(message, isUser: true));
        await ScrollToBottom();

        // Add empty AI bubble for streaming
        var aiBubble = new TextBubbleView("", isUser: false);
        MessageStack.Children.Add(aiBubble);

        try
        {
            if (!_orchestrator.IsAvailable)
            {
                aiBubble.SetText("AI is not available on this device. " +
                    "Apple Intelligence requires iOS/macOS 26+.\n\n" +
                    "Make sure you're running on a device with Apple Intelligence support.");
            }
            else
            {
                aiBubble.SetText("🤔 Thinking...");
                bool firstToken = true;

                // Stream response token by token
                await foreach (var token in _orchestrator.SendMessageStreamingAsync(message))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (firstToken)
                        {
                            aiBubble.SetText(token);
                            firstToken = false;
                        }
                        else
                        {
                            aiBubble.AppendText(token);
                        }
                    });
                    await ScrollToBottom();
                }

                if (firstToken)
                {
                    // No tokens were yielded — the model returned nothing
                    aiBubble.SetText("No response from the model. The AI may still be loading.");
                }
            }

            // Check if any drinks were just saved and show a card
            ShowNewDrinkCards();
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                aiBubble.SetText($"⚠️ Error: {ex.Message}\n\n{ex.GetType().Name}");
            });
            System.Diagnostics.Debug.WriteLine($"Chat error: {ex}");
        }
        finally
        {
            _isSending = false;
            await ScrollToBottom();
        }
    }

    private int _lastKnownDrinkCount;

    private void ShowNewDrinkCards()
    {
        var drinks = _drinkService.GetAll();
        if (drinks.Count > _lastKnownDrinkCount && _lastKnownDrinkCount > 0)
        {
            // Show cards for newly added drinks
            var newDrinks = drinks
                .OrderByDescending(d => d.CreatedAt)
                .Take(drinks.Count - _lastKnownDrinkCount);

            foreach (var drink in newDrinks)
            {
                MessageStack.Children.Add(new DrinkCardView(drink));
            }
        }
        _lastKnownDrinkCount = drinks.Count;
    }

    private async Task ScrollToBottom()
    {
        await Task.Delay(50); // Let layout settle
        await ChatScroll.ScrollToAsync(0, MessageStack.Height, animated: true);
    }
}
