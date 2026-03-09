using Microsoft.Extensions.AI;

namespace OldFashionedMaker.Services;

/// <summary>
/// Orchestrates the conversation between the user and the AI bartender.
/// Wraps IChatClient with function-calling middleware and manages chat history.
/// </summary>
public class ChatOrchestrator
{
    private readonly IChatClient? _client;
    private readonly List<ChatMessage> _history = [];
    private readonly bool _isAvailable;

    public bool IsAvailable => _isAvailable;
    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();

    public ChatOrchestrator(IChatClient? chatClient, BartenderTools tools)
    {
        if (chatClient is null)
        {
            _isAvailable = false;
            return;
        }

        // Wrap with auto function invocation
        _client = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation()
            .Build();

        _isAvailable = true;

        // System prompt — explicit about tool usage so the on-device model reliably calls them
        _history.Add(new ChatMessage(ChatRole.System, """
            You are an Old Fashioned cocktail bartender assistant with access to tools.
            You MUST use your tools to fulfill user requests. You CAN save, search, and retrieve drinks.

            Available tools and WHEN to use them:
            - SaveDrink: Use when the user describes a drink they made, wants to save/log a recipe, or says "save this". Fill in any details they mention; use sensible defaults for the rest.
            - GetHistory: Use when the user asks about past drinks, recent drinks, or their drink log.
            - SearchDrinks: Use when the user asks to find drinks by flavor or taste description.
            - GetStats: Use when the user asks about their preferences, stats, or patterns.

            IMPORTANT: Never say you "can't" save or modify drinks. You have tools that do exactly that.
            When in doubt, USE the tool rather than explaining that you can't help.
            Be friendly, concise, and use cocktail terminology naturally.
            """));

        // Register tools
        ChatOptions = new ChatOptions
        {
            Tools = [
                AIFunctionFactory.Create(tools.SaveDrink),
                AIFunctionFactory.Create(tools.GetHistory),
                AIFunctionFactory.Create(tools.SearchDrinks),
                AIFunctionFactory.Create(tools.GetStats),
            ]
        };
    }

    public ChatOptions? ChatOptions { get; }

    /// <summary>
    /// Send a message and get the complete response.
    /// </summary>
    public async Task<string> SendMessageAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return "AI is not available on this device.";

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        Console.WriteLine($"[Chat] Sending: {userMessage}");
        Console.WriteLine($"[Chat] History length: {_history.Count}, Tools: {ChatOptions?.Tools?.Count ?? 0}");

        var response = await _client.GetResponseAsync(_history, ChatOptions, cancellationToken);

        // Log tool usage for debugging
        foreach (var msg in response.Messages)
        {
            if (msg.Role == ChatRole.Assistant && msg.Contents.OfType<FunctionCallContent>().Any())
            {
                foreach (var fc in msg.Contents.OfType<FunctionCallContent>())
                    Console.WriteLine($"[Chat] Tool called: {fc.Name}({string.Join(", ", fc.Arguments?.Select(kv => $"{kv.Key}={kv.Value}") ?? [])})");
            }
        }

        _history.AddMessages(response);

        Console.WriteLine($"[Chat] Response: {response.Text?[..Math.Min(response.Text?.Length ?? 0, 100)]}...");

        return response.Text ?? "I didn't get a response. Please try again.";
    }

    public void ClearHistory()
    {
        // Keep system prompt, remove everything else
        if (_history.Count > 1)
            _history.RemoveRange(1, _history.Count - 1);
    }
}
