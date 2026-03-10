namespace OldFashionedMaker.Services;

/// <summary>
/// App-wide voice mode state. When voice is turned on in any page,
/// it stays on as you navigate — every chat bar picks it up.
/// </summary>
public class VoiceState
{
    public bool IsActive { get; set; }
    public event Action? Changed;

    public void SetActive(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        Changed?.Invoke();
    }
}
