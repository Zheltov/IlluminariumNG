using System.Numerics;

namespace IlluminariumNG.Domain;

internal record Luminaire(Vector2 Position, Photometry Distribution);
internal sealed record Photometry(double[] Vertical, double[] Horizontal, double[][] Candela)
{
    public static Photometry Sample { get; } = new([0, 30, 60, 90], [0], [[780, 550, 130, 0]]);

    public double Intensity(double vertical, double horizontal)
    {
        if (vertical < Vertical[0] || vertical > Vertical[^1])
            return 0;

        if (Horizontal[^1] is 90 or 180 && horizontal > 180)
            horizontal = 360 - horizontal;
        if (Horizontal[^1] == 90 && horizontal > 90)
            horizontal = 180 - horizontal;

        var (v0, v1, t) = Interval(Vertical, vertical);
        var (h0, h1, u) = Interval(Horizontal, horizontal);
        var lower = double.Lerp(Candela[h0][v0], Candela[h0][v1], t);
        var upper = double.Lerp(Candela[h1][v0], Candela[h1][v1], t);
        return double.Lerp(lower, upper, u);
    }

    private static (int Lower, int Upper, double Fraction) Interval(double[] angles, double angle)
    {
        if (angles.Length == 1)
            return (0, 0, 0);
        var index = Array.BinarySearch(angles, angle);
        if (index >= 0)
            return (index, index, 0);
        var upper = ~index;
        var lower = upper - 1;
        return (lower, upper, (angle - angles[lower]) / (angles[upper] - angles[lower]));
    }
}

internal static class Illuminance
{
    public const int Columns = 32;
    public const int Rows = 24;

    public static double Mean(double[,] result) =>
        result.Cast<double>().Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average();

    public static double[,] Calculate(Room room, IReadOnlyList<Luminaire> lights, CancellationToken cancellation)
    {
        var result = new double[Columns, Rows];
        var height = room.Height - Room.WorkHeight;
        for (var y = 0; y < Rows; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < Columns; x++)
            {
                var point = new RoomPoint(
                    room.MinX + (x + 0.5) * room.Width / Columns,
                    room.MinY + (y + 0.5) * room.Depth / Rows);
                if (!room.Contains(point))
                {
                    result[x, y] = double.NaN;
                    continue;
                }

                foreach (var light in lights)
                {
                    var dx = point.X - light.Position.X;
                    var dy = point.Y - light.Position.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy + height * height);
                    var cosine = height / distance;
                    var gamma = Math.Acos(cosine) * 180 / Math.PI;
                    var azimuth = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                    result[x, y] += light.Distribution.Intensity(gamma, azimuth) * cosine / (distance * distance);
                }
            }
        }

        return result;
    }
}
