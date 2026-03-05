using OldFashionedMaker.Pages;

namespace OldFashionedMaker;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		Routing.RegisterRoute("detail", typeof(DrinkDetailPage));
	}
}
