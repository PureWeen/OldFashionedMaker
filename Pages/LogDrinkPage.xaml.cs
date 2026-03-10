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
    private readonly IChatClient? _aiClient;
    private bool _isSending;
    private bool _isVisible;
    private CancellationTokenSource? _listenCts;
    private CancellationTokenSource? _silenceCts;

    // State machine for form walkthrough
    private enum FormField { Bourbon, Sweetener, Bitters, Garnish, Ice, Rating, Notes, Done }
    private FormField _currentField = FormField.Bourbon;

    private static readonly Dictionary<FormField, string> FieldQuestions = new()
    {
        [FormField.Bourbon] = "🥃 What bourbon are you using today?",
        [FormField.Sweetener] = "🍯 What sweetener? (simple syrup, demerara, maple, or say 'skip')",
        [FormField.Bitters] = "💧 What bitters? (Angostura, orange, Peychaud's, or 'skip')",
        [FormField.Garnish] = "🍊 Garnish? (orange peel, cherry, both, or 'skip')",
        [FormField.Ice] = "🧊 Ice type? (large cube, sphere, crushed, or 'skip')",
        [FormField.Rating] = "⭐ Rate this drink 1-5?",
        [FormField.Notes] = "📝 Any tasting notes? (or 'skip')",
    };

    public LogDrinkPage(DrinkService drinkService, VoiceState voiceState,
        IChatClient? chatClient = null, ISpeechService? speechService = null)
    {
        InitializeComponent();
        _drinkService = drinkService;
        _voiceState = voiceState;
        _speechService = speechService;

        if (chatClient is not null)
        {
            _aiClient = new ChatClientBuilder(chatClient).Build();
        }

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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;

        if (_voiceState.IsActive && _speechService is not null)
        {
            MicButton.Text = "⏹️";
            MicButton.BackgroundColor = Color.FromArgb("#C0392B");
        }

        if (_currentField == FormField.Bourbon)
        {
            _ = Task.Run(async () =>
            {
                await AskCurrentFieldAsync();
                if (_voiceState.IsActive && _isVisible && _speechService is not null)
                    MainThread.BeginInvokeOnMainThread(() => StartVoiceLoop());
            });
        }
        else if (_voiceState.IsActive && _speechService is not null)
        {
            StartVoiceLoop();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isVisible = false;
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

        while (_voiceState.IsActive && _isVisible)
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
                if (!_voiceState.IsActive || !_isVisible) break;

                if (!string.IsNullOrWhiteSpace(result))
                {
                    var text = result.TrimEnd();
                    if (text.EndsWith("send", StringComparison.OrdinalIgnoreCase))
                        text = text[..^4].TrimEnd();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _speechService.StopSpeaking();
                        ChatEntry.Text = string.Empty;
                        await HandleUserInputAsync(text);
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

    #region State Machine

    private async void OnChatSendClicked(object? sender, EventArgs e)
    {
        var message = ChatEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message) || _isSending)
            return;

        ChatEntry.Text = string.Empty;
        await HandleUserInputAsync(message);
    }

    private async Task HandleUserInputAsync(string message)
    {
        if (_isSending) return;

        if (DetectNavigation(message)) return;

        // Question — ask AI for advice, don't advance the form
        if (IsQuestion(message))
        {
            await AskAiQuestionAsync(message);
            return;
        }

        // Form field answer
        _isSending = true;
        try
        {
            ApplyFieldValue(message);
            _currentField = NextField(_currentField);

            if (_currentField == FormField.Done)
            {
                SaveDrink();
                ShowAiResponse("🥃 Drink saved! Say 'go back' or log another.");
                if (_voiceState.IsActive && _speechService is not null)
                {
                    try { await _speechService.SpeakAsync("Drink saved! Say go back to return, or we can log another."); }
                    catch { }
                }
                _currentField = FormField.Bourbon;
            }
            else
            {
                await AskCurrentFieldAsync();
            }
        }
        finally
        {
            _isSending = false;
        }
    }

    private async Task AskCurrentFieldAsync()
    {
        if (!FieldQuestions.TryGetValue(_currentField, out var question)) return;

        ShowAiResponse(question);

        // Speak the question — the calling voice loop will resume listening after this returns
        if (_voiceState.IsActive && _speechService is not null)
        {
            try { await _speechService.SpeakAsync(question); }
            catch { }
        }
    }

    private void ApplyFieldValue(string input)
    {
        var value = input.Trim();
        bool skip = value.Equals("skip", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("default", StringComparison.OrdinalIgnoreCase);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (_currentField)
            {
                case FormField.Bourbon:
                    if (!skip) BourbonEntry.Text = value;
                    break;
                case FormField.Sweetener:
                    if (!skip) SelectPickerItem(SugarPicker, value);
                    break;
                case FormField.Bitters:
                    if (!skip) SelectPickerItem(BittersPicker, value);
                    break;
                case FormField.Garnish:
                    if (!skip) SelectPickerItem(GarnishPicker, value);
                    break;
                case FormField.Ice:
                    if (!skip) SelectPickerItem(IcePicker, value);
                    break;
                case FormField.Rating:
                    if (!skip && int.TryParse(value.Replace("⭐", "").Trim(), out int r))
                    {
                        RatingSlider.Value = Math.Clamp(r, 1, 5);
                        RatingLabel.Text = new string('⭐', Math.Clamp(r, 1, 5));
                    }
                    break;
                case FormField.Notes:
                    if (!skip) NotesEditor.Text = value;
                    break;
            }
        });

        Console.WriteLine($"[LogForm] Set {_currentField} = {(skip ? "(default)" : value)}");
    }

    private static FormField NextField(FormField current) => current switch
    {
        FormField.Bourbon => FormField.Sweetener,
        FormField.Sweetener => FormField.Bitters,
        FormField.Bitters => FormField.Garnish,
        FormField.Garnish => FormField.Ice,
        FormField.Ice => FormField.Rating,
        FormField.Rating => FormField.Notes,
        FormField.Notes => FormField.Done,
        _ => FormField.Done,
    };

    private void SaveDrink()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var drink = new DrinkRecipe
            {
                Bourbon = BourbonEntry.Text?.Trim() ?? "Buffalo Trace",
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
            Console.WriteLine($"[LogForm] Saved: {drink.RecipeSummary}");

            BourbonEntry.Text = string.Empty;
            NotesEditor.Text = string.Empty;
            RatingSlider.Value = 3;
            RatingLabel.Text = "⭐⭐⭐";
        });
    }

    #endregion

    #region AI Questions

    private async Task AskAiQuestionAsync(string question)
    {
        if (_aiClient is null)
        {
            ShowAiResponse("⚠️ AI not available — but I can still log your drink!");
            return;
        }

        _isSending = true;
        ShowAiResponse("🤔 Thinking...");

        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, "You are an expert bartender. Answer concisely about bourbon, cocktails, techniques, ingredients. 2-3 sentences max."),
                new(ChatRole.User, question)
            };

            var response = await _aiClient.GetResponseAsync(messages);

            var text = string.Join("", response.Messages
                .Where(m => m.Role == ChatRole.Assistant)
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(tc => tc.Text)
                .Where(t => t is not null));

            if (string.IsNullOrWhiteSpace(text))
                text = "Hmm, I'm not sure about that. Let's keep going!";

            if (FieldQuestions.TryGetValue(_currentField, out var fieldQ))
                text = $"{text}\n\n{fieldQ}";

            ShowAiResponse(text);

            if (_voiceState.IsActive && _speechService is not null)
            {
                try { await _speechService.SpeakAsync(text); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LogForm] AI error: {ex.Message}");
            var fallback = FieldQuestions.GetValueOrDefault(_currentField, "");
            ShowAiResponse($"Sorry, couldn't get an answer. {fallback}");
        }
        finally
        {
            _isSending = false;
        }
    }

    #endregion

    #region Helpers

    private static bool IsQuestion(string text)
    {
        var lower = text.TrimStart().ToLowerInvariant();
        string[] questionWords = ["what", "why", "how", "which", "recommend", "suggest",
            "best", "good", "should", "would", "could", "tell me", "difference", "compare"];
        return lower.Contains('?') || questionWords.Any(q => lower.StartsWith(q) || lower.Contains($" {q} "));
    }

    private bool DetectNavigation(string message)
    {
        var lower = message.ToLowerInvariant();
        string[] backPhrases = ["go back", "go home", "back to chat", "main page",
            "return", "never mind", "nevermind", "cancel"];
        if (backPhrases.Any(p => lower.Contains(p)))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _listenCts?.Cancel();
                _speechService?.StopListening();
                _speechService?.StopSpeaking();
                await Shell.Current.GoToAsync("..");
            });
            return true;
        }
        return false;
    }

    private void ShowAiResponse(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AiResponseLabel.Text = text;
            AiResponseBorder.IsVisible = true;
        });
    }

    private static void SelectPickerItem(Picker picker, string value)
    {
        if (picker.ItemsSource is IList<string> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Contains(value, StringComparison.OrdinalIgnoreCase))
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

        SaveDrink();
        await DisplayAlertAsync("Saved! 🥃", "Logged your Old Fashioned.", "OK");
    }
}
