<div align="center">

<img src="https://avatars.githubusercontent.com/u/258685104?s=120&v=4" width="90" alt="Moshui Systems" />

# PW Mirror

**Launcher multi-conta para Perfect World com espelhamento de comandos.**
Joga em uma conta e o teclado/mouse vai pra todas as outras ao mesmo tempo.

<sub>by Moshui Systems</sub>

</div>

---

## ⚡ O que é

Um launcher que abre várias contas do PW de uma vez e tem um **Espelho (Mirror)**: os comandos da conta que está em foco são enviados para **todas as outras contas abertas** — ideal pra multibox (rodar tank + healers + DDs juntos).

## ✨ Recursos

- 🪞 **Espelho de comandos** — teclado e mouse da conta ativa vão pra todas as outras
- 👥 **Abrir todas as contas** com um clique
- 🔁 **Combar** — rotação automática de skills (F1–F8) entre as contas
- 🎯 **Troca rápida** de conta por atalho de teclado
- 🛡️ **Crash watch** — reabre conta que fechou sozinha
- 🎨 Visual escuro e botões repaginados

## 📦 Instalação

1. Baixe **todos os arquivos** da release (o `.exe` vem com 6 `.dll` de apoio — precisa de todos):
   - `Perfect Launcher.exe` + `Perfect Launcher.exe.config`
   - `System.Resources.Extensions.dll`, `System.Memory.dll`, `System.Buffers.dll`, `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`
2. Jogue **todos** na **pasta raiz** do seu PW — a que tem a pasta `x64` e a `userdata` dentro.
   > ⚠️ Não coloque dentro de `x64`. Tem que ser na pasta de cima. E não esqueça nenhum `.dll`, senão o launcher não abre.
3. Abra o `Perfect Launcher.exe`. Pronto.

## 🪞 Como usar o Espelho

1. Adicione e abra suas contas no launcher.
2. Clique em **Combar** pra abrir a janela de multibox.
3. Marque o ☑ **Espelhar** (fica no canto inferior direito da janela do Combar).
4. Deixe a conta "mestre" em foco e jogue normal — as outras copiam.

> 💡 Dá pra **ligar/desligar a qualquer momento** com a tecla **`Pause/Break`**, mesmo dentro do jogo.

**Importante:** teclado (skills, F1–F8, etc.) funciona liso em todas as contas. Já o **mouse** funciona bem pra cliques de interface, mas mira/câmera dentro do mundo o PW lê por DirectInput — então nas janelas em segundo plano o mouse no mundo 3D não é 100%.

## 🛠️ Compilar do código-fonte

Precisa do **.NET SDK** (8+). Na raiz do projeto:

```bash
dotnet build "Perfect Launcher.sln" -c Release -p:Platform=x64
```

O `.exe` sai em `Perfect Launcher/bin/x64/Release/`. Distribua a pasta **inteira** (o `.exe`, o `.config` e todos os `.dll` gerados juntos).

## 🙏 Créditos

Baseado no launcher original de [Felipe Libardi](https://github.com/LibardiFelipe/Perfect-Launcher). Espelhamento, correções e rebrand por **Moshui Systems**.
