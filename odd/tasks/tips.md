# Feature: Cobrar con la mano

Objetivo: el cliente que comió deja monedas junto a su bandeja; el jugador las agarra y las echa al frasco de propinas, que cuenta solo las que entran y muestra el total.
Rama: `feat/tips` (desde `feat/bar-customer`, sin pushear). Entrega: `single-pr` (como las anteriores; pronóstico ~500 líneas). Test runner: Unity Test Framework 1.8.0 (EditMode), corrido en batch (`-runTests`) porque Unity quedó cerrado; prueba de Play con un test PlayMode temporal.

## Specs

- **S1 — El cliente deja monedas (CLAUDE.md §5, "Ciclo de un cliente (barra)", paso 5).** "El cliente come unos segundos, deja monedas junto a su bandeja (sin taparla) y se va." "Propinas en mano": "El monto depende de la paciencia que le quedaba al cliente y del combo." El combo queda fuera (ver A4). Un cliente que se va enojado no comió y no deja nada (decisión L2 de bar-customer: "sin pagar").
- **S2 — Frasco de propinas (CLAUDE.md §5).** "El jugador las agarra y las echa al frasco; solo cuentan cuando entran al frasco." "Frasco de propinas en el extremo derecho de la barra (78°, a 0,45), al alcance de la mano." Idea 4: "Frasco de propinas en la barra que se llena de monedas; lo llena el jugador con la mano". `TipJar` (CLAUDE.md §7): "cuenta las monedas que entran al frasco y emite el evento de propina cobrada".
- **S3 — Moneda agarrable (`TipCoin`, CLAUDE.md §7).** "moneda agarrable que deja el cliente". Las ranuras de la bandeja no la aceptan (ya filtran todo lo que no es ingrediente).
- **S4 — Monedas sin recoger.** "Si el jugador no recoge las monedas, quedan en la barra sin castigo". La suma automática "al final del turno (propuesto)" queda para la feature del turno.
- **S5 — Verificación en Play.** Un cliente atendido deja monedas junto a su bandeja; al echarlas al frasco el total sube una vez por moneda; una moneda sostenida dentro del frasco no cuenta hasta soltarla; un cliente que se va enojado no deja monedas.

### Supuestos (ajustables, no pedidos explícitamente)
- A1: Cantidad: de 1 a 3 monedas de valor 1 según la paciencia al recibir el plato: `1 + round(2 · paciencia restante / paciencia máxima)`. Mínimo, máximo y valor parametrizables.
- A2: Dónde caen las monedas ("junto a la bandeja", "a ~0,50"): en la barra curva no hay hueco entre bandejas vecinas (6° ≈ 5 cm), así que se usan puntos sin tapar la bandeja: asiento izquierdo a −65° y derecho a 65°, a 0,50 m (en la punta de afuera de su bandeja); asiento del centro adelante de su bandeja, a 0°, 0,39 m (L3). El builder valida alcance y distancia a dispensadores y ranuras.
- A3: Una moneda que entra al frasco se cobra al soltarla adentro y desaparece; el frasco muestra una pila que crece y un cartel "Propinas: $N". Así no se puede sacar y volver a contar.
- A4: Combo, tirar monedas al cliente, tipos de cliente y voz de la abuela quedan fuera.
- A5: Monedas de 4 cm de diámetro (más grandes que las reales) para que se puedan agarrar en VR.

## Tasks

| ID | Specs | Ruta | Estado | Commit |
|---|---|---|---|---|
| T1 | S1, S2, A1, A3 | inline por el padre (sin razón de escritor): `TipRule` (monedas según paciencia, 0 si se fue enojado) y `TipJar.Deposit` (cuenta una vez, no si está sostenida), tests RED antes | hecho: RED 7 (`NotImplementedException`), GREEN | C1 |
| T2 | S1, S3, A2, A5 | inline: `TipCoin`, monedas al irse desde `BarSeat`, frasco y cartel, lugares de monedas y frasco en el builder con validación | hecho: RED 4 (stubs en cero), GREEN 46/46; builder `errors: 0` | C2 |
| T3 | S5 | padre en batch (Unity cerrado): tests, builder, prueba en Play con test PlayMode temporal | hecho: 46/46, prueba en Play (L5); falta prueba a mano | C2 |

## Log

- **L1 (2026-11-23, pedido del usuario):** "cual es la siguiente feature". Propuesta del padre: cobrar con la mano (el cliente deja monedas según la paciencia, frasco a 78° y 0,45 m donde "solo cuentan cuando entran", contador visible, monedas sin recoger quedan sin castigo, un cliente enojado no deja nada; combo, tirar monedas y suma al cierre para después). Usuario: "si, dale".
- **L2 (2026-11-23, decisión del padre):** lugares de las monedas según A2, calculados: centro (0°, 0,40) a 0,113 m del dispensador más cercano y 0,100 m de la ranura más cercana; costados (±65°, 0,50) a 0,174 m de dispensadores, 0,121 m de ranuras y 0,077 m de la punta de su bandeja; el derecho queda a 0,118 m del frasco. Frasco: 0,131 m del dispensador de papas y 0,229 m de la ranura más cercana.
- **L3 (2026-11-23, corrección del layout):** la validación nueva marcó la moneda del centro a 0,0999 m de la ranura del medio (mínimo 0,10). Se movió de 0,40 a 0,39 m: 0,11 m de la ranura y 0,106 m del dispensador de queso.
- **L4 (2026-11-23, RED y GREEN):** `TipTests` (7 casos) fallaron con `NotImplementedException` sobre stubs; las 2 pruebas nuevas de `BarLayoutTests` fallaron con posiciones en cero. GREEN: 46/46 EditMode. Dos correcciones durante GREEN: un `Tips.cs` con tres clases rompía la serialización de MonoBehaviours (se separó en `TipRule.cs`, `TipCoin.cs`, `TipJar.cs`), y una moneda caía sobre el borde de la bandeja (altura 0,924), así que las monedas van en fila y en los costados la fila corre en sentido radial.
- **L5 (2026-11-23, prueba en Play):** con un test PlayMode temporal sobre `Fonda`: un cliente atendido dejó 3 monedas a 0,904 m de alto, a menos de 0,045 m de su lugar; una moneda sostenida dentro del frasco no contó (total 0); al soltarla, total 1 y cartel "Propinas: $1"; las otras dos, soltadas de a una, total 3, "Propinas: $3" y la pila visible; el siguiente cliente, sin atender, se fue enojado y no dejó monedas (total sigue en 3). Sin excepciones. Scripts temporales borrados. Pendiente: prueba a mano con el simulador.
