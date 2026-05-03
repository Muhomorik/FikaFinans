namespace FikaFinans.Domain.Tests;

[TestFixture]
public class SmokeTests
{
    [Test]
    public void DomainAssembly_Loads()
    {
        var assembly = typeof(FikaFinans.Domain.AssemblyMarker).Assembly;
        Assert.That(assembly, Is.Not.Null);
    }
}
