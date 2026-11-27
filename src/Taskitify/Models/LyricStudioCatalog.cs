namespace Taskitify.Models;

public enum StudioCategory
{
    Essentials, Memories, DigitalNostalgia, MusicCulture, Collectibles, Literary, Gaming,
}

public sealed record StudioField(string Key, string Label, string Default, IReadOnlyList<string>? Choices = null);

public sealed record StudioStyle(LyricCardStyle Style, StudioCategory Category, string? Name, LyricCardFont Font,
    bool Image, IReadOnlyList<StudioField> Fields);

public static class LyricStudioCatalog
{
    private static StudioField F(string key, string label, string value, params string[] choices) =>
        new(key, label, value, choices.Length == 0 ? null : choices);

    private static StudioStyle S(LyricCardStyle style, StudioCategory category, string? name = null,
        LyricCardFont font = LyricCardFont.Modern, bool image = false, params StudioField[] fields) =>
        new(style, category, name, font, image, fields);

    public static IReadOnlyList<StudioStyle> All { get; } =
    [
        S(LyricCardStyle.Classic, StudioCategory.Essentials),
        S(LyricCardStyle.Gradient, StudioCategory.Essentials),
        S(LyricCardStyle.Blur, StudioCategory.Essentials),
        S(LyricCardStyle.Minimal, StudioCategory.Essentials, font: LyricCardFont.Serif),
        S(LyricCardStyle.Karaoke, StudioCategory.Essentials),

        S(LyricCardStyle.Polaroid, StudioCategory.Memories, font: LyricCardFont.Handwritten),
        S(LyricCardStyle.PhotoBooth, StudioCategory.Memories, "Photo Booth", image: true, fields:
        [F("title","Print title","After the music"), F("ref","Print identification","PB–004 / CONTACT PRINT")]),
        S(LyricCardStyle.Letter, StudioCategory.Memories, "Letter", LyricCardFont.Serif, false,
            F("model","Paper","Vintage","Vintage","Romantic","Minimal"), F("to","Recipient","You"), F("hello","Greeting","Dear you,"), F("bye","Closing","With love,"), F("sign","Signature","{artist}"), F("date","Date","{date}"), F("ps","P.S. (optional)","")),
        S(LyricCardStyle.FilmNegative, StudioCategory.Memories, "Film Negative", image: true, fields:
        [F("frame","Frame identification","27A"), F("stock","Film stock","35 MM / 400"), F("note","Exposure note","AVAILABLE LIGHT")]),
        S(LyricCardStyle.Telegram, StudioCategory.Memories, "Telegram", LyricCardFont.Mono, false,
            F("from","Sender","{artist}"), F("to","Recipient","Someone I miss"), F("date","Date","{date}"),
            F("stamp","Delivery stamp","Delivered with feeling"), F("note","Final message","End of transmission.")),
        S(LyricCardStyle.MuseumExhibit, StudioCategory.Memories, "Museum Exhibit", LyricCardFont.Serif, true,
            F("collection","Collection","Permanent collection"), F("id","Work identification","Sound and memory · 027"),
            F("year","Year",""), F("note","Description","Recorded sound. Private collection.")),

        S(LyricCardStyle.Retro, StudioCategory.DigitalNostalgia, "Retro Desktop", LyricCardFont.Mono),
        S(LyricCardStyle.MsnMessenger, StudioCategory.DigitalNostalgia, "MSN Messenger", image: true, fields:
        [F("model","Theme","XP Blue","Classic","XP Blue","XP Silver"), F("who","Contact","{artist}"), F("status","Status","Available"), F("time","Time","{time}"), F("note","Typing field","Send me a song…")]),
        S(LyricCardStyle.WindowsXpError, StudioCategory.DigitalNostalgia, "Windows XP Error", fields:
        [F("title","Window title","Taskitify.exe"), F("message","Message","Unable to forget."), F("code","Error code","MEMORY_STILL_EXISTS"),
            F("icon","Icon","Error","Error","Warning","Info"), F("primary","Primary button","Listen again"), F("secondary","Secondary button","Cancel")]),
        S(LyricCardStyle.FloppyDisk, StudioCategory.DigitalNostalgia, "Floppy Disk", LyricCardFont.Mono, false,
            F("file","Filename","{title}.LRC"), F("device","Disk color (#hex)",""), F("label","Label","Side one / private copy"), F("id","Identification","1.44 MB")),
        S(LyricCardStyle.Terminal, StudioCategory.DigitalNostalgia, "Terminal", LyricCardFont.Mono, false,
            F("user","Username","listener"), F("host","Hostname","localhost"), F("command","Track command","now-playing"),
            F("lyrics","Lyrics command","lyrics --current"), F("note","Final message","selection saved"), F("theme","Palette","Green","Green","Amber","Mono","Blue","Modern")),
        S(LyricCardStyle.BlueScreen, StudioCategory.DigitalNostalgia, "Blue Screen of Feelings", LyricCardFont.Mono, false,
            F("intro","Opening message","A memory has been detected."), F("code","Error code","UNEXPECTED_FEELINGS"), F("note","Final message","Press any key to feel again.")),
        S(LyricCardStyle.AimAway, StudioCategory.DigitalNostalgia, "AIM Away Message", image: true, fields:
        [F("user","Screen name","midnight_listener"), F("time","Time","{time}"), F("status","Status","Away"),
            F("intro","Opening message","If you need me, leave a song."), F("note","Final message","Be back when the record ends.")]),
        S(LyricCardStyle.Calculator, StudioCategory.DigitalNostalgia, "Calculator", LyricCardFont.Mono, false,
            F("expression","Expression","VALUE OF A MOMENT ="), F("result","Display result","Ans = ∞"), F("device","Device color (#hex)","")),
        S(LyricCardStyle.Paint98, StudioCategory.DigitalNostalgia, "Paint 98", fields:
        [F("file","Filename","{title}.bmp"), F("ink","Text color (#hex)",""), F("doodle","Doodle color (#hex)",""), F("doodle2","Second doodle color (#hex)",""),
            F("canvas","Canvas color (#hex)",""), F("shape","Doodle","Stars","Stars","Hearts","Flowers","Notes","Sun","Scribbles","Underline","None"),
            F("deco","Decoration","Spray","Spray","Rainbow","Clouds","Sparkles","None")]),
        S(LyricCardStyle.Orkut, StudioCategory.DigitalNostalgia, "Orkut", image: true, fields:
        [F("profile","Profile name","{artist}"), F("from","From","A friend"), F("date","Date","{date}"), F("section","Section","Testimonials")]),
        S(LyricCardStyle.IpodClassic, StudioCategory.DigitalNostalgia, "iPod Classic", image: true, fields:
        [F("model","Edition","White","White","Black"), F("device","Device color (#hex)",""), F("label","Display header","Now playing"), F("engrave","Engraving","Made for the mixtape")]),

        S(LyricCardStyle.Player, StudioCategory.MusicCulture),
        S(LyricCardStyle.Vinyl, StudioCategory.MusicCulture),
        S(LyricCardStyle.CassetteTape, StudioCategory.MusicCulture, "Cassette Tape", fields:
        [F("model","Shell","Black","Black","Clear","White","Red"), F("sticker","Label design","Stripe","Stripe","Kraft","Rainbow","Plain"),
            F("title","Mixtape title","Songs I kept"), F("to","Dedication","For the road home"), F("side","Tape side","A","A","B"),
            F("label","Label color (#hex)",""), F("note","Tape text","LOW NOISE / HIGH FEELING")]),

        S(LyricCardStyle.ConcertTicket, StudioCategory.Collectibles, "Concert Ticket", image: true, fields:
        [F("id","Ticket number","TKT–027"), F("date","Date","{date}"), F("title","Heading","One song only")]),
        S(LyricCardStyle.LyricReceipt, StudioCategory.Collectibles, "Lyric Receipt", LyricCardFont.Mono, false,
            F("paper","Paper","White","White","Aged thermal"), F("title","Header","MUSIC & MEMORIES"), F("shop","Fictional store","Taskitify Record Shop"), F("id","Receipt number","027"),
            F("date","Date","{date}"), F("extra","Additional field","PAYMENT: one good memory"), F("note","Final message","Thank you for listening!")),
        S(LyricCardStyle.BoardingPass, StudioCategory.Collectibles, "Boarding Pass", fields:
        [F("from","Origin","NOW"), F("to","Destination","MEMORY"), F("id","Journey","TK–027"), F("date","Date","{date}"), F("note","Message","Keep this moment close.")]),
        S(LyricCardStyle.LibraryCard, StudioCategory.Collectibles, "Library Card", LyricCardFont.Serif, false,
            F("library","Library","Library of Listening"), F("category","Category","Recorded music"), F("stamp","Stamp","On loan"),
            F("date","Date","{date}"), F("return","Return date","Never")),
        S(LyricCardStyle.MusicPassport, StudioCategory.Collectibles, "Music Passport", LyricCardFont.Serif, true,
            F("country","Fictional republic","Republic of Memories"), F("origin","Origin","Home"), F("issue","Issued","{date}"),
            F("expire","Expires","Never"), F("stamp","Stamp","HEARD · REMEMBERED"), F("note","Decorative text","An imaginary document for a real song.")),
        S(LyricCardStyle.ClassifiedDocument, StudioCategory.Collectibles, "Classified Document", LyricCardFont.Mono, false,
            F("dept","Department","Department of Musical Memories"), F("class","Classification","Top secret"), F("id","File number","TK–027"),
            F("stamp","Stamp","Declassified"), F("note","Evidence heading","Extracted evidence"), F("footer","Closing note","Distribution limited to people who listen.")),

        S(LyricCardStyle.Editorial, StudioCategory.Literary, font: LyricCardFont.Serif),
        S(LyricCardStyle.Dictionary, StudioCategory.Literary, "Dictionary", LyricCardFont.Serif, false,
            F("word","Headword","{title}"), F("category","Category",""), F("class","Your category (e.g. saudade, road trip)",""), F("number","Definition number","1"), F("note","Additional detail","{artist}"),
            F("memo","Personal note",""), F("memo2","Second personal note","")),
        S(LyricCardStyle.BookPage, StudioCategory.Literary, "Book Page", LyricCardFont.Serif, false,
            F("chapter","Chapter","Chapter VII"), F("page","Page","127"), F("initial","Drop cap","On","On","Off"), F("highlight","Highlight","Off","On","Off"), F("note","Page note","{artist}")),
        S(LyricCardStyle.PeriodicTable, StudioCategory.Literary, "Periodic Table", fields:
        [F("symbol","Symbol",""), F("id","Element number","027"), F("class","Classification",""), F("state","State",""),
            F("half","Half-life","{duration}"), F("note","Properties heading","Observed properties")]),
        S(LyricCardStyle.Crossword, StudioCategory.Literary, "Crossword", fields:
        [F("word","Main word","{title}"), F("id","Puzzle identification","No. 27"), F("clue","Extra clue","A song worth keeping.")]),

        S(LyricCardStyle.GameBoy, StudioCategory.Gaming, "Game Boy", LyricCardFont.Mono, false,
            F("model","Edition","Original gray","Original gray","Clear","Yellow","Black"), F("device","Device color (#hex)",""), F("screen","Screen color (#hex)",""), F("label","Device label","Pocket Lyrics"), F("note","Screen note","PLAY MODE")),
        S(LyricCardStyle.NintendoDs, StudioCategory.Gaming, "Nintendo DS", LyricCardFont.Mono, true,
            F("model","Edition","White","White","Black","Pink","Blue"), F("device","Device color (#hex)",""), F("intro","Dialogue intro","A song was discovered."), F("note","Device engraving","Dual Sound")),
        S(LyricCardStyle.PokemonEncounter, StudioCategory.Gaming, "Pokémon Encounter", LyricCardFont.Mono, true,
            F("intro","Encounter message","A wild song appeared!"), F("level","Level","98"), F("options","Options","LISTEN / FAVORITE / SAVE / RUN"), F("note","Indicator","RARE FIND")),
        S(LyricCardStyle.RpgInventory, StudioCategory.Gaming, "RPG Inventory", LyricCardFont.Serif, false,
            F("rarity","Rarity","Legendary","Common","Rare","Epic","Legendary"), F("item","Item type","Memory"), F("rating","Rating","★★★★★"), F("value","Value","Priceless"),
            F("trade","Tradeable","Never"), F("note","Description","A moment held in sound.")),
        S(LyricCardStyle.MagicCard, StudioCategory.Gaming, "Magic Card", LyricCardFont.Serif, true,
            F("type","Card type","Enchantment"), F("category","Category","Memory"), F("rarity","Rarity","Rare","Common","Rare","Legendary","Holographic"),
            F("symbol","Symbols","◆ ◇"), F("id","Identification","027 / 042")),
        S(LyricCardStyle.Chess, StudioCategory.Gaming, "Chess", LyricCardFont.Serif, false,
            F("title","Heading","The decisive move"), F("result","Result","Unfinished"), F("light","Light square (#hex)",""), F("dark","Dark square (#hex)",""),
            F("note","Comment","The quiet move is the one you remember.")),
    ];

    private static readonly IReadOnlyDictionary<LyricCardStyle, StudioStyle> ByStyle = All.ToDictionary(style => style.Style);

    public static StudioStyle Get(LyricCardStyle style) => ByStyle.TryGetValue(style, out var value) ? value : All[0];
}
