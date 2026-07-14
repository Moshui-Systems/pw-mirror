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
