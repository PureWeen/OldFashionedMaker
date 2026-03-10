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
            You are an Old Fashioned bartender assistant. You MUST use your tools for every request.
            
            CRITICAL RULES:
            - SaveDrink: when user describes/saves a drink
            - GetHistory: when user asks about past/recent drinks
            - SearchDrinks: when user searches by flavor
            - GetStats: when user asks about preferences/stats
            - GuideMeDrink: when user wants step-by-step instructions
            - NavigateTo: when user wants to go to a page (history/search/log/chat)
            
            ALWAYS call NavigateTo when user says anything about going somewhere, showing history,
            logging a drink, searching, or going back. Call the tool — don't just describe navigation.
            Be friendly and concise. Use cocktail terminology naturally.
            """));

        // Register tools
        ChatOptions = new ChatOptions
        {
            Tools = [
                AIFunctionFactory.Create(tools.SaveDrink),
                AIFunctionFactory.Create(tools.GetHistory),
                AIFunctionFactory.Create(tools.SearchDrinks),
                AIFunctionFactory.Create(tools.GetStats),
                AIFunctionFactory.Create(tools.GuideMeDrink),
                AIFunctionFactory.Create(tools.NavigateTo),
            ]
        };
    }

    public ChatOptions? ChatOptions { get; }

    /// <summary>
    /// Send a message and stream the response token-by-token.
    /// Calls onTextUpdate on each chunk so the UI can update incrementally.
    /// </summary>
    public async Task<string> SendMessageStreamingAsync(
        string userMessage,
        Action<string> onTextUpdate,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return "AI is not available on this device.";

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        Console.WriteLine($"[Chat] Streaming: {userMessage}");
        Console.WriteLine($"[Chat] History length: {_history.Count}, Tools: {ChatOptions?.Tools?.Count ?? 0}");

        var fullText = new System.Text.StringBuilder();

        await foreach (var update in _client.GetStreamingResponseAsync(_history, ChatOptions, cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                fullText.Append(text);
                MainThread.BeginInvokeOnMainThread(() => onTextUpdate(fullText.ToString()));
            }
        }

        var responseText = fullText.ToString();

        // Add the complete response to history
        _history.Add(new ChatMessage(ChatRole.Assistant, responseText));

        Console.WriteLine($"[Chat] Streamed: {responseText[..Math.Min(responseText.Length, 100)]}...");

        return responseText.Length > 0 ? responseText : "I didn't get a response. Please try again.";
    }

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
