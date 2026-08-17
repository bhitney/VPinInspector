using Microsoft.Data.Sqlite;

namespace VPin.Inspector.Vpx.Pinup;

/// <summary>One applied WEBGameID change, recorded so it can be undone.</summary>
public sealed record WebGameIdChange(long GameKey, string GameName, string? OldValue, string NewValue);

/// <summary>
/// The outcome of applying a batch of WEBGameID writes.
/// </summary>
public sealed record WebGameIdWriteResult(
    string BackupPath,
    IReadOnlyList<WebGameIdChange> Applied);

/// <summary>
/// Read-write access to the PinUP Popper SQLite database, kept separate from the
/// read-only <see cref="PinupDatabase"/>. Populates the Games.WEBGameID column.
/// Safety: always backs up the database before the first write, targets rows by
/// primary key, and applies all updates inside a single transaction while
/// returning an undo log of old-&gt;new values.
/// </summary>
public sealed class PinupDatabaseWriter
{
    private readonly string _databasePath;

    public PinupDatabaseWriter(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        _databasePath = databasePath;
    }

    /// <summary>
    /// Creates a timestamped backup copy of the database next to it and returns
    /// the backup path.
    /// </summary>
    public string BackupDatabase()
    {
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException($"PinUP database not found at '{_databasePath}'.", _databasePath);
        }

        string dir = Path.GetDirectoryName(_databasePath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(_databasePath);
        string ext = Path.GetExtension(_databasePath);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string backupPath = Path.Combine(dir, $"{name}.backup-{stamp}{ext}");

        File.Copy(_databasePath, backupPath, overwrite: false);
        return backupPath;
    }

    /// <summary>
    /// Applies WEBGameID values (keyed by Games primary key). A backup is created
    /// first; all updates run in one transaction. Returns the backup path and the
    /// undo log of applied changes.
    /// </summary>
    /// <param name="changes">Map of Games primary-key value to the WEBGameID to write.</param>
    public WebGameIdWriteResult ApplyWebGameIds(IReadOnlyList<PendingWebGameIdWrite> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        string backupPath = BackupDatabase();
        var applied = new List<WebGameIdChange>();

        if (changes.Count == 0)
        {
            return new WebGameIdWriteResult(backupPath, applied);
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        string pkColumn = PinupDatabase.ResolveGamesPrimaryKeyColumn(connection);

        using SqliteTransaction transaction = connection.BeginTransaction();

        using SqliteCommand readCmd = connection.CreateCommand();
        readCmd.Transaction = transaction;
        readCmd.CommandText = $"SELECT WEBGameID FROM Games WHERE \"{pkColumn}\" = @pk";
        SqliteParameter readPk = readCmd.CreateParameter();
        readPk.ParameterName = "@pk";
        readCmd.Parameters.Add(readPk);

        using SqliteCommand updateCmd = connection.CreateCommand();
        updateCmd.Transaction = transaction;
        updateCmd.CommandText = $"UPDATE Games SET WEBGameID = @id WHERE \"{pkColumn}\" = @pk";
        SqliteParameter idParam = updateCmd.CreateParameter();
        idParam.ParameterName = "@id";
        updateCmd.Parameters.Add(idParam);
        SqliteParameter pkParam = updateCmd.CreateParameter();
        pkParam.ParameterName = "@pk";
        updateCmd.Parameters.Add(pkParam);

        foreach (PendingWebGameIdWrite change in changes)
        {
            readPk.Value = change.GameKey;
            object? existing = readCmd.ExecuteScalar();
            string? oldValue = existing is null or DBNull ? null : existing.ToString();

            idParam.Value = change.WebGameId;
            pkParam.Value = change.GameKey;
            updateCmd.ExecuteNonQuery();

            applied.Add(new WebGameIdChange(change.GameKey, change.GameName, oldValue, change.WebGameId));
        }

        transaction.Commit();
        return new WebGameIdWriteResult(backupPath, applied);
    }

    /// <summary>
    /// Reverts previously applied changes by restoring each row's old WEBGameID.
    /// </summary>
    public void UndoChanges(IReadOnlyList<WebGameIdChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        string pkColumn = PinupDatabase.ResolveGamesPrimaryKeyColumn(connection);

        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"UPDATE Games SET WEBGameID = @id WHERE \"{pkColumn}\" = @pk";
        SqliteParameter idParam = cmd.CreateParameter();
        idParam.ParameterName = "@id";
        cmd.Parameters.Add(idParam);
        SqliteParameter pkParam = cmd.CreateParameter();
        pkParam.ParameterName = "@pk";
        cmd.Parameters.Add(pkParam);

        foreach (WebGameIdChange change in changes)
        {
            idParam.Value = (object?)change.OldValue ?? DBNull.Value;
            pkParam.Value = change.GameKey;
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}

/// <summary>A single pending WEBGameID write targeting a Games row by primary key.</summary>
public sealed record PendingWebGameIdWrite(long GameKey, string GameName, string WebGameId);
