using OldFashionedMaker.Models;
using OldFashionedMaker.Services;

namespace OldFashionedMaker.Pages;

public partial class LogDrinkPage : ContentPage
{
    private readonly DrinkService _drinkService;

    public LogDrinkPage(DrinkService drinkService)
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
