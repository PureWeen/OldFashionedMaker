using System.Collections.ObjectModel;
using OldFashionedMaker.Models;
using OldFashionedMaker.Services;

namespace OldFashionedMaker.Pages;

public partial class HistoryPage : ContentPage
{
    private readonly DrinkService _drinkService;
    private readonly ObservableCollection<DrinkRecipe> _drinks = [];

    public HistoryPage(DrinkService drinkService)
    {
        InitializeComponent();
        _drinkService = drinkService;
        DrinksCollection.ItemsSource = _drinks;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshDrinks();
    }

    private void RefreshDrinks()
    {
        _drinks.Clear();
        foreach (var drink in _drinkService.GetAll().OrderByDescending(d => d.CreatedAt))
            _drinks.Add(drink);
    }

    private async void OnDrinkSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DrinkRecipe drink)
        {
            DrinksCollection.SelectedItem = null;
            await Shell.Current.GoToAsync($"detail?id={drink.Id}");
        }
    }
}
