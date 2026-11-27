export const shareCopy = {
  en: {
    sent: "Someone sent you a song.",
    feelings: "Some feelings sound better as a song.",
    open: "Open your song",
    to: "To", from: "From", dedication: "A note for you",
    nowPlaying: "Now playing",
    listen: "Listen on Spotify", search: "Find on Spotify",
    made: "Made with Taskitify — Lyrics Studio", about: "Meet Taskitify",
    invalid: "This song link couldn’t be opened.",
    missing: "This song link wasn’t found.",
    unavailable: "Song links are temporarily unavailable.",
    tryAgain: "Try again in a little while.",
    help: "Ask the sender for a fresh copy of the full link.",
    version: "This link uses a newer sharing format.",
    unsupported: "Try opening the link in an up-to-date browser.",
  },
  "pt-BR": {
    sent: "Alguém te mandou uma música.",
    feelings: "Alguns sentimentos soam melhor em forma de música.",
    open: "Abra sua música",
    to: "Para", from: "De", dedication: "Um recado para você",
    nowPlaying: "Tocando agora",
    listen: "Ouvir no Spotify", search: "Buscar no Spotify",
    made: "Feito com Taskitify — Lyrics Studio", about: "Conheça o Taskitify",
    invalid: "Não foi possível abrir o link da música.",
    missing: "Este link de música não foi encontrado.",
    unavailable: "Os links de música estão temporariamente indisponíveis.",
    tryAgain: "Tente novamente daqui a pouco.",
    help: "Peça a quem enviou uma nova cópia do link completo.",
    version: "Este link usa um formato de compartilhamento mais recente.",
    unsupported: "Tente abrir o link em um navegador atualizado.",
  },
  es: {
    sent: "Alguien te envió una canción.",
    feelings: "Algunos sentimientos suenan mejor en una canción.",
    open: "Abre tu canción",
    to: "Para", from: "De", dedication: "Una nota para ti",
    nowPlaying: "Sonando ahora",
    listen: "Escuchar en Spotify", search: "Buscar en Spotify",
    made: "Hecho con Taskitify — Lyrics Studio", about: "Conoce Taskitify",
    invalid: "No se pudo abrir el enlace de la canción.",
    missing: "No se encontró el enlace de la canción.",
    unavailable: "Los enlaces de canciones no están disponibles en este momento.",
    tryAgain: "Inténtalo de nuevo en unos minutos.",
    help: "Pide a quien te lo envió una nueva copia del enlace completo.",
    version: "Este enlace usa un formato más reciente.",
    unsupported: "Intenta abrir el enlace en un navegador actualizado.",
  },
  fr: {
    sent: "Quelqu’un t’a envoyé une chanson.",
    feelings: "Certains sentiments sonnent mieux en chanson.",
    open: "Ouvre ta chanson",
    to: "Pour", from: "De la part de", dedication: "Un mot pour toi",
    nowPlaying: "En cours",
    listen: "Écouter sur Spotify", search: "Rechercher sur Spotify",
    made: "Créé avec Taskitify — Lyrics Studio", about: "Découvrir Taskitify",
    invalid: "Impossible d’ouvrir le lien de la chanson.",
    missing: "Ce lien n’a pas été trouvé.",
    unavailable: "Les liens de chansons sont temporairement indisponibles.",
    tryAgain: "Réessaie dans quelques instants.",
    help: "Demande à la personne qui te l’a envoyé de renvoyer le lien complet.",
    version: "Ce lien utilise un format de partage plus récent.",
    unsupported: "Essaie d’ouvrir le lien dans un navigateur à jour.",
  },
  de: {
    sent: "Jemand hat dir einen Song geschickt.",
    feelings: "Manche Gefühle klingen als Lied einfach besser.",
    open: "Öffne deinen Song",
    to: "Für", from: "Von", dedication: "Eine Nachricht für dich",
    nowPlaying: "L?uft gerade",
    listen: "Auf Spotify anhören", search: "Auf Spotify suchen",
    made: "Erstellt mit Taskitify — Lyrics Studio", about: "Taskitify entdecken",
    invalid: "Der Songlink konnte nicht geöffnet werden.",
    missing: "Dieser Songlink wurde nicht gefunden.",
    unavailable: "Songlinks sind vorübergehend nicht verfügbar.",
    tryAgain: "Versuche es in Kürze erneut.",
    help: "Bitte die Person, die ihn dir geschickt hat, um den vollständigen Link.",
    version: "Dieser Link verwendet ein neueres Format.",
    unsupported: "Versuche, den Link in einem aktuellen Browser zu öffnen.",
  },
  it: {
    sent: "Qualcuno ti ha mandato una canzone.",
    feelings: "Certi sentimenti suonano meglio in una canzone.",
    open: "Apri la tua canzone",
    to: "Per", from: "Da", dedication: "Un pensiero per te",
    nowPlaying: "In riproduzione",
    listen: "Ascolta su Spotify", search: "Cerca su Spotify",
    made: "Creato con Taskitify — Lyrics Studio", about: "Scopri Taskitify",
    invalid: "Impossibile aprire il link della canzone.",
    missing: "Il link della canzone non è stato trovato.",
    unavailable: "I link delle canzoni sono temporaneamente non disponibili.",
    tryAgain: "Riprova tra poco.",
    help: "Chiedi a chi te l’ha inviato di rimandarti il link completo.",
    version: "Questo link usa un formato di condivisione più recente.",
    unsupported: "Prova ad aprire il link in un browser aggiornato.",
  },
};

export type ShareLocale = keyof typeof shareCopy;

export function shareLocale(browser: readonly string[], sender?: string): ShareLocale {
  for (const tag of [...browser, sender]) {
    switch (tag?.toLowerCase().split("-")[0]) {
      case "en": return "en";
      case "pt": return "pt-BR";
      case "es": return "es";
      case "fr": return "fr";
      case "de": return "de";
      case "it": return "it";
    }
  }
  return "en";
}
