using System.ComponentModel;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

Console.WriteLine("=== OldFashionedMaker Pipeline Tests ===\n");

int passed = 0, failed = 0;

void Pass(string test) { Console.WriteLine($"  ✅ PASS: {test}"); passed++; }
void Fail(string test, string reason) { Console.WriteLine($"  ❌ FAIL: {test} — {reason}"); failed++; }

// --- Mock IChatClient that echoes back ---
var mockClient = new MockChatClient();

// TEST 1: Basic GetResponseAsync
Console.WriteLine("TEST 1: IChatClient.GetResponseAsync");
try
{
    var response = await mockClient.GetResponseAsync("Hello bartender");
    if (!string.IsNullOrWhiteSpace(response.Text))
        Pass($"Got response: '{response.Text}'");
    else
        Fail("GetResponseAsync", "Empty response");
}
catch (Exception ex) { Fail("GetResponseAsync", ex.Message); }

// TEST 2: GetStreamingResponseAsync
Console.WriteLine("\nTEST 2: IChatClient.GetStreamingResponseAsync");
try
{
    var tokens = new List<string>();
    await foreach (var update in mockClient.GetStreamingResponseAsync("Tell me about bourbon"))
    {
        if (!string.IsNullOrEmpty(update.Text))
            tokens.Add(update.Text);
    }
    if (tokens.Count > 0)
        Pass($"Got {tokens.Count} tokens: '{string.Join("", tokens)}'");
    else
        Fail("Streaming", "No tokens received");
}
catch (Exception ex) { Fail("Streaming", ex.Message); }

// TEST 3: Multi-turn conversation
Console.WriteLine("\nTEST 3: Multi-turn conversation");
try
{
    var history = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a bartender."),
        new(ChatRole.User, "What's in an Old Fashioned?"),
    };
    var response = await mockClient.GetResponseAsync(history);
    if (response.Text?.Contains("Old Fashioned") == true)
        Pass($"Multi-turn works: '{response.Text}'");
    else
        Fail("Multi-turn", $"Unexpected response: '{response.Text}'");
}
catch (Exception ex) { Fail("Multi-turn", ex.Message); }

// TEST 4: FunctionInvokingChatClient + tool calling
Console.WriteLine("\nTEST 4: Tool calling with FunctionInvokingChatClient");
try
{
    var toolCallClient = new MockToolCallingChatClient();
    var wrappedClient = new ChatClientBuilder(toolCallClient)
        .UseFunctionInvocation()
        .Build();

    [Description("Get the number of drinks logged")]
    static string GetDrinkCount() => "The user has logged 7 drinks.";

    var options = new ChatOptions
    {
        Tools = [AIFunctionFactory.Create(GetDrinkCount)]
    };

    var response = await wrappedClient.GetResponseAsync("How many drinks?", options);
    if (toolCallClient.ToolWasCalled)
        Pass($"Tool was called! Response: '{response.Text}'");
    else
        Fail("Tool calling", "FunctionInvokingChatClient did not invoke the tool");
}
catch (Exception ex) { Fail("Tool calling", ex.Message); }

// TEST 5: Mock IEmbeddingGenerator + cosine similarity
Console.WriteLine("\nTEST 5: Embedding generation + cosine similarity");
try
{
    var embedder = new MockEmbeddingGenerator();
    var texts = new[] { "smoky bourbon", "sweet and fruity", "smoky whiskey" };
    var embeddings = await embedder.GenerateAsync(texts);

    if (embeddings.Count != 3)
    {
        Fail("Embeddings", $"Expected 3, got {embeddings.Count}");
    }
    else
    {
        var sim01 = TensorPrimitives.CosineSimilarity(embeddings[0].Vector.Span, embeddings[1].Vector.Span);
        var sim02 = TensorPrimitives.CosineSimilarity(embeddings[0].Vector.Span, embeddings[2].Vector.Span);
        Pass($"3 embeddings generated (dim={embeddings[0].Vector.Length}), sim(0,1)={sim01:F3}, sim(0,2)={sim02:F3}");
    }
}
catch (Exception ex) { Fail("Embeddings", ex.Message); }

// TEST 6: Full pipeline — orchestrator-style flow
Console.WriteLine("\nTEST 6: Full orchestrator pipeline (chat + tools + streaming)");
try
{
    var toolCallClient = new MockToolCallingChatClient();
    var wrappedClient = new ChatClientBuilder(toolCallClient)
        .UseFunctionInvocation()
        .Build();

    [Description("Save a drink to the log")]
    static string SaveDrink(string bourbon = "Buffalo Trace", int rating = 3)
        => $"Saved: {bourbon} rated {rating}/5";

    var options = new ChatOptions
    {
        Tools = [AIFunctionFactory.Create(SaveDrink)]
    };

    var history = new List<ChatMessage>
    {
        new(ChatRole.System, "You are a bartender. Use SaveDrink to log drinks."),
        new(ChatRole.User, "I just made one with Maker's Mark, it was great, 5 stars"),
    };

    var response = await wrappedClient.GetResponseAsync(history, options);
    history.AddMessages(response);

    if (toolCallClient.ToolWasCalled && history.Count > 2)
        Pass($"Full pipeline works! Tool called, history has {history.Count} messages. Response: '{response.Text}'");
    else
        Fail("Full pipeline", $"Tool called: {toolCallClient.ToolWasCalled}, history: {history.Count}");
}
catch (Exception ex) { Fail("Full pipeline", ex.Message); }

Console.WriteLine($"\n=== Results: {passed} passed, {failed} failed ===");
return failed > 0 ? 1 : 0;

// ============== Mock Implementations ==============

class MockChatClient : IChatClient
{
    public ChatClientMetadata Metadata => new("MockChatClient");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = chatMessages.Last().Text ?? "";
        var responseText = lastMessage.Contains("Old Fashioned")
            ? "An Old Fashioned has bourbon, sugar, bitters, and an orange peel."
            : $"Mock response to: {lastMessage}";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var words = "Here is a streaming response about bourbon".Split(' ');
        foreach (var word in words)
        {
            await Task.Delay(10, cancellationToken);
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(word + " ")] };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

class MockToolCallingChatClient : IChatClient
{
    public bool ToolWasCalled { get; set; }
    public ChatClientMetadata Metadata => new("MockToolCallingChatClient");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = chatMessages.ToList();

        // Check if there's already a tool result in the history
        if (messages.Any(m => m.Contents.OfType<FunctionResultContent>().Any()))
        {
            ToolWasCalled = true;
            var result = messages.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).First();
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, $"Based on the tool: {result.Result}")));
        }

        // If tools are available, request a function call
        if (options?.Tools?.Count > 0)
        {
            var tool = options.Tools.OfType<AIFunction>().First();
            var callContent = new FunctionCallContent(
                callId: "call_1",
                name: tool.Name,
                arguments: tool.Name == "SaveDrink"
                    ? new Dictionary<string, object?> { ["bourbon"] = "Maker's Mark", ["rating"] = 5 }
                    : null);

            var msg = new ChatMessage(ChatRole.Assistant, [callContent]);
            return Task.FromResult(new ChatResponse(msg));
        }

        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "No tools available.")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("MockEmbeddingGenerator");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();
        var rng = new Random(42);
        foreach (var text in values)
        {
            // Create deterministic embeddings based on text hash
            var vec = new float[128];
            var hash = text.GetHashCode();
            for (int i = 0; i < vec.Length; i++)
                vec[i] = (float)Math.Sin(hash + i * 0.1);
            result.Add(new Embedding<float>(vec));
        }
        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
