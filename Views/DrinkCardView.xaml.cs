using OldFashionedMaker.Models;

namespace OldFashionedMaker.Views;

public partial class DrinkCardView : ContentView
{
    public DrinkCardView(DrinkRecipe drink)
    {
        InitializeComponent();
        TitleLabel.Text = $"🥃 {drink.Bourbon} Old Fashioned";
        RecipeLabel.Text = drink.RecipeSummary;
        RatingLabel.Text = new string('⭐', drink.Rating) + $" ({drink.Rating}/5)";

        if (!string.IsNullOrWhiteSpace(drink.TastingNotes))
        {
            NotesLabel.Text = drink.TastingNotes;
            NotesLabel.IsVisible = true;
        }
    }
}
