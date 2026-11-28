# Fontes de letras e karaokê sincronizado

Relatório da evolução do sistema de letras do Taskitify, que agora tem karaokê real: por palavra e por sílaba, com vocais de apoio e duetos. Data: 28/11/2026.

## 1. Arquitetura implementada

O sistema existente foi evoluído, não reescrito. O Spotify continua como fonte principal, e o caminho por palavra que já existia (âncoras do Enhanced LRC → `LyricFlow` → `LyricInk`) passou a ser alimentado por fontes reais.

```
Música muda
  └─ LyricsSyncService.LoadAsync
       ├─ cache → Spotify → LRCLIB (fluxo de sempre) ──► letra exibida NA HORA
       └─ StartKaraokeSearch (depois que a letra já está na tela)
            ├─ a letra do Spotify já tem sílabas? → é o karaokê (sem requisição)
            └─ KaraokeService.FindAsync (em segundo plano, cancelável)
                 ├─ KaraokeCache (disco; "nenhum" lembrado por 12 h)
                 ├─ busca compartilhada por música (deduplicação)
                 ├─ fontes habilitadas em paralelo, prazo de 8 s cada
                 │    ├─ LrcRedProvider: search.json → TTML por ISRC
                 │    └─ LrclibKaraokeProvider: Lyricsfile (YAML) com palavras
                 ├─ pausa da fonte em 429/503 (Retry-After)
                 └─ melhor resultado: sílaba > palavra > vocais de apoio > dueto > ordem do usuário
       └─ ApplyKaraoke: troca a linha do tempo (preserva a posição) → barra, mini player e Minimal juntos
```

### Modelo unificado (`Taskitify.Core/Lyrics/Models.cs`)

- **`LyricsLine`** (já existia) ganhou campos opcionais, todos compatíveis com o que já havia:
  - `End`: fim da linha;
  - `Singer`: lado do dueto (0 = primeiro cantor, 1 = outro);
  - `Background`: vocais de apoio, uma lista de `LyricsVocal`.
- **`LyricsVocal`**: texto, início, fim e âncoras próprias.
- **`LyricsSyncLevel`**: Plain, Line, Word ou Syllable. A granularidade fica **separada** das capacidades vocais (`HasBackground`, `HasDuet`).
- **`KaraokeLyrics`**: as linhas, a granularidade, a fonte, o crédito e o link. Tem uma nota de qualidade usada para escolher a melhor versão.
- **Pausas instrumentais** (`KaraokeLyrics.WithInterludes`): como as fontes de karaokê informam o fim de cada linha, silêncios de 3 s ou mais, e a introdução, viram pausas instrumentais (os pontos animados).

O motor de renderização não conhece formato de API nenhum. Todo formato vira texto mais âncoras (`LyricAnchor`), que o `LyricFlow` e o `LyricInk` já usavam.

### Parsers (`KaraokeParsers.cs`)

- **TTML** (padrão Apple Music, usado pelo lrc.red): `<p>` com `<span>` por palavra ou sílaba (spans colados formam uma palavra), `ttm:role="x-bg"` para vocais de apoio e `ttm:agent` para o lado do dueto (o agente de grupo, `v1000`, não conta como lado). Também aceita os vários formatos de tempo do TTML.
- **Lyricsfile** (YAML do LRCLIB): leitor próprio e pequeno para esse esquema, sem dependência nova. Trata escalares entre aspas e continuações dobradas.
- **Spotify `SYLLABLE_SYNCED`**: vira Enhanced LRC no cache existente.
- **Pausa entre palavras:** uma pausa do cantor de 150 ms ou mais gera uma âncora no fim da palavra, sobre o espaço seguinte. Assim o preenchimento **segura** na pausa em vez de deslizar pelo silêncio.

### Estados (`LyricsSyncService`)

São três coisas separadas, como pedido:

- **Disponibilidade:** `KaraokeAvailability` (Off, NotSearched, Searching, Unavailable, Available).
- **Preferência:** `LyricsSources.KaraokeOn`, salva nas configurações. Uma música sem karaokê não a apaga.
- **Estado efetivo:** `KaraokeActive` (disponível e desejado). Se a música não tem letra sincronizada nenhuma, o karaokê é usado como letra mesmo com a preferência desligada.

## 2. Resultado do benchmark

Feito em 28/11/2026, com 30 músicas de vários gêneros, décadas e idiomas:

- internacionais (pop, rock, hip-hop, dos anos 70 a 2024);
- brasileiras (bossa nova, MPB, sertanejo, funk/pop, rock);
- latinas;
- asiáticas (K-pop, J-pop, C-pop).

Os metadados de referência (título, artista, álbum, duração, ISRC) vieram da API pública do Deezer. Cada fonte foi consultada do jeito que o Taskitify consultaria, com o mesmo User-Agent, em sequência, respeitando os limites de cada uma.

| Fonte | Achou | Palavra/sílaba real | Sílaba | Vocais de apoio | Dueto | Média | Mediana | p95 |
|---|---|---|---|---|---|---|---|---|
| **lrc.red** | 25/30 | 23 | **21** | **10** | **4** | 286 ms | **254 ms** | 536 ms |
| **LRCLIB** (Lyricsfile) | 30/30 | **23** | 0 | 0 | 0 | 379 ms | 332 ms | 462 ms |
| Unison | 9/30 | 9 | 8 | 4 | 3 | 1.025 ms | 1.022 ms | 1.148 ms |
| AMLL TTML DB (índice por título) | 5/30 | 2 | 2 | 5 | 3 | 97 ms | 40 ms | 296 ms |
| LiriQo | 30/30 | 15 | 0 | 1 | 0 | 3.210 ms | 2.916 ms | 6.066 ms |
| LyricsPlus | 29/30 | 28* | 0 | 0 | 0 | 525 ms | 135 ms | 2.100 ms |
| LyricsDB | 30/30 | 29* | 0 | 0 | 0 | 1.339 ms | 843 ms | 3.786 ms |

\* LyricsPlus e LyricsDB foram classificados por uma heurística sobre o JSON, não por parser. O número é otimista e não foi confirmado.

- **lrc.red + LRCLIB juntos:** karaokê real em **26 de 30** músicas.
- **Quem cobre as lacunas:** "Lose Yourself", "Die With A Smile" e "アイドル" só o Unison tinha por sílaba. "Under Pressure" e "Tusa", o LRCLIB por palavra.
- **Músicas brasileiras:** 6 de 7 com karaokê real (lrc.red por sílaba em 6). "Garota de Ipanema" e "Evidências" só por linha.

**Identificação da música:** as fontes que buscam por nome devolvem várias versões (ao vivo, acústica, remix). O lrc.red é filtrado pelo `TrackMatcher` (título normalizado, artista e duração), dando preferência ao mesmo álbum. O `KaraokeService` ainda confere o título devolvido antes de aceitar.

## 3. Provedores selecionados e descartados

**Selecionados (padrão):**

1. **Spotify** (principal, como antes): letra na hora. Agora também aceita `SYLLABLE_SYNCED`, que antes era tratado como letra sem tempo.
2. **lrc.red:**
   - melhor qualidade (sílaba, vocais de apoio, duetos) e menor latência;
   - API pública, sem chave;
   - exige identificação (o User-Agent do Taskitify com o link do projeto, sem e-mail pessoal) e crédito com link, que aparece onde a letra é exibida ("Letra via lrc.red");
   - pausa automática em 429/503.
3. **LRCLIB (Lyricsfile):**
   - já era usado; agora o campo `lyricsfile` (com palavras) é lido;
   - as requisições são **compartilhadas** com o fallback de letras por linha (`MemoizingLrclibClient`), então nenhuma requisição duplicada.

**Descartados:**

| Fonte | Motivo |
|---|---|
| Unison | Cobertura baixa (9/30) e cerca de 1 s por consulta. Pede crédito ao autor de cada letra. Boa candidata a **terceira fonte opcional** no futuro, porque cobre lacunas do lrc.red. |
| AMLL TTML DB | Qualidade excelente, mas pequeno (3.480 letras, foco asiático: 5/30). Exigiria baixar um índice de 1,7 MB ou ter o ID do Spotify. |
| LiriQo | Agregador de 10 fontes, incluindo acesso não oficial a serviços comerciais. Lento (p95 de 6 s), redundante com o que já usamos, e depende de terceiros. |
| LyricsPlus | Agregador que usa segredos extraídos do Spotify e acesso não oficial a outros serviços. O servidor público oscila (outros projetos registraram HTTP 530). Risco ético e de estabilidade. |
| LyricsDB | Agregador (Apple Music, Spotify, NetEase, QQ...). Limite de 6 músicas novas por minuto e p95 de 3,8 s. |
| Better Lyrics API (cf-api) | Exige token Turnstile (verificação de navegador) e a fonte principal dele é o próprio lrc.red. |
| Beautiful Lyrics (servidor) | Servidor privado: receberia o token do Spotify do usuário, o que é proibido pelos requisitos. |

**Fontes com a mesma origem:** o Better Lyrics usa o lrc.red, e LiriQo, LyricsPlus e LyricsDB agregam Apple Music e Musixmatch por vias não oficiais. Usar o lrc.red direto evita a redundância.

## 4. Funcionalidades adicionadas

- **Karaokê real** por palavra e por sílaba, na barra, no mini player, no Player Flutuante e no Minimal Mode. Aparece com o preenchimento de letra ligado ou desligado.
- **Vocais de apoio** como uma segunda linha, menor e um pouco mais apagada, com **preenchimento próprio**.
- **Duetos:** com letra alinhada à esquerda, o outro cantor fica do lado oposto.
- **Pausas instrumentais** detectadas pelos tempos de fim, incluindo a introdução.
- **Botão de karaokê** no mini player, ao lado do curtir e do compartilhar, com o mesmo estilo de ícone e a mesma convenção de "ativo":
  - **Buscando:** o ícone pulsa de leve;
  - **Indisponível:** atenuado;
  - **Disponível:** normal;
  - **Ativo:** verde;
  - **Vocais de apoio ou dueto:** um ponto verde;
  - **Busca sob demanda:** quando a busca automática está desligada;
  - o tooltip informa o estado, as capacidades e a fonte.
- **Crédito com link** da fonte de karaokê, onde a letra aparece.
- **Correção no Spotify:** letras `SYLLABLE_SYNCED` perdiam o tempo. As entradas antigas do cache nessa situação são buscadas de novo uma única vez.
- **Configurações:**
  - **"Fontes de letras"** (aba nova): a conexão com o Spotify, as letras locais, o karaokê (4 ajustes), as fontes de karaokê (descrição, capacidades, ordem, liga/desliga) e o cache (quantidade de letras e de karaokês, espaço ocupado e o botão de limpar);
  - **"Player"** (aba nova): todo o visual do player num lugar só, compartilhado pelo mini player e pelo flutuante.

## 5. Configurações padrão

| Configuração | Padrão |
|---|---|
| Procurar karaokê sincronizado | ligado |
| Procurar automaticamente | ligado |
| Ativar o karaokê quando disponível | ligado |
| Preenchimento estimado sem sincronização real | ligado |
| Fontes de karaokê | lrc.red, depois LRCLIB (ambas ligadas) |
| Prazo por fonte | 8 s |
| Cache negativo ("nenhum karaokê") | 12 h; uma falha de rede não é guardada |
| Pausa entre linhas que vira pausa instrumental | 3 s |

## 6. Arquivos criados e modificados

**Criados:**
- `src/Taskitify.Core/Lyrics/KaraokeParsers.cs`: TTML, Lyricsfile e o construtor de texto mais âncoras.
- `src/Taskitify.Core/Lyrics/KaraokeService.cs`: serviço, cache, consulta e pausa por limite de requisições.
- `src/Taskitify.Core/Lyrics/KaraokeProviders.cs`: lrc.red, LRCLIB (karaokê) e o cliente LRCLIB compartilhado.
- `src/Taskitify/ViewModels/LyricsSourceItem.cs`
- `tests/Taskitify.Tests/KaraokeTests.cs`
- `docs/lyrics-sources.md` (este arquivo)

**Modificados:**
- **Núcleo:**
  - `Models.cs`: modelo e pausas;
  - `LyricFlow.cs`: só tempo real e preenchimento dos vocais de apoio;
  - `LyricsCache.cs`: medição, limpeza recursiva e revisão das entradas antigas;
  - `SpotifyLyricsProvider.cs`: `SYLLABLE_SYNCED`.
- **App:**
  - `LyricsSyncService.cs`: disponibilidade, preferência, estado efetivo e troca de linha do tempo;
  - `App.xaml.cs`: a ligação das peças;
  - `Models.cs`: `LyricsSourcesSettings`;
  - `Log.cs`: o caminho do cache de karaokê;
  - `LyricsCredit.cs`;
  - `MiniPlayerViewModel.cs`, `MiniPlayerWindow.xaml` e `.cs`: o botão, os vocais de apoio, o lado do dueto e o crédito com link;
  - `SettingsViewModel.cs`, `SettingsWindow.xaml` e `.cs`: as abas Player e Fontes de letras;
  - `TourWindow.cs`;
  - os 6 arquivos de idioma.

## 7. Resultados dos testes

300 testes passando: 270 de antes e 30 novos de karaokê. Todos usam fixtures sintéticas com a mesma estrutura das respostas reais e texto inventado; nenhuma letra real foi para o repositório. Os testes cobrem:

- **TTML:** palavras, sílabas, vocais de apoio, lados de dueto, agente de grupo, pausa entre palavras, TTML só com tempo de linha, documentos malformados e formatos de tempo.
- **Lyricsfile:** palavras, aspas, fim de linha, sem palavras e malformado.
- **Spotify:** `SYLLABLE_SYNCED` com e sem sílabas.
- **Serviço:**
  - qualidade vence ordem, e a ordem desempata;
  - fontes desligadas não são consultadas;
  - uma fonte com falha ou lenta não esconde a resposta de outra;
  - versão de mesmo nome mas de outra música é recusada;
  - cache positivo e negativo (com expiração); falha não vai para o cache;
  - busca compartilhada, com um dos consumidores desistindo (música trocada);
  - pausa por 429 com `Retry-After`.
- **lrc.red:** casa a versão certa (descarta a ao vivo) e transforma limite de requisições em pausa.
- **LRCLIB:** o Lyricsfile com palavras e as requisições compartilhadas com o fallback.
- **Pausas instrumentais** e **combinação de vocais de apoio** com preenchimento.
- **Configurações:** ordem e normalização das fontes, persistência.

**Teste real, com uma cópia dos dados do usuário:** em "Something About Us" (Daft Punk), a letra apareceu do cache em 0,09 s e o karaokê do lrc.red (por palavra) chegou 1,7 s depois. O preenchimento seguiu palavra por palavra e a pausa da introdução apareceu. As abas novas foram conferidas em renderização offscreen.

## 8. Impacto no desempenho

- **Nenhum polling novo.** A busca roda uma vez por música, em segundo plano, depois da letra, e é cancelada quando a música muda.
- **Requisições por música:** de 1 a 4 (lrc.red: busca e um TTML, às vezes até 3; LRCLIB: zero quando o fallback já buscou, senão uma ou duas). Depois disso, o cache em disco atende, sem rede.
- **Latência medida:** mediana de 254 ms (lrc.red) e 332 ms (LRCLIB). As fontes rodam em paralelo, então a espera é a da mais lenta, limitada a 8 s.
- **Renderização:** o preenchimento dos vocais de apoio usa o mesmo mecanismo da linha principal (30 quadros/s só enquanto a linha está sendo cantada); sem karaokê, custo zero.
- **Memória do app** depois da mudança: cerca de 115 MB privados. **Não foi feita** a comparação antes/depois com `tools/perf/Measure-Taskitify.ps1`; é o próximo passo para confirmar que não houve regressão.

## 9. Limitações conhecidas

- **Cobertura:** 4 das 30 músicas de teste não têm karaokê real nas fontes padrão.
- **Correspondência por nome:** as fontes buscam por título e artista (o Taskitify não recebe o ISRC do Windows). A versão errada é rara, mas possível.
- **Vocais de apoio de uma linha** aparecem juntos, como uma única linha secundária.
- **Duetos com letra centralizada** ficam centralizados (não dá para espelhar o centro).
- **O ponto do botão** indica vocais de apoio ou dueto, sem diferenciar qual dos dois.
- **Trocar para karaokê** reconstrói a lista de linhas. A posição é preservada, mas se o karaokê chegar no meio de uma linha, a lista pula para a linha certa (normalmente chega menos de 2 s depois da letra).
- **O lrc.red e o LRCLIB** são serviços de terceiros: se saírem do ar, o karaokê some e a letra comum continua funcionando.

## 10. Sugestões para evoluções futuras

1. **Unison como terceira fonte opcional** (desligada por padrão): cobre lacunas do lrc.red. Exige crédito ao autor de cada letra.
2. **Busca por ISRC:** se um dia a resposta do Spotify trouxer o ISRC, o lrc.red responde direto por ele, sem ambiguidade de versão.
3. **Animações por sílaba**, como o beautiful-lyrics: palavra que sobe e brilha ao ser cantada, e destaque de palavras longas e sustentadas.
4. **Romanização** de japonês, chinês e coreano.
5. **Medição com `Measure-Taskitify.ps1`** antes e depois, com músicas com e sem karaokê.
6. **Comparar a letra do karaokê com a do Spotify** (texto e tempos), para trocar sem reconstruir a lista quando forem a mesma letra.
