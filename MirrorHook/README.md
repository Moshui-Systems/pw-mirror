# MirrorHook (Mirror por Ação — experimental)

Próximo passo do Perfect Mirror: em vez de espelhar teclado/mouse, **injetar uma DLL**
em cada cliente do PW e replicar as **ações** (alvo, skill, item, movimento) diretamente,
sem depender de alinhamento de tela nem do streamer mode.

> ⚠️ Experimental / em construção. Hoje só existe o **esqueleto**: a DLL é injetada e
> apenas registra que carregou (não instala hooks ainda). Os hooks das funções de ação
> entram depois da engenharia reversa — ver [RE-PLAYBOOK.md](RE-PLAYBOOK.md).

## Conteúdo
- `dllmain.cpp` — a DLL injetada (`MirrorHook.dll`). Hoje só grava um marcador ao carregar.
- `injector.cpp` — injetor de linha de comando (`injector.exe`), injeta em todas as
  instâncias de um processo por nome.
- `build.bat` — compila os dois com g++ (MinGW-w64 x86_64).
- `RE-PLAYBOOK.md` — guia de RE pra achar as funções de ação do client.

## Compilar
Precisa de g++ (MinGW-w64 x64) no PATH:
```
build.bat
```

## Testar a injeção (seguro)
Não injete no jogo pra testar o pipeline — use um processo descartável:
```
injector.exe notepad.exe "C:\caminho\MirrorHook.dll"
```
Depois confira `%TEMP%\mirrorhook_loaded.txt` — deve ter "MirrorHook carregado no PID ...".

## Injetar no jogo (quando houver hooks)
Como os clientes abertos pelo Perfect Mirror são **elevados**, o injetor precisa rodar
**como administrador**:
```
injector.exe elementclient_64.exe "C:\caminho\MirrorHook.dll"
```

## Status / próximo
Pipeline de injeção ✅ validado. Falta a parte de RE (endereços/offsets das funções de
ação) — seguir o RE-PLAYBOOK. Com os números em mãos, o código de hook + IPC + tradução
de ID entra no `dllmain.cpp`.
