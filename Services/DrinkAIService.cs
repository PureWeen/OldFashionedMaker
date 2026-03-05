using System.Numerics.Tensors;
using Microsoft.Extensions.AI;
using OldFashionedMaker.Models;

namespace OldFashionedMaker.Services;

public class DrinkAIService
{
    private readonly IChatClient? _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;

    public DrinkAIService(
        IChatClient? chatClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        _chatClient = chatClient;
        _embeddingGenerator = embeddingGenerator;
    }

    public bool IsChatAvailable => _chatClient is not null;
    public bool IsEmbeddingAvailable => _embeddingGenerator is not null;

    public async Task<string> GetAdviceAsync(DrinkRecipe current, IReadOnlyList<DrinkRecipe> history)
    {
        if (_chatClient is null)
            return "AI chat is not available on this device.";

        var historyContext = history.Count > 0
            ? string.Join("\n", history.OrderByDescending(d => d.CreatedAt).Take(10)
                .Select(d => $"- {d.CreatedAt:MMM dd}: {d.RecipeSummary} → Rating: {d.Rating}/5. Notes: {d.TastingNotes}"))
            : "No previous drinks logged yet.";

        var prompt = $"""
            You are an expert cocktail advisor specializing in Old Fashioned cocktails.
            
            The user just made this Old Fashioned:
            {current.RecipeSummary}
            Rating: {current.Rating}/5
            Tasting notes: {current.TastingNotes}
            
            Their recent drink history:
            {historyContext}
            
            Based on their current drink and history, suggest 2-3 specific improvements.
            Be concise and practical. Reference their patterns and preferences.
            """;

        var response = await _chatClient.GetResponseAsync(prompt);
        return response.Text ?? "Unable to generate advice.";
    }

    public async Task<string> GenerateTastingNotesAsync(DrinkRecipe drink)
    {
        if (_chatClient is null)
            return "AI chat is not available on this device.";

        var prompt = $"""
            You are a cocktail sommelier. Generate brief, evocative tasting notes for this Old Fashioned:
            {drink.RecipeSummary}
            
            Describe the expected flavor profile in 2-3 sentences covering aroma, taste, and finish.
            Be specific about how the ingredients interact.
            """;

        var response = await _chatClient.GetResponseAsync(prompt);
        return response.Text ?? "Unable to generate tasting notes.";
    }

    public async Task<List<(DrinkRecipe Drink, double Score)>> SemanticSearchAsync(
        string query, IReadOnlyList<DrinkRecipe> drinks)
    {
        if (_embeddingGenerator is null || drinks.Count == 0)
            return [];

        var drinkTexts = drinks.Select(d => d.FlavorProfile).ToList();
        drinkTexts.Add(query);

        var embeddings = await _embeddingGenerator.GenerateAsync(drinkTexts);

        var queryEmbedding = embeddings[^1].Vector;
        var results = new List<(DrinkRecipe Drink, double Score)>();

        for (int i = 0; i < drinks.Count; i++)
        {
            var similarity = TensorPrimitives.CosineSimilarity(
                queryEmbedding.Span, embeddings[i].Vector.Span);
            results.Add((drinks[i], similarity));
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }
}
