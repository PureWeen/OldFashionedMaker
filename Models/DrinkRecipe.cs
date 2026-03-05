namespace OldFashionedMaker.Models;

public class DrinkRecipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Spirit
    public string Bourbon { get; set; } = string.Empty;
    public double BourbonOz { get; set; } = 2.0;

    // Sweetener
    public string SugarType { get; set; } = "Simple Syrup";
    public double SugarAmount { get; set; } = 0.25;

    // Bitters
    public string BittersType { get; set; } = "Angostura";
    public int BittersDashes { get; set; } = 2;

    // Garnish & Technique
    public string Garnish { get; set; } = "Orange Peel";
    public string IceType { get; set; } = "Large Cube";
    public int StirTimeSeconds { get; set; } = 30;

    // Rating & Notes
    public int Rating { get; set; } = 3;
    public string TastingNotes { get; set; } = string.Empty;
    public string AITastingNotes { get; set; } = string.Empty;

    public string FlavorProfile =>
        $"{Bourbon} bourbon, {SugarType} sweetener, {BittersType} bitters, {Garnish} garnish. {TastingNotes}";

    public string RecipeSummary =>
        $"{BourbonOz}oz {Bourbon}, {SugarAmount} {SugarType}, {BittersDashes} dashes {BittersType}, {Garnish}, {IceType} ice, stirred {StirTimeSeconds}s";
}
