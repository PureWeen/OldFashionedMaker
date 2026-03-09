namespace OldFashionedMaker.Services;

/// <summary>
/// Cross-platform speech service interface.
/// Platform implementations use native APIs (Speech.framework + AVFoundation on Apple).
/// </summary>
public interface ISpeechService
{
    bool IsListening { get; }
    bool IsSpeaking { get; }

    /// <summary>Start listening and return the recognized text when done.</summary>
    Task<string?> ListenAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop an in-progress listen.</summary>
    void StopListening();

    /// <summary>Speak the given text aloud.</summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Stop speaking.</summary>
    void StopSpeaking();
}
