# Feature: Combo de rapidez

Objetivo: las entregas rápidas seguidas suben un combo que da más propina; un error o un cliente enojado lo reinicia. El combo dura todo el turno y el resumen muestra el mejor.
Rama: `feat/combo` (desde `feat/shift`, sin pushear). Entrega: `single-pr` (pronóstico ~300 líneas). Test runner: Unity Test Framework 1.8.0 (EditMode), corrido desde el editor abierto con un menú temporal (TestRunnerApi); prueba de Play con un test PlayMode temporal.

## Specs

- **S1 — Combo (CLAUDE.md §5, idea 3).** "Combo de rapidez en todo el turno: cada entrega rápida seguida sube el tono de la campanita y la propina; un error reinicia el combo sin otra penalización." El tono de la campanita queda para la feature de sonido (A5).
- **S2 — Propina con combo (CLAUDE.md §5, "Propinas en mano").** "El monto depende de la paciencia que le quedaba al cliente y del combo." Mecánica de la Escena 3: "combo de rapidez como bonus de propina. Error = rehacer el plato, sin penalización extra."
- **S3 — Se ve y se resume.** Pedido del padre aceptado (L1): el combo "Se ve en un cartel, y el resumen final muestra el mejor combo".
- **S4 — Parametrizable.** "Valores de balance en ScriptableObjects (`DifficultySettings`)."
- **S5 — Verificación en Play.** Entregas rápidas seguidas suben el combo y dejan más monedas; un ingrediente que no se pidió o un cliente que se va enojado lo reinicia; el resumen muestra el mejor combo.

### Supuestos (ajustables, no pedidos explícitamente)
- A1: "Rápida" = el cliente estaba contento al recibir el plato (más del 60 % de paciencia, el umbral de la carita feliz). Parametrizable.
- A2: Una entrega lenta corta la seguidilla: el combo vuelve a 0, igual que con un error. Un cliente que se va enojado también lo reinicia.
- A3: Bonus: +1 moneda por cada paso de combo desde el segundo (×2 → +1, ×3 → +2), con tope de +2 para que entren en la barra (máximo 5 monedas). Parametrizable. Un cliente enojado no deja nada, con o sin combo.
- A4: El "cartel" es el globo del cliente al recibir el plato: "¡Gracias, doña!" y debajo "¡Combo ×N!" desde ×2. Así no hace falta tocar el builder, que otra sesión está cambiando a la barra recta (sin commitear).
- A5: El tono de la campanita, la voz de la abuela al celebrar y la propina tirada quedan fuera.
- A6: En el resumen, la línea "Mejor combo: ×N" aparece solo si hubo combo (×2 o más).

## Tasks

| ID | Specs | Ruta | Estado | Commit |
|---|---|---|---|---|
| T1 | S1, S2, S4, A1–A3, A6 | inline por el padre (sin razón de escritor): `ComboCounter`, `ComboRule`, bonus en `BarSeat.LeaveTip`, valores en `DifficultySettings`, mejor combo en `ShiftSummary`, tests RED antes | hecho: RED 12 (10 `NotImplementedException`, 2 por bonus y línea del resumen faltantes), GREEN 79/79 | c9eb829 |
| T2 | S1, S3, A4 | inline: eventos del cliente (servido, niega, se va enojado), el spawner los reenvía, `ShiftManager` lleva el combo y se lo da al cliente | hecho: prueba en Play (L3) | c9eb829 |
| T3 | S5 | padre en el editor abierto vía computer-use: tests y prueba en Play con test PlayMode temporal | hecho: 79/79, prueba en Play (L3); falta prueba a mano | c9eb829 |

## Log

- **L1 (2026-11-23, pedido del usuario):** "Siguiente feature". Propuesta del padre: el combo de rapidez ("Cada entrega rápida seguida sube el combo, y el combo sube la propina (más monedas). [...] Un error en la bandeja o un cliente que se va enojado lo reinicia; no hay otra penalización. [...] Se ve en un cartel, y el resumen final muestra el mejor combo. Valores en `DifficultySettings`"), elegido porque no toca el builder que otra sesión está cambiando. Usuario: "Si, dale".
- **L2 (2026-11-23, RED y GREEN):** `ComboTests` (12 casos) fallaron: 10 con `NotImplementedException`, el bonus de monedas (3 en vez de 5) y la línea del resumen. GREEN: 79/79 EditMode. Durante GREEN, `Customer.ShakeHead` avisaba el rechazo solo si tenía cabeza; ahora avisa siempre y la cabeza solo afecta la animación.
- **L3 (2026-11-23, prueba en Play):** test PlayMode temporal sobre `Fonda` (la escena con los cambios sin commitear de la barra recta), sirviendo por código. Tres entregas rápidas: combo ×1, ×2, ×3; globos "¡Gracias, doña!", "¡Gracias, doña! / ¡Combo ×2!", "... ×3!"; monedas 3, 4 (+1) y 5 (+2). Un ingrediente que no pidió: "Se corta el combo ×3", combo 0, y la entrega rápida siguiente vuelve a ×1 con 3 monedas. Entrega lenta (14,1 de 23,5 de paciencia): combo 0, sin bonus. Un cliente que se fue enojado cortó un combo ×1. Al final: "Clientes atendidos: 6 | Se fueron enojados: 24 | Mejor combo: ×3 | Propinas: $20 | ($20 sumadas de la barra)". Sin excepciones. Scripts temporales borrados. Pendiente: prueba a mano con el simulador.
- **L4 (2026-11-23, advertencia previa a esta feature):** las monedas nuevas aparecen en los mismos lugares que las que el jugador no recogió; es probable que la física las separe de golpe (no se observó, no se midió). Viene de la feature de propinas; con el combo hay más monedas y se nota más. Queda como seguimiento.
- **L5 (2026-11-23, entrega):** commit `c9eb829` (reglas, cableado, tests y documento). Sin push. No se commitearon los cambios de la barra recta de la otra sesión.
