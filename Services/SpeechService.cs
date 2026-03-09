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
    /// <param name="onPartialResult">Called with each partial transcription as the user speaks.</param>
    /// <param name="cancellationToken">Cancellation token — when cancelled, returns the best result so far.</param>
    Task<string?> ListenAsync(Action<string>? onPartialResult = null, CancellationToken cancellationToken = default);

    /// <summary>Stop an in-progress listen.</summary>
    void StopListening();

    /// <summary>Speak the given text aloud.</summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Stop speaking.</summary>
    void StopSpeaking();
}
