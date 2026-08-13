namespace VPin.Inspector.Core.Model;

/// <summary>
/// Capability interface for elements that carry a built-in timer. Modeled as an
/// interface rather than base-class fields because not every platform (or even
/// every element within a platform) has the concept.
///
/// Cross-platform rules key off this interface: any element from any emulator
/// that implements it automatically participates.
/// </summary>
public interface ITimerElement
{
    /// <summary>Whether the element's timer is enabled.</summary>
    bool TimerEnabled { get; }

    /// <summary>Timer interval in milliseconds; negative when absent.</summary>
    int TimerIntervalMs { get; }

    /// <summary>Convenience: true when a non-negative interval is present.</summary>
    bool HasTimer => TimerIntervalMs >= 0;
}
