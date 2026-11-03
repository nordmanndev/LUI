namespace Nordmann.LUI;

/// <summary>
/// How alike an input and a text are (LUI-Spezifikation §11). It is computed
/// for the candidates only, and only for the stage of the ranking that needs it.
/// </summary>
public interface ILuiSimilarity
{
    /// <summary>The name of the measure.</summary>
    string Name { get; }

    /// <summary>Compares an input with a text.</summary>
    /// <param name="query">The input, normalised.</param>
    /// <param name="text">The text, normalised.</param>
    /// <returns>0 (nothing alike) … 1 (identical).</returns>
    double Similarity(string query, string text);
}
