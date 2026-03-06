using System.ComponentModel;
using System.Numerics.Tensors;
using Microsoft.Extensions.AI;
using OldFashionedMaker.Models;

namespace OldFashionedMaker.Services;

/// <summary>
/// Defines the AI-callable tool functions for the bartender agent.
/// Each method becomes an AIFunction the model can invoke mid-conversation.
/// </summary>
public class BartenderTools
{
    private readonly DrinkService _drinkService;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;

    public BartenderTools(
        DrinkService drinkService,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        _drinkService = drinkService;
        _embeddingGenerator = embeddingGenerator;
    }

    [Description("Save a new Old Fashioned recipe to the user's drink log. Returns the saved drink details.")]
    public string SaveDrink(
        [Description("Bourbon/whiskey name")] string bourbon = "Buffalo Trace",
        [Description("Bourbon amount in oz")] double bourbonOz = 2.0,
        [Description("Sweetener type: Simple Syrup, Demerara Syrup, Sugar Cube, Rich Simple Syrup, Honey Syrup, Maple Syrup")] string sugarType = "Simple Syrup",
        [Description("Sweetener amount in oz")] double sugarAmount = 0.25,
        [Description("Bitters type: Angostura, Orange, Peychaud's, Walnut, Chocolate, Cherry")] string bittersType = "Angostura",
        [Description("Number of dashes of bitters")] int bittersDashes = 2,
        [Description("Garnish: Orange Peel, Luxardo Cherry, Both, Lemon Twist, None")] string garnish = "Orange Peel",
        [Description("Ice type: Large Cube, Ice Sphere, Regular Cubes, Crushed, Neat")] string iceType = "Large Cube",
        [Description("Stir time in seconds")] int stirTimeSeconds = 30,
        [Description("Rating from 1-5")] int rating = 3,
        [Description("User's tasting notes")] string tastingNotes = "")
    {
        var drink = new DrinkRecipe
        {
            Bourbon = bourbon,
            BourbonOz = bourbonOz,
            SugarType = sugarType,
            SugarAmount = sugarAmount,
            BittersType = bittersType,
            BittersDashes = bittersDashes,
            Garnish = garnish,
            IceType = iceType,
            StirTimeSeconds = stirTimeSeconds,
            Rating = rating,
            TastingNotes = tastingNotes,
        };

        _drinkService.Save(drink);
        return $"Saved: {drink.RecipeSummary} (Rating: {rating}/5)";
    }

    [Description("Get the user's drink history, ordered by most recent first. Returns a summary of each drink.")]
    public string GetHistory(
        [Description("Maximum number of drinks to return")] int limit = 5)
    {
        var drinks = _drinkService.GetAll()
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit);

        var lines = drinks.Select(d =>
            $"- {d.CreatedAt:MMM dd h:mm tt}: {d.RecipeSummary} → {d.Rating}/5 ⭐ {(string.IsNullOrWhiteSpace(d.TastingNotes) ? "" : $"Notes: {d.TastingNotes}")}");

        var result = string.Join("\n", lines);
        return string.IsNullOrWhiteSpace(result) ? "No drinks logged yet." : result;
    }

    [Description("Search the user's drink history by flavor description using semantic similarity. Good for finding drinks by taste profile.")]
    public async Task<string> SearchDrinks(
        [Description("Flavor description to search for, e.g. 'smoky and not too sweet' or 'citrusy'")] string query,
        [Description("Maximum results to return")] int limit = 3)
    {
        if (_embeddingGenerator is null)
            return "Embedding search is not available on this device.";

        var drinks = _drinkService.GetAll();
        if (drinks.Count == 0)
            return "No drinks logged yet.";

        var drinkTexts = drinks.Select(d => d.FlavorProfile).ToList();
        drinkTexts.Add(query);

        var embeddings = await _embeddingGenerator.GenerateAsync(drinkTexts);
        var queryEmbedding = embeddings[^1].Vector;

        var results = drinks
            .Select((d, i) => (Drink: d, Score: TensorPrimitives.CosineSimilarity(
                queryEmbedding.Span, embeddings[i].Vector.Span)))
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .Select(r => $"- {r.Drink.Bourbon}: {r.Drink.RecipeSummary} ({r.Score:P0} match, {r.Drink.Rating}/5 ⭐)");

        return $"Drinks matching \"{query}\":\n{string.Join("\n", results)}";
    }

    [Description("Get stats about the user's drink preferences: favorite bourbons, average rating, total drinks logged.")]
    public string GetStats()
    {
        var drinks = _drinkService.GetAll();
        if (drinks.Count == 0)
            return "No drinks logged yet.";

        var avgRating = drinks.Average(d => d.Rating);
        var topBourbon = drinks.GroupBy(d => d.Bourbon)
            .OrderByDescending(g => g.Count())
            .First();
        var topRated = drinks.OrderByDescending(d => d.Rating).First();

        return $"""
            📊 Your Old Fashioned Stats:
            Total drinks: {drinks.Count}
            Average rating: {avgRating:F1}/5
            Most used bourbon: {topBourbon.Key} ({topBourbon.Count()} drinks)
            Highest rated: {topRated.Bourbon} ({topRated.Rating}/5) on {topRated.CreatedAt:MMM dd}
            """;
    }
}
