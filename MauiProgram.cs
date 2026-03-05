using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OldFashionedMaker.Pages;
using OldFashionedMaker.Services;

#pragma warning disable MAUIAI0001 // Experimental Essentials.AI APIs

namespace OldFashionedMaker;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Data services
		builder.Services.AddSingleton<DrinkService>();

		// AI services - Apple Intelligence (on-device)
		IChatClient? chatClient = null;
		IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null;

#if IOS || MACCATALYST
		if (OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26))
		{
			chatClient = new Microsoft.Maui.Essentials.AI.AppleIntelligenceChatClient();
			embeddingGenerator = new Microsoft.Maui.Essentials.AI.NLEmbeddingGenerator();
		}
#endif

		builder.Services.AddSingleton(new DrinkAIService(chatClient, embeddingGenerator));

		// Pages
		builder.Services.AddTransient<LogDrinkPage>();
		builder.Services.AddTransient<HistoryPage>();
		builder.Services.AddTransient<DrinkDetailPage>();
		builder.Services.AddTransient<SearchPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
