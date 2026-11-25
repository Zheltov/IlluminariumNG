using IlluminariumNG.Domain;

namespace IlluminariumNG.Application;

internal static class PlanSnap
{
    public const double GridStep = 0.25;

    public static RoomPoint Point(RoomPoint raw, bool grid, bool geometry,
        IEnumerable<RoomPoint> candidates, double radius, RoomPoint? excluded = null)
    {
        RoomPoint? closest = null;
        var best = radius;
        if (geometry)
        {
            foreach (var candidate in candidates)
            {
                if (candidate == excluded)
                    continue;

                var distance = double.Hypot(candidate.X - raw.X, candidate.Y - raw.Y);
                if (distance <= radius && (closest is null || distance < best))
                {
                    closest = candidate;
                    best = distance;
                }
            }
        }

        return closest ?? (grid ? new RoomPoint(Round(raw.X), Round(raw.Y)) : raw);
    }

    public static double Along(WallSpan span, double offset, double maxOffset, bool grid, bool geometry,
        IEnumerable<RoomPoint> candidates, double radius)
    {
        double? closest = null;
        var best = radius;
        if (geometry)
        {
            var dx = (span.End.X - span.Start.X) / span.Length;
            var dy = (span.End.Y - span.Start.Y) / span.Length;
            foreach (var candidate in candidates)
            {
                var x = candidate.X - span.Start.X;
                var y = candidate.Y - span.Start.Y;
                var along = x * dx + y * dy;
                if (Math.Abs(x * dy - y * dx) > 1e-9 || along < -1e-9 || along > maxOffset + 1e-9)
                    continue;

                var distance = Math.Abs(along - offset);
                if (distance <= radius && (closest is null || distance < best))
                {
                    closest = along;
                    best = distance;
                }
            }
        }

        return Math.Clamp(closest ?? (grid ? Round(offset) : offset), 0, maxOffset);
    }

    public static double Round(double value) => Math.Round(value / GridStep) * GridStep;
}
