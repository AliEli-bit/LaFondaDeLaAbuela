# Feature: Reloj de la Fonda y turno

Objetivo: un turno de duración fija con un reloj de pared que controla la dificultad (llegadas, paciencia, ingredientes por pedido y clientes a la vez) y un resumen al final con clientes atendidos y propinas.
Rama: `feat/shift` (desde `feat/tips`, sin pushear). Entrega: `single-pr` (como las anteriores; pronóstico ~600 líneas). Test runner: Unity Test Framework 1.8.0 (EditMode), corrido desde el editor abierto con un menú temporal (TestRunnerApi); prueba de Play con un test PlayMode temporal.

## Specs

- **S1 — Reloj de la Fonda (CLAUDE.md §4 y §7).** "Reloj visible en el juego (de pared) que además controla la dificultad. Afecta la frecuencia de llegada de clientes y la complejidad de pedidos." "Pared del fondo detrás de los clientes (z = 2,40), con puerta a la izquierda y el reloj a 2,20 de alto." `FondaClock`: "tiempo del turno, calcula `t` y expone los parámetros de dificultad; muestra la hora en el reloj de pared".
- **S2 — Fórmula del Reloj (CLAUDE.md §5).** "`t` = tiempo transcurrido / duración total del turno, de 0 a 1". "Intervalo entre clientes = `12 − 7·t` segundos". "Paciencia del cliente = `25 − 10·t` segundos". "Ingredientes por pedido = `1 + round(2·t)` (1 a 3)". "Clientes simultáneos máx.: Escena 1 = 1, Escena 2 = 2, Escena 3 = 3 sentados + 1 esperando" (el que espera de pie queda fuera, ver A6).
- **S3 — Duración y resumen (CLAUDE.md §5).** "tiempo fijo ~5 min (E1 ≈ 1 min, E2 ≈ 3 min, E3 ≈ 1:30). Al final, pantalla de resumen: clientes atendidos y propinas." `ShiftManager` (CLAUDE.md §7): "flujo del turno: Escena 1 → 2 → 3 → resumen; propinas y clientes atendidos".
- **S4 — Monedas sin recoger.** "Si el jugador no recoge las monedas, quedan en la barra sin castigo y se suman solas al final del turno (propuesto)."
- **S5 — Parametrizable.** "Valores de balance en ScriptableObjects (`DifficultySettings`)."
- **S6 — Verificación en Play.** El reloj avanza y pasa por las 3 escenas; los clientes llegan más seguido, con menos paciencia y pedidos más largos a medida que avanza `t`; nunca hay más clientes que el máximo de la escena; al terminar el tiempo dejan de llegar clientes y, cuando se va el último, aparece el resumen con las monedas de la barra sumadas.

### Supuestos (ajustables, no pedidos explícitamente)
- A1: Duración: 60 + 180 + 90 = 330 s (la suma de las escenas del GDD). Cada escena es parametrizable.
- A2: Al terminar el tiempo dejan de llegar clientes, pero los que están sentados siguen hasta irse (se los puede atender). El resumen aparece cuando se va el último.
- A3: Reloj de pared analógico de agujas que va de las 15:00 a las 20:00 durante el turno.
- A4: El plato se elige al azar entre los del menú con esa cantidad de ingredientes; si no hay ninguno, entre los de cantidad más cercana. `round` redondea la mitad hacia arriba (como las propinas).
- A5: El resumen es un cartel de texto frente al jugador, sobre la barra, con "¡Se terminó el turno!", clientes atendidos, clientes que se fueron enojados y propinas (cobradas y sumadas de la barra).
- A6: El cliente que espera de pie, el tutorial con el cuaderno, el combo y la voz de la abuela quedan fuera.
- A7: `DifficultySettings` se crea en `Assets/Data/DifficultySettings.asset` solo si no existe, para no pisar valores ajustados a mano.

## Tasks

| ID | Specs | Ruta | Estado | Commit |
|---|---|---|---|---|
| T1 | S2, S3, S4, S5, A1, A3, A4 | inline por el padre (sin razón de escritor): `DifficultySettings` (fórmulas, escenas, máximos, hora), `ShiftClock`, `ClockHands`, `MenuRule`, `ShiftSummary`, `TipJar.BankLeftovers`, tests RED antes | hecho: RED 21 (20 `NotImplementedException` + 1 posición en cero), GREEN 67/67 | 7e134d1 |
| T2 | S1, S3, S4, A2 | inline: `FondaClock`, `ShiftManager`, `CustomerSpawner` con el reloj | hecho: prueba en Play (L4) | 7e134d1 |
| T3 | S1, S5, A5, A7 | inline: builder del reloj de pared, cartel del resumen y asset de dificultad | hecho: builder `errors: 0` | 7e134d1, 8563a5e |
| T4 | S6 | padre en el editor abierto vía computer-use (batch bloqueado: Unity abierto): tests, builder, prueba en Play con test PlayMode temporal | hecho: 67/67, prueba en Play (L4); falta prueba a mano | 8563a5e |

## Log

- **L1 (2026-11-23, pedido del usuario):** "Siguiente feature". Propuesta del padre: el Reloj de la Fonda y el turno (`FondaClock` con `t` y reloj de pared, fórmulas del GDD para intervalo, paciencia e ingredientes, máximo de clientes 1/2/3 por escena, `ShiftManager` que corta las llegadas, suma las monedas de la barra y muestra el resumen, valores en `DifficultySettings`; el cliente de pie, el tutorial, el combo y la voz para después). Usuario: "Si, dale".
- **L2 (2026-11-23, RED):** 21 de los 21 casos nuevos de `ShiftTests` fallaron (20 con `NotImplementedException` sobre stubs, `WallClock_HangsOnTheBackWall` con la posición en cero); los 46 anteriores pasaban.
- **L3 (2026-11-23, GREEN y decisiones):** 67/67 EditMode. El reloj de pared va en x = 0,6 (a la derecha del asiento del centro, lejos de la puerta) y su posición sale de la pared del fondo del builder, así que se mueve con ella. El spawner ya no tiene valores fijos: lee todo de `FondaClock.Settings` y avisa con `CustomerLeft` cuando un cliente sale por la puerta. `ShiftManager` cierra cuando terminó el tiempo y no queda nadie; las monedas sin recoger se cobran con `TipJar.BankLeftovers`.
- **L4 (2026-11-23, prueba en Play):** test PlayMode temporal sobre `Fonda` a velocidad ×10, sin atender a nadie salvo al primero (servido por código). Al empezar, la aguja de la hora apunta a las 3 y la del minutero a las 12; en t = 0,5, la hora entre las 5 y las 6 y el minutero a las 6 (17:30). Llegaron 32 clientes: con 1 ingrediente hasta t ≈ 0,21, con 2 desde t = 0,26 y con 3 desde t = 0,76; paciencia de 24,94 a 15,05 s, igual a `25 − 10·t` en cada llegada; 0 pedidos fuera de la fórmula. Máximo a la vez: 1 en la Escena 1, 2 en la 2, 3 en la 3. Cambios de escena a los 60 y 240 s; a los 330 s "Se terminó el tiempo", 0 llegadas después. Los 3 clientes sentados se fueron enojados y a los 347,7 s apareció el resumen: "¡Se terminó el turno! / Clientes atendidos: 1 / Se fueron enojados: 31 / Propinas: $3 / ($3 sumadas de la barra)"; el frasco quedó en $3 y sin monedas en la barra. Sin excepciones. Scripts temporales borrados. Pendiente: prueba a mano con el simulador.
- **L5 (2026-11-23, contexto):** mientras tanto, el autor cambió en `CLAUDE.md` (sin commitear, otra sesión) la estación a una barra recta con la pared del fondo en z = 3,60. Esta feature no toca el layout; el reloj sigue a la pared del builder (hoy z = 2,40) y se moverá con ella cuando se adapte la barra.
- **L6 (2026-11-23, entrega):** commits `7e134d1` (reglas, reloj, turno, spawner, frasco, builder y tests) y `8563a5e` (escena, asset de dificultad, materiales y documento). Sin push.
