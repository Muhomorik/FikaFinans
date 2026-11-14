using FikaFinans.Domain.Funds;

namespace FikaFinans.Domain.Tests.Funds;

/// <summary>
/// The pin rule steps 1 and 10 share: an ISIN pin beats a name-only pin, a name-only pin
/// matches the exact name, and anything unmatched is unpinned — an active position.
/// </summary>
[TestFixture]
[TestOf(typeof(PortfolioStructure))]
public sealed class PortfolioStructureTests
{
    private static PortfolioStructure Structure(params PinnedFund[] pins) => new() { PinnedFunds = pins };

    [Test]
    public void LayerOf_IsinPinned_ReturnsItsLayer()
    {
        // Arrange
        var sut = Structure(new PinnedFund { Isin = "LU0001", Layer = PinnedLayer.Core });

        // Act
        var layer = sut.LayerOf("LU0001", "Any name");

        // Assert
        Assert.That(layer, Is.EqualTo(PinnedLayer.Core));
    }

    [Test]
    public void LayerOf_NamePinned_ReturnsItsLayer()
    {
        // Arrange
        var sut = Structure(new PinnedFund { Name = "Old Fund", Layer = PinnedLayer.Writeoff });

        // Act
        var layer = sut.LayerOf("LU0001", "Old Fund");

        // Assert
        Assert.That(layer, Is.EqualTo(PinnedLayer.Writeoff));
    }

    [Test]
    public void LayerOf_PinnedByIsinAndByName_IsinWins()
    {
        // Arrange — the name pin comes first in the list; order must not matter.
        var sut = Structure(
            new PinnedFund { Name = "Old Fund", Layer = PinnedLayer.Writeoff },
            new PinnedFund { Isin = "LU0001", Layer = PinnedLayer.Core });

        // Act
        var layer = sut.LayerOf("LU0001", "Old Fund");

        // Assert
        Assert.That(layer, Is.EqualTo(PinnedLayer.Core));
    }

    [Test]
    public void LayerOf_PinNamingAnotherIsin_IgnoresItsName()
    {
        // Arrange — a pin with an ISIN is matched by ISIN only, even if the names agree.
        var sut = Structure(new PinnedFund { Isin = "LU0002", Name = "Old Fund", Layer = PinnedLayer.Writeoff });

        // Act
        var layer = sut.LayerOf("LU0001", "Old Fund");

        // Assert
        Assert.That(layer, Is.Null);
    }

    [Test]
    public void LayerOf_NameDiffersInCase_IsNotPinned()
    {
        // Arrange
        var sut = Structure(new PinnedFund { Name = "Old Fund", Layer = PinnedLayer.Writeoff });

        // Act
        var layer = sut.LayerOf("LU0001", "old fund");

        // Assert
        Assert.That(layer, Is.Null);
    }

    [Test]
    public void LayerOf_NoNameGiven_MatchesIsinPinsOnly()
    {
        // Arrange
        var sut = Structure(new PinnedFund { Name = "Old Fund", Layer = PinnedLayer.Writeoff });

        // Act
        var layer = sut.LayerOf("LU0001", null);

        // Assert
        Assert.That(layer, Is.Null);
    }
}
