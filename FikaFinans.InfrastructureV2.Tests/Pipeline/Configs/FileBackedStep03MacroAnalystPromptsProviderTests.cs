using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Infrastructure.Pipeline.Configs;
using Moq;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Configs;

[TestFixture]
[TestOf(typeof(FileBackedStep03MacroAnalystPromptsProvider))]
public sealed class FileBackedStep03MacroAnalystPromptsProviderTests
{
    private IFixture _fixture = null!;
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        // A folder no other test shares — not created here, so the provider must create it.
        _dir = Path.Combine(Path.GetTempPath(), $"macro-prompts-{Guid.NewGuid():N}");

        _fixture.Freeze<Mock<IPathsService>>()
            .Setup(x => x.MacroAnalystPromptsDir)
            .Returns(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private FileBackedStep03MacroAnalystPromptsProvider CreateSut()
        => _fixture.Create<FileBackedStep03MacroAnalystPromptsProvider>();

    private void WritePrompts(string system, string userTemplate, string retry)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "system.md"), system);
        File.WriteAllText(Path.Combine(_dir, "user-template.md"), userTemplate);
        File.WriteAllText(Path.Combine(_dir, "retry.md"), retry);
    }

    [Test]
    public async Task GetPromptsAsync_FilesPresent_ReadsEachIntoItsRole()
    {
        // Arrange
        WritePrompts("system text", "user {iso_week}", "retry {error_message}");

        // Act
        var prompts = await CreateSut().GetPromptsAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompts.SystemPrompt, Is.EqualTo("system text"));
            Assert.That(prompts.UserPromptTemplate, Is.EqualTo("user {iso_week}"));
            Assert.That(prompts.RetryPromptTemplate, Is.EqualTo("retry {error_message}"));
        });
    }

    [Test]
    public async Task GetPromptsAsync_FolderMissing_ExtractsTheEmbeddedDefaults()
    {
        // Act
        var prompts = await CreateSut().GetPromptsAsync();

        // Assert — the defaults land on disk, so they can be edited afterwards.
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_dir, "system.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(_dir, "user-template.md")), Is.True);
            Assert.That(File.Exists(Path.Combine(_dir, "retry.md")), Is.True);
            Assert.That(prompts.UserPromptTemplate, Does.Contain("{category_list}"));
            Assert.That(prompts.RetryPromptTemplate, Does.Contain("{error_message}"));
        });
    }

    [Test]
    public async Task GetPromptsAsync_OneFileMissing_ExtractsOnlyThatOne()
    {
        // Arrange — an edited prompt must survive while a deleted one comes back.
        WritePrompts("my system", "my user", "my retry");
        File.Delete(Path.Combine(_dir, "retry.md"));

        // Act
        var prompts = await CreateSut().GetPromptsAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(prompts.SystemPrompt, Is.EqualTo("my system"));
            Assert.That(prompts.UserPromptTemplate, Is.EqualTo("my user"));
            Assert.That(prompts.RetryPromptTemplate, Does.Contain("{error_message}"));
        });
    }

    [Test]
    public async Task GetPromptsAsync_FileEditedBetweenCalls_ReadsTheEdit()
    {
        // Arrange — an edited prompt must apply to the next LLM call without a restart.
        var sut = CreateSut();
        WritePrompts("first", "user", "retry");
        await sut.GetPromptsAsync();
        await File.WriteAllTextAsync(Path.Combine(_dir, "system.md"), "second");

        // Act
        var prompts = await sut.GetPromptsAsync();

        // Assert
        Assert.That(prompts.SystemPrompt, Is.EqualTo("second"));
    }

    [Test]
    public void GetPromptsAsync_FileEmpty_ThrowsInvalidDataException()
    {
        // Arrange
        WritePrompts("system", "   ", "retry");

        // Act & Assert
        Assert.ThrowsAsync<InvalidDataException>(() => CreateSut().GetPromptsAsync());
    }
}
