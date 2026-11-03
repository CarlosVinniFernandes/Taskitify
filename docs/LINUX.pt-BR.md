# Taskitify no Linux

O Taskitify acompanha o **Spotify Desktop** pelo MPRIS (D-Bus) e mostra a linha da letra sincronizada numa
barra flutuante ou, no GNOME, **na barra superior ao lado do relógio**: o equivalente Linux de cobrir o
widget de clima do Windows 11. As letras vêm do LRCLIB (e dos seus arquivos `.lrc`); não precisa de login
no Spotify.

[English](LINUX.md)

## Instalação

Baixe o `taskitify_<versão>_amd64.deb` nas releases e instale:

```bash
sudo apt install ./taskitify_1.0.0_amd64.deb
```

Ou gere você mesmo (precisa do .NET 8 SDK e do `dpkg-deb`):

```bash
packaging/linux/build-deb.sh 1.0.0
```

Rode `taskitify` (ou abra pelo menu de aplicativos). Ele fica na bandeja e na barra superior; deixe o Spotify
tocando e a linha aparece.

## A letra na barra superior do GNOME

O pacote traz uma extensão do GNOME Shell, `taskitify-lyrics@carlosvinnifernandes.github.io`, para o GNOME
Shell 45 a 50. Ative e **saia e entre de novo na sessão** (no Wayland o GNOME só descobre extensões novas no
login):

```bash
gnome-extensions enable taskitify-lyrics@carlosvinnifernandes.github.io
```

A linha aparece logo depois do botão Atividades, à esquerda do relógio, e some quando nada está tocando. Com
a extensão ligada, a barra flutuante é desligada sozinha; para trazê-la de volta, **Configurações → Barra de
letras flutuante**.

Como funciona: o app escreve a linha atual em `$XDG_RUNTIME_DIR/taskitify/line.json`
(`{"line", "title", "artist", "playing"}`) e a extensão só observa esse arquivo. Qualquer outra coisa pode ler,
por exemplo um módulo do Waybar ou Polybar:

```bash
jq -r .line "$XDG_RUNTIME_DIR/taskitify/line.json"
```

## Barra flutuante e mini player

- **Arraste** a barra para onde quiser; a posição é lembrada. Botão direito → *Restaurar posição da barra*.
- **Clique** na barra (ou no ícone da bandeja) para abrir o mini player: capa, controles, progresso e a letra
  inteira com a linha atual em destaque. Clique numa linha para pular até ela; `Esc` fecha.
- **Botão direito** na barra: buscar a letra de novo, configurações, sair.
- A barra é uma janela sempre no topo. No Wayland o GNOME a roda pelo XWayland, o que preserva a posição;
  compositores que ignoram "sempre no topo" (alguns tiling) podem precisar de uma regra de janela.

## Configurações e arquivos

| O quê | Onde |
|---|---|
| Configurações | `~/.config/taskitify/settings.json` |
| Cache de letras | `~/.cache/taskitify/lyrics/` |
| Seus `.lrc` | `~/.local/share/taskitify/lyrics/` (`Artista - Música.lrc`; usados antes do LRCLIB) |
| Iniciar ao entrar | `~/.config/autostart/taskitify.desktop` (Configurações → Iniciar ao entrar na sessão) |

As variáveis `XDG_*` são respeitadas. Os 6 idiomas do app do Windows são compartilhados (Configurações →
Idioma; o padrão segue o idioma do sistema).

## Letras do próprio Spotify (opcional, experimental)

Mesma ideia do app do Windows: Spotify primeiro, LRCLIB quando o Spotify não tem letra sincronizada.
**Configurações → Letras do Spotify → Conectar** abre uma janela de login do Spotify (WebKitGTK). Precisa de
`python3-gi` e `gir1.2-webkit2-4.1` (o `.deb` recomenda):

```bash
sudo apt install python3-gi gir1.2-webkit2-4.1
```

O login fica num perfil só dele, `~/.local/share/taskitify/spotify-web-session`; os perfis do Spotify Desktop e
do navegador nunca são lidos. O web player escondido só abre quando precisa de token ou busca e fecha 30 s
depois. **Desconectar** apaga esse perfil. Usa o serviço não oficial do player web do Spotify, então pode parar
de funcionar se o Spotify mudar; o LRCLIB continua.

## O que ainda não existe no Linux
- **Volume** se controla no Spotify ou no mixer do sistema, não pela barra.
- O Spotify no navegador não é acompanhado (igual ao Windows, de propósito).

## Desenvolvimento

```bash
dotnet test tests/Taskitify.Tests                                  # Core, compartilhado com o Windows
dbus-run-session -- dotnet test tests/Taskitify.Platform.Linux.Tests   # MPRIS contra um player falso
dotnet run --project src/Taskitify.Linux
xvfb-run -a python3 tests/spotify-web-helper/test_helper.py        # o helper WebKitGTK, contra um web player falso local
```

Sem precisar de conta no Spotify: `tools/fake-spotify/fake_spotify.py` toca uma playlist falsa em
`org.mpris.MediaPlayer2.spotify`, responde a play/pause/next/previous/seek/shuffle/repeat e usa músicas que
existem no LRCLIB. Precisa de `python3-dbus` e `python3-gi`.

O app do Windows continua com a própria implementação GSMTC (`SpotifyMonitor`), sem mudanças.
