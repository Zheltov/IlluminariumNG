using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Lighting;

internal record Luminaire(Vector2 Position, Photometry Distribution);
internal record Room(double Width, double Depth)
{
    public const double MountHeight = 2.8;
    public const double WorkHeight = 0.8;
}

internal sealed record Photometry(double[] Vertical, double[] Horizontal, double[][] Candela)
{
    public static Photometry Sample { get; } = new([0, 30, 60, 90], [0], [[780, 550, 130, 0]]);

    public static Photometry Read(string content)
    {
        var tilt = Regex.Match(content, @"^\s*TILT\s*=\s*([^\r\n]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!tilt.Success || !tilt.Groups[1].Value.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Поддерживается IES с TILT=NONE.");

        var tokens = content[(tilt.Index + tilt.Length)..].Split(
            [' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        double ReadNumber()
        {
            if (index == tokens.Length ||
                !double.TryParse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value))
                throw new FormatException("Некорректные числовые данные IES.");
            return value;
        }

        double[] ReadValues(int count)
        {
            var values = new double[count];
            for (var i = 0; i < count; i++)
                values[i] = ReadNumber();
            return values;
        }

        var header = ReadValues(13);
        var verticalCount = (int)header[3];
        var horizontalCount = (int)header[4];
        if (header[5] != 1 || verticalCount is < 2 or > 10000 || horizontalCount is < 1 or > 10000 ||
            header[3] != verticalCount || header[4] != horizontalCount || header[2] < 0 ||
            (long)verticalCount * horizontalCount + verticalCount + horizontalCount > tokens.Length - index)
            throw new FormatException("Некорректная таблица IES или тип фотометрии, отличный от C.");

        var vertical = ReadValues(verticalCount);
        var horizontal = ReadValues(horizontalCount);
        if (!Increasing(vertical) || !Increasing(horizontal) || vertical[0] < 0 || vertical[^1] > 180 ||
            horizontal[0] != 0 || horizontal[^1] is not (0 or 90 or 180 or 360))
            throw new FormatException("Поддерживаются таблицы C с горизонтальными углами 0°, 0–90°, 0–180° и 0–360°.");

        var candela = new double[horizontalCount][];
        for (var i = 0; i < horizontalCount; i++)
        {
            candela[i] = ReadValues(verticalCount);
            for (var j = 0; j < verticalCount; j++)
            {
                candela[i][j] *= header[2];
                if (!double.IsFinite(candela[i][j]) || candela[i][j] < 0)
                    throw new FormatException("Некорректная сила света в IES.");
            }
        }

        return new Photometry(vertical, horizontal, candela);
    }

    private static bool Increasing(double[] values) =>
        values.Zip(values.Skip(1)).All(pair => pair.First < pair.Second);

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

    public static double[,] Calculate(Room room, IReadOnlyList<Luminaire> lights, CancellationToken cancellation)
    {
        var result = new double[Columns, Rows];
        var height = Room.MountHeight - Room.WorkHeight;
        for (var y = 0; y < Rows; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < Columns; x++)
            {
                foreach (var light in lights)
                {
                    var dx = (x + 0.5) * room.Width / Columns - light.Position.X;
                    var dy = (y + 0.5) * room.Depth / Rows - light.Position.Y;
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
