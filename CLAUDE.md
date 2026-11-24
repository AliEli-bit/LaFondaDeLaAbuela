# La Fonda de la Abuela — Contexto del proyecto

> Documento de contexto para agentes de código. Ubicar en la raíz del repo (puede renombrarse a `AGENTS.md` o `CLAUDE.md`).

## 1. Qué es
Juego VR de **simulación/gestión casual, sin terror**, para una tarea académica. El jugador **es la abuela** y atiende el turno de tarde en su propia fonda: toma pedidos y arma platos **agarrando ingredientes y soltándolos en una bandeja** (grab & drop) antes de que el cliente pierda la paciencia. Es como una **barra**: el cliente se sienta frente al mostrador, el jugador arma el plato frente a su cara y el cliente lo toma cuando coincide con su pedido. El jugador **cobra con la mano**: agarra las propinas y las echa al frasco. Los clientes entran y se sientan solos.

- Tono: ligero y satisfactorio. Se aprende por repetición. Sin "game over", sin amenaza. El reto es el ritmo y la destreza manual.
- Identidad: el jugador ve **manos de abuela** (delantal, pulsera). Los clientes la conocen y la llaman *"doña"* o *"abuelita"*. El humor sobre la edad es cariñoso, nunca de burla.
- Frase de la abuela al abrir (propuesta): *"¡a ver, a ver, a trabajar!"*.

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
- **Un error nunca termina el turno.** Si el jugador pone un ingrediente que el cliente no pidió, el cliente niega con la cabeza y su globo marca el error; el jugador saca ese ingrediente y sigue. El costo es el tiempo perdido.
- Los ingredientes **nunca se agotan** (dispensadores que reponen).
- **No hay personaje ayudante.** Los clientes se sientan solos y todo el ritmo lo marca el jugador.
- Interacción base con componentes de XRI: `XRGrabInteractable` (ingredientes) y `XRSocketInteractor` (ranuras de la bandeja).

## 4. Estructura del juego (GDD resumido)

### Sistema transversal: Reloj de la Fonda
Reloj visible en el juego (de pared) que además controla la dificultad. Afecta la frecuencia de llegada de clientes y la complejidad de pedidos.

### Escena 1 — Apertura del turno (tutorial)
- Un **cuaderno de recetas** sobre la barra muestra el plato con dibujos y la abuela lo lee en voz baja (*"a ver… hamburguesa sola, nomás"*).
- Un solo cliente, plato de **1 ingrediente**. Se sienta en el asiento del centro.
- El tutorial recorre el ciclo completo: armar, entregar y cobrar.
- Acierto: campanita + carita feliz. Error: el cliente niega con la cabeza y el cuaderno marca qué sobra.

### Escena 2 — Turno normal
- Pedidos de **2–3 ingredientes**.
- A veces hay **2 clientes** sentados a la vez (2 asientos, cada uno con su bandeja).
- Carita feliz/enojada sobre el cliente según el tiempo de espera.
- Menú de hamburguesas, lomitos y tazón de papas que reutilizan los mismos ingredientes (ver "Menú").
- El cliente entra por la puerta, se sienta en la barra y se va al terminar (no hay desplazamiento del jugador).

### Escena 3 — Hora pico
- Últimos 60–90 s del turno: llegan 2–3 clientes juntos y el jugador debe priorizar.
- Los 3 asientos están ocupados y 1 cliente más espera de pie; se sienta en el primer asiento que se libere.
- El jugador atiende hasta 3 pedidos a la vez; la fila de espera agrega presión sin agregar trabajo.

## 5. Propuestas de diseño (PENDIENTES DE CONFIRMAR por el autor)
Implementar de forma **parametrizable** (ScriptableObjects o campos serializados) para poder ajustar sin tocar código.

**Mecánica Escena 3:** multitarea (varias órdenes activas) + combo de rapidez como bonus de propina. Error = rehacer el plato, sin penalización extra.

**Ciclo de un cliente (barra):**
1. El cliente entra y se sienta solo en el primer asiento libre. Si no hay, espera de pie.
2. El cliente pide: aparece el globo con los ingredientes y empieza a correr su paciencia.
3. El jugador arma el plato en la bandeja de ese asiento (cada asiento tiene la suya).
4. Cuando la bandeja coincide con el pedido, el cliente toma el plato solo. Si el jugador pone un ingrediente que no pidió, niega con la cabeza; el jugador lo saca y sigue.
5. El cliente come unos segundos, deja monedas junto a su bandeja (sin taparla) y se va. El jugador las agarra y las echa al frasco; solo cuentan cuando entran al frasco.
- Parámetros: tiempo que el cliente se queda comiendo (inicial 3 s); la paciencia del cliente que espera de pie no corre hasta que se sienta (propuesto).

**Propinas en mano:**
- El monto depende de la paciencia que le quedaba al cliente y del combo.
- Si el jugador no recoge las monedas, quedan en la barra sin castigo y se suman solas al final del turno (propuesto). En la hora pico se vuelve una decisión: cobrar ahora o seguir cocinando.
- **Tirar la propina:** el jugador puede tirarle cualquier moneda al cliente. El cliente reacciona de forma cómica (se agacha, se ofende) y la abuela refunfuña (*"¡y encima tacaño!"*). El único efecto es que esa plata se pierde: tirar una propina nunca da nada a cambio, para que siga siendo un gesto y no un truco.

**Salsas (fuera de los pedidos, confirmado por el autor):**
- Una botella de kétchup y una de mostaza (KayKit `ketchup` y `mustard`) en la barra. No forman parte de ningún pedido.
- El jugador agarra la botella y aprieta el gatillo (evento `activated` de XRI): sale un chorro con sonido "splurt" y deja una mancha donde cae.
- El jugador la usa como quiera: sobre el plato, la barra o un comensal. El juego no lo limita.
- En la bandeja la salsa no cuenta como ingrediente ni como error.
- Si el chorro le cae a un comensal, este reacciona según su tipo (ver "Tipos de cliente"). La abuela responde pícara, nunca humillante: *"¡pa' que aprenda!"* si el cliente dejó mala propina, *"¡ay, se me resbaló!"* si no.
- **Afecta según el tipo de cliente** (confirmado por el autor). Así se vuelve una decisión: a quién sí y a quién no. Valores iniciales propuestos:
  - **Le gusta:** +20 % de paciencia (sin pasar del máximo) y propina ×1,25. El combo sigue.
  - **Le da igual:** sin efecto, solo la reacción.
  - **Le molesta:** −20 % de paciencia, propina ×0,75 y el combo se reinicia. Al tacaño le molesta el doble (−40 %, ×0,5).
- Solo cuenta el primer chorro que recibe cada cliente; los siguientes son solo reacción. Así no se puede exprimir la propina del turista.
- Las botellas no se acaban. Si se caen, vuelven solas a su lugar, igual que los ingredientes.

**Tipos de cliente (todo tipo de gente):**
- **Variedad visual:** niños, jóvenes, adultos y abuelitos, con distintas alturas, contexturas, tonos de piel, ropa y accesorios (sombrero, gorra, lentes, casco de obra, audífonos, maletín). Salen todos de la misma figura simple (cuerpo y cabeza), cambiando tamaño, color y accesorio, así que son baratos en el Quest.
- **Comportamiento:** cada tipo es un `CustomerProfile` (ScriptableObject) con multiplicador de paciencia, platos favoritos, multiplicador de propina, tiempo comiendo, reacción a la salsa y frases. Valores iniciales propuestos:

| Tipo | Paciencia | Pide | Propina | Si le cae salsa | Efecto de la salsa |
|---|---|---|---|---|---|
| Oficinista apurado | ×0,7 | cualquiera | buena si es rápido | mira el reloj y se limpia a toda prisa | le molesta |
| Abuelito paciente | ×1,5 | hamburguesa sola, tazón de papas | normal | se ríe y dice *"¡igualita que antes, doña!"* | le da igual |
| Niño | ×1,0 | siempre tazón de papas | monedas chicas | se ríe a carcajadas | le gusta |
| Habitual | ×1,0 | *"lo de siempre"* (sin globo; la abuela da la pista) | buena | se hace el ofendido y después se ríe | le da igual |
| Trabajador de obra | ×1,0 | platos completos | generosa | se limpia con la manga y sigue esperando | le molesta |
| Estudiante | ×1,2 | hamburguesa sola, tazón de papas | chica | saca una foto de la mancha | le da igual |
| Turista | ×1,3 | cualquiera; se queda más tiempo comiendo porque le saca foto al plato | variable | aplaude, cree que es parte del show | le gusta |
| Tacaño | ×1,0 | cualquiera | la peor | se ofende muchísimo | le molesta el doble |

**La voz de la abuela** (son solo audios, sin animación):
- Lee los pedidos en voz baja (*"a ver… lomito con tomate"*).
- Celebra los combos (*"¡eso, todavía tengo mano!"*) y se alegra al cobrar.
- Refunfuña si tira una propina.
- Si tarda con un cliente que pide *"lo de siempre"*, se acuerda en voz alta (*"ay, ¿qué era lo de don Pepe?… ¡lomito completo!"*); funciona como pista.

**Cuaderno de recetas:** en el extremo izquierdo de la barra. En la Escena 1 muestra el paso del tutorial; después queda abierto en el menú como referencia. Solo se lee, no se agarra.

**Ideas para que sea más divertido:**
1. Feedback al soltar: el ingrediente encaja con un rebote, sonido "plop", vibración y una nubecita de vapor.
2. Lanzar ingredientes a la bandeja (`throwOnDetach`). Si falla, cae y el dispensador repone; solo cuesta tiempo.
3. Combo de rapidez en todo el turno: cada entrega rápida seguida sube el tono de la campanita y la propina; un error reinicia el combo sin otra penalización.
4. Frasco de propinas en la barra que se llena de monedas; lo llena el jugador con la mano (ver "Propinas en mano").
5. Todo tipo de clientes, cada uno con su paciencia, sus platos y su reacción (ver "Tipos de cliente").

**Distribución de la estación** (concepto v5, barra curva; medidas en metros, jugador en el origen mirando hacia +z, ángulos medidos desde +z):
- Barra curva alrededor del jugador: arco de −80° a +80°, radios de 0,22 a 0,78, alto 0,90. Una barra recta no sirve: con 3 asientos bien separados, las bandejas de los lados quedarían a ~0,72, fuera de alcance.
- 6 ingredientes en un arco a 0,33, de izquierda a derecha: lechuga (−70°), tomate (−42°), queso (−14°), medallón (14°), lomito (42°), papas (70°). Quedan a ~0,16 entre centros.
- 3 bandejas en un arco a 0,50, a −40°, 0° y 40°, una frente a cada asiento.
- 3 asientos en un arco a 1,00, a −40°, 0° y 40° (0,68 entre clientes), y un lugar de espera de pie en (−0,35; 1,65).
- Pared del fondo detrás de los clientes (z = 2,40), con puerta a la izquierda y el reloj a 2,20 de alto.
- Frasco de propinas en el extremo derecho de la barra (78°, a 0,45), al alcance de la mano. Las monedas aparecen junto a la bandeja de cada asiento, a ~0,50.
- Cuaderno de recetas en el extremo izquierdo de la barra (−65°, a 0,65), fuera de alcance a propósito: solo se lee.
- Botellas de salsa en el extremo izquierdo de la barra, una al lado de la otra (−76°, a 0,50), al alcance de la mano.

**Dirección de arte (confirmada por el autor):**
- Estilo **low-poly de color plano**, con iluminación cálida precalculada y sin sombras en tiempo real. Un solo detalle con textura para darle identidad (por ejemplo, un mantel o un textil; motivo por definir).
- Paquete base: **KayKit Restaurant Bits** (Kay Lousberg, versión gratis, CC0, FBX/OBJ/GLTF). Usa un único atlas de gradiente de 1024×1024, así que casi todo comparte un material y se agrupa bien.
- Regla: un solo autor como base. Si falta algo, primero buscarlo en otros paquetes KayKit (por ejemplo, Furniture Bits) antes de mezclar con otros autores.
- Clientes: figuras simples estilizadas (cuerpo y cabeza) animadas por código.
- Manos de la abuela: las manos de XR Hands / Hands Interaction Demo, vestidas con manga de delantal y pulsera.
- Barra curva: ProBuilder (forma *Pipe* recortada a 160°), pendiente de instalar.
- Presupuesto de referencia en Quest 3: < 200 draw calls y < 1,5 M triángulos por cuadro a 72 Hz (a confirmar midiendo en el visor).
- Registrar cada paquete y su licencia en `CREDITS.md`.

**Fórmula del Reloj** (`t` = tiempo transcurrido / duración total del turno, de 0 a 1):
- Intervalo entre clientes = `12 − 7·t` segundos
- Paciencia del cliente = `25 − 10·t` segundos
- Ingredientes por pedido = `1 + round(2·t)` (1 a 3)
- Clientes simultáneos máx.: Escena 1 = 1, Escena 2 = 2, Escena 3 = 3 sentados + 1 esperando

**Duración del turno:** tiempo fijo ~5 min (E1 ≈ 1 min, E2 ≈ 3 min, E3 ≈ 1:30). Al final, pantalla de resumen: clientes atendidos y propinas.

**Menú (confirmado por el autor)** — 6 ingredientes: medallón, lomito, queso, lechuga, tomate, papas.
- Cada plato tiene una **base** que aparece sola en la bandeja cuando el cliente pide: la base del pan (hamburguesas y lomitos) o un tazón (papas). La base no cuenta como ingrediente.
- La bandeja compara qué ingredientes hay, no en qué orden.
- Al acertar una hamburguesa o un lomito, la **tapa del pan cae sola** encima: es parte del feedback de acierto.

| Plato | Base | Ingredientes |
|---|---|---|
| Hamburguesa sola | pan | medallón (tutorial) |
| Tazón de papas | tazón | papas |
| Hamburguesa con queso | pan | medallón + queso |
| Lomito con tomate | pan | lomito + tomate |
| Hamburguesa completa | pan | medallón + lechuga + tomate |
| Lomito completo | pan | lomito + queso + tomate |

Modelos de KayKit Restaurant Bits: medallón = `food_ingredient_burger_cooked`, lomito = `food_ingredient_steak_pieces`, queso = `food_ingredient_cheese_slice`, lechuga = `food_ingredient_lettuce_slice`, tomate = `food_ingredient_tomato_slice`, papas = `food_ingredient_potato_chopped`, pan = `food_ingredient_bun_bottom` / `food_ingredient_bun_top`, tazón = `bowl`. La cebolla (`food_ingredient_onion_rings`) queda de reserva.

**Feedback:**
- Carita: 3 estados — feliz (> 60 % de paciencia), neutral, enojada (< 25 %).
- Pedido mostrado en un globo de diálogo con íconos de ingredientes.
- Sonidos: "plop" al soltar, campanita al acertar, gruñido corto al fallar.
- Vibración (haptics) en controles al agarrar y soltar.
- Entrega del plato: automática cuando la bandeja del asiento coincide con el pedido; el cliente toma el plato.

## 6. Pendientes abiertos
- Confirmar mecánica final de la Escena 3.
- Confirmar fórmula y valores del Reloj de la Fonda.
- Probar en el visor que 6 dispensadores a ~0,16 entre sí se agarran sin tomar el de al lado.
- Condición de cierre del turno (propuesto: tiempo fijo).
- Diseño visual de la carita y de los sonidos.
- Uso de las propinas (propuesto: el jugador las echa al frasco + resumen final).
- Qué pasa con las monedas que el jugador no recoge (propuesto: se suman solas al final del turno).
- Diseño de las manos de la abuela y guion de sus frases.
- Probar en el visor que las bandejas de los lados (a 40°) se alcanzan cómodo sin moverse del lugar.
- Tiempo que el cliente se queda comiendo antes de liberar el asiento.
- Si la paciencia del cliente que espera de pie corre o no.
- Valores finales de cada tipo de cliente y con qué frecuencia aparece cada uno.
- Probar en el visor que las botellas de salsa (−76°, a 0,50) se alcanzan cómodo.

## 7. Arquitectura sugerida (scripts a crear)
Carpeta `Assets/Scripts/`:

| Script | Responsabilidad |
|---|---|
| `IngredientData` (ScriptableObject) | id, nombre, ícono, prefab |
| `RecipeData` (ScriptableObject) | nombre del plato, base (pan o tazón), lista de ingredientes requeridos |
| `Ingredient` | componente en cada ingrediente; referencia a su `IngredientData` |
| `IngredientDispenser` | repone una copia cuando se agarra el ingrediente (escucha `selectExited`/`selectEntered`) |
| `Tray` | gestiona sockets; pertenece a un asiento; al entrar/salir ingredientes compara contra el pedido del cliente sentado; emite evento de plato correcto/incorrecto |
| `BarSeat` | asiento de la barra: cliente sentado y su bandeja; avisa cuando queda libre |
| `CustomerProfile` (ScriptableObject) | tipo de cliente: aspecto, multiplicadores de paciencia y propina, platos favoritos, tiempo comiendo, reacción y efecto de la salsa (le gusta, le da igual, le molesta) y frases |
| `Customer` | pedido asignado, perfil, barra de paciencia, estado de carita, entrada, espera, asiento y salida; reacciona si le cae salsa o una moneda |
| `SauceBottle` | botella agarrable; al apretar el gatillo lanza el chorro, deja la mancha y avisa al cliente que golpea; vuelve a su lugar si se cae |
| `CustomerSpawner` | genera clientes según el Reloj y el máximo simultáneo; los sienta en el primer asiento libre o los deja esperando de pie |
| `GrandmaVoice` | frases de la abuela según eventos: pedido nuevo, combo, cobro, propina tirada, pista de *"lo de siempre"* |
| `RecipeBook` | cuaderno de recetas: muestra el paso del tutorial y después el menú |
| `TipCoin` | moneda agarrable que deja el cliente; detecta si se la tiran a un cliente y dispara su reacción |
| `TipJar` | cuenta las monedas que entran al frasco y emite el evento de propina cobrada |
| `FondaClock` | tiempo del turno, calcula `t` y expone los parámetros de dificultad; muestra la hora en el reloj de pared |
| `ShiftManager` | flujo del turno: Escena 1 → 2 → 3 → resumen; propinas y clientes atendidos |
| `FeedbackManager` | sonidos, haptics, efectos visuales |
| `GrandmaTutorial` | pasos del tutorial de la Escena 1, usando el cuaderno y la voz de la abuela |

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
