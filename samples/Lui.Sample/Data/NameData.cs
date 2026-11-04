namespace Nordmann.LUI.Sample.Data;

/// <summary>
/// The seed of the <c>names</c> index: people, without a payload. Names are where the typo tolerance is the most useful
/// (nobody knows how "Schröder" is spelled), and where a correction towards the words of another index would be wrong —
/// the dictionary of an index is its own (ADR-0025).
/// </summary>
internal static class NameData
{
    /// <summary>Full name and popularity.</summary>
    internal static IReadOnlyList<(string Text, int Weight)> All { get; } =
    [
        ("Megan Müller", 900), ("Müller Meier", 400), ("Mueller Schmidt", 300), ("Meike Schröder", 800),
        ("Schroeder Andreas", 200), ("Schneider Lena", 700), ("Fischer Jonas", 650), ("Weber Hannah", 640),
        ("Wagner Felix", 630), ("Becker Marie", 620), ("Hoffmann Paul", 610), ("Schäfer Lea", 600),
        ("Koch Maximilian", 590), ("Richter Emma", 580), ("Klein Noah", 570), ("Wolf Mia", 560),
        ("Neumann Ben", 550), ("Schwarz Anna", 540), ("Zimmermann Luca", 530), ("Braun Sophie", 520),
        ("Krüger Elias", 510), ("Hofmann Clara", 500), ("Hartmann Finn", 490), ("Lange Emilia", 480),
        ("Schmitt Leon", 470), ("Werner Lina", 460), ("Krause Theo", 450), ("Meier Johanna", 440),
        ("Lehmann Oskar", 430), ("Köhler Greta", 420), ("Herrmann Anton", 410), ("König Frieda", 400),
        ("Walter Henri", 390), ("Mayer Ida", 380), ("Huber Ella", 370), ("Kaiser Jakob", 360),
        ("Fuchs Charlotte", 350), ("Peters Matteo", 340), ("Lang Pauline", 330), ("Scholz Milo", 320),
        ("Möller Nele", 310), ("Weiß Ruben", 300), ("Jung Leni", 290), ("Hahn Samuel", 280),
        ("Vogel Mathilda", 270), ("Friedrich Emil", 260), ("Keller Marlene", 250), ("Günther Ole", 240),
        ("Đuro Šimić", 160), ("Željko Ćosić", 155), ("Ivana Škorić", 150), ("Łukasz Kowalski", 150), ("Łódź Nowak", 140), ("İsmail Yılmaz", 130), ("Çelik Zeynep", 120),
        ("Jörg Böhm", 110), ("Åsa Lindgren", 100), ("José García", 90), ("François Dubois", 80),
    ];
}
