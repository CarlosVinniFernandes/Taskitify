<p align="center">
  <img src="docs/images/logo.png" alt="Taskitify" width="140" />
</p>

<h1 align="center">Taskitify</h1>

<p align="center"><a href="README.md">English</a> · <b>Português</b></p>

<p align="center">
  <b>As letras do Spotify na barra de tarefas do Windows 11, no lugar do widget de clima.</b><br />
  Sincronizadas, com a cor da capa do álbum e um mini player a um clique.
</p>

<p align="center">
  <a href="https://github.com/CarlosVinniFernandes/Taskitify/releases/latest"><b>⬇ Baixar</b></a> ·
  <a href="#como-usar">Como usar</a> ·
  <a href="#personalização">Personalização</a> ·
  <a href="docs/COMO-FUNCIONA.md">Como funciona</a>
</p>

<p align="center">
  <img src="docs/images/bar.png" alt="Letra sincronizada na barra de tarefas" width="640" />
</p>

---

## O que é

O Taskitify mostra a linha da música que está tocando no Spotify **direto na barra de tarefas**, ocupando o espaço do widget de clima do Windows 11.

- **Tocando:** a letra aparece no lugar do clima e acompanha a música linha por linha.
- **Pausou, parou ou fechou o Spotify:** o clima volta na hora. Se preferir, o Taskitify fica minimizado em vez de sumir.
- **Clicou na letra:** abre um mini player com capa, controles, progresso e a letra rolando.

Não precisa de Spicetify, de login no Spotify nem de Premium. O app lê a música pelas sessões de mídia do próprio Windows e busca as letras no [LRCLIB](https://lrclib.net), um banco aberto de letras sincronizadas.

## Recursos

| | |
|---|---|
| 🎤 **Letra sincronizada** | A linha atual na barra, em uma ou duas linhas, com transição suave. Acompanha pausas, avanços e retrocessos. |
| 🎨 **Cores da capa** | O fundo é um degradê tirado da capa do álbum, como no Spotify, e muda suavemente a cada música. |
| 🎛️ **Player na barra** | Anterior, tocar/pausar, próxima, aleatório, repetir, volume e barra de progresso. Você escolhe quais aparecem. |
| 🪟 **Mini player** | Um clique na letra abre a capa, os controles e a letra completa rolando, com a linha atual em destaque. Clique numa linha para pular até ela. |
| ⏸️ **Player minimizado** | Ao pausar, o Taskitify pode encolher para o tamanho do clima (capa, nome e botão de tocar) em vez de sumir. |
| 🧩 **Monte a sua barra** | Uma réplica do player nas configurações: clique em cada elemento para ligar (verde) ou desligar (branco). |
| 🖥️ **Feito para o Windows 11** | Some junto com a barra em tela cheia, não rouba o foco, não aparece no Alt+Tab e respeita DPI e tema escuro. |
| 🌍 **6 idiomas** | Português, English, Español, Français, Deutsch e Italiano. Troque em Configurações → Geral → Idioma (o padrão é inglês). |
| 🪶 **Leve** | Cerca de 60 MB de memória e ~1% de um núcleo enquanto toca; pausado, fica parado. Nada roda como administrador. |

<p align="center">
  <img src="docs/images/miniplayer.png" alt="Mini player com a letra rolando" width="360" />
  &nbsp;&nbsp;
  <img src="docs/images/minimized.png" alt="Player minimizado ao pausar" width="300" />
</p>
<p align="center"><sub>Mini player (clique na letra) · player minimizado enquanto a música está pausada</sub></p>

## Instalação

1. Baixe o `Taskitify-win-x64.zip` na página de [**Releases**](https://github.com/CarlosVinniFernandes/Taskitify/releases/latest).
2. Extraia o `Taskitify.exe` para uma pasta fixa, por exemplo `Documentos\Taskitify`.
3. Abra o `Taskitify.exe` e toque uma música no Spotify.

> **"O Windows protegeu o computador"?** O executável não tem assinatura digital paga, então o SmartScreen avisa na primeira vez. Clique em **Mais informações** e depois em **Executar assim mesmo**. O código é aberto: você pode conferir tudo aqui ou [compilar você mesmo](#compilar).

**Requisitos:** Windows 11 (ou Windows 10 versão 1809 ou mais nova) de 64 bits, e o **Spotify Desktop** instalado. Não precisa instalar .NET.

## Como usar

| Ação | O que acontece |
|---|---|
| Tocar uma música no Spotify | A letra aparece no lugar do clima |
| Clicar na letra | Abre ou fecha o mini player |
| `Esc` ou clicar fora | Fecha o mini player |
| Clicar numa linha do mini player | Pula a música para aquela linha |
| Roda do mouse no botão de volume | Ajusta o volume do Spotify (clique silencia) |
| Clique direito na letra | Buscar letra de novo, mostrar música e artista, calibrar posição, configurações, sair |
| Ícone na bandeja | Configurações, mini player e sair |

Para abrir junto com o Windows: **Configurações → Geral → Iniciar com o Windows**.

## Personalização

Em **Configurações → Estilo**, o Taskitify mostra uma réplica do player. Clique em cada elemento, inclusive na letra: **verde** aparece na barra de tarefas, **branco** fica escondido. O player minimizado (quando a música está pausada) tem o seu próprio montador e a sua própria largura.

<p align="center">
  <img src="docs/images/settings-style.png" alt="Configurações de estilo com o montador do player" width="480" />
</p>

Também dá para escolher:

- **Fundo:** degradê da capa do álbum ou a mesma cor da barra de tarefas.
- **Texto principal:** a letra ou o nome da música.
- **Barra de progresso:** abaixo de tudo ou à direita da capa (com a capa maior).
- **Ao pausar:** esconder (o clima volta) ou minimizar o player.
- **Sem letra sincronizada:** mostrar o nome da música ou esconder.
- **Tamanho da fonte, largura, animações** e rolagem do texto que não cabe.
- **Ajuste de tempo** (offset), caso a letra esteja adiantada ou atrasada.
- **Calibração da posição**, se o Windows posicionar o clima num lugar diferente.

## Privacidade

- Sem conta, sem login e sem telemetria.
- A única requisição de rede é a busca da letra no [LRCLIB](https://lrclib.net), com o nome da música, o artista, o álbum e a duração.
- Configurações e cache de letras ficam só no seu computador: `%AppData%\Taskitify` e `%LocalAppData%\Taskitify`.

## Perguntas frequentes

**A letra não apareceu.** Algumas músicas não estão no LRCLIB; nesse caso a barra mostra o nome da música. Quando o LRCLIB está sobrecarregado, a letra pode levar alguns segundos para chegar. Clique com o botão direito na letra → **Buscar letra de novo** para forçar uma nova busca.

**A letra está adiantada ou atrasada.** Configurações → Sincronização → **Ajuste de tempo**.

**O widget apareceu no lugar errado.** Clique com o botão direito → **Calibrar posição**, arraste e ajuste a largura com a roda do mouse.

**Funciona com o Spotify no navegador?** Não. Só com o Spotify Desktop (de propósito, para não confundir com vídeos e outras abas).

**Dá para curtir a música pelo Taskitify?** Não. Salvar na biblioteca exige a API do Spotify com login, que o projeto evita.

## Compilar

Requer o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build                                   # compila
dotnet test                                    # 81 testes (letras, sincronização, cores, layout)
dotnet run --project src/Taskitify             # roda

# Executável único, sem precisar de .NET instalado (~179 MB; ~69 MB zipado)
dotnet publish src/Taskitify -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
```

Para gerar o zip de distribuição, compacte `publish\Taskitify.exe` junto com `dist\LEIA-ME.txt`. A arquitetura, as decisões técnicas e as limitações conhecidas estão em [**docs/COMO-FUNCIONA.md**](docs/COMO-FUNCIONA.md).

## Créditos

- Letras: [LRCLIB](https://lrclib.net).
- Inspiração visual: [Spicetify Lyric Miniplayer](https://github.com/FO-SS/Spictify-Lyric-Miniplayer). O Taskitify é um app nativo e independente, não uma extensão do Spotify.
- Spotify é marca registrada da Spotify AB. Este projeto não é afiliado ao Spotify nem à Microsoft.

## Licença

[MIT](LICENSE) © 2026 Carlos Fernandes
