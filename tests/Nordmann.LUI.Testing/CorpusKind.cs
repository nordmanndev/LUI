namespace Nordmann.LUI.Testing;

/// <summary>What the entries of a corpus are.</summary>
public enum CorpusKind
{
    /// <summary>Single words: the word list first, then compounds of two of its words — German is full of them.</summary>
    SingleWord,

    /// <summary>Titles of two to four words of the word list.</summary>
    MultiWord,
}
