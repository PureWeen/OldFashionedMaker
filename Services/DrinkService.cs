using System.Text.Json;
using OldFashionedMaker.Models;

namespace OldFashionedMaker.Services;

public class DrinkService
{
    private readonly string _filePath;
    private List<DrinkRecipe> _drinks = [];

    public DrinkService()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "drinks.json");
        Load();
    }

    public IReadOnlyList<DrinkRecipe> GetAll() => _drinks.AsReadOnly();

    public DrinkRecipe? GetById(string id) => _drinks.FirstOrDefault(d => d.Id == id);

    public void Save(DrinkRecipe drink)
    {
        var existing = _drinks.FindIndex(d => d.Id == drink.Id);
        if (existing >= 0)
            _drinks[existing] = drink;
        else
            _drinks.Add(drink);
        Persist();
    }

    public void Delete(string id)
    {
        _drinks.RemoveAll(d => d.Id == id);
        Persist();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            _drinks = JsonSerializer.Deserialize<List<DrinkRecipe>>(json) ?? [];
        }
        catch
        {
            _drinks = [];
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_drinks, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
