using OldFashionedMaker.Pages;

namespace OldFashionedMaker;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Register routes so the AI (or any code) can navigate to these pages
		Routing.RegisterRoute("history", typeof(HistoryPage));
		Routing.RegisterRoute("search", typeof(SearchPage));
		Routing.RegisterRoute("log", typeof(LogDrinkPage));
		Routing.RegisterRoute("detail", typeof(DrinkDetailPage));
	}
}
