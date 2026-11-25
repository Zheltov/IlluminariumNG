using System.Numerics;
using System.Text;
using NUnit.Framework;

namespace Lighting.Tests;

public class ProjectFileTests
{
    private const string Legacy = """
        {"formatVersion":1,"width":8,"depth":6,"lights":[
          {"x":2,"y":2,"vertical":[0,90],"horizontal":[0],"candela":[[780,0]]}]}
        """;

    internal static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Test]
    public async Task ReadsLegacyRectangleWithOriginalPhotometry()
    {
        using var legacy = Json(Legacy);
        var project = await ProjectFile.ReadAsync(legacy);
        Assert.That(project.Room.Vertices, Is.EqualTo(new Room(8, 6).Vertices));
        Assert.That(project.Room.Height, Is.EqualTo(2.8));
        AssertLamp(project.Lights.Single());
    }

    [Test]
    public async Task RoundTripPreservesOrderedVerticesHeightAndLights()
    {
        using var legacy = Json(Legacy);
        var source = await ProjectFile.ReadAsync(legacy);
        var points = RoomTests.Concave();
        using var file = new MemoryStream();
        await ProjectFile.WriteAsync(file, new LightingProject(new Room(points, 3.4), source.Lights));
        file.Position = 0;
        var project = await ProjectFile.ReadAsync(file);
        Assert.That(project.Room.Vertices, Is.EqualTo(points));
        Assert.That(project.Room.Height, Is.EqualTo(3.4));
        AssertLamp(project.Lights.Single());
    }

    [TestCase("""{"formatVersion":15}""")]
    [TestCase("""{"formatVersion":2,"height":3,"vertices":[{"x":0,"y":0},{"x":2,"y":2},{"x":2,"y":0},{"x":0,"y":2}],"lights":[]}""")]
    [TestCase("""{"formatVersion":2,"height":3,"vertices":[{"x":0},{"x":3,"y":0},{"x":0,"y":3}],"lights":[]}""")]
    public void RejectsUnsupportedVersionOrInvalidContour(string document)
    {
        using var file = Json(document);
        Assert.ThrowsAsync<FormatException>(async () => await ProjectFile.ReadAsync(file));
    }

    private static void AssertLamp(Luminaire lamp)
    {
        Assert.That(lamp.Position, Is.EqualTo(new Vector2(2, 2)));
        Assert.That(lamp.Distribution.Vertical, Is.EqualTo(new double[] { 0, 90 }));
        Assert.That(lamp.Distribution.Horizontal, Is.EqualTo(new double[] { 0 }));
        Assert.That(lamp.Distribution.Candela.Single(), Is.EqualTo(new double[] { 780, 0 }));
    }
}
