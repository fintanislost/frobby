using System.IO;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>
/// Bridges the repo's <c>tests/fixtures/&lt;name&gt;/save/</c> with SDV's save directory
/// (<c>Constants.SavesPath</c>). Stage runs before launching SDV; Capture runs after
/// <c>fixture.save</c> succeeds to pull the newly-saved game state back into the repo.
/// </summary>
public static class FixtureStager
{
    /// <summary>
    /// Copy <c>fixturesRoot/name/save/</c> → <c>sdvSavesDir/name/</c> (delete-and-replace).
    /// Called by RunCommand for each scenario's fixture, and by FixtureBuilder for the base.
    /// </summary>
    public static void Stage(string name, string fixturesRoot, string sdvSavesDir)
    {
        var src = Path.Combine(fixturesRoot, name, "save");
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException(
                $"fixture save directory not found: {src}");

        var dst = Path.Combine(sdvSavesDir, name);
        if (Directory.Exists(dst))
            Directory.Delete(dst, recursive: true);
        CopyRecursive(src, dst);
    }

    /// <summary>
    /// Copy <c>sdvSavesDir/name/</c> → <c>fixturesRoot/name/save/</c> (delete-and-replace).
    /// Called by FixtureBuilder after the harness's <c>fixture.save</c> completes, when
    /// the SDV folder happens to match the target fixture name.
    /// </summary>
    public static void Capture(string name, string sdvSavesDir, string fixturesRoot)
    {
        var src = Path.Combine(sdvSavesDir, name);
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException(
                $"SDV save directory not found — did fixture.save complete? Expected: {src}");
        CaptureFromPath(src, name, fixturesRoot);
    }

    /// <summary>
    /// Copy an arbitrary SDV save directory into the repo under the new fixture's name.
    /// Needed because <c>SaveGame.Save</c> writes to <c>farmName_uniqueID</c>, which
    /// typically differs from the fixture name requested by the builder. Also renames the
    /// inner save-data file so SDV's loader (which expects <c>Saves/folder/folder</c>)
    /// finds it when this fixture is later used as a base.
    /// </summary>
    public static void CaptureFromPath(string sourcePath, string name, string fixturesRoot)
    {
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException(
                $"SDV save directory not found — did fixture.save complete? Expected: {sourcePath}");

        var dst = Path.Combine(fixturesRoot, name, "save");
        if (Directory.Exists(dst))
            Directory.Delete(dst, recursive: true);
        CopyRecursive(sourcePath, dst);

        // SDV's loader expects the save-data file inside the folder to share the folder's
        // name. The source folder was e.g. "m0spike_436515781" with an inner file of the
        // same name; after the copy we rename the inner file to match the new fixture name.
        var sourceFolderName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        var srcFile = Path.Combine(dst, sourceFolderName);
        var dstFile = Path.Combine(dst, name);
        if (File.Exists(srcFile) && !string.Equals(srcFile, dstFile, System.StringComparison.Ordinal))
            File.Move(srcFile, dstFile);
    }

    private static void CopyRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(src))
            CopyRecursive(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }
}
