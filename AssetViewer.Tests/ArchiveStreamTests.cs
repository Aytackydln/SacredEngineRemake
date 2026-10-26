using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;

namespace AssetViewer.Tests;

[TestClass]
public sealed class ArchiveStreamTests
{
    private static string Installation => Environment.GetEnvironmentVariable("SACRED_GAME_DIRECTORY")
        ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    [TestMethod]
    [TestCategory("Installation")]
    public async Task AsyncModelArchiveMatchesDesktopMeshesAndAnimations()
    {
        var pak = Path.Combine(Installation, "pak");
        if (!Directory.Exists(pak)) Assert.Inconclusive("Set SACRED_GAME_DIRECTORY to run installed-data parity checks.");
        using var desktop = ModelsPakArchive.Load(Path.Combine(pak, "models.pak"), Path.Combine(pak, "models.tmp"));
        using var batched = new BatchedArchiveStream(File.OpenRead(Path.Combine(pak, "models.pak")));
        using var browser = await ModelsPakArchive.LoadAsync(batched,
            await File.ReadAllBytesAsync(Path.Combine(pak, "models.tmp")));
        Assert.IsTrue(batched.Batches > 0 && batched.Ranges > batched.Batches);
        var names = ItemsPakArchive.Load(Path.Combine(pak, "Items.pak")).Select(item => item.ModelName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && desktop.ContainsResource(name)).Distinct().Take(8);
        foreach (var name in names)
        {
            var expected = await desktop.LoadModelAsync(name); var actual = await browser.LoadModelAsync(name);
            CollectionAssert.AreEqual(expected.RawBytes, actual.RawBytes, name);
            CollectionAssert.AreEqual(expected.Mesh!.Vertices, actual.Mesh!.Vertices, name);
            CollectionAssert.AreEqual(expected.Mesh.Indices, actual.Mesh.Indices, name);
            var expectedAnimation = await desktop.LoadDefaultCharacterAnimationAsync(name);
            var actualAnimation = await browser.LoadDefaultCharacterAnimationAsync(name);
            Assert.AreEqual(expectedAnimation?.Name, actualAnimation?.Name);
            Assert.AreEqual(expectedAnimation?.DurationSeconds, actualAnimation?.DurationSeconds);
        }
    }

    [TestMethod]
    [TestCategory("Installation")]
    public async Task AsyncTextureArchivePreservesExpansionOwnershipAndDecodedPixels()
    {
        var pak = Path.Combine(Installation, "pak");
        if (!Directory.Exists(pak)) Assert.Inconclusive("Set SACRED_GAME_DIRECTORY to run installed-data parity checks.");
        using var desktop = TexturePakArchive.LoadFromDirectory(pak);
        var streams = Directory.EnumerateFiles(pak, "texture*.pak")
            .Select(path => (Name: Path.GetFileName(path), Stream: new BatchedArchiveStream(File.OpenRead(path)))).ToArray();
        using var browser = await TexturePakArchive.LoadAsync(streams.Select(file => (file.Name, (Stream)file.Stream)));
        Assert.IsTrue(streams.Sum(file => file.Stream.Batches) > 0);
        CollectionAssert.AreEqual(desktop.Entries.ToArray(), browser.Entries.ToArray());
        foreach (var group in desktop.Entries.GroupBy(entry => (entry.ArchiveName, entry.Record.StorageFormat)))
        {
            foreach (var entry in group.Take(3))
            {
                var expected = await desktop.LoadTextureAsync(entry); var actual = await browser.LoadTextureAsync(entry);
                Assert.AreEqual(expected.Name, actual.Name); Assert.AreEqual(expected.Width, actual.Width); Assert.AreEqual(expected.Height, actual.Height);
                CollectionAssert.AreEqual(expected.Rgba8, actual.Rgba8, $"{entry.ArchiveName}:{entry.EntryId}");
            }
        }
    }
}
