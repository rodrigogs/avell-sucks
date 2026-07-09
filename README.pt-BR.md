<div align="right">

[English](README.md) · **Português**

</div>

<div align="center">

<img src="docs/assets/logo.png" alt="AvellSucks" width="120" height="120" />

# AvellSucks

**Um control center não oficial para o notebook gamer da Avell: ventoinha, potência da CPU, plano de energia do Windows e RGB, falando direto com o hardware, com honestidade.**

[![Plataforma](https://img.shields.io/badge/plataforma-Windows%2010%2B%20x64-141018?labelColor=1c1622)](#instala%C3%A7%C3%A3o)
[![.NET](https://img.shields.io/badge/.NET-10.0-A855F7?labelColor=1c1622)](https://dotnet.microsoft.com)
[![UI](https://img.shields.io/badge/UI-WPF-22D3EE?labelColor=1c1622)](#)
[![i18n](https://img.shields.io/badge/i18n-EN%20%7C%20PT--BR-FF2E88?labelColor=1c1622)](#idiomas)
[![Testes](https://img.shields.io/badge/testes-82%20passando-34E5A0?labelColor=1c1622)](#compilar-do-c%C3%B3digo)

<sub>Não oficial · sem vínculo com, aprovação de, ou suporte da Avell. Use no hardware para o qual foi feito.</sub>

</div>

<h4 align="center">
  <a href="#por-que-isto-existe">Por quê</a> &nbsp;·&nbsp;
  <a href="#o-que-ele-faz">Recursos</a> &nbsp;·&nbsp;
  <a href="#instala%C3%A7%C3%A3o">Instalação</a> &nbsp;·&nbsp;
  <a href="#como-funciona">Como funciona</a> &nbsp;·&nbsp;
  <a href="#seguran%C3%A7a">Segurança</a> &nbsp;·&nbsp;
  <a href="#compilar-do-c%C3%B3digo">Compilar</a>
</h4>

<div align="center">

<img src="docs/assets/dashboard.png" alt="Painel do AvellSucks" width="820" />

</div>

---

## Por que isto existe

Comprei um Avell top de linha em 2018. Quando o Windows 11 ganhou a primeira
grande atualização (a versão 22H2, lançada em **20 de setembro de 2022**, que a
Microsoft manteve por meros **24 meses**), o "Gaming Center" da fabricante, o app
que controla a curva da ventoinha, os modos de desempenho, a iluminação do
teclado, toda a personalidade térmica e de energia da máquina, já estava
**descontinuado e abandonado**: datado, pesado, sem manutenção, e ainda assim a
única forma oficial de controlar o hardware do próprio notebook. Uns quatro anos
de uso, e o software que rodava a minha máquina já estava morto.

Então a escolha era conviver com um bloatware abandonado entre mim e meu próprio
silício, ou substituir. Isto é a substituição. O nome não é sutil de propósito:
ele nomeia o motivo pelo qual o projeto precisou existir.

**O AvellSucks faz o que o app original fazia, melhor e com honestidade:** lê e
escreve nos mesmos registradores do Embedded Controller (EC) que a fabricante
usava, troca os mesmos planos de energia do Windows, e nunca mente sobre se uma
escrita no hardware realmente fixou.

## O teclado (por que o RGB não é testado)

Tem um segundo motivo desse notebook ter deixado um gosto amargo, e é por isso que
a aba RGB sai incompleta e não verificada.

Com uns dois anos de uso, abri a máquina pra limpar. Por dentro, o conector da
fita do teclado estava rachado e preso com um pedaço de fita. Não fui eu. De
fábrica. Foi assim que ele veio.

Nessa época o teclado já estava falhando, e não tinha como consertar de verdade.
Procurei a Avell. Não tinham nada a oferecer, a garantia já tinha vencido, e não
fez diferença o defeito ser deles desde o primeiro dia.

Então o teclado embutido desta máquina não funciona mais. O código da iluminação
RGB (ITE HID) está escrito e ligado na interface, mas não tenho como testar contra
um teclado morto. Ele fica atrás de um estado honesto de "indisponível" até haver
hardware pra provar.

## O que ele faz

- **Ventoinha**: modos (auto, boost, personalizado, L1-L5) e uma curva
  temperatura→PWM personalizada. Aplica ao vivo conforme você edita; sem botão de
  aplicar.
- **Desempenho**: quatro modos (Gaming / Alto / Equilibrado / Economia) que trocam
  o **plano de energia do Windows** ativo e escrevem os bytes de limite de potência
  da CPU (PL1/PL2/PL4).
- **RGB**: superfície de iluminação do teclado (ITE HID). Interface e contrato
  prontos, mas o backend está incompleto e não testado (veja [acima](#o-teclado-por-que-o-rgb-n%C3%A3o-%C3%A9-testado)).
- **Painel**: carga de CPU/GPU, temperaturas, clocks, memória, disco, rede e o
  perfil de refrigeração ativo, ao vivo, a ~1 Hz.
- **Reativo**: mudanças feitas fora do app (o app antigo da fabricante, a tecla Fn
  física da ventoinha, outro trocador de plano de energia) aparecem aqui em poucos
  segundos. Ele espelha o dispositivo; nunca assume que sua própria última escrita
  ainda é verdade.
- **Configurações**: idioma, iniciar com o Windows, iniciar minimizado, e esconder
  na bandeja ao minimizar. As preferências ficam salvas em
  `%AppData%\AvellSucks\settings.json`.
- **Idiomas**: inglês e português, alternáveis ao vivo em Configurações, sem
  reiniciar. O padrão segue o idioma de exibição do Windows: português num sistema
  pt/pt-BR, inglês no resto. Se você mudar, a escolha fica salva.

<sub>**Marca:** um instrumento de desempenho cyberpunk: *carregado, preciso, vivo*. Neon magenta→ciano sobre violeta-preto profundo.</sub>

## Instalação

> **Requer Windows 10/11 (x64) e direitos de administrador.** O app fala com o
> Embedded Controller e com sensores ring-0, então precisa rodar elevado.

1. Baixe o **`AvellSucks-Setup.exe`** mais recente na página de
   [**Releases**](https://github.com/rodrigogs/avell-sucks/releases/latest).
2. Execute. Ele instala per-machine no `Arquivos de Programas`, adiciona um atalho
   no menu iniciar, e registra um desinstalador em *Adicionar ou remover programas*.
3. Abra o **AvellSucks** e aprove o prompt do UAC.

O app verifica o GitHub por versões mais novas e pode se atualizar sozinho em
**Configurações → Atualizações** (baixa o novo instalador e relança em silêncio).

> **Nota, instalador não assinado.** Não há certificado de assinatura de código,
> então na primeira vez que você rodar o `AvellSucks-Setup.exe` o SmartScreen do
> Windows vai dizer *"O Windows protegeu o seu PC / editor desconhecido"*. Clique
> em **Mais informações → Executar assim mesmo**. Isso é esperado para uma
> ferramenta pessoal e não assinada; as atualizações seguintes são aplicadas pelo
> atualizador já confiável, então o aviso só aparece na primeira instalação.

### Iniciar com o Windows

O botão **Iniciar com o Windows** registra uma Tarefa Agendada com *privilégios
mais altos* em vez de uma entrada na chave Run, que é a forma suportada de abrir um
app elevado no logon **sem** um prompt do UAC a cada boot.

## Como funciona

Tudo foi obtido por engenharia reversa do app original descompilado mais muita
cutucada ao vivo no hardware.

### Acesso ao EC: interface de teste WMI ACPI
A fabricante nunca usou um driver próprio. Todo o estado de ventoinha/energia vive
na **RAM do Embedded Controller**, alcançada por um método WMI ACPI em `root\WMI`:
`AcpiTest_MULong.GetSetULong` (instância `ACPI\PNP0C14\1_1`).

- **Leitura:** `Data = 0x100_0000_0000 | addr` (2^40 + addr); o valor de retorno é o byte.
- **Escrita:** `Data = (value << 16) | addr`: **sem flag de leitura** (incluir ela
  faz o EC ignorar a escrita em silêncio; isso custou uma sessão de debug pra achar).

### Registradores confirmados
| Endereço | Significado |
|---|---|
| `0x751` (1873) | byte de controle da ventoinha, 0 auto, 0x40 boost, 0xA0 personalizado, 0x81-0x85 L1-L5 |
| `0x743`-`0x747` (1859-1863) | níveis de PWM personalizados |
| `0x783`/`0x784`/`0x785` (1923-1925) | bytes de ajuste PL1/PL2/PL4 (watts) |
| `0x730`-`0x732` / `0x734`-`0x736` | padrões de PL Gaming / Office (somente leitura) |

Nesta placa os registradores de PL leem `0`: os limites reais da CPU são geridos
pelo **Intel XTU / MSR**, não pelo EC, então a aba Desempenho mostra os watts
nominais do preset e a alavanca principal do modo é o plano de energia do Windows.

### Planos de energia
Os quatro modos de desempenho mapeiam 1:1 para esquemas dedicados do Windows que a
máquina traz (`MyGamingMode` / `MyHighPerformance` / `MyBalanced` /
`MyPowerSaving`), trocados via `powercfg /setactive`.

### Pipeline de escrita segura
Toda escrita no EC passa pelo `SafeEcWriter`:
**gate → allowlist → snapshot-antes → escrita → verificação por releitura →
rollback em divergência → auditoria JSONL.** Uma escrita bloqueada ou falha aparece
como bloqueada/falha na interface, nunca falseada. As releituras dos registradores
de controle toleram os bits de status transitórios do firmware e tentam de novo com
backoff (o EC engole escritas no meio da transição, principalmente ao sair do Boost).

### Arquitetura
Solução .NET 10 (`app/AvellSucks.Replacement.slnx`):
- `AvellSucks.Core`: contratos de hardware, pipeline de escrita segura, modelos (portável).
- `AvellSucks.Core.Windows`: `WmiEcBackend` (leitura/escrita WMI no EC).
- `AvellSucks.Api` / `AvellSucks.Server`: API de controle ASP.NET local opcional
  (só loopback), expondo `/api/fan/*`, `/api/system/snapshot`, `/events` (SSE).
- `AvellSucks.UI`: o app WPF (escuro, cyberpunk), telemetria via
  LibreHardwareMonitor, reconciliadores reativos por aba. Localização em tempo de
  execução (`.resx` + um provedor `Loc` e a markup extension `{loc:Tr}`) troca o
  idioma ao vivo; as preferências ficam em JSON sob `%AppData%`, os logs e a
  auditoria de escrita do EC sob `%ProgramData%\AvellSucks`.

## Segurança

Isto escreve em registradores de hardware de baixo nível. A allowlist restringe
*quais* pares (endereço, valor) são permitidos; toda escrita é verificada por
releitura, revertida em divergência, e auditada em JSONL. As escritas de limite de
potência e de EC são as pontas afiadas: a interface deixa o estado
gated/bloqueado/falho legível, nunca o esconde. Use no hardware para o qual foi feito.

## Compilar do código

**Requisitos:** Windows no Avell, .NET 10 SDK, rodar **como Administrador** (acesso
a sensores ring-0 + escritas WMI no EC precisam disso). WPF → só Windows.

```powershell
# a partir do diretório app
dotnet build AvellSucks.Replacement.slnx

# rodar o app WPF (elevado)
dotnet run --project src/AvellSucks.UI

# ou o servidor de controle local + API
dotnet run --project src/AvellSucks.Server -- 5055

# testes (82: pipeline de escrita segura, allowlist, gate de escrita, controlador da ventoinha, log de auditoria)
dotnet test AvellSucks.Replacement.slnx
```

**Publicar um release:** envie uma tag de versão (`git tag v1.2.3 && git push
origin v1.2.3`). O [workflow de release](.github/workflows/release.yml) publica um
build self-contained win-x64, compila o instalador Inno Setup, e anexa o
`AvellSucks-Setup.exe` a um GitHub Release.

**Gate de escrita:** elevado ⇒ escritas no hardware ficam **ligadas por padrão**.
Sobrescreva com a variável de ambiente `GAMINGCENTER_ALLOW_EC_WRITES` (`0`/`false`
força off, modo preview/demo; `1` força on num processo de dev não elevado). *(A
variável mantém o nome original por compatibilidade com o pipeline do servidor.)*

**Nota de performance:** rode a partir de uma cópia em **disco local**, não pelo
caminho UNC do WSL: carregar assemblies por `\\wsl.localhost\...` adiciona ~9 s ao
tempo de inicialização.

## Artefatos de engenharia reversa

- `inventory.json`, `process-detail.json`, `ec-read-probe.json`: inventário + sondagens do EC.
- `analysis/*.strings.txt`: extração estática de strings.
- `scripts/*.ps1`: scripts reproduzíveis de inventário/sondagem no Windows.
- `notes/`, `DESIGN.md` (spec da arquitetura reativa), notas de RE + design.

## Licença

Projeto pessoal e não oficial. **Sem vínculo com a Avell.** "Avell" e "Gaming
Center" são propriedade de seus respectivos donos; usados aqui apenas para
descrever com o que este software é compatível.
