# La Fonda de la Abuela — Contexto del proyecto

> Documento de contexto para agentes de código. Ubicar en la raíz del repo (puede renombrarse a `AGENTS.md` o `CLAUDE.md`).

## 1. Qué es
Juego VR de **simulación/gestión casual, sin terror**, para una tarea académica. El jugador atiende el turno de tarde en la fonda de su abuela: toma pedidos y arma platos **agarrando ingredientes y soltándolos en una bandeja** (grab & drop) antes de que el cliente pierda la paciencia.

- Tono: ligero y satisfactorio. Se aprende por repetición. Sin "game over", sin amenaza. El reto es el ritmo y la destreza manual.
- Frase de la abuela al inicio: *"no los hagas esperar mucho, mijo"*.

## 2. Stack y entorno
- **Motor:** Unity 6 (6000.6.10f1, build Apple Silicon)
- **Plantilla:** VR (incluye XR Interaction Toolkit + OpenXR + escena de ejemplo)
- **Render:** URP
- **Lenguaje:** C#
- **Input:** nuevo Input System
- **Máquina de desarrollo:** MacBook M3 Pro (macOS)
- **Control de versiones:** Git + GitHub (repo `LaFondaDeLaAbuela`), Git LFS recomendado para assets
- **Ruta local:** `/Users/alielivalverde/My project`

### Pruebas sin visor
- Se prueba en el editor con el **XR Interaction Simulator / XR Device Simulator** (teclado + mouse).
- Activable en *Project Settings → XR Plug-in Management → XR Interaction Toolkit → "Use XR Interaction Simulator in scenes"*.
- **Quest Link no funciona en macOS.** Para probar en un Meta Quest real: build Android (APK) + Meta Quest Developer Hub. Desactivar el simulador en builds.

## 3. Restricciones de diseño (no romper)
- **Sin locomoción.** El jugador está fijo frente a un mostrador. Desactivar move/turn providers del XR Origin.
- Todo lo interactuable debe estar **al alcance del brazo** (mostrador ~0,9 m de alto, ingredientes a < 0,6 m del jugador).
- **Un error nunca termina el turno.** Si el plato está mal, el cliente vuelve a pedir; el costo es el tiempo perdido.
- Los ingredientes **nunca se agotan** (dispensadores que reponen).
- Interacción base con componentes de XRI: `XRGrabInteractable` (ingredientes) y `XRSocketInteractor` (ranuras de la bandeja).

## 4. Estructura del juego (GDD resumido)

### Sistema transversal: Reloj de la Fonda
Reloj visible en el juego (de pared) que además controla la dificultad. Afecta la frecuencia de llegada de clientes y la complejidad de pedidos.

### Escena 1 — Apertura del turno (tutorial)
- La abuela explica cómo agarrar ingredientes y armar un plato.
- Un solo cliente, plato de **1 ingrediente**.
- Acierto: campanita + carita feliz. Error: el cliente pide de nuevo.

### Escena 2 — Turno normal
- Pedidos de **2–3 ingredientes**.
- A veces **2 clientes** esperan a la vez (2 bandejas).
- Carita feliz/enojada sobre el cliente según el tiempo de espera.
- Menú de 3–4 platos que reutilizan los mismos ingredientes.
- El cliente entra y sale del cuadro (no hay desplazamiento del jugador).

### Escena 3 — Hora pico
- Últimos 60–90 s del turno: llegan 2–3 clientes juntos y el jugador debe priorizar.

## 5. Propuestas de diseño (PENDIENTES DE CONFIRMAR por el autor)
Implementar de forma **parametrizable** (ScriptableObjects o campos serializados) para poder ajustar sin tocar código.

**Mecánica Escena 3:** multitarea (varias órdenes activas) + combo de rapidez como bonus de propina. Error = rehacer el plato, sin penalización extra.

**Fórmula del Reloj** (`t` = tiempo transcurrido / duración total del turno, de 0 a 1):
- Intervalo entre clientes = `12 − 7·t` segundos
- Paciencia del cliente = `25 − 10·t` segundos
- Ingredientes por pedido = `1 + round(2·t)` (1 a 3)
- Clientes simultáneos máx.: Escena 1 = 1, Escena 2 = 2, Escena 3 = 3

**Duración del turno:** tiempo fijo ~5 min (E1 ≈ 1 min, E2 ≈ 3 min, E3 ≈ 1:30). Al final, pantalla de resumen: clientes atendidos y propinas.

**Menú tentativo** (5 ingredientes: arroz, huevo, papa, carne, ensalada):
| Plato | Ingredientes |
|---|---|
| Plato de papa | papa (tutorial) |
| Arroz con huevo | arroz + huevo |
| Plato de carne | carne + arroz + ensalada |
| Plato completo | carne + papa + huevo |

**Feedback:**
- Carita: 3 estados — feliz (> 60 % de paciencia), neutral, enojada (< 25 %).
- Pedido mostrado en un globo de diálogo con íconos de ingredientes.
- Sonidos: "plop" al soltar, campanita al acertar, gruñido corto al fallar.
- Vibración (haptics) en controles al agarrar y soltar.
- Entrega del plato: automática cuando la bandeja coincide con el pedido.

## 6. Pendientes abiertos
- Confirmar mecánica final de la Escena 3.
- Confirmar fórmula y valores del Reloj de la Fonda.
- Cantidad y variedad final del menú.
- Condición de cierre del turno (propuesto: tiempo fijo).
- Diseño visual de la carita y de los sonidos.
- Uso de las propinas (por ahora solo contador/resumen).

## 7. Arquitectura sugerida (scripts a crear)
Carpeta `Assets/Scripts/`:

| Script | Responsabilidad |
|---|---|
| `IngredientData` (ScriptableObject) | id, nombre, ícono, prefab |
| `RecipeData` (ScriptableObject) | nombre del plato, lista de ingredientes requeridos |
| `Ingredient` | componente en cada ingrediente; referencia a su `IngredientData` |
| `IngredientDispenser` | repone una copia cuando se agarra el ingrediente (escucha `selectExited`/`selectEntered`) |
| `Tray` | gestiona sockets; al entrar/salir ingredientes compara contra el pedido activo; emite evento de plato correcto/incorrecto |
| `Customer` | pedido asignado, barra de paciencia, estado de carita, entrada/salida del cuadro |
| `CustomerSpawner` | genera clientes según el Reloj y el máximo simultáneo |
| `FondaClock` | tiempo del turno, calcula `t` y expone los parámetros de dificultad; muestra la hora en el reloj de pared |
| `ShiftManager` | flujo del turno: Escena 1 → 2 → 3 → resumen; propinas y clientes atendidos |
| `FeedbackManager` | sonidos, haptics, efectos visuales |
| `GrandmaTutorial` | diálogos/pasos del tutorial de la Escena 1 |

Convenciones:
- Comunicación por eventos (`UnityEvent` / C# events), evitar `FindObjectOfType` en Update.
- Valores de balance en ScriptableObjects (`DifficultySettings`).
- Comentarios y nombres de variables en inglés; textos de UI en español.

## 8. Estado actual
- [x] Unity 6 LTS instalado (Apple Silicon)
- [x] Proyecto creado con plantilla VR y repo de GitHub
- [ ] Verificar el simulador en la escena de ejemplo
- [ ] Crear escena `Fonda` con XR Origin, piso y mostrador
- [ ] Implementar scripts base (dispensador, bandeja, cliente)
- [ ] Spawner + Reloj de la Fonda
- [ ] Escenas 1, 2 y 3 + resumen final
- [ ] Feedback (carita, sonidos, haptics)
