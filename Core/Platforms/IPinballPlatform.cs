using VPin.Inspector.Core.Model;

namespace VPin.Inspector.Core.Platforms;

/// <summary>
/// A pluggable emulator/format. Implement this (plus <see cref="PinballTable"/>
/// and <see cref="TableElement"/>) to teach the inspector a new platform such as
/// Future Pinball. The core discovers platforms through registration and never
/// needs to know the concrete type.
///
/// Contract for a new-platform author:
///   1. Implement IPinballPlatform (Id, FileExtensions, CanHandle, Load).
///   2. Subclass PinballTable and TableElement for your format.
///   3. Register the platform. No core files change.
/// </summary>
public interface IPinballPlatform
{
    /// <summary>Stable id, e.g. "vpx" or "fpt".</summary>
    string Id { get; }

    /// <summary>Human-facing name, e.g. "Visual Pinball X".</summary>
    string DisplayName { get; }

    /// <summary>
    /// File extensions this platform owns, including the dot, lowercased
    /// (e.g. ".vpx"). Used for folder-scan discovery.
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Quick check of whether this platform can open the given file.</summary>
    bool CanHandle(string filePath);

    /// <summary>
    /// Parses the file into a platform-neutral <see cref="PinballTable"/>.
    /// Implementations should parse lazily/efficiently; the core caches the
    /// result per scan.
    /// </summary>
    PinballTable Load(string filePath);

    /// <summary>
    /// Produces a lightweight table carrying only cheap metadata (file path and
    /// name) without parsing the table body. Used when a scan selects only
    /// <see cref="Rules.AnalysisDepth.Quick"/> rules, so the expensive body parse
    /// is skipped. The default falls back to <see cref="Load"/>; platforms with a
    /// slow parse should override it.
    /// </summary>
    PinballTable LoadShallow(string filePath) => Load(filePath);
}
