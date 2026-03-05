using System.Collections.ObjectModel;
using OldFashionedMaker.Models;
using OldFashionedMaker.Services;

namespace OldFashionedMaker.Pages;

public partial class SearchPage : ContentPage
{
    private readonly DrinkService _drinkService;
    private readonly DrinkAIService _aiService;
    private readonly ObservableCollection<SearchResult> _results = [];

    public SearchPage(DrinkService drinkService, DrinkAIService aiService)
    {
        InitializeComponent();
        _drinkService = drinkService;
        _aiService = aiService;
        ResultsCollection.ItemsSource = _results;

        SearchEntry.Completed += (s, e) => OnSearchClicked(s, e);

        if (!_aiService.IsEmbeddingAvailable)
            StatusLabel.Text = "⚠️ Embedding search is not available on this device.";
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        var query = SearchEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        StatusLabel.Text = "🔍 Searching...";
        _results.Clear();

        try
        {
            var drinks = _drinkService.GetAll();
            if (drinks.Count == 0)
            {
                StatusLabel.Text = "No drinks logged yet. Log some drinks first!";
                return;
            }

            var results = await _aiService.SemanticSearchAsync(query, drinks);

            foreach (var (drink, score) in results.Take(10))
            {
                _results.Add(new SearchResult(drink, score));
            }

            StatusLabel.Text = $"Found {_results.Count} results for \"{query}\"";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Search error: {ex.Message}";
        }
    }

    private async void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is SearchResult result)
        {
            ResultsCollection.SelectedItem = null;
            await Shell.Current.GoToAsync($"detail?id={result.Drink.Id}");
        }
    }
}

public record SearchResult(DrinkRecipe Drink, double Score)
{
    public string ScoreText => $"{Score:P0} match";
}
