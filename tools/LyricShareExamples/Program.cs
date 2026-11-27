using System.Text;
using System.Windows.Media;
using Taskitify.Models;
using Taskitify.Services;
using Taskitify.Views;

// Prints the examples of shared/lyrics-share-v1.md, made by the app's own serializer, and checks that
// every code decodes back to the same JSON. Songs, lines and names are original demonstration content.
//   dotnet run --project tools/LyricShareExamples
Console.OutputEncoding = Encoding.UTF8;
var date = new DateTime(2026, 11, 27);

LyricCardContent Song(string title, string artist, string? album, params (string Text, double? At)[] lines) =>
    new(title, artist, album, lines.Select(l => new LyricCardLine(l.Text, l.At is { } s ? TimeSpan.FromSeconds(s) : null)).ToList(),
        null, Colors.Teal, Colors.Black, Colors.Teal);

var examples = new List<(string Name, LyricShareLink.Payload Payload)>();

// 1. The composition alone, everything at its defaults.
examples.Add(("composition-only", LyricShareLink.Create(
    Song("After the Rain", "Demo Artist", "Evening Studies", ("Every little moment", 13), ("Some moments stay with us.", 19)),
    new LyricCardOptions { Style = LyricCardStyle.Paint98 }, null, null, null, null, date)));

// 2. Customized template, square format, a Spotify id and a full dedication.
var receipt = new LyricCardOptions
{
    Style = LyricCardStyle.LyricReceipt, Format = LyricCardFormat.Square, Font = LyricCardFont.Mono, FontScale = 1.1,
    Align = LyricCardAlign.Left, Color = "#c98b3a", Mark = LyricCardMark.Taskitify, ShowTimestamp = true,
};
receipt.Variants[LyricCardStyle.LyricReceipt] = new LyricCardVariant
{
    Fields = new(StringComparer.Ordinal) { ["paper"] = "Aged thermal", ["title"] = "SONGS FOR YOU", ["note"] = "Keep the change, keep the song" },
};
examples.Add(("customized-with-dedication", LyricShareLink.Create(
    Song("Golden Hour", "Demo Artist", "Coastline", ("Let the world slow down", 6), ("Golden hour on the avenue", 12), ("Every color sounds like you", 18)),
    receipt, "1a2B3c4D5e6F7g8H9i0J1k", "Carlos", "Ana", "This one always makes me think of our walks by the sea.", date)));

// 3. Accents and emoji, portrait format, a personal photo replaced by nothing (images never travel).
var polaroid = new LyricCardOptions { Style = LyricCardStyle.Polaroid, Format = LyricCardFormat.Portrait, ShowCover = false };
polaroid.Variants[LyricCardStyle.Polaroid] = new LyricCardVariant { ImageSource = StudioImageSource.Personal, Typeface = LyricCardFont.Handwritten };
examples.Add(("unicode-dedication", LyricShareLink.Create(
    Song("Canção do Caminho", "Artista de Demonstração", null, ("Café quente na janela", null), ("o céu de outono em mim", null)),
    polaroid, null, "João", "Zoë 🌙", "Feliz aniversário! ✨ Que essa música te acompanhe.", date)));

var ok = true;
foreach (var (name, payload) in examples)
{
    var json = LyricShareLink.ToJson(payload);
    var code = LyricShareLink.Encode(json);
    var back = LyricShareLink.Decode(code);
    var same = back == json && LyricShareLink.ToJson(LyricShareLink.Parse(back)!) == json;
    ok &= same;
    Console.WriteLine($"### {name}\nJSON ({Encoding.UTF8.GetByteCount(json)} bytes):\n{json}\nCODE ({code.Length} chars, round trip {(same ? "OK" : "FAILED")}):\n{code}\nURL:\n{LyricShareLink.BaseUrl}#{code}\n");
}

// Worst case within the limits: 6 long lines, long names, a full message, 16 fields of hard-to-compress text.
var random = new Random(7);
string Noise(int n) => new(Enumerable.Range(0, n).Select(_ => "abcdefghijklmnopqrstuvwxyzáéíóúçãõ ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"[random.Next(70)]).ToArray());
var worst = new LyricCardOptions { Style = LyricCardStyle.CassetteTape, Format = LyricCardFormat.Landscape };
worst.Variants[LyricCardStyle.CassetteTape] = new LyricCardVariant
{
    Fields = Enumerable.Range(0, LyricShareLink.MaxFields).ToDictionary(i => $"field{i:00}", _ => Noise(LyricShareLink.MaxFieldValue), StringComparer.Ordinal),
};
var worstPayload = LyricShareLink.Create(
    Song(Noise(120), Noise(120), Noise(120), Enumerable.Range(0, 6).Select(i => (Noise(200), (double?)(i * 10))).ToArray()),
    worst, "1a2B3c4D5e6F7g8H9i0J1k", Noise(40), Noise(40), Noise(280), date);
var worstCode = LyricShareLink.Encode(LyricShareLink.ToJson(worstPayload));
Console.WriteLine($"### worst case within limits: {worstCode.Length} chars (limit {LyricShareLink.MaxCodeLength}); link {(LyricShareLink.Link(worstPayload) is null ? "refused" : "made")}");

// Without custom fields (the usual case at the limits).
worst.Variants.Clear();
var common = LyricShareLink.Create(
    Song(Noise(120), Noise(120), null, Enumerable.Range(0, 6).Select(i => (Noise(200), (double?)(i * 10))).ToArray()),
    worst, null, Noise(40), Noise(40), Noise(280), date);
Console.WriteLine($"### longest without custom fields: {LyricShareLink.Encode(LyricShareLink.ToJson(common)).Length} chars");
Console.WriteLine(ok ? "ALL ROUND TRIPS OK" : "ROUND TRIP FAILURE");
return ok ? 0 : 1;
