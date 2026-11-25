using System.Numerics;
using System.Text;
using NUnit.Framework;
using IlluminariumNG.Adapters.Files;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Tests;

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
        var project = await new JsonProjectFile().ReadAsync(legacy);
        Assert.That(project.Room.Vertices, Is.EqualTo(new Room(8, 6).Vertices));
        Assert.That(project.Room.Height, Is.EqualTo(2.8));
        AssertLamp(project.Lights.Single());
        Assert.That(project.Elements, Is.Empty);
    }

    [Test]
    public async Task VersionFourRoundTripPreservesGeometryAndPhotometry()
    {
        using var legacy = Json(Legacy);
        var source = await new JsonProjectFile().ReadAsync(legacy);
        var points = RoomTests.Concave();
        using var file = new MemoryStream();
        InteriorElement[] elements =
        [
            new WallOpening(0, 1, 0.9, 0, 2),
            new Partition(new(0, 1), new(2, 1), 2.5) { Openings = [new(0.5, 0.9, 0, 2)] },
            new BoxObstacle(new(0.25, 3), new(1.5, 4), 1)
        ];
        await JsonProjectFile.WriteAsync(file, new LightingProject(new Room(points, 3.4), source.Lights) { Elements = elements });
        file.Position = 0;
        var project = await new JsonProjectFile().ReadAsync(file);
        Assert.That(project.Room.Vertices, Is.EqualTo(points));
        Assert.That(project.Room.Height, Is.EqualTo(3.4));
        Assert.That(project.Elements.OfType<WallOpening>(), Is.EqualTo(elements.OfType<WallOpening>()));
        Assert.That(project.Elements.OfType<BoxObstacle>(), Is.EqualTo(elements.OfType<BoxObstacle>()));
        var actual = project.Elements.OfType<Partition>().Single();
        var expected = elements.OfType<Partition>().Single();
        Assert.That(actual with { Openings = expected.Openings }, Is.EqualTo(expected));
        Assert.That(actual.Openings, Is.EqualTo(expected.Openings));
        AssertLamp(project.Lights.Single());
    }

    [TestCase("""{"formatVersion":15}""")]
    [TestCase("""{"formatVersion":2,"height":3,"vertices":[{"x":0,"y":0},{"x":2,"y":2},{"x":2,"y":0},{"x":0,"y":2}],"lights":[]}""")]
    [TestCase("""{"formatVersion":2,"height":3,"vertices":[{"x":0},{"x":3,"y":0},{"x":0,"y":3}],"lights":[]}""")]
    public void RejectsUnsupportedVersionOrInvalidContour(string document)
    {
        using var file = Json(document);
        Assert.ThrowsAsync<FormatException>(async () => await new JsonProjectFile().ReadAsync(file));
    }

    [Test]
    public async Task ReadsVersionTwoWithEmptyElements()
    {
        using var file = Json("""
            {"formatVersion":2,"height":3,"vertices":[{"x":0,"y":0},{"x":4,"y":0},{"x":0,"y":3}],"lights":[]}
            """);
        var project = await new JsonProjectFile().ReadAsync(file);
        Assert.That(project.Room.Area, Is.EqualTo(6));
        Assert.That(project.Elements, Is.Empty);
    }

    [TestCase("""{"type":"opening","height":2,"wall":9,"width":1,"offset":1,"bottom":0}""")]
    [TestCase("""{"type":"obstacle","height":2,"start":{"x":1,"y":1},"end":{"x":5,"y":5}}""")]
    [TestCase("""{"type":"partition","height":1,"start":{"x":1},"end":{"x":2,"y":1}}""")]
    public void RejectsMalformedOrInvalidVersionThreeElement(string element)
    {
        using var file = Json($$"""
            {"formatVersion":3,"height":3,"vertices":[{"x":0,"y":0},{"x":4,"y":0},{"x":0,"y":3}],
             "lights":[],"elements":[{{element}}]}
            """);
        Assert.ThrowsAsync<FormatException>(async () => await new JsonProjectFile().ReadAsync(file));
    }

    [Test]
    public async Task ReadsVersionThreePartitionWithoutPartitionOpenings()
    {
        using var file = Json("""
            {"formatVersion":3,"height":3,"vertices":[{"x":0,"y":0},{"x":4,"y":0},{"x":0,"y":3}],
             "lights":[],"elements":[{"type":"opening","height":2,"wall":0,"width":1,"offset":1,"bottom":0},
             {"type":"partition","height":2,"start":{"x":0,"y":1},"end":{"x":2,"y":1}}]}
            """);
        var project = await new JsonProjectFile().ReadAsync(file);
        Assert.That(project.Elements.OfType<WallOpening>().Count(), Is.EqualTo(1));
        Assert.That(project.Elements.OfType<Partition>().Single().Openings, Is.Empty);
    }

    [TestCase("""{"offset":1,"width":1,"bottom":0,"height":3}""")]
    [TestCase("""{"offset":1,"width":1,"height":1}""")]
    public void RejectsInvalidPartitionOpeningInVersionFour(string opening)
    {
        using var file = Json($$"""
            {"formatVersion":4,"height":3,"vertices":[{"x":0,"y":0},{"x":4,"y":0},{"x":0,"y":3}],
             "lights":[],"elements":[{"type":"partition","height":2,"start":{"x":0,"y":1},
             "end":{"x":2,"y":1},"openings":[{{opening}}]}]}
            """);
        Assert.ThrowsAsync<FormatException>(async () => await new JsonProjectFile().ReadAsync(file));
    }

    [Test]
    public void MalformedJsonIsReportedAsAProjectFormatError()
    {
        using var file = Json("{");
        Assert.ThrowsAsync<FormatException>(async () => await new JsonProjectFile().ReadAsync(file));
    }

    private static void AssertLamp(Luminaire lamp)
    {
        Assert.That(lamp.Position, Is.EqualTo(new Vector2(2, 2)));
        Assert.That(lamp.Distribution.Vertical, Is.EqualTo(new double[] { 0, 90 }));
        Assert.That(lamp.Distribution.Horizontal, Is.EqualTo(new double[] { 0 }));
        Assert.That(lamp.Distribution.Candela.Single(), Is.EqualTo(new double[] { 780, 0 }));
    }
}
