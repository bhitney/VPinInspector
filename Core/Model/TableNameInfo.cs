using System.Text.RegularExpressions;

namespace VPin.Inspector.Core.Model;

/// <summary>
/// The structured facts parsed out of a table's display/file name, e.g.
/// "Addams Family (Bally 1992) PUP.vpx" -> Name="Addams Family",
/// Manufacturer="Bally", Year=1992, IsPup=true.
///
/// Original (virtual-only) tables usually use "Original" as the manufacturer.
/// Names that don't follow the "Name (Manufacturer Year)" convention leave the
/// missing parts null and report <see cref="IsWellFormed"/> = false.
/// </summary>
public sealed record TableNameInfo(
    string Name,
    string? Manufacturer,
    int? Year,
    bool IsPup)
{
    /// <summary>An empty/unparsed value.</summary>
    public static readonly TableNameInfo Empty = new(string.Empty, null, null, false);

    /// <summary>
    /// True when the name matched the expected "Name (Manufacturer Year)" shape,
    /// i.e. name, manufacturer, and a four-digit year were all present.
    /// </summary>
    public bool IsWellFormed =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Manufacturer) &&
        Year is not null;
}

/// <summary>
/// Parses a table file/display name into a <see cref="TableNameInfo"/>. Kept in
/// Core so every platform and the online-database matching share one definition
/// of "well-formed".
/// </summary>
public static partial class TableNameParser
{
    // Matches each parenthesized group's inner text, e.g. "(Stern 2012)".
    [GeneratedRegex(
        @"\(\s*(?<content>[^)]*?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GroupRegex();

    // A group that ends in a four-digit year, e.g. "Stern 2012". The text before
    // the year is the manufacturer.
    [GeneratedRegex(
        @"^(?<manufacturer>.+?)\s+(?<year>\d{4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManufacturerYearRegex();

    // Detects a standalone "PUP" token anywhere (its own word).
    [GeneratedRegex(
        @"(?:^|\s|\()PUP(?:\s|\)|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PupMarkerRegex();

    /// <summary>
    /// Parses <paramref name="tableName"/> (with or without a file extension)
    /// into its component fields. Never throws; unparseable input yields the raw
    /// name with null manufacturer/year.
    ///
    /// The name is the text before the first parenthesized group. Among the
    /// parenthesized groups, the one ending in a four-digit year (e.g.
    /// "Stern 2012") wins as manufacturer + year, regardless of position — this
    /// lets a leading model group ("Let There Be Rock Limited Edition") and a
    /// trailing mod group ("Brian Mod") sit around it. When no group carries a
    /// year, the first group is taken as the manufacturer (year unknown).
    /// </summary>
    public static TableNameInfo Parse(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return TableNameInfo.Empty;
        }

        string stem = StripExtension(tableName.Trim());
        bool isPup = PupMarkerRegex().IsMatch(stem);

        MatchCollection groups = GroupRegex().Matches(stem);
        if (groups.Count == 0)
        {
            return new TableNameInfo(stem, null, null, isPup);
        }

        // Name is whatever precedes the first parenthesized group.
        string name = stem[..groups[0].Index].Trim();
        if (name.Length == 0)
        {
            name = stem;
        }

        // Prefer the group that carries a four-digit year.
        foreach (Match group in groups)
        {
            Match my = ManufacturerYearRegex().Match(group.Groups["content"].Value.Trim());
            if (my.Success)
            {
                return new TableNameInfo(
                    name,
                    my.Groups["manufacturer"].Value.Trim(),
                    int.Parse(my.Groups["year"].Value),
                    isPup);
            }
        }

        // No year anywhere: the first group is the manufacturer, year unknown.
        string firstGroup = groups[0].Groups["content"].Value.Trim();
        return new TableNameInfo(
            name,
            firstGroup.Length == 0 ? null : firstGroup,
            null,
            isPup);
    }

    private static string StripExtension(string value)
    {
        int lastDot = value.LastIndexOf('.');
        // Only treat a short trailing token as an extension (e.g. .vpx, .fpt).
        if (lastDot > 0 && lastDot >= value.Length - 5 && !value.AsSpan(lastDot + 1).Contains(' '))
        {
            return value[..lastDot];
        }

        return value;
    }
}
