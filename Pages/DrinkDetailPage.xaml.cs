using OldFashionedMaker.Models;
using OldFashionedMaker.Services;

namespace OldFashionedMaker.Pages;

[QueryProperty(nameof(DrinkId), "id")]
public partial class DrinkDetailPage : ContentPage
{
    private readonly DrinkService _drinkService;
    private readonly DrinkAIService _aiService;
    private DrinkRecipe? _drink;

    public string DrinkId { get; set; } = string.Empty;

    public DrinkDetailPage(DrinkService drinkService, DrinkAIService aiService)
    {
        InitializeComponent();
        _drinkService = drinkService;
        _aiService = aiService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadDrink();
    }

    private void LoadDrink()
    {
        _drink = _drinkService.GetById(DrinkId);
        if (_drink is null) return;

        TitleLabel.Text = $"🥃 {_drink.Bourbon} Old Fashioned";
        DateLabel.Text = _drink.CreatedAt.ToString("MMMM dd, yyyy h:mm tt");
        RecipeLabel.Text = _drink.RecipeSummary;
        RatingLabel.Text = new string('⭐', _drink.Rating) + $" ({_drink.Rating}/5)";
        NotesLabel.Text = string.IsNullOrWhiteSpace(_drink.TastingNotes)
            ? "No notes recorded"
            : _drink.TastingNotes;

        if (!string.IsNullOrWhiteSpace(_drink.AITastingNotes))
            AINotesLabel.Text = _drink.AITastingNotes;

        GenerateNotesBtn.IsEnabled = _aiService.IsChatAvailable;
        GetAdviceBtn.IsEnabled = _aiService.IsChatAvailable;
    }

    private async void OnGenerateNotesClicked(object? sender, EventArgs e)
    {
        if (_drink is null) return;

        GenerateNotesBtn.IsEnabled = false;
        GenerateNotesBtn.Text = "⏳ Generating...";
        AINotesLabel.Text = "Thinking...";

        try
        {
            var notes = await _aiService.GenerateTastingNotesAsync(_drink);
            AINotesLabel.Text = notes;
            _drink.AITastingNotes = notes;
            _drinkService.Save(_drink);
        }
        catch (Exception ex)
        {
            AINotesLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            GenerateNotesBtn.Text = "✨ Generate Tasting Notes";
            GenerateNotesBtn.IsEnabled = true;
        }
    }

    private async void OnGetAdviceClicked(object? sender, EventArgs e)
    {
        if (_drink is null) return;

        GetAdviceBtn.IsEnabled = false;
        GetAdviceBtn.Text = "⏳ Thinking...";
        AdviceLabel.Text = "Analyzing your drink history...";

        try
        {
            var history = _drinkService.GetAll();
            var advice = await _aiService.GetAdviceAsync(_drink, history);
            AdviceLabel.Text = advice;
        }
        catch (Exception ex)
        {
            AdviceLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            GetAdviceBtn.Text = "💡 Improve My Drink";
            GetAdviceBtn.IsEnabled = true;
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_drink is null) return;

        bool confirm = await DisplayAlertAsync("Delete?", "Remove this drink from your history?", "Delete", "Cancel");
        if (!confirm) return;

        _drinkService.Delete(_drink.Id);
        await Shell.Current.GoToAsync("..");
    }
}
