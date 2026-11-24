# Feature: Tipos de cliente

Objetivo: cada cliente es uno de los 8 tipos del GDD, con su personaje de Blender, su paciencia, sus platos, su propina y su tiempo comiendo, todo en `CustomerProfile` (ScriptableObject).
Rama: `feat/customer-types` (desde `feat/combo` en 807e4f5, sin pushear). Entrega: `single-pr` (pronóstico ~350 líneas). Test runner: Unity Test Framework (EditMode), corrido desde el editor abierto con un menú temporal (TestRunnerApi); prueba de Play con un test PlayMode temporal.

## Specs

- **S1 — Perfil (CLAUDE.md §5).** "cada tipo es un `CustomerProfile` (ScriptableObject) con multiplicador de paciencia, platos favoritos, multiplicador de propina, tiempo comiendo, reacción a la salsa y frases."
- **S2 — Valores de la tabla (CLAUDE.md §5, "Tipos de cliente").**

| Tipo | Paciencia | Pide | Propina |
|---|---|---|---|
| Oficinista apurado | ×0,7 | cualquiera | buena si es rápido |
| Abuelito paciente | ×1,5 | hamburguesa sola, tazón de papas | normal |
| Niño | ×1,0 | siempre tazón de papas | monedas chicas |
| Habitual | ×1,0 | *"lo de siempre"* (sin globo; la abuela da la pista) | buena |
| Trabajador de obra | ×1,0 | platos completos | generosa |
| Estudiante | ×1,2 | hamburguesa sola, tazón de papas | chica |
| Turista | ×1,3 | cualquiera; se queda más tiempo comiendo porque le saca foto al plato | variable |
| Tacaño | ×1,0 | cualquiera | la peor |

- **S3 — Aspecto.** Cada tipo usa su personaje de Blender (`Customer_<Tipo>.fbx`), en el orden de `FondaCoreLoopBuilder.CustomerTypes`: Office, Grandpa, Child, Regular, Worker, Student, Tourist, Stingy.
- **S4 — Parametrizable.** "Implementar de forma **parametrizable** (ScriptableObjects o campos serializados) para poder ajustar sin tocar código." Pendiente abierto del GDD: "Valores finales de cada tipo de cliente y con qué frecuencia aparece cada uno."
- **S5 — Verificación en Play.** Entran clientes de distintos tipos, cada uno con su personaje, su paciencia y sus platos; las monedas siguen la propina del tipo.

### Supuestos (ajustables, no pedidos explícitamente)
- A1: La propina de cada tipo es un rango de monedas (mín.–máx.) que se reparte por la paciencia que le quedaba, como hoy (1–3). Oficinista 0–3 (buena solo si es rápido), Abuelito 1–3, Niño 1–2, Habitual 2–3, Trabajador 2–3, Estudiante 1–2, Turista 0–3 (variable: depende más de la rapidez), Tacaño 0–1. Con el combo (+2) nadie pasa de 5 monedas.
- A2: Tiempo comiendo = el del turno × multiplicador del tipo. Turista ×2; el resto ×1.
- A3: "Platos completos" = Hamburguesa completa y Lomito completo. "Cualquiera" = todo el menú.
- A4: El plato sale de los favoritos del tipo, con la cantidad de ingredientes que pide el reloj, o la más cercana. Si algún tipo tiene un favorito con la cantidad exacta, solo esos tipos pueden entrar; así en la apertura (1 ingrediente) no entra el trabajador con un plato de 3.
- A5: Frecuencia: un peso por tipo, todos 1 (igual probabilidad).
- A6: El habitual dice "¡Lo de siempre, doña!" en el globo y abajo los ingredientes. Sin la voz de la abuela todavía no hay pista, así que el globo no se esconde.
- A7: Quedan fuera: la reacción y el efecto de la salsa (no hay salsas todavía), las frases y la voz.
- A8: Los perfiles son assets en `Assets/Data/Customers/`; el builder los crea con estos valores solo si faltan, para no pisar ajustes a mano.

## Tasks

| ID | Specs | Ruta | Estado | Commit |
|---|---|---|---|---|
| T1 | S1, S2, S4, A1–A5 | inline (sin razón de escritor): `CustomerProfile`, `CustomerTypeRule` (tipos elegibles, elección por peso, plato), propina por tipo, tests RED antes | hecho: RED 15 de 17 (6 `NotImplementedException`, 8 valores por defecto faltantes, globo y propina), GREEN 176/176 | ver L5 |
| T2 | S3, A6, A8 | inline: `Customer` usa el aspecto y el globo del tipo; el spawner elige el tipo; el builder crea los 8 perfiles | hecho: escena construida con 0 errores, 8 perfiles en `Assets/Data/Customers/` | ver L5 |
| T3 | S5 | padre en el editor abierto vía computer-use: tests y prueba en Play con test PlayMode temporal | hecho: 176/176, prueba en Play (L3); falta prueba a mano | ver L5 |

## Log

- **L1 (2026-11-23, pedido del usuario):** "Siguiente feature". Propuesta del padre: tipos de cliente (`CustomerProfile` con los 8 personajes de Blender). Bloqueada hasta que la sesión de diseño commiteara la barra recta y los personajes (807e4f5, 2026-11-24). Usuario: "Sí, dale".
- **L2 (2026-11-24, RED y GREEN):** `CustomerTypeTests` (17 casos): fallaron 15 (6 con `NotImplementedException`, los 8 valores por defecto, el globo del habitual y la propina del oficinista lento). `Defaults_FitTheBar` pasó vacío con la lista vacía. GREEN: 176/176 EditMode. Escena `Fonda` reconstruida desde el menú: errores 0.
- **L3 (2026-11-24, prueba en Play):** test PlayMode temporal sobre `Fonda`, turno completo a ×10, sirviendo por código a 3 de cada 4 clientes. 38 clientes de 7 tipos (el estudiante no salió en esta corrida). Cada uno mostró su personaje (`Customer_<Tipo>`). Paciencia = la del reloj × el tipo (0,70, 1,00, 1,30, 1,50). El turista comió 6 s; el resto, 3 s. Abuelito y niño pidieron tazón de papas; el trabajador solo entró con platos de 3 ingredientes. Monedas con paciencia llena: oficinista 3, habitual 3, turista 3, tacaño 1, más el combo. Resumen: "Clientes atendidos: 29 | Se fueron enojados: 9 | Mejor combo: ×4 | Propinas: $110". Sin errores ni excepciones. Scripts temporales borrados. Pendiente: prueba a mano con el simulador.
- **L4 (2026-11-24, consecuencia de A4, para decidir):** con A4, los tipos que solo piden platos de 1 ingrediente (abuelito, niño, estudiante) entran solo en la apertura, y el trabajador solo en la hora pico. En el turno normal (2 ingredientes) entran solo los que piden "cualquiera". Alternativa: dejar entrar a cualquier tipo y que pida su favorito más cercano a lo que marca el reloj.
