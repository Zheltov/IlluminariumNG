using IlluminariumNG.Domain;

namespace IlluminariumNG.Application;

internal sealed record LightingProject(Room Room, Luminaire[] Lights)
{
    public IReadOnlyList<InteriorElement> Elements { get; init; } = Array.Empty<InteriorElement>();
}
