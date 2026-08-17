using System.Net.Http;

namespace VPin.Inspector.Vps;

/// <summary>
/// Downloads the Virtual Pinball Spreadsheet (VPS) reference data files and
/// stores them alongside the application so future rules can use them as a
/// reference source. Currently fetches:
/// <list type="bullet">
/// <item><c>puplookup.csv</c> — PinUP Popper name lookup table.</item>
/// <item><c>vpsdb.json</c> — the full VPS database.</item>
/// </list>
/// </summary>
public sealed class VpsDownloader
{
    /// <summary>Base URL for the published VPS database files.</summary>
    public const string BaseUrl = "https://virtualpinballspreadsheet.github.io/vps-db/db/";

    /// <summary>The VPS files this downloader retrieves (remote name = local name).</summary>
    public static readonly IReadOnlyList<string> FileNames = new[]
    {
        "puplookup.csv",
        "vpsdb.json",
    };

    private readonly string _targetDirectory;

    /// <summary>
    /// Creates a downloader that writes into <paramref name="targetDirectory"/>,
    /// defaulting to the application directory when null.
    /// </summary>
    public VpsDownloader(string? targetDirectory = null)
    {
        _targetDirectory = targetDirectory ?? AppContext.BaseDirectory;
    }

    /// <summary>The directory the VPS files are written to.</summary>
    public string TargetDirectory => _targetDirectory;

    /// <summary>Full local path a given VPS file is (or will be) stored at.</summary>
    public string GetLocalPath(string fileName) => Path.Combine(_targetDirectory, fileName);

    /// <summary>
    /// Downloads all VPS files, reporting progress per file. Existing files are
    /// overwritten. Returns the local paths written.
    /// </summary>
    public async Task<IReadOnlyList<string>> DownloadAllAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_targetDirectory);

        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);

        var written = new List<string>();
        foreach (string fileName in FileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url = BaseUrl + fileName;
            string destination = GetLocalPath(fileName);

            progress?.Report($"Downloading {fileName}...");

            using HttpResponseMessage response =
                await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            string tempPath = destination + ".tmp";
            await using (FileStream file = File.Create(tempPath))
            await using (Stream content = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(tempPath, destination, overwrite: true);

            long size = new FileInfo(destination).Length;
            progress?.Report($"Saved {fileName} ({size:N0} bytes).");
            written.Add(destination);
        }

        return written;
    }
}
