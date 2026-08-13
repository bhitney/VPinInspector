using System.Text;

namespace VPX_Inspector.Vpx.Dof;

/// <summary>
/// Parses a DirectOutput Framework (DOF) configuration .ini file. For now only
/// the ROM column (the first field of each data row in the <c>[Config DOF]</c>
/// section) is extracted, so we can tell which games are configured for DOF.
/// The wider CSV grid (per-toy columns such as "Shaker") is intentionally left
/// for a later pass.
/// </summary>
public sealed class DofConfig
{
    /// <summary>The stable id used to enable/disable the DOF deep-analysis check.</summary>
    public const string CheckId = "dof-check";

    /// <summary>The default DOF config location when none is configured.</summary>
    public const string DefaultConfigPath = @"C:\DirectOutput\Config\directoutputconfig51.ini";

    private readonly HashSet<string> _roms;

    private DofConfig(string path, HashSet<string> roms)
    {
        Path = path;
        _roms = roms;
    }

    /// <summary>The file this config was loaded from.</summary>
    public string Path { get; }

    /// <summary>All ROM names configured for DOF (case-insensitive).</summary>
    public IReadOnlyCollection<string> Roms => _roms;

    /// <summary>
    /// True when the given ROM/cGameName has a DOF entry (case-insensitive).
    /// Matching is layered to avoid false-positive warnings:
    /// 1. Exact match.
    /// 2. Underscore fallback: VPINMAME/DOF uses an underscore suffix to denote a
    ///    mod of a base game, so "serioussam_bm" falls back to "serioussam".
    /// 3. Prefix match: DOF entries are often a catch-all for era variants, so a
    ///    key like "cheetah" is intended to cover "cheetah2", "cheetah3", etc.
    /// </summary>
    public bool HasRom(string rom)
    {
        if (string.IsNullOrWhiteSpace(rom))
        {
            return false;
        }

        string trimmed = rom.Trim();
        if (_roms.Contains(trimmed))
        {
            return true;
        }

        int underscore = trimmed.IndexOf('_');
        if (underscore > 0)
        {
            string baseName = trimmed[..underscore];
            if (_roms.Contains(baseName))
            {
                return true;
            }
        }

        // Prefix match: a DOF key that is a prefix of the cGameName covers all
        // variants (e.g. "cheetah" -> "cheetah2"). Requires the key to be a
        // strict, non-empty prefix so unrelated names don't collide.
        foreach (string key in _roms)
        {
            if (key.Length > 0
                && key.Length < trimmed.Length
                && trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the effective DOF config path: the supplied path when non-empty,
    /// otherwise the default path when it exists on disk. Returns null when neither
    /// is usable.
    /// </summary>
    public static string? ResolveConfigPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath.Trim();
        }

        return File.Exists(DefaultConfigPath) ? DefaultConfigPath : null;
    }

    /// <summary>
    /// Loads the DOF config from the given (already resolved) path, extracting the
    /// ROM set from the <c>[Config DOF]</c> section. Throws when the file cannot be
    /// read; returns a config with an empty ROM set when the section is absent.
    /// </summary>
    public static DofConfig LoadFromFile(string path)
    {
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        var roms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool inSection = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();

            // Section headers look like "[Config DOF]" / "[Colors DOF]".
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(
                    line, "[Config DOF]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection || line.Length == 0)
            {
                continue;
            }

            // The first line under the section is a '#'-prefixed column-header
            // comment; skip any comment lines.
            if (line.StartsWith('#'))
            {
                continue;
            }

            // Data row: the ROM is the first comma-separated field.
            int comma = line.IndexOf(',');
            string rom = (comma >= 0 ? line[..comma] : line).Trim();
            if (rom.Length > 0)
            {
                roms.Add(rom);
            }
        }

        return new DofConfig(path, roms);
    }
}
