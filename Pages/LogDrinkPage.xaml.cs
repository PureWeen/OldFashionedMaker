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
    private bool _isVisible;
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
                You are a bartender helping log an Old Fashioned and answering questions.
                IMPORTANT: If user asks a question (what, why, how, which, recommend, suggest, best, good),
                ANSWER the question FIRST with helpful advice, THEN ask the next form field.
                When logging: ask ONE field at a time, call FillDrinkForm after each answer.
                Order: bourbon → sweetener → bitters → garnish → ice → rating → notes.
                After ALL fields, call SaveCurrentDrink. "skip" keeps default.
                """));

            _chatOptions = new ChatOptions
            {
                Tools = [
                    AIFunctionFactory.Create(FillDrinkForm),
                    AIFunctionFactory.Create(SaveCurrentDrink),
                ]
            };
        }
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

        // Kick off the conversational workflow, then start voice loop
        if (_formClient is not null && _chatHistory.Count == 1)
        {
            _ = Task.Run(async () =>
            {
                await SendToAiAsync("I want to log a new Old Fashioned.");
                if (_voiceState.IsActive && _isVisible)
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
                        // Await so we don't listen again until AI responds and TTS finishes
                        await SendToAiAsync(text);
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

        // Handle navigation commands client-side
        if (DetectNavigation(message))
            return;

        if (_isSending) return;
        _isSending = true;
        ShowAiResponse("🤔 Thinking...");

        try
        {
            bool isQuestion = IsQuestion(message);
            _chatHistory.Add(new ChatMessage(ChatRole.User, message));
            Console.WriteLine($"[LogForm] Sending ({(isQuestion ? "question" : "form")}): {message}");

            // Trim history to avoid context overflow
            TrimHistory(maxUserMessages: 6);

            // For questions: send WITHOUT tools so AI answers instead of calling FillDrinkForm
            ChatOptions? options = isQuestion ? new ChatOptions() : _chatOptions;

            var response = await _formClient.GetResponseAsync(_chatHistory, options);
            _chatHistory.AddMessages(response);

            // Extract only the text content, filtering out null from tool-call messages
            var text = string.Join("", response.Messages
                .Where(m => m.Role == ChatRole.Assistant)
                .SelectMany(m => m.Contents)
                .OfType<TextContent>()
                .Select(tc => tc.Text)
                .Where(t => t is not null));

            // Detect tool-call-as-text (AI writes "FillDrinkForm(...)" instead of calling it)
            var toolCallResult = TryParseToolCallAsText(text);
            if (toolCallResult is not null)
                text = toolCallResult;

            if (string.IsNullOrWhiteSpace(text))
                text = "✅ Got it! What's next?";

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
            if (ex.Message.Contains("context window", StringComparison.OrdinalIgnoreCase))
            {
                // Context overflow — aggressively trim and retry
                TrimHistory(maxUserMessages: 2);
                ShowAiResponse("Let me try that again... What would you like to do?");
            }
            else
            {
                ShowAiResponse($"⚠️ {ex.Message}");
            }
        }
        finally
        {
            _isSending = false;
        }
    }

    /// <summary>
    /// If the AI outputs tool call as text instead of structured call, parse and execute it.
    /// Returns the display text, or null if no tool-call-as-text was detected.
    /// </summary>
    private string? TryParseToolCallAsText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Detect patterns like "FillDrinkForm(bourbon: "Jack Daniels")" or "FillDrinkForm(bourbon: "X", ...)"
        if (text.Contains("FillDrinkForm", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[LogForm] Detected tool-call-as-text, parsing: {text}");

            var args = ParseToolArgs(text);
            string bourbon = args.GetValueOrDefault("bourbon", "Buffalo Trace");
            double bourbonOz = double.TryParse(args.GetValueOrDefault("bourbonOz", ""), out var bOz) ? bOz : 2.0;
            string sugarType = args.GetValueOrDefault("sugarType", args.GetValueOrDefault("sweetener", "Simple Syrup"));
            double sugarAmount = double.TryParse(args.GetValueOrDefault("sugarAmount", ""), out var sAmt) ? sAmt : 0.25;
            string bittersType = args.GetValueOrDefault("bittersType", args.GetValueOrDefault("bitters", "Angostura"));
            int bittersDashes = int.TryParse(args.GetValueOrDefault("bittersDashes", args.GetValueOrDefault("dashes", "")), out var bd) ? bd : 2;
            string garnish = args.GetValueOrDefault("garnish", "Orange Peel");
            string iceType = args.GetValueOrDefault("iceType", args.GetValueOrDefault("ice", "Large Cube"));
            int stirTime = int.TryParse(args.GetValueOrDefault("stirTimeSeconds", args.GetValueOrDefault("stir", "")), out var st) ? st : 30;
            int rating = int.TryParse(args.GetValueOrDefault("rating", ""), out var r) ? r : 3;
            string notes = args.GetValueOrDefault("tastingNotes", args.GetValueOrDefault("notes", ""));

            FillDrinkForm(bourbon, bourbonOz, sugarType, sugarAmount, bittersType, bittersDashes, garnish, iceType, stirTime, rating, notes);

            return $"Got it — {bourbon}! What sweetener do you prefer?";
        }

        if (text.Contains("SaveCurrentDrink", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[LogForm] Detected SaveCurrentDrink as text, executing.");
            SaveCurrentDrink();
            return "🥃 Drink saved! Want to log another?";
        }

        return null;
    }

    private static Dictionary<string, string> ParseToolArgs(string text)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Match patterns like key: "value" or key: value
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text, @"(\w+)\s*:\s*""([^""]*)""|(\w+)\s*:\s*(\S+)");

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (m.Groups[1].Success)
                args[m.Groups[1].Value] = m.Groups[2].Value;
            else if (m.Groups[3].Success)
                args[m.Groups[3].Value] = m.Groups[4].Value.TrimEnd(',', ')');
        }

        return args;
    }

    /// <summary>Keep conversation short to avoid Apple Intelligence context overflow.</summary>
    private void TrimHistory(int maxUserMessages)
    {
        // Count user messages (skip system prompt at index 0)
        int userCount = _chatHistory.Count(m => m.Role == ChatRole.User);
        if (userCount <= maxUserMessages) return;

        // Keep system prompt + last N exchanges
        var system = _chatHistory.First(m => m.Role == ChatRole.System);
        var recent = _chatHistory.Skip(1).TakeLast(maxUserMessages * 2).ToList();
        _chatHistory.Clear();
        _chatHistory.Add(system);
        _chatHistory.AddRange(recent);
        Console.WriteLine($"[LogForm] Trimmed history to {_chatHistory.Count} messages");
    }

    private static bool IsQuestion(string text)
    {
        var lower = text.TrimStart().ToLowerInvariant();
        string[] questionWords = ["what", "why", "how", "which", "recommend", "suggest", "best", "good", "should", "would", "could", "tell me", "difference", "compare"];
        return lower.Contains('?') || questionWords.Any(q => lower.StartsWith(q) || lower.Contains($" {q} "));
    }

    private bool DetectNavigation(string message)
    {
        var lower = message.ToLowerInvariant();
        string[] backPhrases = ["go back", "go home", "back to chat", "main page", "return", "never mind", "nevermind", "cancel"];
        if (backPhrases.Any(p => lower.Contains(p)))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _voiceState.SetActive(false);
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

    [Description("Save the current drink. Call after all fields are filled.")]
    private string SaveCurrentDrink()
    {
        string summary = "";
        ManualResetEventSlim done = new();

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
            summary = $"Saved! {drink.RecipeSummary} ({drink.Rating}/5 ⭐)";

            BourbonEntry.Text = string.Empty;
            NotesEditor.Text = string.Empty;
            RatingSlider.Value = 3;
            RatingLabel.Text = "⭐⭐⭐";

            done.Set();
        });

        done.Wait(TimeSpan.FromSeconds(5));
        return summary.Length > 0 ? summary : "Drink saved!";
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
