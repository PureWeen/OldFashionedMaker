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
    private CancellationTokenSource? _silenceCts;
    private CancellationTokenSource? _aiCts;
    private string? _lastPartial;

    public ChatPage(ChatOrchestrator orchestrator, DrinkService drinkService, ISpeechService? speechService = null)
    {
        InitializeComponent();
        _orchestrator = orchestrator;
        _drinkService = drinkService;
        _speechService = speechService;

        MessageEntry.Completed += OnSendClicked;
        SendButton.Clicked += OnSendClicked;
        MicButton.Clicked += OnMicClicked;
        HelpButton.Clicked += OnHelpClicked;
    }

    private async void OnHelpClicked(object? sender, EventArgs e)
    {
        var help = """
            🎙️ VOICE MODE
            • Tap 🎙️ to start hands-free mode
            • Just talk — text appears as you speak
            • Say "send" to send immediately
            • Pause 3 seconds to auto-send
            • Tap ⏹️ to exit voice mode
            • Speak while AI is talking to interrupt

            🥃 DRINKS
            • "Save my drink — Buffalo Trace, neat"
            • "I made one with Maker's Mark, cherry garnish, 4 stars"
            • "What are my stats?"
            • "Show me my recent drinks"
            • "Find my smoky drinks"

            👨‍🍳 GUIDED MODE
            • "Walk me through making an Old Fashioned"
            • "Guide me step by step"
            • Say "next" or "ready" to advance steps

            🗺️ NAVIGATION
            • "Show me my history"
            • "Let me search my drinks"
            • "I want to log a drink manually"
            • "Go back to chat"

            💬 GENERAL
            • Ask about cocktail techniques
            • Get advice on improving your drink
            • Chat about bourbon recommendations
            """;

        await DisplayAlertAsync("What can I do? 🥃", help, "Got it!");
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

        if (_voiceMode)
        {
            // Exit voice mode entirely
            _voiceMode = false;
            _silenceCts?.Cancel();
            _listenCts?.Cancel();
            _speechService.StopListening();
            _speechService.StopSpeaking();
            StopVoiceUI();
            return;
        }

        // Enter continuous voice mode — loop: listen → send → listen
        _voiceMode = true;
        MicButton.Text = "⏹️";
        MicButton.BackgroundColor = Color.FromArgb("#C0392B");

        while (_voiceMode)
        {
            MessageEntry.Text = string.Empty;
            MessageEntry.Placeholder = "Listening...";
            _listenCts = new CancellationTokenSource();

            try
            {
                var result = await _speechService.ListenAsync(
                    onPartialResult: OnSpeechPartial,
                    cancellationToken: _listenCts.Token);

                _silenceCts?.Cancel();

                if (!_voiceMode) break;

                if (!string.IsNullOrWhiteSpace(result))
                {
                    var text = StripSendCommand(result);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Cancel any in-flight AI response and TTS
                        _aiCts?.Cancel();
                        _speechService.StopSpeaking();

                        MessageEntry.Text = text;
                        OnSendClicked(this, EventArgs.Empty);
                    }
                }

                // Brief pause before restarting the recognizer
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Speech] Listen error: {ex.Message}");
                _silenceCts?.Cancel();
                // Delay before retry to avoid tight error loop
                await Task.Delay(1000);
            }
        }

        StopVoiceUI();
    }

    private void OnSpeechPartial(string partial)
    {
        MessageEntry.Text = partial;
        _lastPartial = partial;

        // "Send" spoken — stop listening and send immediately
        if (partial.TrimEnd().EndsWith("send", StringComparison.OrdinalIgnoreCase))
        {
            _listenCts?.Cancel();
            return;
        }

        // Reset 3-second silence timer on every new partial
        _silenceCts?.Cancel();
        _silenceCts = new CancellationTokenSource();
        var token = _silenceCts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(3), token).ContinueWith(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() => _listenCts?.Cancel());
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void StopVoiceUI()
    {
        MicButton.Text = "🎙️";
        MicButton.BackgroundColor = Color.FromArgb("#4A3228");
        MessageEntry.Placeholder = "Ask your bartender...";
    }

    private static string StripSendCommand(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.EndsWith("send", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^4].TrimEnd();
        return trimmed;
    }

    private void OnSendClicked(object? sender, EventArgs e)
    {
        var message = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return;

        // If an AI call is in-flight, cancel it so we can send the new message
        if (_isSending)
        {
            _aiCts?.Cancel();
            _speechService?.StopSpeaking();
            _isSending = false;
        }

        _isSending = true;
        MessageEntry.Text = string.Empty;

        MessageStack.Children.Add(new TextBubbleView(message, isUser: true));

        var aiBubble = new TextBubbleView("", isUser: false);
        MessageStack.Children.Add(aiBubble);

        if (!_orchestrator.IsAvailable)
        {
            aiBubble.SetText("⚠️ AI not available. Apple Intelligence requires iOS/macOS 26+.");
            _isSending = false;
            return;
        }

        var msg = message;
        var orchestrator = _orchestrator;
        var shouldSpeak = _voiceMode && _speechService is not null;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var aiToken = _aiCts.Token;

        new Thread(async () =>
        {
            try
            {
                var spokenUpTo = 0;
                var responseText = await orchestrator.SendMessageStreamingAsync(msg,
                    onTextUpdate: text =>
                    {
                        aiBubble.SetText(text);
                        _ = ScrollToBottom();

                        // In voice mode, speak completed sentences as they stream in
                        if (shouldSpeak && !aiToken.IsCancellationRequested)
                        {
                            var sentenceEnd = text.LastIndexOfAny(['.', '!', '?', '\n'], text.Length - 1, text.Length - spokenUpTo);
                            if (sentenceEnd > spokenUpTo)
                            {
                                var newSentence = text[spokenUpTo..(sentenceEnd + 1)].Trim();
                                if (newSentence.Length > 0)
                                {
                                    spokenUpTo = sentenceEnd + 1;
                                    _ = Task.Run(async () =>
                                    {
                                        try { await _speechService!.SpeakAsync(newSentence, aiToken); }
                                        catch { /* cancelled or error */ }
                                    });
                                }
                            }
                        }
                    },
                    cancellationToken: aiToken);

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ShowNewDrinkCards();
                    _isSending = false;
                    _ = ScrollToBottom();
                });

                // Speak any remaining text that didn't end with punctuation
                if (shouldSpeak && !aiToken.IsCancellationRequested && spokenUpTo < responseText.Length)
                {
                    var remaining = responseText[spokenUpTo..].Trim();
                    if (remaining.Length > 0)
                    {
                        try { await _speechService!.SpeakAsync(remaining, aiToken); }
                        catch { /* cancelled */ }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    aiBubble.SetText(aiBubble.GetText() is { Length: > 0 } partial ? partial + "\n\n⚠️ Interrupted" : "⚠️ Interrupted");
                    _isSending = false;
                    _ = ScrollToBottom();
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    aiBubble.SetText($"⚠️ Error: {ex.GetType().Name}\n{ex.Message}");
                    _isSending = false;
                    _ = ScrollToBottom();
                });
            }
        }) { IsBackground = true }.Start();
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
