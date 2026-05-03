using System.Text.RegularExpressions;
using Azure.AI.Extensions.OpenAI;
using FikaFinans.Application.Agents;
using FikaFinans.Infrastructure.Foundry;
using NLog;
using OpenAI.Files;

namespace FikaFinans.Infrastructure.Tests.Foundry;

/// <summary>
/// Sandbox variant of <see cref="FoundryFileStoreIntegrationTests"/> that runs
/// against the reduced real-data fixture folder <c>TestDataSbd</c>.
/// </summary>
[TestFixture]
[Explicit("Burns Foundry tokens / hits live OpenAI Files API.")]
[Category("Integration")]
[Category("Foundry")]
[Category("Sandbox")]
public sealed class SandboxFoundryFileStoreIntegrationTests : SandboxFoundryIntegrationTestBase
{
    private string _tempDataFolder = null!;
    private string _tempSidecarPath = null!;
    private FoundryFileStore _sut = null!;
    private FundDataFileSet _fileSet = null!;

    [SetUp]
    public void SetUp()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"FikaFinans-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        _tempDataFolder = Path.Combine(scratch, "data");
        Directory.CreateDirectory(_tempDataFolder);
        _tempSidecarPath = Path.Combine(scratch, "foundry-files.json");

        foreach (var name in FundDataFiles.All)
        {
            File.Copy(Path.Combine(TestDataDir, name), Path.Combine(_tempDataFolder, name));
        }

        _fileSet = FundDataFileSet.FromFolder(_tempDataFolder);
        _sut = new FoundryFileStore(
            logger: LogManager.GetLogger(nameof(SandboxFoundryFileStoreIntegrationTests)),
            projectClient: ProjectClient,
            sidecarPath: _tempSidecarPath);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (File.Exists(_tempSidecarPath))
        {
            var fileClient = ProjectClient.ProjectOpenAIClient.GetOpenAIFileClient();
            var json = await File.ReadAllTextAsync(_tempSidecarPath);
            foreach (Match match in Regex.Matches(json, "assistant-[A-Za-z0-9]+"))
            {
                try { await fileClient.DeleteFileAsync(match.Value); }
                catch { /* swallow — the test asserts what it asserts */ }
            }
        }

        var scratch = Path.GetDirectoryName(_tempSidecarPath);
        if (scratch is not null && Directory.Exists(scratch))
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch { /* file lock from a still-open stream — not fatal */ }
        }
    }

    [Test]
    public async Task EnsureFilesUploadedAsync_FirstCall_UploadsAllFour()
    {
        var result = await _sut.EnsureFilesUploadedAsync(_fileSet);

        Assert.That(result.Keys, Is.EquivalentTo(FundDataFiles.All));
        Assert.That(result.Values, Has.All.StartsWith("assistant-"));
    }

    [Test]
    public async Task EnsureFilesUploadedAsync_SecondCallSameMtime_ReusesSameFileIds()
    {
        var first = await _sut.EnsureFilesUploadedAsync(_fileSet);
        var second = await _sut.EnsureFilesUploadedAsync(_fileSet);

        Assert.That(second, Is.EquivalentTo(first), "cache hit should reuse fileIds");
    }

    [Test]
    public async Task EnsureFilesUploadedAsync_FileDeletedServerSide_DetectsAndReuploads()
    {
        var first = await _sut.EnsureFilesUploadedAsync(_fileSet);
        var doomedFileId = first[FundDataFiles.Positions];

        var fileClient = ProjectClient.ProjectOpenAIClient.GetOpenAIFileClient();
        await fileClient.DeleteFileAsync(doomedFileId);

        var second = await _sut.EnsureFilesUploadedAsync(_fileSet);

        Assert.Multiple(() =>
        {
            Assert.That(second[FundDataFiles.Positions], Is.Not.EqualTo(doomedFileId),
                "deleted positions.csv should re-upload with a fresh id");
            Assert.That(second[FundDataFiles.Summary], Is.EqualTo(first[FundDataFiles.Summary]),
                "untouched summary.csv should keep its original id");
        });
    }
}
