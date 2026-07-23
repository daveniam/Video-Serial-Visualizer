// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Text.RegularExpressions;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Ordena texto alfanuméricamente tratando los números como valores (Clase 2 antes que Clase 10),
/// en vez de compararlos caracter por caracter.
/// </summary>
public class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private static readonly Regex TokenPattern = new(@"\d+|\D+", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);

        var tokensX = TokenPattern.Matches(x);
        var tokensY = TokenPattern.Matches(y);
        var count = Math.Min(tokensX.Count, tokensY.Count);

        for (var i = 0; i < count; i++)
        {
            var tokenX = tokensX[i].Value;
            var tokenY = tokensY[i].Value;

            var isNumericX = char.IsDigit(tokenX[0]);
            var isNumericY = char.IsDigit(tokenY[0]);

            int comparison;
            if (isNumericX && isNumericY)
            {
                comparison = ulong.TryParse(tokenX, out var numX) && ulong.TryParse(tokenY, out var numY)
                    ? numX.CompareTo(numY)
                    : string.Compare(tokenX, tokenY, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                comparison = string.Compare(tokenX, tokenY, StringComparison.OrdinalIgnoreCase);
            }

            if (comparison != 0)
                return comparison;
        }

        return tokensX.Count.CompareTo(tokensY.Count);
    }
}
