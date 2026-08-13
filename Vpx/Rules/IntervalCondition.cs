using System.Globalization;

namespace VPin.Inspector.Vpx.Rules;

/// <summary>
/// Parses a simple interval condition such as "&gt;=10", "&lt;10", "==135" or "&gt;40"
/// and evaluates it against an integer value (milliseconds).
/// </summary>
public sealed class IntervalCondition
{
    private enum Op
    {
        GreaterOrEqual,
        LessOrEqual,
        Greater,
        Less,
        Equal,
        NotEqual,
    }

    private readonly Op _op;
    private readonly int _threshold;

    private IntervalCondition(Op op, int threshold)
    {
        _op = op;
        _threshold = threshold;
    }

    public string Text { get; private init; } = string.Empty;

    /// <summary>
    /// Attempts to parse an expression like "&gt;=10". Returns null when the text
    /// is null/empty, so callers can treat "no condition" as "always matches".
    /// </summary>
    public static IntervalCondition? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();

        // Order matters: check two-character operators before single-character ones.
        (string token, Op op)[] operators =
        {
            (">=", Op.GreaterOrEqual),
            ("<=", Op.LessOrEqual),
            ("==", Op.Equal),
            ("!=", Op.NotEqual),
            (">", Op.Greater),
            ("<", Op.Less),
            ("=", Op.Equal),
        };

        foreach (var (token, op) in operators)
        {
            if (trimmed.StartsWith(token, StringComparison.Ordinal))
            {
                string numberPart = trimmed[token.Length..].Trim();
                if (int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    return new IntervalCondition(op, value) { Text = trimmed };
                }

                throw new FormatException($"Invalid number in interval condition '{text}'.");
            }
        }

        // Bare number means equality.
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bare))
        {
            return new IntervalCondition(Op.Equal, bare) { Text = trimmed };
        }

        throw new FormatException($"Unrecognized interval condition '{text}'.");
    }

    public bool IsSatisfiedBy(int intervalMs) => _op switch
    {
        Op.GreaterOrEqual => intervalMs >= _threshold,
        Op.LessOrEqual => intervalMs <= _threshold,
        Op.Greater => intervalMs > _threshold,
        Op.Less => intervalMs < _threshold,
        Op.Equal => intervalMs == _threshold,
        Op.NotEqual => intervalMs != _threshold,
        _ => false,
    };
}
