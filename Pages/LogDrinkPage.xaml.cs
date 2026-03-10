using System.ComponentModel;
using Microsoft.Extensions.AI;
using OldFashionedMaker.Models;
using OldFashionedMaker.Services;

namespace OldFashionedMaker.Pages;

public partial class LogDrinkPage : ContentPage
{
    private readonly DrinkService _drinkService;
    private readonly IChatClient? _formClient;
    private readonly List<ChatMessage> _chatHistory = [];
    private readonly ChatOptions? _chatOptions;
    private bool _isSending;

    public LogDrinkPage(DrinkService drinkService, IChatClient? chatClient = null)
    {
        InitializeComponent();
        _drinkService = drinkService;

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

        if (chatClient is not null)
        {
            _formClient = new ChatClientBuilder(chatClient)
                .UseFunctionInvocation()
                .Build();

            _chatHistory.Add(new ChatMessage(ChatRole.System, """
                You are a bartender assistant helping the user fill out a drink logging form.
                The form has fields for: bourbon name, bourbon oz, sugar type, sugar amount, bitters type, bitters dashes, garnish, ice type, stir time, rating (1-5), and tasting notes.
                When the user describes ANY drink details, you MUST call FillDrinkForm to set those values on the form.
                Use sensible Old Fashioned defaults for anything they don't specify.
                After filling, briefly list what you set. The user will tap Save when ready.
                If the user wants changes, call FillDrinkForm again with updated values.
                Be concise — one sentence.
                """));

            _chatOptions = new ChatOptions
            {
                Tools = [
                    AIFunctionFactory.Create(FillDrinkForm),
                ]
            };
        }
    }

    [Description("Fill the drink log form with the specified values. Call this whenever the user describes a drink or wants to change form values.")]
    private string FillDrinkForm(
        [Description("Bourbon/whiskey name")] string bourbon = "Buffalo Trace",
        [Description("Bourbon amount in oz (1-4)")] double bourbonOz = 2.0,
        [Description("Sweetener: Simple Syrup, Demerara Syrup, Sugar Cube, Rich Simple Syrup, Honey Syrup, Maple Syrup")] string sugarType = "Simple Syrup",
        [Description("Sweetener amount in oz (0-1)")] double sugarAmount = 0.25,
        [Description("Bitters: Angostura, Orange, Peychaud's, Walnut, Chocolate, Cherry")] string bittersType = "Angostura",
        [Description("Dashes of bitters (1-6)")] int bittersDashes = 2,
        [Description("Garnish: Orange Peel, Luxardo Cherry, Both, Lemon Twist, None")] string garnish = "Orange Peel",
        [Description("Ice: Large Cube, Ice Sphere, Regular Cubes, Crushed, Neat")] string iceType = "Large Cube",
        [Description("Stir time in seconds (10-90)")] int stirTimeSeconds = 30,
        [Description("Rating 1-5")] int rating = 3,
        [Description("Tasting notes")] string tastingNotes = "")
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

        return $"Form filled: {bourbonOz}oz {bourbon}, {sugarAmount} {sugarType}, {bittersDashes} dashes {bittersType}, {garnish}, {iceType}, stirred {stirTimeSeconds}s, {rating}/5 stars.";
    }

    [Description("Save the drink that's currently filled in the form. Call when the user says save, log it, or done.")]
    private string SaveCurrentDrink()
    {
        string summary = "";
        ManualResetEventSlim done = new();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var drink = new DrinkRecipe
            {
                Bourbon = BourbonEntry.Text?.Trim() ?? "Unknown",
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
            summary = $"Saved: {drink.RecipeSummary} ({drink.Rating}/5 ⭐)";

            // Reset form
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

    private async void OnChatSendClicked(object? sender, EventArgs e)
    {
        var message = ChatEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(message) || _isSending)
            return;

        if (_formClient is null)
        {
            ShowAiResponse("⚠️ AI not available on this device.");
            return;
        }

        _isSending = true;
        ChatEntry.Text = string.Empty;
        ShowAiResponse("🤔 Thinking...");

        try
        {
            _chatHistory.Add(new ChatMessage(ChatRole.User, message));

            Console.WriteLine($"[LogForm] Sending: {message}");

            var response = await _formClient.GetResponseAsync(_chatHistory, _chatOptions);
            _chatHistory.AddMessages(response);

            var text = response.Text ?? "Done!";
            Console.WriteLine($"[LogForm] Response: {text}");
            ShowAiResponse(text);
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

        // Reset form
        BourbonEntry.Text = string.Empty;
        NotesEditor.Text = string.Empty;
        RatingSlider.Value = 3;
    }
}
