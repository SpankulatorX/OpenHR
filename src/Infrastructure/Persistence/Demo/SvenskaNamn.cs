namespace RegionHR.Infrastructure.Persistence.Demo;

/// <summary>
/// Inbäddade listor med vanliga svenska förnamn (kvinnliga/manliga) och efternamn.
/// Används av den procedurella demo-befolkningsgeneratorn för att skapa realistiska,
/// könskonsekventa namnkombinationer. Ren data — inga externa beroenden.
///
/// ~150 kvinnliga + ~150 manliga förnamn + ~200 efternamn ger flera hundratusen
/// unika hela-namn-kombinationer, vilket räcker gott för en ~11 000-personers styrka.
/// </summary>
public static class SvenskaNamn
{
    /// <summary>Kvinnliga förnamn (juridiskt kön Kvinna → jämn näst sista personnummersiffra).</summary>
    public static readonly string[] Kvinnonamn =
    [
        "Anna", "Maria", "Margareta", "Elisabeth", "Eva", "Kristina", "Birgitta", "Karin",
        "Marie", "Ingrid", "Christina", "Linnéa", "Kerstin", "Marianne", "Lena", "Helena",
        "Emma", "Johanna", "Linda", "Sofia", "Anita", "Sara", "Malin", "Hanna", "Inger",
        "Cecilia", "Ulla", "Susanne", "Annika", "Åsa", "Camilla", "Barbro", "Jenny", "Ida",
        "Josefin", "Julia", "Ellen", "Alice", "Astrid", "Wilma", "Ebba", "Elin", "Agneta",
        "Gunilla", "Katarina", "Monica", "Berit", "Yvonne", "Carina", "Britt", "Louise",
        "Frida", "Amanda", "Matilda", "Klara", "Isabelle", "Moa", "Nathalie", "Rebecca",
        "Elsa", "Alva", "Lovisa", "Saga", "Alma", "Lilly", "Maja", "Olivia", "Nova", "Ella",
        "Freja", "Agnes", "Signe", "Vera", "Ines", "Stella", "Selma", "Tuva", "Iris", "Molly",
        "Ellie", "Juni", "Siri", "Nellie", "Tilde", "Meja", "Hedda", "Wilhelmina", "Ester",
        "Edith", "Greta", "Dagny", "Solveig", "Gudrun", "Rut", "Majken", "Viola", "Doris",
        "Gerd", "Rakel", "Gun", "Ann", "Ingegerd", "Siv", "Britt-Marie", "Ann-Christine",
        "Gun-Britt", "Marita", "Pia", "Therese", "Petra", "Charlotta", "Emelie", "Sofie",
        "Caroline", "Madeleine", "Victoria", "Emilia", "Felicia", "Cornelia", "Tindra",
        "Melissa", "Isabella", "Alicia", "Leia", "My", "Ellinor", "Hilda", "Ottilia",
        "Elvira", "Livia", "Thea", "Nora", "Lykke", "Minna", "Ronja", "Idun", "Gabriella",
        "Amelia", "Beata", "Desirée", "Henrietta", "Ingela", "Lisbeth", "Ulrika", "Vendela",
        "Zara", "Nadia", "Fatima", "Layla", "Sonja", "Vanja", "Majvor", "Elly"
    ];

    /// <summary>Manliga förnamn (juridiskt kön Man → udda näst sista personnummersiffra).</summary>
    public static readonly string[] Mansnamn =
    [
        "Erik", "Lars", "Karl", "Anders", "Per", "Johan", "Nils", "Lennart", "Mikael", "Jan",
        "Hans", "Carl", "Sven", "Peter", "Fredrik", "Daniel", "Gunnar", "Bengt", "Bo", "Åke",
        "Mats", "Thomas", "Andreas", "Stefan", "Göran", "Björn", "Christer", "Ulf", "Magnus",
        "Leif", "Rolf", "Kjell", "Marcus", "Henrik", "Roger", "Bertil", "David", "Alexander",
        "Emil", "Oscar", "Anton", "Gustav", "Filip", "Ludvig", "William", "Lucas", "Elias",
        "Hugo", "Liam", "Oliver", "Adam", "Isak", "Vincent", "Theodor", "Axel", "Leo", "Noah",
        "Arvid", "Albin", "Melvin", "Wilmer", "Charlie", "Viktor", "Simon", "Jonathan",
        "Sebastian", "Rasmus", "Robin", "Jesper", "Pontus", "Linus", "Martin", "Jonas",
        "Niklas", "Patrik", "Joakim", "Tobias", "Jakob", "Samuel", "Benjamin", "Felix",
        "Gabriel", "Love", "Sixten", "Frank", "Malte", "Loke", "Vidar", "Alfred", "Edvin",
        "Folke", "Nataniel", "Kevin", "Kasper", "Måns", "Melker", "Milton", "Vilgot", "Ture",
        "Ivar", "Harald", "Ragnar", "Sigge", "Sten", "Torbjörn", "Håkan", "Roland", "Ingemar",
        "Sune", "Evert", "Holger", "Yngve", "Verner", "Assar", "Gösta", "Ove", "Tage", "Valter",
        "Georg", "Curt", "Alf", "Arne", "Egon", "Helge", "Knut", "Olof", "Sigurd", "Vilhelm",
        "Aron", "Casper", "Dante", "Elton", "Hjalmar", "Jack", "Joel", "Kian", "Ali", "Omar",
        "Mohammed", "Hassan", "Ahmad", "Ibrahim", "Yusuf", "Sten-Åke", "Karl-Erik", "Jan-Erik",
        "Lars-Göran", "Bror", "Einar", "Gottfrid", "Ejnar"
    ];

    /// <summary>Vanliga svenska efternamn (könsneutrala).</summary>
    public static readonly string[] Efternamn =
    [
        "Andersson", "Johansson", "Karlsson", "Nilsson", "Eriksson", "Larsson", "Olsson",
        "Persson", "Svensson", "Gustafsson", "Pettersson", "Jonsson", "Jansson", "Hansson",
        "Bengtsson", "Jönsson", "Lindberg", "Jakobsson", "Magnusson", "Olofsson", "Lindström",
        "Lindqvist", "Lindgren", "Berg", "Axelsson", "Bergström", "Lundberg", "Lind",
        "Lundgren", "Lundqvist", "Mattsson", "Berglund", "Fredriksson", "Sandberg",
        "Henriksson", "Forsberg", "Sjöberg", "Wallin", "Engström", "Eklund", "Danielsson",
        "Håkansson", "Lundin", "Gunnarsson", "Holm", "Bergman", "Björk", "Wikström",
        "Isaksson", "Fransson", "Bergqvist", "Nyström", "Holmberg", "Arvidsson", "Löfgren",
        "Söderberg", "Nyberg", "Blomqvist", "Claesson", "Nordström", "Mårtensson",
        "Lundström", "Björklund", "Eliasson", "Pålsson", "Viklund", "Berggren", "Sandström",
        "Nordin", "Lindholm", "Hedlund", "Dahlberg", "Hellström", "Sjögren", "Abrahamsson",
        "Ek", "Blom", "Åberg", "Gustavsson", "Sundberg", "Öberg", "Strömberg", "Ottosson",
        "Hermansson", "Backlund", "Sundström", "Åkesson", "Norberg", "Dahl", "Falk", "Ström",
        "Åström", "Blomgren", "Karlström", "Franzén", "Sundqvist", "Holmgren", "Samuelsson",
        "Nordqvist", "Ahlström", "Lund", "Öhman", "Månsson", "Rosén", "Hedström", "Sjölund",
        "Ivarsson", "Molin", "Wallander", "Åkerlund", "Ohlsson", "Petersson", "Sjöström",
        "Sundin", "Almqvist", "Nyholm", "Rydberg", "Jonasson", "Palm", "Östberg", "Bohlin",
        "Ekström", "Melin", "Norling", "Hägglund", "Björkman", "Sundell", "Wennberg",
        "Ekberg", "Dahlström", "Boström", "Löfqvist", "Sjödin", "Kjellberg", "Norén",
        "Wester", "Enqvist", "Grönlund", "Lindell", "Bergstrand", "Wiberg", "Svahn",
        "Zetterlund", "Sköld", "Ljung", "Frisk", "Ahlin", "Rask", "Modig", "Stål", "Lönn",
        "Kvist", "Florén", "Lindblad", "Sjöqvist", "Hallberg", "Wallgren", "Bäckström",
        "Palmqvist", "Källström", "Rönnberg", "Malm", "Hjort", "Löf", "Ohlin", "Tegnér",
        "Brandt", "Cederholm", "Hall", "Josefsson", "Lager", "Nordlund", "Roos", "Törnqvist",
        "Wahlgren", "Åhlén", "Sylvan", "Wikner", "Ceder", "Ternström", "Halvarsson",
        "Rehn", "Björnsson", "Ryman", "Öqvist", "Grahn", "Norman", "Ranger", "Söderlund",
        "Wallström", "Åhman", "Ljungqvist", "Fagerberg", "Sträng", "Elmqvist", "Byström",
        "Kihlström", "Rudberg", "Ahmadi", "Hussein", "Rahman", "Khan", "Yilmaz", "Nguyen"
    ];
}
