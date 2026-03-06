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

        // System prompt
        _history.Add(new ChatMessage(ChatRole.System, """
            You are an expert Old Fashioned cocktail bartender and advisor. You help the user:
            - Log new drinks they've made (ask for details and use the SaveDrink tool)
            - Review their drink history (use GetHistory tool)
            - Search for drinks by flavor (use SearchDrinks tool)
            - Get stats about their preferences (use GetStats tool)
            - Give advice on improving their drinks based on their history
            
            Be friendly, knowledgeable, and concise. Use cocktail terminology naturally.
            When the user describes a drink they made, use SaveDrink to log it.
            When they ask about past drinks, use GetHistory or SearchDrinks.
            Always reference their actual data when giving advice.
            Use emoji sparingly but naturally. Keep responses conversational and brief.
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
    /// Send a message and stream the response back token-by-token.
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageStreamingAsync(string userMessage)
    {
        if (_client is null)
        {
            yield return "AI is not available on this device. Apple Intelligence requires iOS/macOS 26+.";
            yield break;
        }

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in _client.GetStreamingResponseAsync(_history, ChatOptions))
        {
            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }

        // Add response to history for multi-turn context
        _history.AddMessages(updates);
    }

    /// <summary>
    /// Send a message and get the complete response.
    /// </summary>
    public async Task<string> SendMessageAsync(string userMessage)
    {
        if (_client is null)
            return "AI is not available on this device. Apple Intelligence requires iOS/macOS 26+.";

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        var response = await _client.GetResponseAsync(_history, ChatOptions);
        _history.AddMessages(response);

        return response.Text ?? "I didn't get a response. Please try again.";
    }

    public void ClearHistory()
    {
        // Keep system prompt, remove everything else
        if (_history.Count > 1)
            _history.RemoveRange(1, _history.Count - 1);
    }
}
