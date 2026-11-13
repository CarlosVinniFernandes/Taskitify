namespace Taskitify.Linux;

/// <summary>The few strings that only exist on Linux (the shared catalog is the Windows app's).</summary>
internal static class LinuxStrings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        ["StartAtLogin"] = new()
        {
            ["en"] = "Start at login", ["pt"] = "Iniciar ao entrar na sessão", ["es"] = "Iniciar al abrir sesión",
            ["fr"] = "Lancer à l'ouverture de session", ["de"] = "Beim Anmelden starten", ["it"] = "Avvia all'accesso",
        },
        ["HideWhenPaused"] = new()
        {
            ["en"] = "Hide the lyrics bar while paused", ["pt"] = "Esconder a barra de letras ao pausar", ["es"] = "Ocultar la barra de letras al pausar",
            ["fr"] = "Masquer la barre de paroles en pause", ["de"] = "Textleiste bei Pause ausblenden", ["it"] = "Nascondi la barra dei testi in pausa",
        },
        ["ShowTrackWhenNoLyrics"] = new()
        {
            ["en"] = "Show the song name when there are no synced lyrics", ["pt"] = "Mostrar o nome da música sem letra sincronizada",
            ["es"] = "Mostrar el nombre de la canción sin letra sincronizada", ["fr"] = "Afficher le titre sans paroles synchronisées",
            ["de"] = "Songnamen ohne synchronisierten Text anzeigen", ["it"] = "Mostra il titolo senza testo sincronizzato",
        },
        ["FloatingBar"] = new()
        {
            ["en"] = "Floating lyrics bar (off: only the GNOME top bar)", ["pt"] = "Barra de letras flutuante (desligada: só a barra do GNOME)",
            ["es"] = "Barra de letras flotante (desactivada: solo la barra de GNOME)", ["fr"] = "Barre de paroles flottante (désactivée : barre GNOME seule)",
            ["de"] = "Schwebende Textleiste (aus: nur GNOME-Leiste)", ["it"] = "Barra dei testi mobile (spenta: solo la barra GNOME)",
        },
        ["SpotifyBrowserError"] = new()
        {
            ["en"] = "Could not start the Spotify web player. Install python3-gi and gir1.2-webkit2-4.1, then try again.",
            ["pt"] = "Não foi possível abrir o player web do Spotify. Instale python3-gi e gir1.2-webkit2-4.1 e tente de novo.",
            ["es"] = "No se pudo iniciar el reproductor web de Spotify. Instala python3-gi y gir1.2-webkit2-4.1 e inténtalo de nuevo.",
            ["fr"] = "Impossible de lancer le lecteur web Spotify. Installez python3-gi et gir1.2-webkit2-4.1, puis réessayez.",
            ["de"] = "Der Spotify-Webplayer ließ sich nicht starten. Installiere python3-gi und gir1.2-webkit2-4.1 und versuche es erneut.",
            ["it"] = "Impossibile avviare il web player di Spotify. Installa python3-gi e gir1.2-webkit2-4.1 e riprova.",
        },
        ["Width"] = new()
        {
            ["en"] = "Bar width", ["pt"] = "Largura da barra", ["es"] = "Ancho de la barra", ["fr"] = "Largeur de la barre", ["de"] = "Breite der Leiste", ["it"] = "Larghezza della barra",
        },
        ["ResetPosition"] = new()
        {
            ["en"] = "Reset bar position", ["pt"] = "Restaurar posição da barra", ["es"] = "Restablecer la posición", ["fr"] = "Réinitialiser la position",
            ["de"] = "Position zurücksetzen", ["it"] = "Ripristina la posizione",
        },
        ["SpotifyNotFound"] = new()
        {
            ["en"] = "Waiting for Spotify…", ["pt"] = "Esperando o Spotify…", ["es"] = "Esperando a Spotify…", ["fr"] = "En attente de Spotify…", ["de"] = "Warte auf Spotify …", ["it"] = "In attesa di Spotify…",
        },
    };

    public static string Get(string? language, string key)
    {
        if (!Table.TryGetValue(key, out var byLanguage))
            return key;
        return byLanguage.TryGetValue(language ?? "en", out var value) ? value : byLanguage["en"];
    }
}
