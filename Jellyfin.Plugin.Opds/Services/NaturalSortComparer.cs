using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Compares strings using a natural (numeric aware) ordering, so that
/// "2" sorts before "10".
/// </summary>
internal sealed class NaturalSortComparer : IComparer<string>
{
    /// <summary>
    /// The shared instance of the <see cref="NaturalSortComparer"/> class.
    /// </summary>
    public static readonly NaturalSortComparer Instance = new();

    private NaturalSortComparer()
    {
    }

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                // Skip the leading zeroes of both digit runs.
                var xi = i;
                while (xi < x.Length && x[xi] == '0')
                {
                    xi++;
                }

                var yj = j;
                while (yj < y.Length && y[yj] == '0')
                {
                    yj++;
                }

                // Find the end of both digit runs.
                var xe = xi;
                while (xe < x.Length && char.IsDigit(x[xe]))
                {
                    xe++;
                }

                var ye = yj;
                while (ye < y.Length && char.IsDigit(y[ye]))
                {
                    ye++;
                }

                // The number with more digits is the larger one.
                if (xe - xi != ye - yj)
                {
                    return (xe - xi) - (ye - yj);
                }

                // Both numbers have the same amount of digits, compare them digit by digit.
                for (var k = 0; k < xe - xi; k++)
                {
                    if (x[xi + k] != y[yj + k])
                    {
                        return x[xi + k] - y[yj + k];
                    }
                }

                // Both numbers are equal, the one with fewer leading zeroes comes first.
                if (xi - i != yj - j)
                {
                    return (xi - i) - (yj - j);
                }

                i = xe;
                j = ye;
            }
            else
            {
                var cx = char.ToUpperInvariant(x[i]);
                var cy = char.ToUpperInvariant(y[j]);
                if (cx != cy)
                {
                    return cx - cy;
                }

                i++;
                j++;
            }
        }

        return (x.Length - i) - (y.Length - j);
    }
}
