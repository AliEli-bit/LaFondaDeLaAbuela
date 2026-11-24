# Feature: Sonidos y vibración

Objetivo: el juego suena y vibra: "plop" al soltar en la bandeja, campanita al acertar (más aguda con el combo), error al fallar, monedas al dejar la propina y al cobrar, y vibración al soltar.
Rama: `feat/feedback` (desde `main` en 61ee03c). Entrega: `single-pr` (pronóstico ~450 líneas). Test runner: Unity Test Framework (EditMode), corrido desde el editor abierto con un menú temporal (TestRunnerApi); prueba de Play con un test PlayMode temporal.

## Specs

- **S1 — Sonidos (CLAUDE.md §5, "Feedback").** "Sonidos: "plop" al soltar, campanita al acertar, gruñido corto al fallar."
- **S2 — Vibración (CLAUDE.md §5, "Feedback").** "Vibración (haptics) en controles al agarrar y soltar."
- **S3 — Combo (CLAUDE.md §5, idea 3).** "cada entrega rápida seguida sube el tono de la campanita y la propina; un error reinicia el combo sin otra penalización."
- **S4 — Monedas.** Sonido de monedas cuando el cliente deja la propina y cuando una moneda entra al frasco (tabla de sonidos aceptada por el usuario, L2).
- **S5 — Parametrizable (CLAUDE.md §5).** "Implementar de forma **parametrizable** (ScriptableObjects o campos serializados) para poder ajustar sin tocar código."
- **S6 — Créditos (CLAUDE.md §5).** "Registrar cada paquete y su licencia en `CREDITS.md`."

### Supuestos (ajustables, no pedidos explícitamente)
- A1: El "plop" suena cuando un ingrediente entra en una ranura de la bandeja, no cuando cae en otro lado.
- A2: No hay gruñido grabado: al fallar suena `error_008` (Kenney Interface Sounds) hasta que haya uno.
- A3: La campanita suena cuando el cliente toma el plato, con tono `1 + 0,08 × (combo − 1)`, hasta 1,5. Una entrega lenta (combo 0) suena con el tono base.
- A4: Al agarrar ya vibra (Starter Assets del XR rig: 0,5 por 0,1 s, y 0,25 al pasar la mano). Se activa la vibración al soltar en los interactores del rig, con los valores que ya trae.
- A5: Los sonidos se oyen en 3D, desde la bandeja, el asiento o el frasco. Cada evento tiene variantes que se eligen al azar sin repetir la anterior.
- A6: Los clips y volúmenes viven en `Assets/Data/FeedbackSettings.asset`; el builder lo crea solo si falta, para no pisar ajustes a mano.
- A7: Quedan fuera: la voz de la abuela, la carita y los efectos visuales nuevos.

## Tasks

| ID | Specs | Ruta | Estado | Commit |
|---|---|---|---|---|
| T1 | S1, S3, S4, S5, A1–A3, A5 | inline (sin razón de escritor): `FeedbackSettings`, `FeedbackRule` (tono, variante), `FeedbackManager` (voces 3D), eventos nuevos en `Tray`, `BarSeat` y `ShiftManager`, tests RED antes | hecho: RED 10 de 10, GREEN 186/186 | ver L6 |
| T2 | S2, A4 | inline: el builder de la escena activa la vibración al soltar del rig | hecho: 6 `SimpleHapticFeedback` del rig con soltar activado | ver L6 |
| T3 | S5, S6, A6 | inline: clips en `Assets/ThirdParty/Kenney_Audio/`, builder crea `FeedbackSettings` y el `FeedbackManager`, `CREDITS.md` | hecho: escena con 0 errores, 11 clips en el asset | ver L6 |
| T4 | todos | padre en el editor abierto vía computer-use: tests y prueba en Play | hecho (L5); falta prueba a mano y en el visor | ver L6 |

## Log

- **L1 (2026-11-24, pedido del usuario):** "dale, empecemos con los sonidos y la vibración".
- **L2 (2026-11-24, decisiones del usuario):** fuente de sonidos: "Paquetes de Kenney (Recommended)". Descarga: "Sí, descargalos". Bajados por el navegador integrado desde kenney.nl: `kenney_impact-sounds.zip` (800.850 B), `kenney_interface-sounds.zip` (834.536 B), `kenney_rpg-audio.zip` (964.837 B), todos CC0. Elegidos: `drop_001`–`003` (plop), `impactBell_heavy_004` (campanita), `error_008` (error), `handleCoins2` (propina), `impactGlass_light_000`–`004` (frasco).
- **L3 (2026-11-24, RED):** `FeedbackTests` (10 casos): fallaron los 10 (9 con `NotImplementedException`, `DefaultClips_AreInTheProject` con la tabla vacía); los 176 anteriores pasaron.
- **L4 (2026-11-24, GREEN):** 186/186 EditMode. Escena `Fonda` reconstruida desde el menú: errores 0. Los 6 `SimpleHapticFeedback` del rig quedan con `m_PlaySelectExited: 1` como override de la escena; `Assets/Data/FeedbackSettings.asset` con 11 clips.
- **L5 (2026-11-24, prueba en Play):** test PlayMode temporal sobre `Fonda`, turno completo a ×10, ingredientes puestos por código en las ranuras (1 de cada 3 con un ingrediente equivocado antes, 1 de cada 4 clientes sin atender) y la mitad de las monedas al frasco. Turno cerrado, 0 errores. Plop 62 (= 62 ingredientes puestos), error 9 (= 9 equivocados), campanita 29 (= 29 entregas), monedas 29, frasco 44 (43 a mano + 1 al cierre). Tono de la campanita por combo: ×1 1,00, ×2 1,08, ×3 1,16, ×4 1,24. Un primer intento no corrió ningún test: el asmdef temporal era solo de editor. Scripts temporales borrados. Pendiente: escuchar los sonidos a mano en el simulador y sentir la vibración en el visor (el simulador no vibra).
- **L6 (2026-11-24, entrega):** commit de la feature en `feat/feedback`. Sin push.
