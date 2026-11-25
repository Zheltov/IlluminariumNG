using System.Globalization;
using System.Text.RegularExpressions;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Adapters.Files;

internal static class IesPhotometryReader
{
    public static Photometry Parse(string content)
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
}
