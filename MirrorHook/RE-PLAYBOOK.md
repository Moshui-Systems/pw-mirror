# Mirror por Ação — Playbook de Engenharia Reversa

Objetivo: em vez de espelhar teclado/mouse (que depende de alinhamento e do streamer
mode), **hookar as funções de ação do `elementclient_64.exe`** e replicar a ação
(alvo, skill, item, movimento) nas outras instâncias — reresolvendo o alvo pelo ID do
jogo em cada cliente.

> ⚠️ Tudo isso é específico deste build do client (`The Classic PW 1.8.7`,
> `elementclient_64.exe`, PE32+ x64). Se o servidor atualizar o client, os endereços
> mudam e é preciso reachar (por isso a gente busca por *padrões*/assinaturas, não por
> endereços fixos). Teste sempre em conta descartável; injeção mal feita = cliente
> travado (como já vimos, processo elevado travado só sai com reboot).

## Duas abordagens (leia isto primeiro)

**A) Hook no ENVIO DE PACOTE (recomendada — mais simples).**
Quando o client manda a ação pro servidor, o alvo já é o **ID global** do objeto (o
mesmo pra todos os clients), não um ponteiro por-processo. Então:
- hooka **uma** função (o `SendPacket` interno do client, ainda em claro);
- no master, captura o pacote da ação;
- manda cada slave chamar o **próprio** `SendPacket` com o mesmo comando.
Sem tradução de ponteiro. Cada slave envia pela sua conexão (sessão/sequência corretas).
O muro do "ponteiro por-processo" **não existe** aqui.

**B) Hook nas funções de LÓGICA do jogo (`SelectTarget`, `CastSkill`…).**
Mais trabalho: várias funções e é preciso **traduzir o ID global → ponteiro local** em
cada slave (Etapas 1-4 abaixo). Use como alternativa se a A não for viável (ex.: se não
achar um ponto em claro antes da cifra).

> O resto deste documento (Etapas 1-4) descreve a abordagem B. Para a A, ver
> "Abordagem A — detalhe" no fim.

## Ferramentas
- **Cheat Engine** — achar structs (player, entity list, alvo) e fazer "find out what
  writes/accesses this address".
- **x64dbg** — breakpoints em funções, ver argumentos (RCX/RDX/R8/R9 = x64 fastcall),
  achar o começo das funções.
- **Ghidra** ou **IDA Free** — descompilar pra entender assinatura e structs.
- **MinHook** (lib de hooking): https://github.com/TsudaKageyu/minhook — vamos vendorar
  no projeto quando começar a hookar.

## Convenção x64 (importante)
Windows x64 fastcall: 1º arg = **RCX**, 2º = **RDX**, 3º = **R8**, 4º = **R9**, resto na
pilha. `this` (métodos C++) vem em **RCX**. Retorno em **RAX**. Isso é o que você lê no
x64dbg quando parar numa função.

## Etapa 1 — Achar o "eu" (local player) e a entity list
1. No Cheat Engine, anexe a UM client. Ache algo estável e visível: **HP atual**
   (First Scan, valor exato; tome dano/cure e Next Scan até 1-2 endereços).
2. Botão direito no endereço → **Find out what accesses this address**. Ande/lute pra
   gerar acessos. Anote a instrução e o **base pointer** (ex.: `[rax+XXX]`).
3. Pointer scan até chegar num ponteiro **estático** (verde) → esse é o ponteiro pro
   struct do player. Guarde o offset do HP dentro do struct.
4. Perto do player geralmente está o **alvo atual** (target). Ache o ID/ponteiro do alvo:
   selecione um NPC → o campo "target" no struct muda. Isso dá o offset do alvo.
5. A **entity list** (lista de todos objetos visíveis) costuma ser um array/hashmap de
   {ID do jogo → ponteiro do objeto}. Ache-a partindo do alvo: o objeto do alvo tem um
   campo **ID** (32/64-bit) estável entre clientes. Esse ID é a chave da tradução.

## Etapa 2 — Achar a função de "selecionar alvo" / clicar objeto
1. x64dbg no client. Coloque um breakpoint de acesso/escrita no campo **target** do
   player (achado na Etapa 1).
2. Clique num NPC no jogo. O breakpoint bate na função que seta o alvo. Suba a pilha
   (Call Stack) até a função pública que recebe o **ID/ponteiro** do objeto clicado.
3. Anote: endereço da função (relativo à base do módulo), e os argumentos (ex.:
   `void SelectTarget(Player* this /*RCX*/, uint32 targetId /*EDX*/)`).
4. Grave uma **assinatura** (10-20 bytes do início da função) pra reachar depois de updates.

## Etapa 3 — Achar "lançar skill" e "usar item"
Mesmo método: breakpoint no que muda ao usar a skill (cooldown, energia) ou trace a
tecla F1..F8. Ache a função tipo `CastSkill(this, skillId, targetId)`. Idem item/move.

## Etapa 4 — Hookar e replicar (o código na DLL)
Arquitetura (a implementar em `dllmain.cpp` após as etapas acima):
1. **MinHook** para dar hook em `SelectTarget`, `CastSkill`, etc.
2. No client **master** (o que está em foco), o hook:
   - deixa a ação original rodar;
   - extrai a ação semântica: `{tipo: CastSkill, skillId: X, targetGameId: Y}`;
   - publica isso via **IPC** (named pipe `\\.\pipe\MirrorHook` ou shared memory) para
     um coordenador (pode ser o próprio Perfect Mirror).
3. Nos clients **slaves**, um listener recebe a ação e:
   - **reresolve** `targetGameId` → ponteiro do objeto NA entity list DAQUELE client
     (por isso a Etapa 1 é crucial: o ID é global, o ponteiro é por-processo);
   - chama a função (`CastSkill(localPlayer, skillId, localTargetPtr)`) diretamente.
4. Como saber quem é master? O coordenador marca o client cuja janela está em foco
   (`GetForegroundWindow`) como master; os outros são slaves.

### O muro principal
`targetGameId` precisa ser um **ID estável do jogo** (o servidor usa o mesmo pra todos),
NÃO um ponteiro. Se a função de ação recebe ponteiro, ache o campo ID dentro do objeto e
use-o na tradução. Se ela já recebe ID, melhor ainda.

## Ordem sugerida (1 ação de ponta a ponta primeiro)
1. Etapa 1 (player + entity list + campo ID). 
2. Etapa 2 (SelectTarget) — mais simples, ótimo pra validar o conceito.
3. DLL: hook SelectTarget no master → IPC → slaves reresolvem e chamam. Testar em 2 contas.
4. Só então CastSkill, item, move.

Quando a Etapa 1/2 estiverem mapeadas (endereços/offsets/assinaturas), me passa os
números e eu escrevo o código da DLL (MinHook + IPC + tradução de ID).

---

## Abordagem A — detalhe (hook no envio de pacote)

### A1 — Achar a função de envio de pacote
Opções, da melhor pra pior:
1. **`SendPacket` interno do client** (em claro): procure a função que monta o pacote
   antes de cifrar. Como achar:
   - x64dbg: breakpoint em `ws2_32.send` e `ws2_32.WSASend`. Faça uma ação (skill).
     Suba a Call Stack: as funções acima do `send` são a pilha de rede do client. A que
     recebe o buffer **em claro** (antes da que cifra) é o alvo. Frequentemente há uma
     função tipo `SendPacket(this, opcode, buffer, len)`.
   - Confirme vendo o buffer: logo após uma ação, ele deve conter o **opcode** + o **ID
     do alvo** (compare com o ID achado no jogo — ex.: selecione o NPC e veja o número
     aparecer no buffer).
2. Se tudo estiver cifrado até o socket, hooke a função **de cifra** e pegue o buffer
   antes dela (o argumento de entrada dela é o pacote em claro).

Anote: endereço (relativo à base do módulo), assinatura, e a **calling convention**
(quais registradores = opcode/buffer/len).

### A2 — Entender o mínimo do formato
Você não precisa decodificar tudo. Precisa saber:
- Onde está o **opcode** (pra filtrar quais pacotes replicar: skill, alvo, item, move…).
- Que o **ID do alvo** dentro do payload é o ID global (confirmado comparando entre
  clients: o mesmo NPC tem o mesmo número nos dois).
Faça um log: hooke o envio e escreva `opcode + hex do buffer` num arquivo enquanto joga.
Manda esse log que a gente identifica os opcodes de ação juntos.

### A3 — Replicar
Na DLL (com MinHook):
- Hook em `SendPacket`.
- Se este processo é o **master** (janela em foco) e o opcode está na lista de ações:
  publica `{opcode, bytes do payload}` via IPC (named pipe `\\.\pipe\MirrorHook`).
- Sempre: deixa o pacote original seguir.
- Um listener em cada processo recebe do pipe e, se **não** for o master, chama o
  `SendPacket` local com os mesmos bytes → o slave executa a ação pela sua conexão.
- Coordenação master/slave: quem tem `GetForegroundWindow()` == sua janela é o master.

### A4 — Cuidados
- Filtrar bem os opcodes (não replicar login/heartbeat/chat sem querer).
- Movimento é por coordenada de mundo (global) — replicar deixa todos irem pro mesmo
  ponto (formação). Deixar isso opcional.
- Reentrância: ao chamar `SendPacket` no slave, marque uma flag pra o seu próprio hook
  não re-publicar aquilo (senão loop entre clients).
