using System.ComponentModel;
using Microsoft.Extensions.AI;
using OldFashionedMaker.Models;
using OldFashionedMaker.Services;

namespace OldFashionedMaker.Pages;

public partial class LogDrinkPage : ContentPage
{
    private readonly DrinkService _drinkService;
    private readonly ISpeechService? _speechService;
    private readonly VoiceState _voiceState;
    private readonly IChatClient? _formClient;
    private readonly List<ChatMessage> _chatHistory = [];
    private readonly ChatOptions? _chatOptions;
    private bool _isSending;
    private CancellationTokenSource? _listenCts;
    private CancellationTokenSource? _silenceCts;

    public LogDrinkPage(DrinkService drinkService, VoiceState voiceState,
        IChatClient? chatClient = null, ISpeechService? speechService = null)
    {
        InitializeComponent();
        _drinkService = drinkService;
        _voiceState = voiceState;
        _speechService = speechService;

        SugarPicker.SelectedIndex = 0;
        BittersPicker.SelectedIndex = 0;
        GarnishPicker.SelectedIndex = 0;
        IcePicker.SelectedIndex = 0;

        BourbonOzStepper.ValueChanged += (s, e) => BourbonOzLabel.Text = $"{e.NewValue} oz";
        SugarStepper.ValueChanged += (s, e) => SugarLabel.Text = $"{e.NewValue} oz";
        BittersStepper.ValueChanged += (s, e) => BittersLabel.Text = $"{(int)e.NewValue} dashes";
        StirStepper.ValueChanged += (s, e) => StirLabel.Text = $"{(int)e.NewValue} seconds";
        RatingSlider.ValueChanged += (s, e) =>
        {
            int rating = (int)Math.Round(e.NewValue);
            RatingLabel.Text = new string('⭐', rating);
        };

        ChatEntry.Completed += OnChatSendClicked;
        ChatSendButton.Clicked += OnChatSendClicked;
        MicButton.Clicked += OnMicClicked;

        if (chatClient is not null)
        {
            _formClient = new ChatClientBuilder(chatClient)
                .UseFunctionInvocation()
                .Build();

            _chatHistory.Add(new ChatMessage(ChatRole.System, """
                You help log Old Fashioned drinks by filling a form. Ask about ONE field at a time.
                After each answer, call FillDrinkForm, then ask about the next field.
                Order: bourbon → sweetener → bitters → garnish → ice → rating → notes.
                If the user gives multiple details, fill them all and skip ahead.
                Say "skip" keeps the default. After all fields, say "All set! Tap Save."
                Be very concise.
                """));

            _chatOptions = new ChatOptions
            {
                Tools = [
                    AIFunctionFactory.Create(FillDrinkForm),
                ]
            };
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Resume voice mode if globally active
        if (_voiceState.IsActive && _speechService is not null)
        {
            MicButton.Text = "⏹️";
            MicButton.BackgroundColor = Color.FromArgb("#C0392B");
            StartVoiceLoop();
        }

        // Kick off the conversational workflow
        if (_formClient is not null && _chatHistory.Count == 1)
            _ = SendToAiAsync("I want to log a new Old Fashioned.");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _listenCts?.Cancel();
        _silenceCts?.Cancel();
        _speechService?.StopListening();
    }

    #region Voice

    private async void OnMicClicked(object? sender, EventArgs e)
    {
        if (_speechService is null)
        {
            ShowAiResponse("⚠️ Speech not available on this device.");
            return;
        }

        if (_voiceState.IsActive)
        {
            _voiceState.SetActive(false);
            _silenceCts?.Cancel();
            _listenCts?.Cancel();
            _speechService.StopListening();
            _speechService.StopSpeaking();
            StopVoiceUI();
            return;
        }

        _voiceState.SetActive(true);
        MicButton.Text = "⏹️";
        MicButton.BackgroundColor = Color.FromArgb("#C0392B");
        StartVoiceLoop();
    }

    private async void StartVoiceLoop()
    {
        if (_speechService is null) return;

        while (_voiceState.IsActive)
        {
            ChatEntry.Text = string.Empty;
            ChatEntry.Placeholder = "Listening...";
            _listenCts = new CancellationTokenSource();

            try
            {
                var result = await _speechService.ListenAsync(
                    onPartialResult: partial =>
                    {
                        MainThread.BeginInvokeOnMainThread(() => ChatEntry.Text = partial);

                        if (partial.TrimEnd().EndsWith("send", StringComparison.OrdinalIgnoreCase))
                        {
                            _listenCts?.Cancel();
                            return;
                        }

                        _silenceCts?.Cancel();
                        _silenceCts = new CancellationTokenSource();
                        var token = _silenceCts.Token;
                        _ = Task.Delay(TimeSpan.FromSeconds(3), token).ContinueWith(_ =>
                        {
                            MainThread.BeginInvokeOnMainThread(() => _listenCts?.Cancel());
                        }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    },
                    cancellationToken: _listenCts.Token);

                _silenceCts?.Cancel();
                if (!_voiceState.IsActive) break;

                if (!string.IsNullOrWhiteSpace(result))
                {
                    var text = result.TrimEnd();
                    if (text.EndsWith("send", StringComparison.OrdinalIgnoreCase))
                        text = text[..^4].TrimEnd();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _speechService.StopSpeaking();
                        ChatEntry.Text = string.Empty;
                        _ = SendToAiAsync(text);
                    }
                }

                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LogForm] Listen error: {ex.Message}");
                _silenceCts?.Cancel();
                await Task.Delay(1000);
            }
        }

        StopVoiceUI();
    }

    private void StopVoiceUI()
    {
        MicButton.Text = "🎙️";
        MicButton.BackgroundColor = Color.FromArgb("#4A3228");
        ChatEntry.Placeholder = "Describe your drink...";
    }

    #endregion

    #region AI Chat

    private async void OnChatSendClicked(object? sender, EventArgs e)
    {
        var message = ChatEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message) || _isSending)
            return;

        ChatEntry.Text = string.Empty;
        await SendToAiAsync(message);
    }

    private async Task SendToAiAsync(string message)
    {
        if (_formClient is null)
        {
            ShowAiResponse("⚠️ AI not available on this device.");
            return;
        }

        if (_isSending) return;
        _isSending = true;
        ShowAiResponse("🤔 Thinking...");

        try
        {
            _chatHistory.Add(new ChatMessage(ChatRole.User, message));
            Console.WriteLine($"[LogForm] Sending: {message}");

            var response = await _formClient.GetResponseAsync(_chatHistory, _chatOptions);
            _chatHistory.AddMessages(response);

            // Extract only the text content, filtering out null from tool-call messages
            var text = string.Join("", response.Messages
                .Where(m => m.Role == ChatRole.Assistant)
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(tc => tc.Text)
                .Where(t => t is not null));

            if (string.IsNullOrWhiteSpace(text))
                text = "Done! Tap 💾 Save Drink when ready.";

            Console.WriteLine($"[LogForm] Response: {text}");
            ShowAiResponse(text);

            // Speak the response if voice mode is on
            if (_voiceState.IsActive && _speechService is not null)
            {
                try { await _speechService.SpeakAsync(text); }
                catch { /* cancelled */ }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LogForm] Error: {ex.Message}");
            ShowAiResponse($"⚠️ {ex.Message}");
        }
        finally
        {
            _isSending = false;
        }
    }

    private void ShowAiResponse(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AiResponseLabel.Text = text;
            AiResponseBorder.IsVisible = true;
        });
    }

    #endregion

    #region Form Tool

    [Description("Fill the drink form. Call after each user answer.")]
    private string FillDrinkForm(
        [Description("Bourbon name")] string bourbon = "Buffalo Trace",
        [Description("Oz (1-4)")] double bourbonOz = 2.0,
        [Description("Sugar type")] string sugarType = "Simple Syrup",
        [Description("Sugar oz (0-1)")] double sugarAmount = 0.25,
        [Description("Bitters type")] string bittersType = "Angostura",
        [Description("Dashes (1-6)")] int bittersDashes = 2,
        [Description("Garnish")] string garnish = "Orange Peel",
        [Description("Ice type")] string iceType = "Large Cube",
        [Description("Stir seconds")] int stirTimeSeconds = 30,
        [Description("Rating 1-5")] int rating = 3,
        [Description("Notes")] string tastingNotes = "")
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BourbonEntry.Text = bourbon;
            BourbonOzStepper.Value = Math.Clamp(bourbonOz, 1, 4);
            BourbonOzLabel.Text = $"{BourbonOzStepper.Value} oz";

            SelectPickerItem(SugarPicker, sugarType);
            SugarStepper.Value = Math.Clamp(sugarAmount, 0, 1);
            SugarLabel.Text = $"{SugarStepper.Value} oz";

            SelectPickerItem(BittersPicker, bittersType);
            BittersStepper.Value = Math.Clamp(bittersDashes, 1, 6);
            BittersLabel.Text = $"{(int)BittersStepper.Value} dashes";

            SelectPickerItem(GarnishPicker, garnish);
            SelectPickerItem(IcePicker, iceType);

            StirStepper.Value = Math.Clamp(stirTimeSeconds, 10, 90);
            StirLabel.Text = $"{(int)StirStepper.Value} seconds";

            RatingSlider.Value = Math.Clamp(rating, 1, 5);
            RatingLabel.Text = new string('⭐', Math.Clamp(rating, 1, 5));

            if (!string.IsNullOrWhiteSpace(tastingNotes))
                NotesEditor.Text = tastingNotes;
        });

        return $"Form updated: {bourbonOz}oz {bourbon}, {sugarAmount} {sugarType}, {bittersDashes} dashes {bittersType}, {garnish}, {iceType}, stirred {stirTimeSeconds}s, {rating}/5.";
    }

    private static void SelectPickerItem(Picker picker, string value)
    {
        if (picker.ItemsSource is IList<string> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    picker.SelectedIndex = i;
                    return;
                }
            }
        }
    }

    #endregion

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BourbonEntry.Text))
        {
            await DisplayAlertAsync("Missing Info", "Please enter a bourbon name.", "OK");
            return;
        }

        var drink = new DrinkRecipe
        {
            Bourbon = BourbonEntry.Text.Trim(),
            BourbonOz = BourbonOzStepper.Value,
            SugarType = SugarPicker.SelectedItem?.ToString() ?? "Simple Syrup",
            SugarAmount = SugarStepper.Value,
            BittersType = BittersPicker.SelectedItem?.ToString() ?? "Angostura",
            BittersDashes = (int)BittersStepper.Value,
            Garnish = GarnishPicker.SelectedItem?.ToString() ?? "Orange Peel",
            IceType = IcePicker.SelectedItem?.ToString() ?? "Large Cube",
            StirTimeSeconds = (int)StirStepper.Value,
            Rating = (int)Math.Round(RatingSlider.Value),
            TastingNotes = NotesEditor.Text?.Trim() ?? string.Empty,
        };

        _drinkService.Save(drink);

        await DisplayAlertAsync("Saved! 🥃", $"Logged your {drink.Bourbon} Old Fashioned.", "OK");

        BourbonEntry.Text = string.Empty;
        NotesEditor.Text = string.Empty;
        RatingSlider.Value = 3;
    }
}
