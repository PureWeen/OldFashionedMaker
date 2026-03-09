using OldFashionedMaker.Services;
using OldFashionedMaker.Views;

namespace OldFashionedMaker.Pages;

public partial class ChatPage : ContentPage
{
    private readonly ChatOrchestrator _orchestrator;
    private readonly DrinkService _drinkService;
    private readonly ISpeechService? _speechService;
    private bool _isSending;
    private bool _voiceMode;
    private CancellationTokenSource? _listenCts;

    public ChatPage(ChatOrchestrator orchestrator, DrinkService drinkService, ISpeechService? speechService = null)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _drinkService = drinkService;
        _speechService = speechService;

        MessageEntry.Completed += OnSendClicked;
        SendButton.Clicked += OnSendClicked;
        MicButton.Clicked += OnMicClicked;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (MessageStack.Children.Count == 0)
        {
            var welcome = new TextBubbleView(
                "Welcome! I'm your Old Fashioned bartender. 🥃\n\n" +
                "Tell me about a drink you made, ask for advice, " +
                "search your history by flavor, or just chat about cocktails.\n\n" +
                "Tap 🎙️ to go hands-free while you mix!",
                isUser: false);
            MessageStack.Children.Add(welcome);
            await ScrollToBottom();
        }
    }

    private async void OnMicClicked(object? sender, EventArgs e)
    {
        if (_speechService is null)
        {
            MessageStack.Children.Add(new TextBubbleView("⚠️ Speech not available on this device.", isUser: false));
            await ScrollToBottom();
            return;
        }

        if (_speechService.IsListening)
        {
            _listenCts?.Cancel();
            _speechService.StopListening();
            MicButton.Text = "🎙️";
            MicButton.BackgroundColor = Color.FromArgb("#4A3228");
            return;
        }

        // Start listening — enable voice mode so AI reads response aloud
        _voiceMode = true;
        MicButton.Text = "⏹️";
        MicButton.BackgroundColor = Color.FromArgb("#C0392B");
        _listenCts = new CancellationTokenSource();

        try
        {
            var result = await _speechService.ListenAsync(_listenCts.Token);

            MicButton.Text = "🎙️";
            MicButton.BackgroundColor = Color.FromArgb("#4A3228");

            if (!string.IsNullOrWhiteSpace(result))
            {
                MessageEntry.Text = result;
                OnSendClicked(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Speech] Listen error: {ex.Message}");
            MicButton.Text = "🎙️";
            MicButton.BackgroundColor = Color.FromArgb("#4A3228");
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var message = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message) || _isSending)
            return;

        _isSending = true;
        MessageEntry.Text = string.Empty;

        MessageStack.Children.Add(new TextBubbleView(message, isUser: true));

        var aiBubble = new TextBubbleView("🤔 Thinking...", isUser: false);
        MessageStack.Children.Add(aiBubble);

        if (!_orchestrator.IsAvailable)
        {
            aiBubble.SetText("⚠️ AI not available. Apple Intelligence requires iOS/macOS 26+.");
            _isSending = false;
            return;
        }

        // Repro for dotnet/maui#34394: await directly on main thread.
        // AppleIntelligenceChatClient.GetResponseAsync() deadlocks because the Swift
        // onComplete callback dispatches back to the (blocked) main dispatch queue.
        try
        {
            var responseText = await _orchestrator.SendMessageAsync(message);

            aiBubble.SetText(responseText);
            ShowNewDrinkCards();
            await ScrollToBottom();

            if (_voiceMode && _speechService is not null)
            {
                try { await _speechService.SpeakAsync(responseText); }
                catch (Exception ex) { Console.WriteLine($"[Speech] TTS error: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            aiBubble.SetText($"⚠️ Error: {ex.GetType().Name}\n{ex.Message}");
            await ScrollToBottom();
        }
        finally
        {
            _isSending = false;
        }
    }

    private int _lastKnownDrinkCount;

    private void ShowNewDrinkCards()
    {
        var drinks = _drinkService.GetAll();
        if (drinks.Count > _lastKnownDrinkCount)
        {
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
        await Task.Delay(50);
        await ChatScroll.ScrollToAsync(0, MessageStack.Height, animated: true);
    }
}
