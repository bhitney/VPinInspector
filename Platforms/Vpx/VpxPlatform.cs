using OpenMcdf;
using VPin.Inspector.Core.Model;
using VPin.Inspector.Core.Platforms;
using VPin.Inspector.Platforms.Vpx.Model;
using VPin.Inspector.Vpx; // reuse existing VpxCompoundFile.GetScript + ScriptAnalyzer

namespace VPin.Inspector.Platforms.Vpx;

/// <summary>
/// VPX platform adapter. Wraps the existing OpenMcdf-based reading so the core
/// only ever sees the neutral <see cref="PinballTable"/> abstraction.
/// </summary>
public sealed class VpxPlatform : IPinballPlatform
{
    public string Id => "vpx";

    public string DisplayName => "Visual Pinball X";

    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".vpx" };

    public bool CanHandle(string filePath) =>
        string.Equals(Path.GetExtension(filePath), ".vpx", StringComparison.OrdinalIgnoreCase);

    public PinballTable Load(string filePath)
    {
        var elements = new List<TableElement>();

        using (var root = RootStorage.OpenRead(filePath))
        {
            Storage gameStg = root.OpenStorage("GameStg");
            foreach (var entry in gameStg.EnumerateEntries())
            {
                if (entry.Type != EntryType.Stream ||
                    !entry.Name.StartsWith("GameItem", StringComparison.Ordinal))
                {
                    continue;
                }

                using CfbStream stream = gameStg.OpenStream(entry.Name);
                byte[] bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                elements.Add(VpxGameItem.Parse(entry.Name, bytes));
            }
        }

        string script = VpxCompoundFile.GetScript(filePath);
        string gameName = ScriptAnalyzer.ResolveGameName(
            script, Path.GetFileNameWithoutExtension(filePath));

        VpxCompoundFile.EmbeddedTableInfo info = VpxCompoundFile.GetTableInfo(filePath);

        return new VpxTable
        {
            FilePath = filePath,
            TableName = Path.GetFileName(filePath),
            ElementsList = elements,
            Script = script,
            GameName = gameName,
            EmbeddedTableName = info.TableName,
            EmbeddedAuthor = info.Author,
            EmbeddedFileVersion = info.Version,
        };
    }

    public PinballTable LoadShallow(string filePath) => new VpxTable
    {
        FilePath = filePath,
        TableName = Path.GetFileName(filePath),
        ElementsList = Array.Empty<TableElement>(),
        Script = null,
        GameName = null,
    };
}
