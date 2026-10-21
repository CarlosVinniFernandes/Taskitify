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
  Lyrics/LyricsCache          1 JSON por música (com a fonte); "não encontrada" expira em 6 h; erros não são cacheados
  Lyrics/LyricsService        arquivos .lrc locais → cache → fontes remotas; guarda de qual fonte veio cada letra
  Lyrics/LyricsProvider       interface das fontes + LRCLIB (search filtrado → get exato só se preciso)
  Lyrics/LocalLyricsProvider  arquivos .lrc do usuário
  Lyrics/FallbackLyricsProvider  Spotify primeiro; LRCLIB só quando o Spotify não tem letra sincronizada
  Lyrics/SpotifyLyricsProvider   busca da faixa e letra (color-lyrics) com o token do web player
  Lyrics/SpotifySearchTemplate   a busca do próprio web player, capturada e repetida sem o navegador
  Layout/OverlayLayout        retângulo do overlay sem cobrir botões vizinhos
  Imaging/AlbumPalette        cores do degradê a partir da capa (dominante escura → mais vibrante), sempre legível com texto branco

src/Taskitify            (WPF, net8.0-windows10.0.19041.0)
  Services/SpotifyMonitor          GSMTC: sessão do Spotify, faixa, estado, timeline, capa, controles
  Services/SpotifyLyricsSession    WebView2 escondido com o login do Spotify: token e captura da busca
  Services/LyricsSyncService       busca (debounce, cancelamento, backoff) + timer armado para a próxima linha
  Services/TaskbarPositionService  UI Automation: acha o "WidgetsButton" e os botões vizinhos
  Services/TaskbarOverlayService   visibilidade, z-order, cor, tela cheia, Explorer reiniciando, DPI
  Services/AppVolumeService        volume do Spotify no mixer do Windows (sessão de áudio Core Audio)
  Services/ThemeService, SettingsService, StartupService, TrayIconService
  ViewModels/  Taskbar, MiniPlayer, Settings, BarLayout (MVVM)
  Views/       TaskbarOverlay, MiniPlayerWindow, SettingsWindow, PlayerPicker, DebugWindow
  Infrastructure/  P/Invoke, hooks, log

tests/Taskitify.Tests    xUnit
tools/perf               Measure-Taskitify.ps1 (memória/CPU do app e do WebView2, cenários reproduzíveis), Compare-Runs.ps1
```

### Sincronização

- Logo depois de uma troca de faixa, o Spotify continua expondo por alguns segundos a linha do tempo da música **anterior** (ele a atualiza a cada ~4–5 s). A duração antiga é ignorada até chegar uma atualização da música nova, e a busca da letra espera por ela até 6 s. Antes, a busca às vezes usava a duração errada e não achava a letra.
- A sessão de mídia informa a posição só em eventos (play, pause, seek, troca de faixa), com o horário da amostra (`LastUpdatedTime`). O `PlaybackClock` projeta `posição + (agora − horário da amostra)` enquanto toca e congela ao pausar.
- Só relatórios *novos* da timeline são aplicados. Reaplicar um relatório antigo depois de uma pausa faria a projeção partir da origem errada.
- Em vez de polling a cada X ms, um único timer é armado para o instante exato da próxima linha e rearmado a cada seek, pausa, retomada ou mudança de offset.
- Enquanto toca, uma checagem leve a cada 1,5 s relê o estado e a timeline para corrigir desvios caso o Spotify não emita evento. Pausado ou sem Spotify, nada roda além dos eventos.
- O evento de troca de música (`MediaPropertiesChanged`) pode chegar atrasado: medido 10 s depois de um skip cujo título a sessão já devolvia na hora. Quando a timeline muda de duração ou volta para o começo, o app confere a música na hora, e o relatório que revelou a troca já vale como timeline da música nova. Consultar a música a cada 1,5 s resolveria o mesmo, mas cada consulta deixa ~0,2 MB de memória nativa (a capa) presa até uma coleta do GC; por isso há uma coleta jovem (~1 ms) por música.

### Letras (LRCLIB)

1. `GET /api/search` com título e artista. Uma única requisição traz todas as versões da música com as letras, o que importa com o limite de requisições do LRCLIB. O `GET /api/get` exato (título, artista, álbum e duração) só é usado se a busca não tiver letra sincronizada; antes ele vinha primeiro e costumava gastar o limite com um 404.
2. Só são aceitos candidatos com título e artista iguais após a normalização e duração a no máximo 8 s, preferindo sempre a versão de duração mais próxima. As durações do Spotify e do LRCLIB costumam diferir alguns segundos para a mesma gravação: 172,7 s contra 176 s, 271,7 s contra 276 s. Com a tolerância antiga de 3 s, várias músicas conhecidas ficavam sem letra. Na dúvida, o resultado é "não encontrada": nunca aparece a letra de outra música.
3. **Limite de requisições.** O LRCLIB fica atrás de um limite do Cloudflare. Nos testes daqui, com o servidor carregado, ele aceitou cerca de 1 requisição a cada 5–6 s por IP e devolveu `429` ou `503` nas outras. Por isso as requisições do app saem uma por vez, com pelo menos 1 s entre elas, e cada `429`/`503` é repetido respeitando o `Retry-After` (ou esperando 1, 2, 4, 6, 8, 8, 10, 10 e 10 s, cerca de 1 min no total). Antes, o app desistia na primeira recusa, e era por isso que muitas músicas ficavam sem letra. Com o servidor congestionado, a letra pode levar de 10 a 30 s para aparecer; até lá a barra mostra o nome da música.
4. Os resultados vão para o cache em memória e em disco, então cada música só é buscada uma vez. Se mesmo assim falhar, por exemplo sem internet, nada é cacheado e o app tenta de novo depois de 10 s, 20 s, 40 s… até 5 min.

### Letras do Spotify

Opcional, com o login do usuário num perfil WebView2 separado. Medido com `tools/perf/Measure-Taskitify.ps1`:

- A busca pública (`api.spotify.com/v1/search`) responde `429` às credenciais do web player em praticamente todas as tentativas. O app usa então a busca do próprio web player (`searchTracks`, uma consulta GraphQL persistida): captura essa requisição na primeira vez que o web player busca e depois a repete com outro termo pelo `HttpClient`. Nada é fixado no código; se o Spotify trocar a consulta, a repetição falha, o web player busca de novo e a nova requisição passa a ser usada. Troca de música → letra em ~1,5 s, sem abrir o navegador. A exceção é o que a busca não acha (podcasts, arquivos locais, versões com metadados diferentes): esses ainda passam uma vez pela busca do web player, que pode trazer uma consulta melhor que a capturada.
- Se o Spotify não responder à busca (sem internet, tempo esgotado, `429`, `5xx`), isso conta como falha, não como "sem letra". O LRCLIB ainda é consultado, mas, se ele também não tiver a letra, nada vai para o cache e o app tenta de novo depois de 10 s, 20 s, 40 s… até 5 min. Com o token válido, essas novas tentativas não abrem o navegador, que buscaria nos mesmos servidores. Sem internet e com o token vencido, a página que não carrega pausa o navegador por 1 min, ou até a rede mudar (Wi-Fi reconectado, cabo de volta). O mesmo vale para a primeira busca depois de abrir o app, se o web player não conseguir capturar a busca dele. Antes, uma busca que falhava virava "não encontrada" e ficava 6 h no cache. E se o Spotify falha enquanto o LRCLIB não tem a letra, as novas tentativas reaproveitam a resposta do LRCLIB por 10 min em vez de consultá-lo de novo.
- Letra que o LRCLIB deu enquanto o Spotify falhava é provisória: fica no cache só 6 h, como o "não encontrada", e depois o Spotify é consultado de novo. Entradas gravadas antes de o cache registrar a fonte também (podem ser a versão do LRCLIB de quando o Spotify falhava quase sempre). Se a nova busca falhar também, por exemplo sem internet, a letra provisória vencida ainda aparece em vez de nada. Letra que só o LRCLIB tem continua no cache para sempre.
- O navegador escondido só abre para obter o token (o Spotify entrega o mesmo token até ele vencer, ~1 h; a validade real vem da resposta do próprio web player) e, depois de iniciar o app, para a primeira busca, já direto na página de busca dessa música. A página é descarregada 5 s depois do uso e o navegador fecha 30 s depois. Suspender não adiantava: a memória continuava reservada (~400 MB) e todo uso recarrega a página.
- Escondido, o web player não recebe imagens, fontes, mídia nem iframes de outros sites; a janela de login recebe tudo isso. O DRM fica bloqueado nas duas, porque nada toca ali (o processo do DRM sozinho tinha ~70 MB).
- Um navegador recém-aberto recebe uma navegação só. Duas navegações seguidas num WebView2 novo deixavam as duas penduradas até o navegador fechar, e a letra caía para o LRCLIB depois de 8 s.
- Token rejeitado (`401`) é renovado uma vez antes de recorrer ao LRCLIB. Sem login, o Spotify fica de fora por 10 min em vez de cada música esperar 15 s pelo token. Sem login quer dizer sem o cookie de sessão do Spotify. Uma página que não carrega (sem internet, Spotify fora do ar), ou um web player logado que demora mais de 15 s para dar o token, não conta como falta de login: a busca falha e é repetida, sem essa pausa e sem pedir login. Logado e sem token por 3 páginas seguidas (uma página de verificação, uma mudança no Spotify), o app pede login e faz a pausa, para o usuário abrir a janela e ver o que o Spotify quer. Ao entrar na conta, a música atual é buscada de novo; uma renovação de token comum não recarrega nada.
- Com o modo de depuração ligado, o log registra de qual fonte veio cada letra (Spotify, LRCLIB, arquivos locais, cache), os tempos de cada etapa e o ciclo do navegador. Tokens, cookies e URLs com dados de sessão nunca entram no log.

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
- Primeira execução (sem `settings.json`): no idioma do Windows, quando o app o tem, uma tela explica as duas formas de buscar letras (só LRCLIB, ou Spotify + LRCLIB) e o que a conexão com o Spotify faz: nada na conta, só busca letras. "Conectar o Spotify" abre a janela de login; as configurações são salvas na hora, então a tela aparece uma vez só
- Troca de música: nova capa e nova letra
- Capa: vem da sessão de mídia do Windows, mas o Spotify às vezes publica a música sem ela (medido: uma música ainda sem capa minutos depois de começar). O app olha de novo 2, 5 e 10 s depois da troca; se continuar sem, e o Spotify estiver conectado, usa a capa que a busca do Spotify mostra (a da busca da letra, ou a busca repetida com um token ainda válido; o navegador nunca abre por uma capa), baixada do CDN de imagens do Spotify. As conexões do Spotify tentam os endereços como um navegador (IPv6 e IPv4 alternados, o próximo 250 ms depois): na rede medida, o IPv6 até esse CDN nunca respondia e o .NET esperava por ele até o tempo esgotar
- Menu da letra: **Buscar letra de novo** abre um submenu com uma busca por fonte (Spotify, LRCLIB). A fonte escolhida é consultada sozinha, sem cache; o que ela achar passa a ser a letra salva, e se ela não tiver a letra (ou falhar) a salva continua. O submenu é outro popup: o fechamento por clique fora do menu também o considera
- Mini player: abre ao clicar, recebe foco mesmo com outro app em primeiro plano, fecha com Esc (devolvendo o foco), com clique fora e com novo clique no widget; linha atual destacada e centralizada
- Consumo medido com `tools/perf/Measure-Taskitify.ps1` (publish Release, executável único): cerca de 55–70 MB de memória privada (35–45 MB no conjunto de trabalho) e menos de 1% de um núcleo enquanto toca com o overlay visível. O overlay é desenhado por software, o que deixa o dispositivo Direct3D e o driver da GPU (~13 MB, medido com o overlay sozinho) fora do processo, sem gastar mais CPU, nem com o letreiro rolando. O mini player usa a GPU enquanto está aberto; depois da primeira abertura, ~12 MB do driver ficam no processo até o app reiniciar. Com as letras do Spotify, o navegador escondido (até ~310 MB) fica aberto cerca de 35 s depois de iniciar e quando o token vence; nas trocas de música ele não abre, exceto quando a busca não acha a música. Pausado, nenhum timer roda.
- Estilos testados na barra: só letra, capa + letra, player e completo, com fundo em degradê e igual à barra
- Aleatório (dois cliques rápidos voltam ao estado inicial), volume (roda do mouse 100% → 95% → 100%, silenciar e reativar) e cliques nos botões sem abrir o popup
- Mini player com o degradê da capa
- Player minimizado ao pausar, montador com letra ocultável e migração das configurações do nome antigo (Taskbar Lyrics)
- 169 testes unitários passando

**Não** verificado (sem o hardware ou a configuração aqui):

- Escala de 125 %, 150 % ou mais e múltiplos monitores com DPIs diferentes
- Barra centralizada
- Ocultação automática da barra
- Tema claro
- Spotify da Microsoft Store
- Reinício do Explorer com o app aberto
- Jogos em tela cheia exclusiva
