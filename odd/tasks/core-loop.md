# Feature: Ciclo base de armado de un plato

Objetivo: primer ciclo jugable en `Fonda`: agarrar ingredientes de dispensadores que reponen, soltarlos en una bandeja con ranuras y que la bandeja diga si el plato coincide con un pedido fijo.
Rama: `feat/core-loop` (desde `feat/setup-fonda`, PR #1 sin mergear). Entrega: `single-pr` (misma estrategia que setup-fonda). Test runner: Unity Test Framework 1.8.0 (EditMode), corrido desde el editor.

## Specs

- **S1 — Datos de ingredientes y recetas.** `IngredientData` y `RecipeData` como ScriptableObjects (CLAUDE.md §7: "id, nombre, ícono, prefab" y "nombre del plato, lista de ingredientes requeridos"). Assets para los 5 ingredientes del menú tentativo ("arroz, huevo, papa, carne, ensalada") y la receta "Arroz con huevo" (arroz + huevo).
- **S2 — Ingrediente agarrable.** Componente `Ingredient` con referencia a su `IngredientData`, sobre un objeto con `XRGrabInteractable` (CLAUDE.md §3). Un prefab por ingrediente, con primitivas y color plano distinto.
- **S3 — Dispensador que repone.** `IngredientDispenser`: "repone una copia cuando se agarra el ingrediente (escucha `selectExited`/`selectEntered`)". "Los ingredientes **nunca se agotan**". Un dispensador por ingrediente en `Fonda`.
- **S4 — Bandeja que evalúa el pedido.** `Tray` con `XRSocketInteractor` por ranura: "al entrar/salir ingredientes compara contra el pedido activo; emite evento de plato correcto/incorrecto". La cantidad de ranuras activas es la cantidad de ingredientes del pedido.
  - Regla acordada con el usuario (L3): el error se marca **recién cuando la bandeja está llena** (ranuras ocupadas = ingredientes del pedido) y lo que hay no coincide con el pedido. Con la bandeja incompleta no se evalúa nada, así que sacar un ingrediente equivocado antes de completar no cuesta nada.
  - La comparación no depende del orden ni de qué ranura ocupa cada ingrediente, pero sí cuenta repetidos (arroz + arroz no es arroz + huevo).
  - "Entrega del plato: automática cuando la bandeja coincide con el pedido."
- **S5 — Pedido fijo de prueba.** La bandeja de `Fonda` arranca con el pedido "Arroz con huevo". Sin cliente, spawner ni reloj.
- **S6 — Alcance.** "Todo lo interactuable debe estar al alcance del brazo (mostrador ~0,9 m de alto, ingredientes a < 0,6 m del jugador)." Dispensadores y ranuras de la bandeja a < 0,6 m en horizontal del origen del jugador; el builder aborta si no se cumple.
- **S7 — Verificación en Play.** En `Fonda` con el simulador se puede agarrar un ingrediente, el dispensador repone, y soltar ingredientes en la bandeja produce el evento correcto/incorrecto según S4. Cierra el S3 pendiente de setup-fonda (agarrar/soltar a mano).

### Supuestos (ajustables, no pedidos explícitamente)
- A1: Tras evaluar (correcto o incorrecto) la bandeja se vacía: los ingredientes colocados se destruyen y el pedido sigue siendo el mismo. En el correcto es la entrega; en el incorrecto es "el cliente vuelve a pedir", y el costo es el tiempo.
- A2: Feedback mínimo solo para verificar: `Debug.Log` y un destello de color en la bandeja (verde correcto, rojo incorrecto). Sonidos, haptics y carita quedan para la feature de feedback.
- A3: La escena se sigue generando con el builder de editor (`Tools/Fonda/Build Fonda Scene`), que ahora también crea los assets de datos, prefabs, dispensadores y bandeja de forma idempotente.
- A4: Para poder testear, el código pasa a asmdefs: `Fonda.Runtime` (`Assets/Scripts`), `Fonda.Editor` (`Assets/Scripts/Editor`) y `Fonda.Tests.EditMode` (`Assets/Tests/EditMode`). La regla de S4 vive en una clase C# pura testeable sin escena.
- A5: Los ingredientes sueltos fuera de la bandeja no se limpian en esta feature.

## Tasks

| ID | Specs | Ruta | Quién | Estado | Commit |
|---|---|---|---|---|---|
| T1 | S4 | delegado (Sonnet 5.5, pedido del usuario): asmdefs + regla pura + tests EditMode (RED escritos antes) | agente | hecho: 4/4 GREEN; mutación (quitar conteo de repetidos) hizo fallar solo el test de repetidos | 86e0321 |
| T2 | S1–S5 | delegado (mismo agente): `IngredientData`, `RecipeData`, `Ingredient`, `IngredientDispenser`, `Tray` | agente | hecho (un error CS0619 corregido por el padre) | 86e0321 |
| T3 | S1–S6, A3 | delegado (mismo agente): extensión del builder (assets, prefabs, layout, chequeo de alcance) | agente | hecho: builder `errors: 0` | b929979 |
| T4 | S4, S6, S7 | padre vía computer-use: compilar, correr tests EditMode, correr builder, Play | padre + usuario | parcial: prueba automática en Play OK; falta agarrar/soltar a mano con el simulador | b929979 |

Orden: T1 → T2 → T3 → T4. El agente no puede compilar (editor abierto, sin `-batchmode`): la compilación y los tests los corre el padre en T4.

## Log

- **L1 (2026-11-23, pedido del usuario):** "cual es la siguiente feature"
- **L2 (2026-11-23, propuesta del padre):** ciclo base (datos, ingrediente agarrable, dispensador, bandeja con sockets, pedido fijo). Decisión abierta: cuándo un plato cuenta como "mal". Usuario: "no entiendo". Explicación con ejemplo: pedido arroz + huevo, bandeja de 2 lugares; poner arroz no hace nada; poner papa llena la bandeja y "Recién ahí cuenta como error".
- **L3 (2026-11-23, decisión del usuario):** "si, vamos con esa, klanza un agente sonnet 5.5"
- **L4 (2026-11-23, decisión):** rama `feat/core-loop` desde `feat/setup-fonda` porque necesita la escena y el builder de PR #1. Pronóstico ~600 líneas (código + tests + builder); se mantiene `single-pr` como en setup-fonda.
- **L5 (2026-11-23, agente escritor):** Escrito sin compilar. T1: tres asmdefs (`Fonda.Runtime` -> `Unity.XR.Interaction.Toolkit`; `Fonda.Editor` -> `Fonda.Runtime` + XRI, solo Editor; `Fonda.Tests.EditMode` con nunit y `UNITY_INCLUDE_TESTS`), `TrayRule.Evaluate` (enum `Incomplete/Correct/Wrong`, multiconjunto con duplicados) y 4 tests en `TrayRuleTests`. Los tests se escribieron antes que la regla, pero no se observo RED porque no se puede compilar. T2: `IngredientData`, `RecipeData`, `Ingredient`, `IngredientDispenser`, `Tray` (eventos C# `OrderCorrect/OrderWrong` + UnityEvents, `SetOrder`, vaciado A1 via `CancelInteractorSelection` + `Destroy`, destello A2 con `MaterialPropertyBlock` `_BaseColor`). T3: `FondaCoreLoopBuilder` (assets en `Assets/Data`, prefabs en `Assets/Prefabs/Ingredients`, 5 dispensadores a z=0.38, bandeja de 3 ranuras a z=0.55, `ValidateLayout` aborta antes de crear la escena); `FondaSceneBuilder` ahora expone como `internal` las constantes del mostrador y `GetOrCreateMaterial`, y `BuildBlockout` devuelve la raiz. APIs verificadas en el paquete XRI 3.6.1: `XRSocketInteractor` en `...Interactors`, `XRGrabInteractable` en `...Interactables`, `XRInteractionManager`, `SelectEnterEventArgs` y `BaseInteractionEventArgs` en `UnityEngine.XR.Interaction.Toolkit`; `CancelInteractorSelection` es publico y virtual.
- **L6 (2026-11-23, compilación):** un error: `Tray.cs(153)` CS0619, sobrecarga obsoleta `CancelInteractorSelection(XRBaseInteractor)`; corregido con cast a `IXRSelectInteractor`. Luego compila sin errores.
- **L7 (2026-11-23, tests):** con un menú temporal (`TestRunnerApi`, la ventana del Test Runner no es alcanzable en segundo plano): 4/4 pasan. Mutación: sin `|| n == 0` en `TrayRule` falla solo `DuplicatesCount_ArrozArrozIsNotArrozHuevo` (esperado Wrong, obtenido Correct); restaurado, 4/4.
- **L8 (2026-11-23, builder):** el usuario confirmó el diálogo de sobrescritura (no alcanzable en segundo plano). `errors: 0`; escena con `Tray` (3 ranuras), 5 `Dispenser_*`, 5 prefabs y 6 assets de datos.
- **L9 (2026-11-23, Play):** sin excepciones (solo los errores de OpenXR por falta de runtime en macOS). Prueba automática temporal que agarra con el `Near-Far Interactor` real vía `XRInteractionManager.SelectEnter/SelectExit` y suelta en las ranuras: agarrar repone el dispensador (1→2) para arroz, huevo y papa; solo arroz → sin evaluación; arroz+huevo → `[Tray] Plato correcto`, bandeja vacía; arroz+papa → `[Tray] Plato incorrecto`, bandeja vacía; ranura 3 inactiva. Scripts temporales borrados antes del commit. Pendiente: agarrar/soltar a mano con teclado y mouse (también cierra S3 de setup-fonda).
- **L10 (2026-11-23, entrega):** commits `86e0321` (regla, componentes, tests) y `b929979` (builder, assets, escena).
- **L11 (2026-11-23, evidencia del usuario):** captura en Play tras probar a mano: agarró una carne y el dispensador repuso, pero los dispensadores de arroz, huevo, papa y ensalada quedaron vacíos. El log muestra 2× `[Tray] Plato incorrecto`.
- **L12 (2026-11-23, diagnóstico y corrección):** en reposo los 5 ingredientes quedan quietos 6 s. Al agarrar papa sin mover la mano, la copia nueva nacía superpuesta con la agarrada y la física la empujaba de (0, 0.94, 0.38) a (-0.117, 0.94, 0.415); las formas redondas ruedan y pueden caer del mostrador (la carne, cubo, se queda). Corrección: `IngredientDispenser` repone recién cuando el ingrediente agarrado se aleja más de `clearRadius` (0,12 m) del punto de aparición o se destruye. Reprueba: la copia nueva queda en (0, 0.94, 0.38), sin excepciones. Pendiente: que el usuario repita la prueba a mano.
