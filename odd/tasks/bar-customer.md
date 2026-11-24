# Feature: Cliente en la barra

Objetivo: reemplazar el pedido fijo por clientes que entran, se sientan en una barra curva de 3 asientos con una bandeja cada uno, piden, esperan con paciencia y se van al recibir su plato o al quedarse sin paciencia.
Rama: `feat/bar-customer` (desde `feat/core-loop`, sin pushear). Entrega: `single-pr` (como las features anteriores; pronóstico ~750 líneas). Test runner: Unity Test Framework 1.8.0 (EditMode), corrido desde el editor con un menú temporal.

## Specs

- **S1 — Barra curva (CLAUDE.md §5, "Distribución de la estación").** "Barra curva alrededor del jugador: arco de −80° a +80°, radios de 0,22 a 0,78, alto 0,90." "6 ingredientes en un arco a 0,33, de izquierda a derecha: lechuga (−70°), tomate (−42°), queso (−14°), medallón (14°), lomito (42°), papas (70°)." "3 bandejas en un arco a 0,50, a −40°, 0° y 40°, una frente a cada asiento." "3 asientos en un arco a 1,00, a −40°, 0° y 40°". "Pared del fondo detrás de los clientes (z = 2,40), con puerta a la izquierda". Reemplaza el mostrador recto. El builder sigue abortando si algo interactuable queda a ≥ 0,6 m del jugador (S6 de core-loop).
- **S2 — Asiento (`BarSeat`, CLAUDE.md §7).** "asiento de la barra: cliente sentado y su bandeja; avisa cuando queda libre". Al sentarse un cliente, su bandeja toma el pedido; al irse, la bandeja queda sin pedido y vacía.
- **S3 — Ciclo del cliente (CLAUDE.md §5, "Ciclo de un cliente (barra)").**
  1. "El cliente entra y se sienta solo en el primer asiento libre."
  2. "El cliente pide: aparece el globo con los ingredientes y empieza a correr su paciencia."
  3. "Cuando la bandeja coincide con el pedido, el cliente toma el plato solo. Si el jugador pone un ingrediente que no pidió, niega con la cabeza; el jugador lo saca y sigue."
  4. "El cliente come unos segundos [...] y se va." "Parámetros: tiempo que el cliente se queda comiendo (inicial 3 s)".
- **S4 — Paciencia y carita.** "Carita: 3 estados — feliz (> 60 % de paciencia), neutral, enojada (< 25 %)." Paciencia inicial 25 s ("Paciencia del cliente = `25 − 10·t` segundos" con t = 0, sin reloj todavía), parametrizable. Decisión del usuario (L2): al llegar a 0, el cliente "Se va enojado": se levanta sin comer y libera el asiento.
- **S5 — Llegada de clientes (`CustomerSpawner`).** "genera clientes según el Reloj y el máximo simultáneo; los sienta en el primer asiento libre". Sin reloj: intervalo fijo de 12 s ("Intervalo entre clientes = `12 − 7·t`" con t = 0) y máximo simultáneo 1 (pedido del padre aceptado: "un spawner simple que mande un cliente a la vez"), ambos parametrizables. Cada cliente pide un plato al azar del menú.
- **S6 — Verificación en Play.** En `Fonda` llega un cliente, se sienta, muestra su pedido; armar el plato en su bandeja hace que coma y se vaya; un ingrediente que no pidió lo hace negar con la cabeza; sin atenderlo, se va enojado al acabarse la paciencia; después llega el siguiente.

### Supuestos (ajustables, no pedidos explícitamente)
- A1: Cliente de bloqueo: cuerpo (cápsula) y cabeza (esfera) de colores, animados por código: camina en línea recta de la puerta al asiento, se sienta, y vuelve a la puerta al irse.
- A2: El globo es texto en el mundo (TextMesh Pro) con el nombre del plato y sus ingredientes; los íconos quedan para cuando haya arte. La carita es un disco de color sobre la cabeza (verde, amarillo, rojo) hasta que se diseñe (pendiente abierto del GDD).
- A3: "Toma el plato": la bandeja se vacía como hoy (entrega de core-loop) y el cliente come sin plato visible. Sin propinas ni monedas en esta feature.
- A4: "Primer asiento libre" se recorre en el orden centro, izquierda, derecha, para que un cliente solo use el asiento del centro (Escena 1).
- A5: Con todos los asientos ocupados el spawner espera; el lugar de espera de pie queda para la hora pico.
- A6: La paciencia corre solo mientras el cliente está sentado esperando; caminando y comiendo no corre.
- A7: Si el cliente se va enojado, lo que quedó en su bandeja se descarta.
- A8: Salsas, recetario, frasco de propinas, reloj de pared y modelos KayKit quedan fuera.

## Tasks

| ID | Specs | Ruta | Estado | Commit |
|---|---|---|---|---|
| T1 | S3, S4, A6 | inline por el padre (sin razón de escritor): regla pura `CustomerCycle` (estados, paciencia, comer, irse enojado), carita y elección de asiento, con tests RED antes | hecho: RED 9 (`NotImplementedException`), GREEN 9/9 | bf54d94, 349e4de |
| T2 | S2, S3, S5, A1–A4, A7 | inline: `BarSeat`, `Customer`, `CustomerSpawner`, globo y carita; `Tray` sin pedido hasta que se sienta alguien | hecho: prueba en Play (L6) | bf54d94, 349e4de |
| T3 | S1 | inline: builder de la barra curva, arcos de ingredientes, 3 bandejas, 3 asientos, pared y puerta, chequeo de alcance | hecho: RED 9 (stubs en cero), GREEN; builder `errors: 0` | bf54d94, 349e4de |
| T4 | S6 | padre vía computer-use: compilar, tests, builder, prueba en Play | hecho: 35/35, prueba en Play (L6); falta la prueba a mano | bf54d94, 349e4de |

## Log

- **L1 (2026-11-23, pedido del usuario):** "cual es la siguiente feature". Propuesta del padre: el cliente en la barra (barra curva con 3 asientos y bandejas, `Customer` que se sienta, pide, tiene paciencia con carita, toma el plato y se va, niega con la cabeza ante un error, y "Un spawner simple que mande un cliente a la vez"). Usuario: "si, dale".
- **L2 (2026-11-23, decisión del usuario):** a la pregunta de qué pasa cuando a un cliente sentado se le acaba la paciencia, eligió "Se va enojado": "Se levanta sin comer, sin pagar, y libera el asiento."
- **L3 (2026-11-23, decisión del padre):** la barra se genera con una malla de arco propia en el builder; no se depende de `FondaSketchBuilder.cs` ni de `Assets/Art/Sketch/` (sin commitear, de otra sesión).
- **L4 (2026-11-23, RED):** `CustomerCycleTests` (9) fallaron con `NotImplementedException` sobre stubs; `BarLayoutTests` (9 de 10) fallaron con posiciones en cero (radio esperado 0,33/0,50/1,00, obtenido 0). La validación del layout pasaba también con el mostrador recto.
- **L5 (2026-11-23, GREEN y decisiones de layout):** 35/35. Para que las bandejas de asientos vecinos no se pisen, las ranuras van a 0,10 m entre sí (antes 0,15) con radio de socket 0,045 m y bandejas de 0,30 × 0,14 m; la validación del builder ahora también chequea que ninguna ranura se superponga con otra y que todo esté sobre la barra. La barra es una malla de arco propia con `MeshCollider` (`Assets/Meshes/Blockout/FondaBar.asset`). Fonda.Runtime y Fonda.Editor referencian `Unity.TextMeshPro`. Se borraron los materiales de los marcadores viejos (cliente y zona de ingredientes).
- **L6 (2026-11-23, prueba en Play):** con una prueba temporal: las 3 bandejas arrancan cerradas; a los 2 s entra un cliente por la puerta y a los 3,5 s se sienta en el centro, pide "Hamburguesa sola" (globo "Hamburguesa sola / Medallón"), solo esa bandeja abre 1 ranura, cara feliz y paciencia 25. Tomate en la ranura → `[Tray] Ingrediente que no se pidio`, la cabeza gira hasta 22° y el tomate queda. Al sacarlo y poner medallón → `Plato correcto`, el cliente pasa a comer y la bandeja se cierra; a los 3 s "Terminó de comer y se va", el asiento queda libre y el cliente desaparece en la puerta. El siguiente cliente llega al centro ("Lomito completo"); sin atenderlo (a 5× de velocidad) pasa por neutral y enojada y a los 25 s "Se va enojado [...] sin comer", con el asiento libre y la bandeja cerrada. Sin excepciones. Captura de la vista de escena: pared con puerta a la izquierda, barra y cliente con el globo legible. Scripts temporales borrados. Pendiente: probar a mano con el simulador y revisar la barra curva en el visor.

- **L7 (2026-11-23, entrega):** commits `bf54d94` (regla, asiento, cliente, spawner y tests) y `349e4de` (builder de la barra curva, escena, prefab y documento). Sin push.
