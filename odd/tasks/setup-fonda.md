# Feature: Setup del proyecto + escena Fonda

Objetivo: dejar el proyecto listo para probar sin visor y una escena `Fonda` vacía de lógica, con el jugador fijo frente al mostrador.
Rama: `feat/setup-fonda` (a crear desde `main`). Entrega: `single-pr`. Test runner: no aplica (verificación manual en el editor con el simulador).

## Specs

- **S1 — Simulador activo solo en editor.** El XR Interaction Simulator se instancia automáticamente al dar Play en el editor y **no** se incluye en builds.
  - Hoy: `Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset` → `m_AutomaticallyInstantiateSimulatorPrefab: 0`.
  - Aceptación: `m_AutomaticallyInstantiateSimulatorPrefab: 1`, `m_AutomaticallyInstantiateInEditorOnly: 1` y `m_SimulatorPrefab` apunta al prefab del sample **XR Interaction Simulator** (requiere importar ese sample; ver L8).
- **S2 — Samples de XRI alineados con el paquete.** Los samples `Starter Assets` (y `Hands Interaction Demo`, si se conserva) pasan de **3.5.1** a la versión del paquete instalado (**3.6.1**).
  - Aceptación: existe `Assets/Samples/XR Interaction Toolkit/3.6.1/`, no queda la carpeta `3.5.1/` y la consola no muestra errores de compilación.
- **S3 — Simulador verificado.** En `Assets/Scenes/SampleScene.unity`, con Play: se mueve la cabeza/mandos con teclado y mouse y se puede agarrar y soltar un objeto interactuable.
- **S4 — Escena `Fonda`.** Existe `Assets/Scenes/Fonda.unity` con: luz direccional, `XR Origin (XR Rig)` del Starter Assets (3.6.1) y `XR Interaction Manager`.
- **S5 — Sin locomoción.** "Sin locomoción. El jugador está fijo frente a un mostrador. Desactivar move/turn providers del XR Origin."
  - Aceptación: en la escena `Fonda` quedan deshabilitados los providers bajo `Locomotion` (`Move`, `Turn`, `Teleportation`, `Climb`, `Climb Teleport`, `Grab Move`, `Jump`) y los interactores de teleport de los mandos. Con el simulador, ni el stick ni el teleport mueven al jugador.
- **S6 — Blockout de la fonda (primitivas).** Piso, mostrador a "~0,9 m de alto", pared de fondo detrás del jugador y un punto marcado donde se ubicará el cliente, al otro lado del mostrador.
- **S7 — Alcance del brazo.** "Todo lo interactuable debe estar al alcance del brazo (mostrador ~0,9 m de alto, ingredientes a < 0,6 m del jugador)."
  - Aceptación: hay una zona marcada en el mostrador para ingredientes/bandeja cuyo punto más lejano está a < 0,6 m (en horizontal) del origen del jugador.

### Supuestos (ajustables, no pedidos explícitamente)
- A1: Medidas del blockout: mostrador 1,6 m de ancho × 0,6 m de fondo; borde frontal a 0,3 m del jugador; cliente a ~1,3 m del jugador.
- A2: `Fonda` se agrega a Build Settings como escena 0 (necesario para el build de Quest más adelante).
- A3: Materiales simples de URP (colores planos); sin assets finales.

## Tasks

| ID | Specs | Ruta | Quién | Estado | Commit |
|---|---|---|---|---|---|
| T1 | S1 | delegado (agente Sonnet 5.5): editar el asset, una línea | agente | hecho (diff de 1 línea verificado) | d912c90 / e40eda2 |
| T2 | S1, S2 | inline (padre): copia de `Samples~` del paquete 3.6.1 (Starter Assets, Hands Interaction Demo, XR Interaction Simulator), borrado de 3.5.1, enlace de `m_SimulatorPrefab` | agente | hecho (GUIDs iguales a 3.5.1; refresh sin `error CS`) | d912c90 / e40eda2 |
| T3 | S3 | Play vía menú de Unity (computer-use) | padre + usuario | parcial: en Play de `Fonda` el simulador aparece (mandos simulados visibles), sin excepciones; falta probar agarrar/soltar a mano en SampleScene | — |
| T4 | S4–S7, A1–A3 | delegado (Sonnet 5.5) + correcciones del padre; menú ejecutado por el padre | agente | hecho: `errors: 0`, zona 0,559 m, `Fonda.unity` en índice 0 | d912c90 / e40eda2 |
| T5 | S5, S7 | Play en Fonda vía computer-use (teclas en segundo plano) | padre | hecho: control positivo W movió el cuerpo simulado; tras I×3, L×2, J, K×2 el XR Origin siguió en posición (0,0,0) y rotación (0,0,0); sin excepciones en el log | d912c90 / e40eda2 |

Orden: T1 → T2 → T3 → T4 → T5. T2 debe ir antes de T4 para que la escena use el prefab 3.6.1.

## Log

- **L1 (2026-11-23, pedido del usuario):** "que vamos a desarrolllar la feature del setup, no desarrolles nada aun solo hagamos la feature"
- **L2 (2026-11-23, hallazgos):** XRI 3.6.1 instalado; samples en 3.5.1; simulador con auto-instanciado apagado; el prefab `XR Origin (XR Rig)` trae `Locomotion` con `Move`, `Turn`, `Teleportation`, `Climb`, `Climb Teleport`, `Grab Move`, `Jump`, `Gravity`. `.gitignore` de Unity y `.gitattributes` ya existen.
- **L3 (2026-11-23, decisión pendiente):** ruta de T4. Script de editor = escena reproducible y revisable en git, pero agrega código de editor. Manual = más rápido de arrancar, pero el `.unity` no se puede revisar bien en diffs.
- **L4 (2026-11-23, pedido del usuario):** "pasale la feature para que un agente trabaje en esta featrure con sonnet 5.5"
- **L5 (2026-11-23, decisión):** T4 por script de editor. Motivo: un agente no puede operar el editor de Unity, así que la única forma de que el agente haga T4 es con código; además deja la escena reproducible. El editor está abierto (lockfile presente), así que no se puede usar `-batchmode`: el usuario ejecuta el menú. El script busca el prefab `XR Origin (XR Rig)` sin fijar versión de sample, para funcionar antes y después de T2.
- **L6 (2026-11-23, resultado del agente):** T1 hecho. `Assets/Scripts/Editor/FondaSceneBuilder.cs` (320 líneas) escrito. Además de desactivar `Teleport Interactor`, anula `m_TeleportInteractor`, `m_TeleportMode` y `m_TeleportModeCancel` en cada `ControllerInputActionManager`, porque ese manager reactiva el teleport y apaga el grab near-far al recibir input de teleport. Sin evidencia de compilación: el editor no refrescó en segundo plano (el log está en `Logs/Editor.log`).
- **L7 (2026-11-23, revisión del padre):** lectura completa del script sin bloqueantes (orientación de quads, escala del plano, medidas S7 = 0,559 m, build settings sin duplicados). `gentle-ai review assess` = `unassessable` por archivos sin trackear; se reevalúa sobre el commit.
- **L8 (2026-11-23, usuario):** "no cambio nada cuando lo ejecute"
- **L9 (2026-11-23, diagnóstico):** el script compiló (solo warning CS0618 en línea 154, corregido usando `FindObjectsByType<T>()`). El Play en SampleScene registró: "XR Interaction Simulator prefab was missing, cannot automatically instantiate." Causa: `m_SimulatorPrefab: {fileID: 0}` porque el sample "XR Interaction Simulator" nunca se importó. El menú `Tools/Fonda/Build Fonda Scene` no se ejecutó (no hay `Fonda.unity` ni líneas `[FondaSceneBuilder]`). Samples siguen en 3.5.1.
- **L10 (2026-11-23, pedido del usuario):** "podrias hacerlo tu"
- **L11 (2026-11-23, ejecución):** samples 3.6.1 copiados desde la caché del paquete y 3.5.1 borrado (los prefabs 3.5.1 tenían cambios de reserialización de Unity, no del agente). `m_SimulatorPrefab` enlazado al prefab `XR Interaction Simulator` (guid 58d0a4ac…). Primer intento del menú falló con "InvalidOperationException: This cannot be used during play mode." (el editor estaba en Play): se agregó guarda de Play al script. Segundo intento: "Locomotion child 'Climb Teleport' not found." porque `Climb Teleport` es hijo de `Climb`, no de `Locomotion`: se quitó de la lista. Tercer intento: `errors: 0`.
- **L12 (2026-11-23, usuario):** "solo veo los mandos"
- **L13 (2026-11-23, diagnóstico):** no es un bug. Cámara a 1,36 m (`m_CameraYOffset: 1.36144`) mirando al frente; con FOV vertical ~60° (±30°) el borde inferior de la vista toca 0,9 m recién a ~0,9 m de distancia, y el mostrador está entre 0,3 y 0,9 m bajo los ojos (27°–57° hacia abajo). El marcador del cliente está en el piso a 1,3 m (46° abajo) y la pared está detrás. Hay que mirar hacia abajo: en el simulador, mantener clic derecho y mover el mouse (`Toggle Mouse` = `<Mouse>/rightButton`, `Mouse Rotation Delta`).
- **L14 (2026-11-23, evidencia del usuario):** captura del Game view en Play de `Fonda`: se ven mostrador y zona verde de ingredientes al mirar hacia abajo (S6, S7 visual OK).
- **L15 (2026-11-23, pedido del usuario):** "probalo vos"
- **L16 (2026-11-23, verificación S5):** ver T5. Limitación: no hay evidencia directa de que I/J/K/L llegaran al mando (van por la misma vía de teclado que W, que sí llegó). Agarrar/soltar (S3) sigue sin probar a mano; en `Fonda` no hay objetos agarrables todavía.
- **L17 (2026-11-23, entrega):** commits `d912c90` (samples 3.6.1 + simulador) y `e40eda2` (builder + escena Fonda). `gentle-ai review assess --base-ref main --committed-only --untracked-scope=exclude`: riesgo `medium`, `review_due: false` (`under_budget`); sin revisión nativa.
- **Pendiente:** S3 agarrar/soltar a mano en SampleScene. Push y PR quedan a decisión del usuario.
