# Diseño: bandeja del sistema, autoarranque y soporte multi-mando

- **Fecha:** 2026-09-02
- **Estado:** aprobado, pendiente de plan de implementación
- **Versión objetivo:** 0.2.0
- **Rama:** `feature/tray-autostart-multipad`

## 1. Problema

La app hoy exige interacción manual en cada uso: abrirla y pulsar **START SERVICE**. Se busca que arranque
con Windows, sin ventana, con los servicios ya activos. Al analizar el requisito aparecieron dos bloqueos
en el código actual y un requisito adicional del usuario:

1. **El remapper no reintenta.** `BluetoothRemapper.RunAsync` (`BluetoothRemapper.cs:37-42`) enumera una
   sola vez; si no hay dispositivo DirectInput conectado reporta `NotFound` y retorna. Al arrancar con
   Windows el mando casi siempre está apagado o el Bluetooth aún no negoció, así que el auto-start moriría
   en el primer segundo y la app quedaría inerte en la bandeja. Sin reintento, la feature no sirve.
2. **El estado del servicio vive en la ventana.** `MainWindow` es dueña del `CancellationTokenSource`, del
   flag `_isRunning` y del último `RemapperStatus`; `OnClosed` llama a `Application.Current.Shutdown()`
   (`MainWindow.xaml.cs:170`). Con bandeja, el servicio debe correr sin ventana visible y el menú de la
   bandeja debe poder controlarlo. La ventana pasa a ser una vista opcional sobre un estado ajeno.
3. **Multi-mando** (pedido durante el brainstorming): varios mandos mapeados simultáneamente. Hoy el código
   toma `devices[0]` a ciegas (`BluetoothRemapper.cs:44`), crea un `ViGEmClient` y un pad virtual, y corre
   el loop de polling inline. `RemapperStatus` es un enum único, no una colección.

El loop de reconexión y el seguimiento de N dispositivos viven en el mismo lugar: un supervisor que enumera,
levanta un worker por mando y lo recoge cuando muere. Escribirlo primero para un mando y rehacerlo después
para N significa escribirlo dos veces. Por eso ambas cosas van en un solo diseño, en dos fases de entrega.

## 2. Decisiones tomadas

| Decisión | Elección | Descartado |
|---|---|---|
| Dónde vive la app minimizada | Bandeja del sistema (`NotifyIcon`) | Minimizada en barra de tareas |
| Control del autoarranque | Dos switches en la UI, persistidos | Siempre activo sin opciones; un solo switch combinado |
| Sin mando al arrancar | Reintento infinito en background | Reintento acotado; sin reintentos |
| Qué dispositivos reclamar | Solo 8BitDo (allow-list), con rechazo del par `045E/028E` | Todos los DInput con exclusiones; override manual en UI |
| Secuenciación | Un spec, dos fases | Features separadas |
| Arquitectura | Capa de servicio con estado propio | Supervisor estático con estado en la ventana |

### 2.1 Arquitectura: por qué capa de servicio

**Elegido:** un `ControllerService` singleton dueño del ciclo de vida, que expone
`ObservableCollection<ControllerEntry>`, un estado agregado y `Start()`/`Stop()`. `MainWindow` y el icono de
bandeja son dos consumidores independientes; ninguno es dueño del estado.

**Descartado — supervisor estático con estado en la ventana:** menos abstracciones y calca el idiom actual
(clases estáticas con `RunAsync` + callbacks), pero convierte a la ventana oculta en un singleton de estado
disfrazado de UI, y cada acción de la bandeja tiene que atravesarla.

**Descartado — servicio de Windows real + cliente UI:** un Windows Service corre en sesión 0, donde
DirectInput y el Bluetooth del usuario no son accesibles de forma confiable, y el force feedback de ViGEm
necesita la sesión interactiva. No es viable.

## 3. Estructura de archivos

```
Infrastructure/
  AppPaths.cs              %APPDATA%\8BitDoFixer\  (settings.json, crash.log)
  CrashLogger.cs           reemplaza el log duplicado de Program.cs + App.xaml.cs
  SingleInstanceGuard.cs   Mutex nombrado + EventWaitHandle "mostrá la ventana"
Settings/
  AppSettings.cs           record: AutoStartServices, StartWithWindows, IsEnglish, HasShownTrayHint
  SettingsStore.cs         load/save JSON, tolerante a archivo corrupto, escritura atómica
  StartupRegistration.cs   clave HKCU ...\CurrentVersion\Run (+ lectura de StartupApproved)
Models/
  ControllerState.cs       enum: Connecting, Mapped, Lost, DriverMissing
  ControllerEntry.cs       INotifyPropertyChanged: InstanceGuid, Name, State, PlayerIndex, RumbleSupported
  BatteryEntry.cs          BleDeviceId, Name, Level, LastRead
Services/
  ControllerService.cs     singleton: Start()/Stop(), colección observable, estado agregado
  ControllerSupervisor.cs  loop de enumeración + diff + reintento infinito
  ControllerWorker.cs      un mando: acquire, FFB, pad virtual, poll loop
  DeviceFilter.cs          reconoce 8BitDo, excluye ViGEm
  BatteryService.cs        refactor del monitor, keyeado por device id de BLE
  Xbox360Mapping.cs        funciones puras de mapeo
Tray/
  TrayIconHost.cs          TaskbarIcon, menú contextual, tooltip dinámico
tests/
  8bitdofixer.Tests/       xunit, net10.0-windows
8bitdofixer.sln            nueva: app + tests
```

**Eliminados/absorbidos:** `BluetoothRemapper.cs` → su loop se vuelve `ControllerWorker`; sus funciones
puras (`NormalizeAxis`, `ApplyDeadzone`, `NegateAxis`, `ApplyDpad`, `GetBtn`) se van a `Xbox360Mapping`,
porque son lo único de la app testeable sin hardware. `BluetoothBatteryMonitor.cs` → `BatteryService`.

**Modificados:** `Program.cs` (parseo de args + instancia única), `App.xaml` (`ShutdownMode="OnExplicitShutdown"`,
se elimina `StartupUri`), `App.xaml.cs` (orquestación de arranque), `MainWindow.xaml/.cs` (listas, switches,
ocultar en X), `Localization.cs` (strings nuevos), `8bitdofixer.csproj` (`H.NotifyIcon.Wpf`).

### 3.1 Dependencia nueva: H.NotifyIcon.Wpf

WPF no trae `NotifyIcon`. La alternativa sin NuGet es `<UseWindowsForms>true</UseWindowsForms>` con
`System.Windows.Forms.NotifyIcon`, pero eso mete WinForms entero en un single-file self-contained y deja el
menú contextual con estilo nativo WinForms, chocando con Material Design. `H.NotifyIcon.Wpf` da un
`TaskbarIcon` con `ContextMenu` de WPF que hereda los estilos existentes.

## 4. Restricciones técnicas críticas

Tres detalles que rompen la implementación si se ignoran.

### 4.1 `Environment.ProcessPath`, nunca `Assembly.Location`

Con `PublishSingleFile=true` (ya activo en `8bitdofixer.csproj`), `Assembly.Location` devuelve **string
vacío** en runtime. La clave del registro necesita la ruta real del `.exe`: se usa `Environment.ProcessPath`,
entre comillas (rutas con espacios), seguido de `--minimized`.

### 4.2 El HWND debe sobrevivir a la ventana oculta

`joystick.SetCooperativeLevel(hwnd, Exclusive | Background)` (`BluetoothRemapper.cs:48`) requiere un handle
válido. Si la ventana nunca se muestra, `WindowInteropHelper.Handle` es `IntPtr.Zero` y el acquire falla.
Por lo tanto:

- `App` llama a `EnsureHandle()` sobre `MainWindow` antes de cualquier `Start()`.
- La X hace `Hide()`, **nunca** `Close()`. Si la ventana se cerrara, el HWND se destruye y el acquire de
  todos los workers se cae.
- `ShutdownMode` pasa a `OnExplicitShutdown`; el único camino de salida es "Salir" en la bandeja.
- `MainWindow.OnClosed` deja de llamar a `Application.Current.Shutdown()`.

### 4.3 Recursos COM compartidos y de vida larga

- **Un solo `ViGEmClient`**, propiedad del servicio, del que se sacan N pads con
  `CreateXbox360Controller(0x045E, 0x028E)`. Hoy el cliente se crea dentro del método por-dispositivo
  (`BluetoothRemapper.cs:94`); con N mandos serían N clientes.
- **Un solo `DirectInput`**, propiedad del supervisor. Hoy es `using var` dentro del método
  (`BluetoothRemapper.cs:32`). Debe disponerse **después** de que todos los workers pararon, porque los
  objetos `Joystick` lo referencian.

## 5. Filtro de dispositivos

El filtro es una **lista de permitidos**, con un rechazo explícito como red de seguridad.

**Acepta** si `VID == 0x2DC8` (8BitDo) o el `InstanceName` contiene "8BitDo" o "Ultimate".

**Rechaza** el par exacto `(VID, PID) == (0x045E, 0x028E)`, incluso si pasara el allow-list. Ese es el par
que la app asigna a sus **propios pads virtuales** (`CreateXbox360Controller(0x045E, 0x028E)`), que también
se enumeran en DirectInput. Sin ese rechazo: pad virtual → se enumera → se crea otro pad virtual → loop
infinito.

Se rechaza el par exacto y no el VID `0x045E` completo por precisión: un dispositivo que reporta exactamente
`045E/028E` es por definición un pad XInput y no necesita esta app. Los mandos Xbox reales (otros PID bajo el
mismo VID) quedan afuera igual, porque no pasan el allow-list.

En DirectInput, VID y PID se extraen del `ProductGuid` del `DeviceInstance`.

**Las constantes no se hardcodean por deducción.** En modo Bluetooth el Ultimate 2C puede reportar VID/PID
distintos que por USB. La Fase 1a es un paso de medición explícito: un build instrumentado loguea
`ProductGuid`, VID, PID e `InstanceName` de los mandos reales (uno, y después dos simultáneos), y con esa
salida se fijan las constantes del filtro.

## 6. Flujo del supervisor

Cada 2 s: enumera DirectInput → aplica `DeviceFilter` → diffea contra los workers activos por `InstanceGuid`.

- **GUID nuevo:** crea `ControllerEntry` en `Connecting`, levanta un worker con un CTS enlazado al del servicio.
- **Worker terminado** (dispositivo perdido o excepción): entry a `Lost`; el **pad virtual se mantiene
  conectado 15 s** antes de soltarse (constante `VirtualPadGraceSeconds = 15`, no expuesta en la UI). Esa
  ventana de gracia preserva el número de jugador: ViGEm asigna
  slots XInput por orden de creación, así que sin gracia un mando que se cae y vuelve puede pasar a ser
  player 2 a mitad de sesión.
- **Worker que explota:** el supervisor lo captura y hace respawn con backoff 2 s → cap 30 s. Un mando que
  falla no puede matar a los otros ni al servicio. Hoy una excepción termina el único remapper y no hay
  recuperación.
- **Nunca encuentra nada:** no es error, es el estado de reposo normal. Reintenta indefinidamente y loguea
  el **cambio** de estado, no cada intento — con autoarranque, loguear cada intento produce un log de horas.

Enumerar por polling cada 2 s es deliberado: DirectInput no ofrece notificación de arribo de forma cómoda, y
una enumeración COM cada 2 s tiene costo despreciable.

## 7. Datos hacia la UI

`ControllerService` expone `ObservableCollection<ControllerEntry>` que alimenta directo un `ItemsControl`,
más un estado agregado para el tooltip y el ícono de bandeja.

El estado agregado (`ServiceState`) es uno de: `Stopped`, `Searching` (corriendo, cero mandos), `Mapped`
(uno o más) y `DriverMissing`. `ControllerState` describe un mando individual, y una `ControllerEntry` solo
existe cuando ya se detectó un dispositivo: por eso no hay estado `Searching` a nivel de mando.

La tarjeta "CONNECTION STATUS" actual (una sola, con un `TextBlock` de 28 pt) pasa a ser una lista de filas:
nombre + estado + player index + badge de rumble. Estado vacío: "Buscando mandos 8BitDo…".

El `TxtLogs` recibe un límite circular de ~500 líneas. Con la app corriendo siempre, hoy crecería sin techo.

### 7.1 Batería: lista independiente, sin join fabricado

`BatteryService` sigue recorriendo los dispositivos BLE con servicio de batería y nombre 8BitDo, pero keyea
por `devInfo.Id` en vez de `service.Device.Name`: dos Ultimate 2C idénticos comparten nombre y hoy el
segundo sobreescribe al primero en la misma tarjeta (`BluetoothBatteryMonitor.cs:81-82`).

Lo que **no** hace: afirmar qué batería corresponde a qué mando mapeado. No existe join confiable entre un
`InstanceGuid` de DirectInput y un device id de BLE. La tarjeta de batería es su propia lista, independiente
de la lista de mandos. Con un mando se ve igual que hoy; con dos se ven dos barras que no afirman una
correspondencia que no se puede garantizar.

Los errores de batería siguen siendo best-effort con catch por dispositivo, pero se loguean una vez por
dispositivo en vez de en cada poll: con poll cada 300 s y la app 24/7, hoy serían 288 líneas por día por mando.

## 8. Secuencia de arranque

```
Program.Main
  ├─ parsea args (--minimized)
  ├─ SingleInstanceGuard.TryAcquire()
  │    └─ ya corre otra → señala "mostrá la ventana" y sale con código 0
  └─ App.Run()
       ├─ CrashLogger.Install()             → %APPDATA%, sin MessageBox
       ├─ SettingsStore.Load()              → corrupto o ausente = defaults
       ├─ MainWindow ctor + EnsureHandle()  → HWND estable, sin mostrar
       ├─ TrayIconHost.Create()
       ├─ reconcilia StartWithWindows ↔ registro (autocura si movieron el .exe)
       ├─ if (!--minimized) window.Show()
       └─ if (settings.AutoStartServices) ControllerService.Start()
```

El `--minimized` viene en el valor de la clave `Run`. Desde el logon: sin ventana, directo a bandeja, con
servicios arrancando. Doble click en el `.exe`: ventana normal. Un solo mecanismo cubre ambos casos, y por eso
**no** hace falta un tercer switch de "iniciar minimizado".

## 9. Bandeja

Tooltip dinámico según el estado agregado: "8BitDo Fixer — 2 mandos mapeados" / "— buscando mandos" /
"— detenido" / "— falta ViGEmBus".

Menú contextual: **Abrir** · ─ · **Iniciar servicios** (deshabilitado si ya corre) · **Detener servicios** ·
─ · **Iniciar con Windows** (checkable, espejo del switch de la UI) · ─ · **Salir**. Doble click muestra y
enfoca la ventana.

Un solo ícono (`Assets/logo.ico`); el estado vive en el tooltip. Variar el ícono por estado pediría 2-3
`.ico` extra: fuera de alcance.

**Cortesía de la primera X:** la primera vez que se oculta la ventana con la X, un balloon avisa "sigo
corriendo en la bandeja", una sola vez, recordado en `HasShownTrayHint`. Sin ese aviso el usuario cree que
cerró la app, la vuelve a abrir, y la guardia de instancia única le devuelve la misma ventana sin explicación.

## 10. Settings

Los dos switches de la UI son **«Iniciar con Windows»** (`StartWithWindows`, ver §11) y **«Iniciar servicios
automáticamente»** (`AutoStartServices`).

`%APPDATA%\8BitDoFixer\settings.json`. `AppSettings` como `record` con defaults:

| Campo | Default |
|---|---|
| `AutoStartServices` | `false` |
| `StartWithWindows` | `false` |
| `HasShownTrayHint` | `false` |
| `IsEnglish` | detectado de la cultura del sistema en el primer arranque |

Sobre `IsEnglish`: el README promete "automatic language detection" pero `Localization.cs:11` hardcodea
`_isEnglish = true` — no hay detección. Como la app ahora se reinicia en cada logon, persistir el idioma pasa
de detalle cosmético a molestia real, así que se persiste y se detecta en el primer run.

Escritura **atómica**: `.tmp` + `File.Move`, para no dejar un JSON truncado si el proceso muere mientras
guarda. `System.Text.Json` con contexto source-generated: es barato y deja la puerta abierta a
`PublishTrimmed` sin que se rompa.

## 11. Autoarranque

Clave `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `8BitDoFixer`, contenido
`"<Environment.ProcessPath>" --minimized`. Sin admin, per-user, y visible en Administrador de tareas → Inicio,
lo que es una ventaja: el usuario puede desactivarlo desde ahí.

**El estado del registro no alcanza para saber si está activo.** Cuando el usuario lo desactiva desde el
Administrador de tareas, Windows escribe en `StartupApproved\Run` y **deja la clave `Run` intacta**. Si el
checkbox solo leyera `Run`, mostraría "activado" mientras Windows lo bloquea. Por eso `IsEnabled` lee ambas
claves, y si está bloqueado el switch se muestra activado con la nota "Windows lo tiene desactivado". Es un
`GetValue` extra a cambio de que la UI no mienta.

**Autocura:** es un `.exe` single-file que el usuario puede mover de carpeta. En cada arranque, si el setting
está activo y el valor del registro no coincide con `Environment.ProcessPath`, se reescribe.

## 12. Instancia única

`Mutex` nombrado `Local\8BitDoFixer.SingleInstance` — prefijo `Local\`, no `Global\`, para que en Fast User
Switching cada usuario tenga su propia instancia. Más un `EventWaitHandle` `Local\8BitDoFixer.ShowWindow`:
la segunda instancia lo señala y sale con 0; la primera tiene un hilo esperándolo y muestra la ventana.

Esto no era necesario antes y ahora sí: con autoarranque, el `.exe` **ya está corriendo** cuando el usuario
hace doble click en él. Sin guardia habría dos procesos peleando por el acquire `Exclusive` de DirectInput y
dos pads virtuales por cada mando físico.

## 13. Manejo de errores

| Caso | Comportamiento actual | Diseño |
|---|---|---|
| ViGEmBus ausente | `VigemBusNotFoundException` cae en el catch genérico → "Disconnected" | Estado propio `DriverMissing`, balloon con el link de descarga y botón en la UI. El reintento **no se corta**: pasa de 2 s a un backoff lento de 60 s, para que instalar el driver recupere la app sin reiniciarla, y sin spamear el log mientras falta. Sin este caso aparte, el reintento infinito convierte un problema con solución clara en un misterio eterno |
| Crash logging | Ruta relativa duplicada en `Program.cs:24` y `App.xaml.cs:28`, más `MessageBox` | `CrashLogger` único a `%APPDATA%`, **sin MessageBox**. Lanzado desde la clave `Run` el CWD es `C:\Windows\System32`: el handler tira `UnauthorizedAccessException` y se come el crash real. Y un modal invisible en un arranque de logon cuelga la app. Balloon + log |
| `settings.json` corrupto | n/a | Defaults, el archivo se renombra a `.bad`, se loguea. Nunca crashea por un JSON roto |
| Escritura de registro denegada | n/a | El toggle se revierte y muestra el error; no persiste un estado que miente |
| Worker que explota | Mata el único remapper, sin recuperación | Capturado por el supervisor, entry a `Lost`, respawn con backoff 2 s → 30 s |
| Errores de batería | Catch silencioso por dispositivo, en cada poll | Best-effort, logueado una vez por dispositivo |

## 14. Rendimiento

Cada worker corre un `PeriodicTimer` de 5 ms (200 Hz). Dos cosas se **miden**, no se suponen: la resolución
de timer por defecto de Windows es ~15,6 ms, así que un `PeriodicTimer` de 5 ms probablemente no entregue
200 Hz reales; y con N mandos son N timers. El intervalo se mantiene en 5 ms y **medir CPU con dos mandos es
un ítem explícito del checkpoint de la Fase 1c**.

## 15. Testing

El proyecto no tiene tests ni `.sln`. Se agregan `8bitdofixer.sln` (app + tests) y `tests/8bitdofixer.Tests`
(xunit, `net10.0-windows`).

**Restricción de entorno:** el desarrollo ocurre en macOS, donde este proyecto **no compila** —
`net10.0-windows`, WPF, WinRT (`Windows.Devices.Bluetooth`) y ViGEm son Windows-only, y no hay SDK de .NET
instalado. Todo `dotnet build`, `dotnet test` y verificación manual los ejecuta el usuario en Windows. El
plan se estructura alrededor de checkpoints que requieren esa ejecución.

**Testeable sin hardware** (TDD aplica; es la razón de extraer `Xbox360Mapping` y `DeviceFilter`):

- `Xbox360Mapping`: `NormalizeAxis` (0 / 32767 / 65535 / bordes), `ApplyDeadzone`, `NegateAxis` (caso
  `short.MinValue`), POV → dpad en las 8 direcciones más el centrado (`-1`).
- `DeviceFilter`: acepta VID 8BitDo; **rechaza `0x045E`** (este test es el que impide que el loop de pads
  virtuales vuelva); rechaza desconocidos; acepta por nombre.
- `SettingsStore`: roundtrip; archivo ausente → defaults; JSON corrupto → defaults + `.bad`; escritura atómica.
- `StartupRegistration.BuildRunCommand`: ruta con espacios queda entre comillas; incluye `--minimized`.

**No testeable unitariamente** (checklist manual por fase, ejecutado en Windows): acquire de DirectInput, pad
de ViGEm, batería BLE, comportamiento de bandeja, arranque en logon, reordenamiento de player index.

## 16. Fases de entrega

| Fase | Alcance | Checkpoint (en Windows) |
|---|---|---|
| **0** | Fork; `git remote rename origin upstream`; fork del usuario como `origin`; rama `feature/tray-autostart-multipad`; `.sln`; proyecto de tests vacío | `dotnet build` y `dotnet test` verdes |
| **1a** | Build instrumentado que loguea `ProductGuid`/VID/PID/`InstanceName` — un mando, después dos | El usuario pega la salida; se fijan las constantes del filtro. La instrumentación es temporal: se absorbe en `DeviceFilter` en la Fase 1b |
| **1b** | Extraer `Xbox360Mapping` + `DeviceFilter`, tests primero | `dotnet test` |
| **1c** | `ControllerService` + supervisor + workers; reintento infinito; gracia de 15 s; UI a listas | Un mando: conecta / desconecta / reconecta. Dos simultáneos. Player index estable dentro de la gracia. CPU con dos mandos |
| **2a** | `AppPaths` + `CrashLogger` + `SettingsStore`, con tests | `dotnet test` + inspección de `%APPDATA%` |
| **2b** | Bandeja + instancia única + X que oculta | Doble click con la app corriendo devuelve la ventana, no una segunda instancia |
| **2c** | `StartupRegistration` + los dos switches + reconciliación | Reiniciar Windows: arranque silencioso con servicios activos |
| **3** | README (crédito upstream + fork), versión 0.2.0, `FooterText` | — |

Cada fase deja algo funcionando y verificable. Son 8 checkpoints que dependen de ejecución en Windows,
incluido un reinicio: el trabajo abarca varias sesiones.

## 17. Fork y licencia

`origin` apunta a `git@github.com:bezelye404/Ultimate2CbluetoothFix.git`, el repositorio **upstream**. Los 8
commits del historial son todos de `bezelye404 <k7arslan@proton.me>`; el usuario no tiene ninguno y su `main`
local es idéntico a `origin/main`.

Por eso el fork es la Fase 0 y no un trámite final: si se commitea con `origin` apuntando a upstream,
cualquier push va al repositorio de otra persona.

El proyecto es MIT, así que forkear y modificar es válido. El crédito se mantiene: el README ya tiene sección
Credits y se le agrega "fork de bezelye404/Ultimate2CbluetoothFix". `Localization.FooterText` hardcodea
`"v0.1.0 • github.com/bezelye404"` (`Localization.cs:31`); en el fork apunta al repo del usuario, con el
crédito upstream preservado en el README, no borrado.

## 18. Riesgos y contingencias

- **El filtro depende de una medición.** Si en modo Bluetooth el Ultimate 2C no reporta `VID 0x2DC8` ni un
  `InstanceName` reconocible, el allow-list por VID/nombre no alcanza y hay que fijarlo por el PID medido.
  Es exactamente el motivo de que la Fase 1a sea un paso separado con checkpoint **antes** de escribir el
  filtro, y no una deducción.
- **La ventana de gracia tiene un costo.** Mientras el pad virtual sigue conectado y el físico está ausente,
  un juego ve un mando conectado que no responde. Es preferible al reordenamiento de player index a mitad de
  sesión, pero es un trade-off consciente; si molesta, se baja la constante.
- **El ícono de bandeja en single-file.** Con `PublishSingleFile=true` hay que verificar que
  `H.NotifyIcon.Wpf` cargue el ícono desde recurso embebido y no desde disco. Es un ítem del checkpoint de
  la Fase 2b, no un supuesto.
- **La frecuencia real de polling** con N mandos: ver §14, se mide en la Fase 1c.

## 19. Fuera de alcance

- **El "cliff" del deadzone.** `ApplyDeadzone` devuelve 0 o el valor crudo sin reescalar, así que hay un
  salto de 0 a 4000 en el borde. Es un problema real de calidad de input, pero no lo toca este trabajo.
- **`WM_DEVICECHANGE`** en el HWND oculto para detección instantánea en lugar de hasta 2 s de lag.
- **Íconos de bandeja por estado.**
- **Triggers digitales**, ya documentados como limitación de hardware del Ultimate 2C en Bluetooth.
- **Unir batería BLE con mando DirectInput**: técnicamente no hay join confiable (ver §7.1).
