namespace FikaFinans.Infrastructure.Tests;

[TestFixture]
public class SmokeTests
{
    [Test]
    public void InfrastructureAssembly_Loads()
    {
        var assembly = typeof(FikaFinans.Infrastructure.AssemblyMarker).Assembly;
        Assert.That(assembly, Is.Not.Null);
    }
}
