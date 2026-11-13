namespace Reforged.Formats;

/// <summary>Build-192: every H2O archive of an install's Archives folder, opened in place. Paths are the ones the
/// import's extracted/ folder uses, archive name first: "Models/Units/Good/Aragorn/G_Aragorn.lmd" is
/// Units\Good\Aragorn\G_Aragorn.lmd in Models.H2O. Either separator, any case.</summary>
public sealed class GameArchives : IDisposable
{
    private readonly Dictionary<string,H2OArchive> _byName = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<H2OArchive> Archives { get; }

    private GameArchives(List<H2OArchive> archives)
    {
        Archives = archives;
        foreach (var archive in archives) _byName[archive.Name] = archive;
    }

    /// <summary>Opens <paramref name="folder"/>/*.H2O (an install's Archives folder) in name order.</summary>
    public static GameArchives Open(string folder)
    {
        var archives = new List<H2OArchive>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f).Equals(".h2o",StringComparison.OrdinalIgnoreCase))
                         .Order(StringComparer.OrdinalIgnoreCase))
                archives.Add(new H2OArchive(file));
        }
        catch { foreach (var archive in archives) archive.Dispose(); throw; }
        return new(archives);
    }

    public static GameArchives OpenInstall(string install) => Open(Path.Combine(install,"Archives"));

    public H2OArchive? Archive(string name) => _byName.GetValueOrDefault(name);

    public bool TryGet(string path,out H2OArchive archive,out H2OEntry entry)
    {
        string normalized = H2OArchive.Normalize(path);
        int separator = normalized.IndexOf('\\');
        entry = null!;
        archive = separator > 0 ? Archive(normalized[..separator])! : null!;
        return archive is not null && archive.TryGet(normalized[(separator+1)..],out entry);
    }

    public bool Contains(string path) => TryGet(path,out _,out _);

    public byte[] Read(string path,bool verify = true) =>
        TryGet(path,out var archive,out var entry) ? archive.Read(entry,verify) : throw new FileNotFoundException($"No archive holds {path}",path);

    public void Dispose() { foreach (var archive in Archives) archive.Dispose(); }
}
