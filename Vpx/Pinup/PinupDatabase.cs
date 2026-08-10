using Microsoft.Data.Sqlite;

namespace VPX_Inspector.Vpx.Pinup;

/// <summary>A row from the PinUP Popper Emulators table (subset of columns).</summary>
public sealed record PinupEmulator(int EmuId, string EmuName, string DirGames, bool Visible);

/// <summary>A row from the PinUP Popper Games table (subset of columns).</summary>
public sealed record PinupGame(int EmuId, string GameName, string GameFileName, bool Visible);

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

    public void Dispose() => _connection.Dispose();
}
