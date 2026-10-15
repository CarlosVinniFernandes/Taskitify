# Como o Taskitify funciona

Documentação técnica: arquitetura, sincronização, busca de letras e a integração com a barra de tarefas. Para instalar e usar, veja o [README](../README.md).

## Estrutura do código

```
src/Taskitify.Core        (net8.0, sem WPF, 100% testável)
  Lyrics/LrcParser            LRC: múltiplas tags, [offset:], word tags <mm:ss.xx>
  Lyrics/LyricsTimeline       busca binária da linha ativa / próxima troca
  Lyrics/PlaybackClock        projeta a posição entre atualizações da sessão
  Lyrics/TrackMatcher         normalização e matching conservador (título + artista + duração ±8 s)
  Lyrics/LrclibClient         /api/get e /api/search
  Lyrics/LyricsCache          1 JSON por música; "não encontrada" expira em 6 h; erros não são cacheados
  Lyrics/LyricsService        search filtrado → get exato só se preciso, preferindo letra sincronizada
  Layout/OverlayLayout        retângulo do overlay sem cobrir botões vizinhos
  Imaging/AlbumPalette        cores do degradê a partir da capa (dominante escura → mais vibrante), sempre legível com texto branco

src/Taskitify            (WPF, net8.0-windows10.0.19041.0)
  Services/SpotifyMonitor          GSMTC: sessão do Spotify, faixa, estado, timeline, capa, controles
  Services/LyricsSyncService       busca (debounce, cancelamento, backoff) + timer armado para a próxima linha
  Services/TaskbarPositionService  UI Automation: acha o "WidgetsButton" e os botões vizinhos
  Services/TaskbarOverlayService   visibilidade, z-order, cor, tela cheia, Explorer reiniciando, DPI
  Services/AppVolumeService        volume do Spotify no mixer do Windows (sessão de áudio Core Audio)
  Services/ThemeService, SettingsService, StartupService, TrayIconService
  ViewModels/  Taskbar, MiniPlayer, Settings, BarLayout (MVVM)
  Views/       TaskbarOverlay, MiniPlayerWindow, SettingsWindow, PlayerPicker, DebugWindow
  Infrastructure/  P/Invoke, hooks, log

tests/Taskitify.Tests    xUnit
```

### Sincronização

- Logo depois de uma troca de faixa, o Spotify continua expondo por alguns segundos a linha do tempo da música **anterior** (ele a atualiza a cada ~4–5 s). A duração antiga é ignorada até chegar uma atualização da música nova, e a busca da letra espera por ela até 6 s. Antes, a busca às vezes usava a duração errada e não achava a letra.
- A sessão de mídia informa a posição só em eventos (play, pause, seek, troca de faixa), com o horário da amostra (`LastUpdatedTime`). O `PlaybackClock` projeta `posição + (agora − horário da amostra)` enquanto toca e congela ao pausar.
- Só relatórios *novos* da timeline são aplicados. Reaplicar um relatório antigo depois de uma pausa faria a projeção partir da origem errada.
- Em vez de polling a cada X ms, um único timer é armado para o instante exato da próxima linha e rearmado a cada seek, pausa, retomada ou mudança de offset.
- Enquanto toca, uma checagem leve a cada 1,5 s relê o estado e a timeline para corrigir desvios caso o Spotify não emita evento. Pausado ou sem Spotify, nada roda além dos eventos.

### Letras (LRCLIB)

1. `GET /api/search` com título e artista. Uma única requisição traz todas as versões da música com as letras, o que importa com o limite de requisições do LRCLIB. O `GET /api/get` exato (título, artista, álbum e duração) só é usado se a busca não tiver letra sincronizada; antes ele vinha primeiro e costumava gastar o limite com um 404.
2. Só são aceitos candidatos com título e artista iguais após a normalização e duração a no máximo 8 s, preferindo sempre a versão de duração mais próxima. As durações do Spotify e do LRCLIB costumam diferir alguns segundos para a mesma gravação: 172,7 s contra 176 s, 271,7 s contra 276 s. Com a tolerância antiga de 3 s, várias músicas conhecidas ficavam sem letra. Na dúvida, o resultado é "não encontrada": nunca aparece a letra de outra música.
3. **Limite de requisições.** O LRCLIB fica atrás de um limite do Cloudflare. Nos testes daqui, com o servidor carregado, ele aceitou cerca de 1 requisição a cada 5–6 s por IP e devolveu `429` ou `503` nas outras. Por isso as requisições do app saem uma por vez, com pelo menos 1 s entre elas, e cada `429`/`503` é repetido respeitando o `Retry-After` (ou esperando 1, 2, 4, 6, 8, 8, 10, 10 e 10 s, cerca de 1 min no total). Antes, o app desistia na primeira recusa, e era por isso que muitas músicas ficavam sem letra. Com o servidor congestionado, a letra pode levar de 10 a 30 s para aparecer; até lá a barra mostra o nome da música.
4. Os resultados vão para o cache em memória e em disco, então cada música só é buscada uma vez. Se mesmo assim falhar, por exemplo sem internet, nada é cacheado e o app tenta de novo depois de 10 s, 20 s, 40 s… até 5 min.

### Barra de tarefas

Não existe API pública para substituir o widget de clima. A abordagem é:

1. **Detecção.** O botão do clima é exposto via UI Automation com `AutomationId = "WidgetsButton"` dentro de `Shell_TrayWnd`. O `BoundingRectangle` vem em pixels físicos (o app é Per-Monitor DPI v2). Os demais botões da barra viram "obstáculos": o overlay pode ser mais largo que o clima, mas para antes de qualquer botão.
2. **Overlay.** Uma janela WPF sem borda, topmost, `WS_EX_TOOLWINDOW` (fora do Alt+Tab e da barra) e `WS_EX_NOACTIVATE` (não rouba foco), posicionada com `SetWindowPos` em pixels físicos.
3. **Cor.** Pixels da barra logo ao lado do overlay (nunca embaixo dele) são amostrados periodicamente, então o fundo combina com Mica, acrílico ou cor sólida. A cor do texto segue a luminância.
4. **Z-order.** A barra também é topmost e pode subir por cima ao ser ativada. O `HWND_TOPMOST` é reafirmado a cada troca de janela em primeiro plano (`SetWinEventHook` fora de contexto, sem injeção) e a cada 3 s enquanto visível.
5. **Robustez.**
   - `TaskbarCreated` (Explorer reiniciou) e `WM_DISPLAYCHANGE`/`DisplaySettingsChanged` refazem a detecção.
   - `WM_DPICHANGED` reaplica os limites.
   - Tela cheia esconde o overlay, junto com a barra do Windows. O overlay se registra como *appbar* sem reservar espaço, só para receber o mesmo aviso que o Windows usa (`ABN_FULLSCREENAPP`). Assim, a mesma janela entrando em tela cheia (F11 no navegador, vídeo, jogo sem borda) também é detectada. Há ainda `SHQueryUserNotificationState`, uma checagem de janela sem borda cobrindo o monitor, e o filtro de só considerar o monitor onde está o overlay. Janelas apenas maximizadas não contam.
   - O overlay é excluído do Aero Peek (`DWMWA_EXCLUDED_FROM_PEEK`). Sem isso, ao passar o mouse sobre a miniatura de uma janela na barra, o Windows escondia o overlay junto com as outras janelas e o clima aparecia por baixo.
   - Com ocultação automática da barra, o overlay acompanha a barra aparecendo e sumindo.
   - Se o botão não for encontrado, a posição é estimada (barra centralizada: clima à esquerda; alinhada à esquerda: clima antes da bandeja) e fica disponível a calibração manual.

Nada disso desativa os Widgets, reinicia o `explorer.exe`, altera arquivos do sistema ou injeta código em outro processo.

### Degradê e controles

- **Degradê.** A capa vinda da sessão de mídia é reduzida para 48 px. As cores são agrupadas em 4096 faixas: a mais frequente vira o lado esquerdo (escurecido) e a mais vibrante vira o lado direito. As duas são ajustadas até ter contraste de pelo menos 4,5:1 com texto branco (WCAG AA). Na troca de música, o degradê faz uma transição suave para as novas cores. O mini player usa as mesmas cores na diagonal.
- **Aleatório e repetir.** Usam `TryChangeShuffleActiveAsync` e `TryChangeAutoRepeatModeAsync` da sessão de mídia. Só aparecem se o Spotify informar suporte, o que acontece no Spotify Desktop atual. O novo estado é aplicado na hora para que cliques rápidos alternem certo.
- **Volume.** A sessão de mídia não controla volume. O botão ajusta o volume do Spotify no **mixer do Windows** (`ISimpleAudioVolume` da sessão de áudio do processo `Spotify.exe`), que é diferente do controle deslizante dentro do Spotify.
- **Curtir ("+").** Não existe: salvar música na biblioteca exige a Web API do Spotify com login, que este projeto evita de propósito.

## Dependências de versão do Windows

| Ponto | Depende de |
|---|---|
| `AutomationId = "WidgetsButton"` | Taskbar XAML do Windows 11 (22H2+). A Microsoft pode renomear o id em builds futuros. Nesse caso entra a estimativa ou a calibração manual. |
| Posição do clima | Barra **centralizada**: clima à esquerda. **Alinhada à esquerda** (`TaskbarAl=0`): clima à direita, perto da bandeja. O layout cresce para o lado oposto à borda. |
| GSMTC (`Windows.Media.Control`) | Windows 10 1809+. O Spotify Desktop registra a sessão como `Spotify.exe`; a versão da Store como `SpotifyAB.SpotifyMusic_…`. O Spotify Web no navegador **não** é capturado de propósito. |
| `LastUpdatedTime` da timeline | Comportamento do Spotify. Se o Spotify parar de enviar eventos, a correção de 1,5 s cobre. |
| Segoe Fluent Icons / Segoe UI Variable | Fontes do Windows 11. No Windows 10 o app cai para Segoe MDL2 Assets / Segoe UI. |
| Widgets desativados (`TaskbarDa=0`) | Não há clima para cobrir. O overlay usa a posição estimada. |

## O que foi verificado e o que não foi

Verificado na máquina de desenvolvimento (Windows 11 build 26200, 1920×1200 a 100 %, barra alinhada à esquerda, tema escuro, Spotify Desktop):

- Detecção da sessão `Spotify.exe`, da faixa, do estado e da capa
- Detecção do `WidgetsButton` (1415,1152 152×48) e overlay de 300 px crescendo para a esquerda sem cobrir a bandeja
- Clima coberto enquanto toca; pausar mostra o clima de novo e retomar volta à linha certa
- Letra sincronizada vinda do LRCLIB; erro 429 tratado com mensagem no popup, título na barra e nova tentativa com backoff
- Troca de música: nova capa e nova letra
- Mini player: abre ao clicar, recebe foco mesmo com outro app em primeiro plano, fecha com Esc (devolvendo o foco), com clique fora e com novo clique no widget; linha atual destacada e centralizada
- Consumo medido (publish Release): cerca de 57 MB de memória privada e cerca de 1–1,5% de um núcleo enquanto toca com o overlay visível, com picos curtos na troca de música. Pausado, nenhum timer roda.
- Estilos testados na barra: só letra, capa + letra, player e completo, com fundo em degradê e igual à barra
- Aleatório (dois cliques rápidos voltam ao estado inicial), volume (roda do mouse 100% → 95% → 100%, silenciar e reativar) e cliques nos botões sem abrir o popup
- Mini player com o degradê da capa
- Player minimizado ao pausar, montador com letra ocultável e migração das configurações do nome antigo (Taskbar Lyrics)
- 74 testes unitários passando

**Não** verificado (sem o hardware ou a configuração aqui):

- Escala de 125 %, 150 % ou mais e múltiplos monitores com DPIs diferentes
- Barra centralizada
- Ocultação automática da barra
- Tema claro
- Spotify da Microsoft Store
- Reinício do Explorer com o app aberto
- Jogos em tela cheia exclusiva
