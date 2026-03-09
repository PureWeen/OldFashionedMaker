using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MauiDevFlow.Agent;
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

		// Register embedding generator for tools
		if (embeddingGenerator is not null)
			builder.Services.AddSingleton(embeddingGenerator);

		// Bartender tools (AI-callable functions)
		builder.Services.AddSingleton(sp => new BartenderTools(
			sp.GetRequiredService<DrinkService>(),
			embeddingGenerator));

		// Chat orchestrator (manages conversation + function calling pipeline)
		builder.Services.AddSingleton(sp => new ChatOrchestrator(
			chatClient,
			sp.GetRequiredService<BartenderTools>()));

		// Speech service (voice chat — STT + TTS)
#if IOS || MACCATALYST
		builder.Services.AddSingleton<ISpeechService, AppleSpeechService>();
#endif

		// Pages
		builder.Services.AddTransient<ChatPage>();

#if DEBUG
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent();
#endif

		return builder.Build();
	}
}
