# Taskitify MSIX

O script gera um pacote **x64 para Windows 11**, com .NET incluído, ícone no menu Iniciar, seis idiomas e integração com a inicialização do Windows. O executável portátil continua disponível separadamente.

## Gerar um pacote local

Pré-requisitos: Windows, .NET SDK 8 e Windows SDK com `MakeAppx.exe`.

Na raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging/Build-Msix.ps1
```

Saída: `dist/Taskitify_1.0.0.0_x64_local-unsigned.msix`, junto com o SHA-256. A identidade `Taskitify.Local` é provisória. Esse arquivo não está assinado e não deve ser enviado à loja com essa identidade. Para instalação local, é necessário assinar com um certificado confiável para a máquina de teste. O script não cria certificados nem altera a confiança do Windows.

## Gerar para a Microsoft Store

A identidade oficial do Taskitify está salva em `StoreIdentity.json` (Store ID: `9NC2227DJ01D`). Para gerar o pacote com esses dados:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging/Build-StoreMsix.ps1 -Version 1.0.0.0
```

Envie `dist/Taskitify_1.0.0.0_x64_store.msix` na submissão do Taskitify no Partner Center. PFN e SID são identificadores derivados; ficam registrados para conferência e não são inseridos como atributos no manifesto.

### Configurar outra identidade

Reserve o aplicativo no Partner Center e copie os valores de **Product identity**:

- `Package/Identity/Name`
- `Package/Identity/Publisher`
- `Package/Properties/PublisherDisplayName`

Use os valores exatos, substituindo os exemplos:

```powershell
.\packaging\Build-Msix.ps1 -ForStore `
    -IdentityName '12345SeuNome.Taskitify' `
    -Publisher 'CN=00000000-0000-0000-0000-000000000000' `
    -PublisherDisplayName 'Seu nome no Partner Center' `
    -DisplayName 'Taskitify' `
    -Version '1.0.0.0'
```

A saída será `dist/Taskitify_1.0.0.0_x64_store.msix`. O envio à Store não exige assinatura local; a Microsoft assina o pacote após a certificação. O nome de exibição precisa corresponder a um nome reservado. Incremente a versão nas atualizações e mantenha o quarto componente em zero. `-ForStore` recusa a identidade local padrão, mas a correspondência com sua conta só pode ser confirmada no Partner Center.

O script valida o manifesto com MakeAppx e guarda o log e os arquivos intermediários em `publish-msix/<build-id>/`. Essa validação não substitui a certificação da Store nem testes de instalação e execução do pacote assinado.

## Integração com o Windows

- `runFullTrust` é necessário porque o aplicativo é WPF/Win32 e acompanha a sessão de mídia, o mixer de áudio e a barra de tarefas. Explique essa finalidade ao preencher a submissão.
- `globalMediaControl` permite consultar a faixa e controlar a reprodução do Spotify pela API de sessões de mídia do Windows.
- Na versão MSIX, “Start with Windows” usa `Windows.ApplicationModel.StartupTask`, desativado por padrão. O app respeita bloqueios feitos pelo usuário ou por políticas do Windows. Na versão portátil, continua usando o registro do usuário.
- O pacote não escreve em sua pasta de instalação. Configurações e cache continuam usando as pastas de dados do usuário, sujeitas ao redirecionamento de dados do MSIX.
- Antes de testar o pacote instalado, encerre a versão portátil: ambas usam a mesma proteção contra duas instâncias simultâneas.

Referências: [empacotamento manual](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion), [identidade do aplicativo](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details), [assinatura MSIX](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
