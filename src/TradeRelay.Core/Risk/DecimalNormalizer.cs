namespace TradeRelay.Core.Risk;

/// <summary>Provides explicit decimal step and tick normalization.</summary>
public static class DecimalNormalizer
{
    /// <summary>Rounds a value down to the nearest positive step.</summary>
    public static decimal RoundDownToStep(decimal value, decimal step)
    {
        if (step <= 0m) throw new ArgumentOutOfRangeException(nameof(step));
        return decimal.Floor(value / step) * step;
    }

    /// <summary>Rounds a value to the nearest positive tick using midpoint-away-from-zero behavior.</summary>
    public static decimal RoundToTick(decimal value, decimal tickSize)
    {
        if (tickSize <= 0m) throw new ArgumentOutOfRangeException(nameof(tickSize));
        return decimal.Round(value / tickSize, 0, MidpointRounding.AwayFromZero) * tickSize;
    }

    /// <summary>Rounds a value up to the nearest positive step.</summary>
    public static decimal RoundUpToStep(decimal value, decimal step)
    {
        if (step <= 0m) throw new ArgumentOutOfRangeException(nameof(step));
        return decimal.Ceiling(value / step) * step;
    }
}
