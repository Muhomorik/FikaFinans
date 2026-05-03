namespace FikaFinans.Application.Tests;

[TestFixture]
public class SmokeTests
{
    [Test]
    public void ApplicationAssembly_Loads()
    {
        var assembly = typeof(FikaFinans.Application.AssemblyMarker).Assembly;
        Assert.That(assembly, Is.Not.Null);
    }
}
