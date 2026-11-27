# Lyric Flow e Lyric Motion

No Windows, abra **Configurações → Mini player → Letras**. As opções de Flow e Motion são independentes do modo, fundo, fonte, alinhamento, destaque e controles. Os efeitos começam desativados em configurações existentes e novas. Aplicar um preset continua alterando apenas a aparência prevista pelo preset.

| Opção | Comportamento |
| --- | --- |
| Letras em fluxo → Desativado | Destaque normal do verso. |
| Letras em fluxo → Linear | Preenche a largura desenhada em velocidade constante, da entrada do verso à próxima entrada. No último verso usa o fim da música quando disponível. |
| Letras em fluxo → Adaptativo | Distribui o tempo entre palavras com uma estimativa leve. Quando a janela é muito maior que a frase, termina o preenchimento e mantém o verso iluminado. |
| Letras em fluxo → Automático | Usa marcas Enhanced LRC válidas quando disponíveis. Sem marcas, usa a estimativa ou destaca o verso inteiro quando faltam dados suficientes. |
| Movimento das letras → Desativado | Mantém a transição de rolagem padrão. |
| Movimento das letras → Suave | Usa desaceleração cúbica e aumenta a separação entre verso ativo e vizinhos. |
| Movimento das letras → Expressivo | Usa desaceleração quártica com maior variação de escala. |

A rolagem e os ajustes de opacidade e escala respeitam as opções de animação do aplicativo e do Windows. Saltos manuais reposicionam a letra imediatamente. **Configurações → Barra de tarefas → Conteúdo** oferece uma seleção de Flow própria. Ela aparece somente quando a barra mostra uma letra sincronizada. O estilo **Karaoke** do Estúdio de cartões é um recurso independente. A primeira amostra de cor do Estúdio é o destaque automático do álbum e parte do tom cromático da capa que o aplicativo já extrai.

## Sincronização

- O Spotify fornece marcas por verso no formato já consumido pelo aplicativo. O LRCLIB fornece texto e LRC. A pasta local também aceita LRC, inclusive Enhanced LRC. O parser preserva as marcas Enhanced LRC existentes no texto recebido e o cache já guarda o LRC bruto. A mudança usa os provedores e o cache existentes.
- A estimativa usa agrupamentos vocálicos, comprimento e pontuação para repartir o tempo. A mediana local descarta janelas incompatíveis com canto contínuo. Quando faltam dados locais, usa o andamento representativo da faixa ou uma taxa inicial conservadora. Palavras sustentadas e pausas continuam sendo estimativas. Uma marca Enhanced LRC final ausente requer estimativa apenas para o último trecho.
- Uma música pode ter linhas vazias marcando intervalos instrumentais. Duplicatas preservam a ordem do LRC e a última entrada no mesmo instante é a ativa. Letras sem timestamps seguem a apresentação existente.
- A posição usada na tela vem de **PlaybackClock**, via monitor de mídia e correção de offset do **LyricsSyncService**. O relógio reancora ao ajustar a velocidade reportada. A notificação de retomada do Windows atualiza a sessão existente.

## Renderização

- A máscara de opacidade usa os limites de cursor do próprio TextBlock após o layout, em ordem de leitura. Ao quebrar um verso, completa a primeira linha antes da próxima. Ajustes de tinta e movimento mantêm a mesma geometria.
- O avanço da máscara atualiza retângulos reaproveitados. São 30 atualizações por segundo como alvo no mini player e 20 na barra. O temporizador de pintura para quando a linha está iluminada, pausada, fora da vista ou com Flow desligado. O cache é refeito quando as métricas relevantes mudam.
- A rolagem usa TranslateTransform. O verso usa ScaleTransform e Opacity. As animações substituem a anterior a partir do estado visual atual. A duração diminui em entradas próximas. Foi mantida uma transição coordenada sem escalonamento por verso para preservar a continuidade entre o bloco, a tinta e o relógio.
- A Inter Display expressiva é a referência dos modos de Motion. A fonte e o alinhamento escolhidos permanecem. Dentro de uma linha animada o peso fica estável e a hierarquia é feita com escala e opacidade. Isso mantém a palavra na mesma linha durante a transição.

## Plataformas e verificação

A interface de Flow e Motion e a adaptação da barra são WPF/Windows. O parser Enhanced LRC, o motor temporal determinístico e o PlaybackClock ficam no Core, disponível ao Linux. A interface Avalonia segue com o destaque por verso.

O [harness de revisão](../tools/LyricMotionReview) usa as janelas WPF reais, com um relógio de teste e entradas LRC. Ele permite repetir a validação de layout e comparar modos sem procurar letras na rede.
