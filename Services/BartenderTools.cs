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

    [Description("Navigate the app. Pages: history, search, log, detail, chat. Always use this when user wants to go somewhere.")]
    public string NavigateTo(
        [Description("Page: history, search, log, detail, chat")] string page,
        [Description("Drink ID for detail page")] string? drinkId = null)
    {
        var route = page.ToLowerInvariant() switch
        {
            "history" => "history",
            "search" => "search",
            "log" => "log",
            "detail" when !string.IsNullOrEmpty(drinkId) => $"detail?id={drinkId}",
            "detail" => null,
            "chat" => "..",
            _ => null,
        };

        if (route is null)
            return page == "detail"
                ? "I need a drink ID to show details. Try asking about your history first."
                : $"Unknown page '{page}'. Available: history, search, log, detail, chat.";

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.GoToAsync(route);
        });

        return page switch
        {
            "history" => "Navigating to your drink history. You can tap any drink to see its details.",
            "search" => "Navigating to flavor search. Type a flavor description to find matching drinks.",
            "log" => "Navigating to the drink log form. Fill in the details of your Old Fashioned.",
            "detail" => $"Showing the details for that drink.",
            "chat" => "Heading back to our chat.",
            _ => $"Navigating to {page}.",
        };
    }

    [Description("Start step-by-step guided drink making. Returns one step at a time.")]
    public string GuideMeDrink(
        [Description("Drink type, e.g. 'classic Old Fashioned'")] string drinkDescription = "classic Old Fashioned",
        [Description("Step number (1-based)")] int stepNumber = 1)
    {
        // Provide structured step data — the AI model will present it conversationally
        var steps = new Dictionary<string, string[]>
        {
            ["classic Old Fashioned"] =
            [
                "GATHER: You'll need bourbon (2oz), a sugar cube or 1/4oz simple syrup, 2 dashes Angostura bitters, an orange peel, and a large ice cube.",
                "MUDDLE/SWEETEN: Place the sugar cube in your rocks glass. Add 2 dashes of Angostura bitters directly onto the sugar. Add a small splash of water. Muddle until dissolved. (Skip muddling if using simple syrup — just pour it in with the bitters.)",
                "ADD BOURBON: Pour 2oz of your bourbon over the sweetener. Give it a gentle stir to combine.",
                "ICE: Add one large ice cube (or ice sphere) to the glass. Stir for about 30 seconds to chill and dilute slightly.",
                "GARNISH: Express an orange peel over the drink by holding it over the glass and giving it a firm squeeze. You should see the oils spray across the surface. Drop it in or rest it on the rim.",
                "ENJOY: Your Old Fashioned is ready! Take a sip and savor it. 🥃"
            ],
        };

        // Try to find a matching recipe, fall back to a generic prompt
        if (steps.TryGetValue(drinkDescription, out var recipeSteps) && stepNumber >= 1 && stepNumber <= recipeSteps.Length)
        {
            var total = recipeSteps.Length;
            var step = recipeSteps[stepNumber - 1];
            var isLast = stepNumber == total;
            return $"Step {stepNumber} of {total}: {step}" +
                   (isLast ? "\n\nThat's the last step! Would you like to save this drink to your log?" : "\n\nSay 'next' when you're ready for the next step.");
        }

        // For unknown drinks, return instructions for the AI to improvise
        return $"Please walk the user through making a {drinkDescription}, step {stepNumber}. " +
               "Give ONE short, clear instruction. End with 'Say next when ready.' if there are more steps, " +
               "or 'That's the last step!' if done. Include the step number and total.";
    }
}
