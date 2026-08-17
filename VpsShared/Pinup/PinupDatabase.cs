using Microsoft.Data.Sqlite;

namespace VPin.Inspector.Vpx.Pinup;

/// <summary>A row from the PinUP Popper Emulators table (subset of columns).</summary>
public sealed record PinupEmulator(int EmuId, string EmuName, string DirGames, bool Visible);

/// <summary>A row from the PinUP Popper Games table (subset of columns).</summary>
public sealed record PinupGame(int EmuId, string GameName, string GameFileName, bool Visible);

/// <summary>
/// A game's identity used by the PinUP hygiene check: its display GameName plus
/// the descriptive Manufacturer, Year, and Version, as recorded in the PinUP
/// Popper Games table. Compared against the VPS puplookup.csv reference file.
/// <see cref="WebGameId"/> is the VPS WEBGameID when populated (mostly empty
/// today); it is the definitive match key when present.
/// </summary>
public sealed record PinupGameIdentity(
    int EmuId,
    string GameName,
    string GameFileName,
    string? Manufacturer,
    string? Year,
    string? Version,
    string? WebGameId,
    bool Visible)
{
    /// <summary>The game's author, when the Games table has an author column.</summary>
    public string? Author { get; init; }

    /// <summary>When the game record was last updated (DateUpdated), when present.</summary>
    public string? DateUpdated { get; init; }

    /// <summary>When the game file was last updated (DateFileUpdated), when present.</summary>
    public string? DateFileUpdated { get; init; }
}

/// <summary>
/// A <see cref="PinupGameIdentity"/> paired with the primary-key value of its row
/// in the PinUP Popper Games table, so a writer can target that exact row.
/// </summary>
public sealed record PinupGameIdentityKeyed(long GameKey, PinupGameIdentity Identity);

/// <summary>
/// A game's descriptive metadata as recorded in the PinUP Popper Games table:
/// ROM (the table's cGameName), Manufacturer, Year, and Version. Used to compare
/// against the values inferred from / embedded in each table file.
/// </summary>
public sealed record PinupGameMetadata(
    int EmuId,
    string GameFileName,
    string? Rom,
    string? Manufacturer,
    string? Year,
    string? Version,
    bool Visible);

/// <summary>
/// A game joined with the media directory Popper resolves for it: the emulator's
/// DirMedia when set, otherwise the GlobalSettings GlobalMediaDir fallback.
/// </summary>
public sealed record PinupGameMedia(int EmuId, string GameName, string GameFileName, string MediaDir, bool Visible);

/// <summary>
/// Read-only access to the PinUP Popper SQLite database (PUPDatabase.db).
/// </summary>
public sealed class PinupDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private PinupDatabase(SqliteConnection connection) => _connection = connection;

    /// <summary>
    /// Opens the database read-only. Throws when the file does not exist.
    /// </summary>
    public static PinupDatabase Open(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"PinUP database not found at '{databasePath}'.", databasePath);
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return new PinupDatabase(connection);
    }

    /// <summary>Returns all emulators.</summary>
    public IReadOnlyList<PinupEmulator> GetEmulators()
    {
        var list = new List<PinupEmulator>();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT EMUID, EmuName, DirGames, Visible FROM Emulators";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PinupEmulator(
                EmuId: reader.GetInt32(0),
                EmuName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                DirGames: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Visible: !reader.IsDBNull(3) && reader.GetInt32(3) != 0));
        }

        return list;
    }

    /// <summary>
    /// Returns the games belonging to the given emulator IDs.
    /// </summary>
    public IReadOnlyList<PinupGame> GetGames(IReadOnlyCollection<int> emulatorIds)
    {
        var list = new List<PinupGame>();
        if (emulatorIds.Count == 0)
        {
            return list;
        }

        // Build a parameterized IN clause.
        var paramNames = emulatorIds.Select((_, i) => "@e" + i).ToList();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            $"SELECT EMUID, GameName, GameFileName, Visible FROM Games " +
            $"WHERE EMUID IN ({string.Join(", ", paramNames)}) " +
            $"ORDER BY GameName ASC";

        int index = 0;
        foreach (int id in emulatorIds)
        {
            cmd.Parameters.AddWithValue(paramNames[index++], id);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PinupGame(
                EmuId: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                GameName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                GameFileName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Visible: !reader.IsDBNull(3) && reader.GetInt32(3) != 0));
        }

        return list;
    }

    /// <summary>
    /// Returns the games belonging to the given emulator IDs, each paired with the
    /// media directory Popper looks in: the emulator's DirMedia, falling back to
    /// the GlobalSettings GlobalMediaDir when the emulator has none.
    /// </summary>
    public IReadOnlyList<PinupGameMedia> GetGameMedia(IReadOnlyCollection<int> emulatorIds)
    {
        var list = new List<PinupGameMedia>();
        if (emulatorIds.Count == 0)
        {
            return list;
        }

        // Build a parameterized IN clause.
        var paramNames = emulatorIds.Select((_, i) => "@e" + i).ToList();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT g.EMUID, g.GameName, g.GameFileName, " +
            "COALESCE(e.DirMedia, (SELECT GlobalMediaDir FROM GlobalSettings LIMIT 1)) AS MediaDir, " +
            "g.Visible " +
            "FROM Games g " +
            "JOIN Emulators e ON e.EMUID = g.EMUID " +
            $"WHERE g.EMUID IN ({string.Join(", ", paramNames)}) " +
            "ORDER BY g.GameName ASC";

        int index = 0;
        foreach (int id in emulatorIds)
        {
            cmd.Parameters.AddWithValue(paramNames[index++], id);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PinupGameMedia(
                EmuId: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                GameName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                GameFileName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                MediaDir: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Visible: !reader.IsDBNull(4) && reader.GetInt32(4) != 0));
        }

        return list;
    }

    /// <summary>
    /// Returns the game identities (GameName + Manufacturer/Year/Version) for the
    /// given emulator IDs, used by the PinUP hygiene check to compare against the
    /// VPS puplookup.csv. String columns are trimmed; empty values become null.
    /// </summary>
    public IReadOnlyList<PinupGameIdentity> GetGameIdentities(IReadOnlyCollection<int> emulatorIds)
    {
        var list = new List<PinupGameIdentity>();
        if (emulatorIds.Count == 0)
        {
            return list;
        }

        var paramNames = emulatorIds.Select((_, i) => "@e" + i).ToList();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT EMUID, GameName, GameFileName, Manufact, GameYear, GAMEVER, WEBGameID, Visible FROM Games " +
            $"WHERE EMUID IN ({string.Join(", ", paramNames)}) " +
            "ORDER BY GameName ASC";

        int index = 0;
        foreach (int id in emulatorIds)
        {
            cmd.Parameters.AddWithValue(paramNames[index++], id);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PinupGameIdentity(
                EmuId: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                GameName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                GameFileName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Manufacturer: ReadTrimmed(reader, 3),
                Year: ReadTrimmed(reader, 4),
                Version: ReadTrimmed(reader, 5),
                WebGameId: ReadTrimmed(reader, 6),
                Visible: !reader.IsDBNull(7) && reader.GetInt32(7) != 0));
        }

        return list;
    }

    private static string? ReadTrimmed(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        // GameYear can be stored as an integer; read defensively as a string.
        string value = reader.GetFieldType(ordinal) == typeof(string)
            ? reader.GetString(ordinal)
            : reader.GetValue(ordinal)?.ToString() ?? string.Empty;

        value = value.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Returns descriptive metadata (ROM, Manufacturer, Year, Version) for the
    /// games belonging to the given emulator IDs. String columns are returned
    /// trimmed, with empty values normalized to null.
    /// </summary>
    public IReadOnlyList<PinupGameMetadata> GetGameMetadata(IReadOnlyCollection<int> emulatorIds)
    {
        var list = new List<PinupGameMetadata>();
        if (emulatorIds.Count == 0)
        {
            return list;
        }

        var paramNames = emulatorIds.Select((_, i) => "@e" + i).ToList();

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT EMUID, GameFileName, ROM, Manufact, GameYear, GAMEVER, Visible FROM Games " +
            $"WHERE EMUID IN ({string.Join(", ", paramNames)}) " +
            "ORDER BY GameName ASC";

        int index = 0;
        foreach (int id in emulatorIds)
        {
            cmd.Parameters.AddWithValue(paramNames[index++], id);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PinupGameMetadata(
                EmuId: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                GameFileName: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Rom: ReadTrimmed(reader, 2),
                Manufacturer: ReadTrimmed(reader, 3),
                Year: ReadTrimmed(reader, 4),
                Version: ReadTrimmed(reader, 5),
                Visible: !reader.IsDBNull(6) && reader.GetInt32(6) != 0));
        }

        return list;
    }

    /// <summary>
    /// Resolves the primary-key column of the Games table (typically GameID) by
    /// inspecting the schema. Falls back to "GameID" when no PK flag is found.
    /// </summary>
    public string ResolveGamesPrimaryKeyColumn()
    {
        return ResolveGamesPrimaryKeyColumn(_connection);
    }

    /// <summary>
    /// Resolves the Games-table primary key column for the given open connection.
    /// </summary>
    public static string ResolveGamesPrimaryKeyColumn(SqliteConnection connection)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('Games')";

        string? fallback = null;
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            // Columns: cid, name, type, notnull, dflt_value, pk
            string name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            long pk = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
            if (pk > 0)
            {
                return name;
            }

            if (fallback is null && string.Equals(name, "GameID", StringComparison.OrdinalIgnoreCase))
            {
                fallback = name;
            }
        }

        return fallback ?? "GameID";
    }

    /// <summary>
    /// Returns game identities for the given emulator IDs, each paired with the
    /// Games-table primary-key value so a writer can target the exact row.
    /// </summary>
    public IReadOnlyList<PinupGameIdentityKeyed> GetGameIdentitiesWithKey(IReadOnlyCollection<int> emulatorIds)
    {
        var list = new List<PinupGameIdentityKeyed>();
        if (emulatorIds.Count == 0)
        {
            return list;
        }

        string pkColumn = ResolveGamesPrimaryKeyColumn();
        string? authorColumn = ResolveGamesColumn("AUTHOR", "Author", "GameAuthor");
        string? dateUpdatedColumn = ResolveGamesColumn("DateUpdated");
        string? dateFileUpdatedColumn = ResolveGamesColumn("DateFileUpdated");
        var paramNames = emulatorIds.Select((_, i) => "@e" + i).ToList();

        var extraColumns = new List<string>();
        if (authorColumn is not null) extraColumns.Add(authorColumn);
        if (dateUpdatedColumn is not null) extraColumns.Add(dateUpdatedColumn);
        if (dateFileUpdatedColumn is not null) extraColumns.Add(dateFileUpdatedColumn);

        int authorOrdinal = authorColumn is null ? -1 : 9;
        int dateUpdatedOrdinal = dateUpdatedColumn is null ? -1 : 9 + (authorColumn is null ? 0 : 1);
        int dateFileUpdatedOrdinal = dateFileUpdatedColumn is null
            ? -1
            : 9 + (authorColumn is null ? 0 : 1) + (dateUpdatedColumn is null ? 0 : 1);

        string extraSelect = extraColumns.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", extraColumns.Select(c => $"\"{c}\""));

        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText =
            $"SELECT \"{pkColumn}\", EMUID, GameName, GameFileName, Manufact, GameYear, GAMEVER, WEBGameID, Visible{extraSelect} FROM Games " +
            $"WHERE EMUID IN ({string.Join(", ", paramNames)}) " +
            "ORDER BY GameName ASC";

        int index = 0;
        foreach (int id in emulatorIds)
        {
            cmd.Parameters.AddWithValue(paramNames[index++], id);
        }

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var identity = new PinupGameIdentity(
                EmuId: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                GameName: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                GameFileName: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Manufacturer: ReadTrimmed(reader, 4),
                Year: ReadTrimmed(reader, 5),
                Version: ReadTrimmed(reader, 6),
                WebGameId: ReadTrimmed(reader, 7),
                Visible: !reader.IsDBNull(8) && reader.GetInt32(8) != 0)
            {
                Author = authorOrdinal < 0 ? null : ReadTrimmed(reader, authorOrdinal),
                DateUpdated = dateUpdatedOrdinal < 0 ? null : ReadDate(reader, dateUpdatedOrdinal),
                DateFileUpdated = dateFileUpdatedOrdinal < 0 ? null : ReadDate(reader, dateFileUpdatedOrdinal),
            };

            long key = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
            list.Add(new PinupGameIdentityKeyed(key, identity));
        }

        return list;
    }

    /// <summary>
    /// Reads a date-ish column and formats it as a short date (no time). Accepts
    /// stored DateTime values, ISO strings, or unix seconds; returns the raw
    /// trimmed value when it cannot be parsed.
    /// </summary>
    private static string? ReadDate(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        object value = reader.GetValue(ordinal);

        if (value is DateTime dt)
        {
            return dt.ToShortDateString();
        }

        if (value is long or int or double)
        {
            double seconds = Convert.ToDouble(value);
            // Popper stores several date columns as unix seconds.
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime.ToShortDateString();
        }

        string raw = value.ToString()?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return null;
        }

        return DateTime.TryParse(raw, out DateTime parsed) ? parsed.ToShortDateString() : raw;
    }

    /// <summary>
    /// Returns the first of the given candidate column names that exists on the
    /// Games table (case-insensitive), or null when none are present.
    /// </summary>
    private string? ResolveGamesColumn(params string[] candidateNames)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('Games')";
            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(1))
                {
                    existing.Add(reader.GetString(1));
                }
            }
        }

        foreach (string name in candidateNames)
        {
            if (existing.Contains(name))
            {
                return name;
            }
        }

        return null;
    }

    public void Dispose() => _connection.Dispose();
}
