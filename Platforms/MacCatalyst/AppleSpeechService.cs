#if IOS || MACCATALYST
using AVFoundation;
using Speech;

namespace OldFashionedMaker.Services;

/// <summary>
/// Apple platform speech service using SFSpeechRecognizer (STT) and AVSpeechSynthesizer (TTS).
/// </summary>
public class AppleSpeechService : ISpeechService
{
    private SFSpeechRecognizer? _recognizer;
    private SFSpeechAudioBufferRecognitionRequest? _recognitionRequest;
    private SFSpeechRecognitionTask? _recognitionTask;
    private AVAudioEngine? _audioEngine;
    private AVSpeechSynthesizer? _synthesizer;

    public bool IsListening => _audioEngine?.Running == true;
    public bool IsSpeaking => _synthesizer?.Speaking == true;

    public async Task<string?> ListenAsync(CancellationToken cancellationToken = default)
    {
        var authStatus = await RequestSpeechAuthorizationAsync();
        if (authStatus != SFSpeechRecognizerAuthorizationStatus.Authorized)
        {
            Console.WriteLine($"[Speech] Authorization denied: {authStatus}");
            return null;
        }

        _recognizer = new SFSpeechRecognizer(Foundation.NSLocale.CurrentLocale);
        if (!_recognizer.Available)
        {
            Console.WriteLine("[Speech] Recognizer not available");
            return null;
        }

        _audioEngine = new AVAudioEngine();
        _recognitionRequest = new SFSpeechAudioBufferRecognitionRequest
        {
            ShouldReportPartialResults = true
        };

        var inputNode = _audioEngine.InputNode;
        var recordingFormat = inputNode.GetBusOutputFormat(0);

        inputNode.InstallTapOnBus(0, 1024, recordingFormat, (buffer, when) =>
        {
            _recognitionRequest?.Append(buffer);
        });

        var tcs = new TaskCompletionSource<string?>();

        // Wire up cancellation
        cancellationToken.Register(() =>
        {
            StopListening();
            tcs.TrySetResult(null);
        });

        string? bestTranscription = null;

        _recognitionTask = _recognizer.GetRecognitionTask(_recognitionRequest, (result, error) =>
        {
            if (result != null)
            {
                bestTranscription = result.BestTranscription.FormattedString;
                Console.WriteLine($"[Speech] Partial: {bestTranscription}");

                if (result.Final)
                {
                    StopListening();
                    tcs.TrySetResult(bestTranscription);
                }
            }

            if (error != null)
            {
                Console.WriteLine($"[Speech] Error: {error.LocalizedDescription}");
                StopListening();
                tcs.TrySetResult(bestTranscription);
            }
        });

        try
        {
            _audioEngine.Prepare();
            _audioEngine.StartAndReturnError(out var engineError);
            if (engineError != null)
            {
                Console.WriteLine($"[Speech] Audio engine error: {engineError.LocalizedDescription}");
                return null;
            }
            Console.WriteLine("[Speech] Listening...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Speech] Start error: {ex.Message}");
            return null;
        }

        return await tcs.Task;
    }

    public void StopListening()
    {
        try
        {
            _audioEngine?.Stop();
            _audioEngine?.InputNode?.RemoveTapOnBus(0);
            _recognitionRequest?.EndAudio();
            _recognitionTask?.Cancel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Speech] Stop error: {ex.Message}");
        }
        finally
        {
            _recognitionRequest = null;
            _recognitionTask = null;
            _audioEngine = null;
        }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        _synthesizer ??= new AVSpeechSynthesizer();

        // Strip markdown-style formatting for cleaner speech
        var cleanText = text
            .Replace("**", "")
            .Replace("##", "")
            .Replace("- ", "");

        var utterance = new AVSpeechUtterance(cleanText)
        {
            Voice = AVSpeechSynthesisVoice.FromLanguage("en-US"),
            Rate = 0.52f,
            PitchMultiplier = 1.0f,
            Volume = 1.0f
        };

        var tcs = new TaskCompletionSource();

        cancellationToken.Register(() =>
        {
            _synthesizer?.StopSpeaking(AVSpeechBoundary.Immediate);
            tcs.TrySetResult();
        });

        _synthesizer.DidFinishSpeechUtterance += Handler;

        _synthesizer.SpeakUtterance(utterance);
        await tcs.Task;

        _synthesizer.DidFinishSpeechUtterance -= Handler;

        void Handler(object? s, AVSpeechSynthesizerUteranceEventArgs e)
        {
            tcs.TrySetResult();
        }
    }

    public void StopSpeaking()
    {
        _synthesizer?.StopSpeaking(AVSpeechBoundary.Immediate);
    }

    private static Task<SFSpeechRecognizerAuthorizationStatus> RequestSpeechAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>();
        SFSpeechRecognizer.RequestAuthorization(status => tcs.TrySetResult(status));
        return tcs.Task;
    }
}
#endif
