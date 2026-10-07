using System.Security.Cryptography;
using System.Text.RegularExpressions;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed class HitsoundResourcePlan
{
    private sealed record FileChange(string Path, byte[]? Before, byte[]? After);
    private readonly List<FileChange> changes = [];
    private string? deletionBackup;
    public static HitsoundResourcePlan Empty => new();
    public IReadOnlyList<string> DisplayNames => changes.Select(c => c.Path).ToArray();

    public static HitsoundResourcePlan Copy(MapDocument source, MapDocument result, MapDocument target, IEnumerable<MapDocument>? set = null, bool compensateTinyDroplets = true)
    {
        return CopyGroup(source, [(result, target)], set ?? [target], compensateTinyDroplets);
    }

    public static HitsoundResourcePlan CopyBatch(MapDocument source,
        IEnumerable<(MapDocument Result, MapDocument Target)> targets, IEnumerable<MapDocument> set, bool compensateTinyDroplets = true)
    {
        var plan = new HitsoundResourcePlan();
        foreach (var group in targets.GroupBy(t => Root(t.Target), StringComparer.OrdinalIgnoreCase))
            plan.changes.AddRange(CopyGroup(source, group.ToArray(), set, compensateTinyDroplets).changes);
        return plan;
    }

    private static HitsoundResourcePlan CopyGroup(MapDocument source,
        (MapDocument Result, MapDocument Target)[] targets, IEnumerable<MapDocument> set, bool compensateTinyDroplets)
    {
        var plan = new HitsoundResourcePlan();
        string destination = Root(targets[0].Target) ?? throw new InvalidOperationException(L.Get("copier.saveFirst"));
        SafePath(destination,".hitsound-root");
        var sourceEvents = OsuBeatmapWriter.Serialize(source, compensateTinyDroplets).PlayableObjects;
        var resolver = new HitsoundResolver(source, sourceEvents);
        var times = sourceEvents.Where(HitsoundCopier.Audible).Select(o=>o.TimeMs).Distinct().Order().ToArray();
        var matches = targets.Select(t => OsuBeatmapWriter.Serialize(t.Target, compensateTinyDroplets).PlayableObjects.Where(HitsoundCopier.Audible)
            .Where(o=>HitsoundCopier.NearestEvent(times,o.TimeMs)>=0).Select(o=>(o.SourceId,o.EventIndex)).ToHashSet()).ToArray();
        var wanted = targets.SelectMany((t,i) => t.Result.HitsoundOverrides
            .Where(o=>matches[i].Contains((o.SourceId,o.EventIndex))).Select(o => o.Sample)).ToHashSet();
        var used = sourceEvents.Where(HitsoundCopier.Audible).Where(o => wanted.Contains(resolver.Describe(o))).ToArray();
        string sourceRoot = Root(source) ?? throw new InvalidOperationException(L.Get("copier.saveFirst"));
        SafePath(sourceRoot,".hitsound-root");
        var files = used.SelectMany(resolver.Resolve).Select(s => s.FilePath).OfType<string>()
            .Where(p => Inside(sourceRoot, p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var indexRemap = new Dictionary<int, int>();
        var customRemap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var indexed = new Regex(@"^(normal|soft|drum)-(hitnormal|hitwhistle|hitfinish|hitclap|slidertick)(\d*)$", RegexOptions.IgnoreCase);
        var numbered = files.Select(p => (Path: p, Match: indexed.Match(Path.GetFileNameWithoutExtension(p))))
            .Where(p => p.Match.Success).GroupBy(p => p.Match.Groups[3].Value.Length == 0 ? 1 : int.Parse(p.Match.Groups[3].Value));
        var reserved = Directory.EnumerateFiles(destination).Select(p => indexed.Match(Path.GetFileNameWithoutExtension(p)))
            .Where(m => m.Success).Select(m => m.Groups[3].Value.Length == 0 ? 1 : int.Parse(m.Groups[3].Value)).ToHashSet();
        reserved.UnionWith(numbered.Select(g => g.Key));
        foreach(var document in set)
        {
            reserved.UnionWith(document.TimingPoints.Select(p=>p.SampleIndex));
            reserved.UnionWith(document.HitsoundOverrides.Select(o=>o.Sample.Index));
            foreach(string line in document.Fruits.Select(f=>f.OriginalLine).Concat(document.Tracks.Select(t=>t.OriginalLine))
                .Concat(document.ImportedSliders.Select(s=>s.OriginalLine)).OfType<string>())
            {
                var fields=line.Split(',');
                int field=fields.Length>3 && int.TryParse(fields[3],out var type) && (type&2)!=0 ? 10 : 5;
                var sample=fields.Length>field ? fields[field].Split(':') : [];
                if(sample.Length>2 && int.TryParse(sample[2],out int index)) reserved.Add(index);
            }
        }
        foreach (var group in numbered)
        {
            int index = group.Key;
            if (group.Any(p => Conflicts(Path.Combine(destination, Path.GetFileName(p.Path)), File.ReadAllBytes(p.Path))))
            {
                int next = 2; while (reserved.Contains(next)) next++;
                reserved.Add(next); indexRemap[index] = next; index = next;
            }
            foreach (var p in group)
            {
                string name = p.Match.Groups[1].Value + "-" + p.Match.Groups[2].Value + (index > 1 ? index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") + Path.GetExtension(p.Path);
                SafePath(sourceRoot,Path.GetRelativePath(sourceRoot,p.Path));
                plan.AddCopy(SafePath(destination, name), File.ReadAllBytes(p.Path));
            }
        }
        foreach (var item in used.Select(resolver.Describe).Where(s => s.FileName.Length > 0).Distinct())
        {
            if (Path.GetExtension(item.FileName).ToLowerInvariant() is not (".wav" or ".ogg" or ".mp3"))
                throw new InvalidDataException(L.Get("copier.audioOnly"));
            string from = SafePath(sourceRoot, item.FileName);
            if (!File.Exists(from)) throw new FileNotFoundException(L.Get("copier.missingFile", item.FileName));
            byte[] bytes = File.ReadAllBytes(from);
            string name = item.FileName;
            string to = SafePath(destination, name);
            if (Conflicts(to, bytes))
            {
                string hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
                name = Path.Combine("hitsounds", Path.GetFileNameWithoutExtension(name) + "-" + hash + Path.GetExtension(name)).Replace('\\','/');
                to = SafePath(destination, name);
                if (Conflicts(to, bytes)) throw new IOException(L.Get("copier.fileChanged", to));
            }
            customRemap[item.FileName] = name; plan.AddCopy(to, bytes);
        }
        for (int t = 0; t < targets.Length; t++)
        {
            var result = targets[t].Result; var matched = matches[t];
            foreach (var point in result.TimingPoints)
                if (indexRemap.TryGetValue(point.SampleIndex,out int newIndex)) { point.SampleIndex=newIndex; point.OriginalLine=null; }
            for (int i = 0; i < result.HitsoundOverrides.Count; i++)
            {
                var item = result.HitsoundOverrides[i];
                if (!matched.Contains((item.SourceId,item.EventIndex))) continue;
                int originalIndex = item.Sample.Index;
                result.HitsoundOverrides[i] = item with { Sample = item.Sample with {
                    Index = indexRemap.GetValueOrDefault(originalIndex, item.Sample.Index),
                    FileName = customRemap.GetValueOrDefault(item.Sample.FileName, item.Sample.FileName) } };
            }
        }
        return plan;
    }

    public static HitsoundResourcePlan Delete(MapDocument current, IEnumerable<MapDocument> set)
    {
        var plan = new HitsoundResourcePlan();
        string? root = Root(current);
        if (root is null) return plan;
        plan.deletionBackup = SafePath(root, Path.Combine(".hitsound-copier-backups", Guid.NewGuid().ToString("N")));
        var protectedFiles = set.Where(d => !ReferenceEquals(d, current)).SelectMany(Referenced)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in set)
            if (d.AudioPath is not null) protectedFiles.Add(Path.GetFullPath(d.AudioPath));
        foreach (var path in StoryboardSamples(current)) protectedFiles.Add(path);
        // Files may also be shared with difficulties not loaded into this project.
        foreach (string map in Directory.EnumerateFiles(root,"*.osu"))
        {
            if (string.Equals(Path.GetFullPath(map),current.SourcePath,StringComparison.OrdinalIgnoreCase)) continue;
            try { protectedFiles.UnionWith(Referenced(OsuBeatmapReader.ReadFile(map))); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            { return plan; }
        }
        foreach (string storyboard in Directory.EnumerateFiles(root,"*.osb"))
        {
            var context = new MapDocument { SourcePath = current.SourcePath ?? current.AudioPath };
            var section = new OsuSection { Name = "Events" }; section.Lines.AddRange(File.ReadAllLines(storyboard));
            context.OriginalSections.Add(section); protectedFiles.UnionWith(StoryboardSamples(context));
        }
        foreach (var path in Referenced(current).Where(p => Inside(root, p)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!protectedFiles.Contains(path)) { SafePath(root, Path.GetRelativePath(root,path)); plan.changes.Add(new(path, File.ReadAllBytes(path), null)); }
        return plan;
    }

    private static IEnumerable<string> Referenced(MapDocument document)
    {
        var events = OsuBeatmapWriter.Serialize(document).PlayableObjects;
        var resolver = new HitsoundResolver(document, events);
        string? root = Root(document);
        foreach (var p in events.Where(HitsoundCopier.Audible).SelectMany(resolver.Resolve).Select(s => s.FilePath).OfType<string>())
            if (root is not null && Inside(root,p)) yield return Path.GetFullPath(p);
        foreach (var path in StoryboardSamples(document)) yield return path;
    }
    private static IEnumerable<string> StoryboardSamples(MapDocument document)
    {
        string? root = Root(document);
        if (root is not null)
            foreach (string line in document.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines))
            {
                var p = line.Split(',');
                if (p.Length >= 4 && p[0].Trim() is "Sample" or "5")
                {
                    string path = SafePath(root, p[3].Trim().Trim('"'));
                    if (File.Exists(path)) yield return path;
                }
            }
    }

    public void Apply(IEnumerable<MapDocument>? consumers = null) => Transition(false, consumers);
    public void Restore(IEnumerable<MapDocument>? consumers = null) => Transition(true, consumers);
    private void Transition(bool restore, IEnumerable<MapDocument>? consumers)
    {
        var protectedFiles = consumers?.SelectMany(Referenced).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var change in changes)
        {
            if ((restore ? change.Before : change.After) is null && protectedFiles.Contains(change.Path)) continue;
            SafePath(Path.GetDirectoryName(change.Path)!,Path.GetFileName(change.Path));
            var expected = restore ? change.After : change.Before;
            var desired = restore ? change.Before : change.After;
            var actual = File.Exists(change.Path) ? File.ReadAllBytes(change.Path) : null;
            if (Equal(actual, desired)) continue;
            if (!Equal(actual, expected)) throw new IOException(L.Get("copier.fileChanged", change.Path));
        }
        if (!restore && deletionBackup is not null && changes.Count > 0)
        {
            for (int i = 0; i < changes.Count; i++)
                if (changes[i].Before is { } bytes) Put(SafePath(deletionBackup,$"{i}.bak"),bytes);
            AtomicFile.Write(SafePath(deletionBackup,"files.json"),System.Text.Json.JsonSerializer.Serialize(changes.Select(c=>c.Path)));
        }
        var completed = new List<FileChange>();
        try
        {
            foreach (var change in changes)
            {
                var desired = restore ? change.Before : change.After;
                if (desired is null && protectedFiles.Contains(change.Path)) continue;
                if (Equal(File.Exists(change.Path) ? File.ReadAllBytes(change.Path) : null,desired)) continue;
                Put(change.Path, desired); completed.Add(change);
            }
        }
        catch
        {
            foreach (var change in completed.AsEnumerable().Reverse()) Put(change.Path, restore ? change.After : change.Before);
            throw;
        }
    }
    private static void Put(string path, byte[]? bytes)
    {
        if (bytes is null) { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void AddCopy(string path, byte[] bytes)
    {
        SafePath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        if (changes.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        if (!Equal(before, bytes)) changes.Add(new(path, before, bytes));
    }
    private static bool Conflicts(string path, byte[] bytes) => File.Exists(path) && !Equal(File.ReadAllBytes(path), bytes);
    private static bool Equal(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static string? Root(MapDocument d) => Path.GetDirectoryName(d.SourcePath ?? d.AudioPath);
    private static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string SafePath(string root, string name)
    {
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!Inside(root, path)) throw new InvalidDataException(L.Get("copier.unsafePath"));
        for (string? check = path; check is not null; check = Path.GetDirectoryName(check))
            if ((File.Exists(check) || Directory.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(L.Get("copier.unsafePath"));
        return path;
    }
}
