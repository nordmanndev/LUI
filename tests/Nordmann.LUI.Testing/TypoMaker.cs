namespace Nordmann.LUI.Testing;

/// <summary>Makes the typos the tests type.</summary>
public static class TypoMaker
{
    /// <summary>One edit of the four kinds, after the first letter (a typo in the first letter is rare).</summary>
    public static string WithOneTypo(string beginning, Random random)
    {
        var position = random.Next(1, beginning.Length);
        switch (random.Next(4))
        {
            case 0: // a letter left out
                return beginning.Remove(position, 1);
            case 1: // a letter too many
                return beginning.Insert(position, ((char)('a' + random.Next(26))).ToString());
            case 2: // a letter wrong
                var wrong = (char)('a' + random.Next(26));
                return wrong == beginning[position]
                    ? beginning.Remove(position, 1).Insert(position, wrong == 'z' ? "a" : ((char)(wrong + 1)).ToString())
                    : beginning.Remove(position, 1).Insert(position, wrong.ToString());
            default: // two letters swapped
                if (position == beginning.Length - 1)
                {
                    position--;
                }

                var chars = beginning.ToCharArray();
                (chars[position], chars[position + 1]) = (chars[position + 1], chars[position]);
                return new string(chars);
        }
    }
}
