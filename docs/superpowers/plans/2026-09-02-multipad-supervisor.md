# Supervisor multi-mando con reconexión — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar el remapper de un solo dispositivo por un supervisor que mapea varios mandos 8BitDo a la vez y se reconecta indefinidamente por su cuenta.

**Architecture:** Un `ControllerService` singleton es dueño del ciclo de vida del servicio, independiente de la ventana. Adentro, un `ControllerSupervisor` enumera DirectInput cada 2 s, diffea contra los workers activos por `InstanceGuid`, y levanta un `ControllerWorker` por mando; cada worker corre su propio loop de polling contra su propio pad virtual, sacado de un único `ViGEmClient` compartido. Los pads virtuales sobreviven 15 s a la pérdida del mando físico para preservar el número de jugador. La UI consume una `ObservableCollection` en vez de campos escalares.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`), WPF, MaterialDesignThemes 5.3.0, Nefarius.ViGEm.Client 1.x, SharpDX.DirectInput 4.2.0, xunit (nuevo).

**Spec:** `docs/superpowers/specs/2026-09-02-tray-autostart-multipad-design.md`

**Plan siguiente:** `docs/superpowers/plans/2026-09-02-tray-autostart.md` (Fases 2 y 3 del spec: bandeja, settings, autoarranque). Este plan es un punto de parada válido: al terminarlo la app funciona, con multi-mando y reconexión, y sigue arrancando a mano.

## Global Constraints

- **El entorno de desarrollo es macOS y este proyecto NO compila ahí.** `net10.0-windows`, WPF, WinRT (`Windows.Devices.Bluetooth`) y ViGEm son Windows-only, y no hay SDK de .NET instalado en la máquina de desarrollo. Todo paso marcado **[WINDOWS]** lo ejecuta el usuario en su máquina Windows y pega la salida. Ningún paso de este plan debe asumir que `dotnet` corre localmente.
- Target framework: `net10.0-windows10.0.19041.0` en app y tests.
- `RootNamespace` es `BitDoFixer`. Los archivos nuevos en subcarpetas usan sub-namespace (`BitDoFixer.Services`, `BitDoFixer.Models`).
- `<Nullable>enable</Nullable>` y `<ImplicitUsings>enable</ImplicitUsings>` en ambos proyectos.
- Todo string visible al usuario pasa por `Localization.cs`, con variante inglés y turco. Las traducciones al turco quedan marcadas para revisión del usuario.
- Intervalo de enumeración: 2 s. `VirtualPadGraceSeconds = 15`. Backoff de worker: 2 s → cap 30 s. Cap del log de la UI: 500 líneas.
- Identidad del pad virtual: `CreateXbox360Controller(0x045E, 0x028E)`.
- Filtro de dispositivos: allow-list por `VID == 0x2DC8` o `InstanceName` que contenga "8BitDo"/"Ultimate"; rechazo del par exacto `(0x045E, 0x028E)`.
- Publicar con `dotnet publish -c Release -r win-x64` (single-file self-contained). Un build normal **no** es self-contained, a propósito — ver Tarea 2.
- Commits frecuentes, uno por tarea como mínimo. Todo en la rama `feature/tray-autostart-multipad`.

---

---

## Enmienda 2026-09-02: tres proyectos y verificación local

Al instalar el SDK de .NET 10 en la máquina de desarrollo (macOS) se descubrió que
**sí se puede compilar y testear localmente**, con dos cambios. Esta enmienda pisa lo
que dicen las Global Constraints y las tareas sobre `[WINDOWS]`.

**1. `EnableWindowsTargeting=true`** en `8bitdofixer.csproj` permite restaurar y
compilar un proyecto que apunta a Windows desde macOS o Linux (no-op en Windows). Sin
esto el build falla con NETSDK1100.

**2. Un tercer proyecto, `core/8bitdofixer.Core/` (TFM `net10.0`, sin sufijo de
plataforma).** Un testhost de `net10.0-windows` exige el runtime
`Microsoft.WindowsDesktop.App`, que no existe fuera de Windows, así que los tests eran
inejecutables sin una máquina Windows. Moviendo el código puro a un assembly neutral,
el proyecto de tests apunta a `net10.0` y corre en cualquier OS.

### Qué vive en cada proyecto

| Proyecto | TFM | Contenido |
|---|---|---|
| `core/8bitdofixer.Core` | `net10.0` | `Localization`, `Models/*`, `Xbox360Mapping`, y **todo lo que se pueda testear**: `DeviceDescriptor`, `DeviceFilter` (Tarea 5) |
| `8bitdofixer` (raíz) | `net10.0-windows10.0.19041.0` | WPF, WinRT y COM: `ControllerWorker`, `ControllerSupervisor`, `ControllerService`, `BatteryService`, `BluetoothRemapper`, ventanas |
| `tests/8bitdofixer.Tests` | `net10.0` | Referencia **solo** a Core. Corre en macOS, Linux y Windows |

Los namespaces no cambian (`BitDoFixer`, `BitDoFixer.Models`, `BitDoFixer.Services`):
un namespace puede abarcar varios assemblies, así que no hubo churn de `using`.

### Cambios concretos a las tareas

- **Tarea 2:** el csproj de la app vive en la raíz del repo, así que su glob por defecto
  `**/*.cs` estaba compilando los fuentes de los tests dentro de la app (y pasándolos al
  markup compiler de WPF). Hace falta `<Compile Remove="tests/**;core/**" />` y lo mismo
  para `None`, `Page` y `ApplicationDefinition`. **Este bug se habría manifestado igual
  en Windows.** El `InternalsVisibleTo` se muda al csproj de Core.
- **Tarea 4:** `Xbox360Mapping` y `DpadState` son `public`, no `internal`: ahora viven en
  otro assembly que su consumidor.
- **Tarea 5:** `DeviceDescriptor` y `DeviceFilter` van en **Core** y son `public`. Sus
  tests corren localmente; ya no dependen de Windows.
- **Tarea 6:** los modelos y `Localization` van en Core. Como `Localization` cambió de
  assembly, `MainWindow.xaml` necesita
  `xmlns:local="clr-namespace:BitDoFixer;assembly=8bitdofixer.Core"`.
- **Tarea 9:** `ControllerService` se queda en la app (usa el `Dispatcher` de WPF), pero
  **`ComputeState` se mueve a Core** como tipo propio y puro, para que
  `ControllerServiceStateTests` corra localmente.

### Qué sigue necesitando Windows

Solo lo que toca hardware o el sistema:

- **Tarea 3** (medición de VID/PID) — necesita los mandos.
- **Tarea 11 Step 4** (checklist manual) — necesita mandos y ViGEmBus.
- Ejecutar la app: ViGEm es un driver de kernel y DirectInput es COM.

Los pasos de `dotnet build` / `dotnet test` **ya no requieren Windows**. Donde una tarea
diga `[WINDOWS]` para build o test, se corre localmente.

### Setup local

`brew install dotnet` (10.0.400 en la máquina de desarrollo). No hace falta exportar
`DOTNET_ROOT`. Verificado el 2026-09-02: 3 proyectos compilan sin warnings, 44 tests en
verde, y `dotnet publish -c Release -r win-x64` produce un `.exe` PE32+ GUI x86-64
single-file de 169 MB desde macOS.


## Estructura de archivos

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `8bitdofixer.sln` | Solución: app + tests | 2 |
| `tests/8bitdofixer.Tests/8bitdofixer.Tests.csproj` | Proyecto de tests xunit | 2 |
| `8bitdofixer.csproj` | Props de publish condicionales + `InternalsVisibleTo` | 2 |
| `Services/Xbox360Mapping.cs` | Funciones puras DirectInput → Xbox360. Sin estado, sin COM | 4 |
| `Services/DeviceDescriptor.cs` | Snapshot testeable de un dispositivo DirectInput | 5 |
| `Services/DeviceFilter.cs` | Decide qué dispositivos reclamar | 5 |
| `Models/ControllerState.cs` | `ControllerState` + `ServiceState` | 6 |
| `Models/ControllerEntry.cs` | Un mando observable por la UI | 6 |
| `Models/BatteryEntry.cs` | Un dispositivo BLE con batería | 6 |
| `Services/IControllerSink.cs` | Cómo el supervisor le habla al servicio | 7 |
| `Services/ControllerWorker.cs` | Un mando: acquire, FFB, poll loop | 7 |
| `Services/ControllerSupervisor.cs` | Enumeración, diff, gracia, backoff | 8 |
| `Services/ControllerService.cs` | Singleton, `Start`/`Stop`, colecciones, marshalling al Dispatcher | 9 |
| `Services/BatteryService.cs` | Reemplaza `BluetoothBatteryMonitor`, keyeado por device id BLE | 10 |
| `MainWindow.xaml` / `.cs` | Listas en vez de campos escalares, cap de log | 11 |
| `Localization.cs` | Strings nuevos | 6, 11 |
| ~~`BluetoothRemapper.cs`~~ | Se elimina; su contenido se reparte entre 4, 7 y 8 | 7 |
| ~~`BluetoothBatteryMonitor.cs`~~ | Se elimina; reemplazado por `BatteryService` | 10 |

### Desvíos del spec, deliberados

1. **`DriverMissing` sale de `ControllerState`.** El spec §7 lo listaba en los dos enums. El `ViGEmClient` se crea una sola vez a nivel de servicio, así que "falta el driver" es una condición global, no de un mando: queda solo en `ServiceState`. Un `ControllerEntry` nunca puede estar en `DriverMissing` porque sin cliente no se crea ninguna entry.
2. **`PlayerIndex` se asigna con un free-list propio, no leyendo `IXbox360Controller.UserIndex`.** No pude verificar offline que esa propiedad exista en Nefarius.ViGEm.Client 1.x. Un contador de slots propio es determinista, no depende de la API y funciona igual con la ventana de gracia.

---

## Task 1: Fork y remotes

**Files:** ninguno (solo configuración de git)

**Interfaces:**
- Consumes: nada
- Produces: `origin` apuntando al fork del usuario; `upstream` apuntando a `bezelye404/Ultimate2CbluetoothFix`; rama `feature/tray-autostart-multipad` con upstream de tracking en `origin`

**Por qué es la tarea 1:** `origin` hoy apunta a `git@github.com:bezelye404/Ultimate2CbluetoothFix.git`, el repositorio de otra persona. Los 8 commits del historial son todos de `bezelye404 <k7arslan@proton.me>`. Si se commitea y pushea antes de arreglar los remotes, el push va al repo de un tercero.

- [x] **Step 1: [WINDOWS o macOS] Crear el fork**

Requiere el usuario de GitHub del usuario. Con `gh` CLI:

```bash
gh repo fork bezelye404/Ultimate2CbluetoothFix --remote=false --clone=false
```

Alternativa manual: botón **Fork** en `https://github.com/bezelye404/Ultimate2CbluetoothFix`.

- [x] **Step 2: Reapuntar los remotes**

Reemplazar `<USUARIO>` por el usuario de GitHub real:

```bash
git remote rename origin upstream
git remote add origin git@github.com:<USUARIO>/Ultimate2CbluetoothFix.git
git remote -v
```

Salida esperada: `origin` con el fork, `upstream` con `bezelye404`.

- [x] **Step 3: Verificar que nada quedó apuntando a upstream para escritura**

```bash
git config --get branch.main.remote
```

Si devuelve `upstream`, dejarlo así a propósito: `main` sigue a upstream para poder traer cambios del autor original.

- [x] **Step 4: Publicar la rama de trabajo en el fork**

```bash
git push -u origin feature/tray-autostart-multipad
```

Expected: la rama queda creada en el fork, con el commit del spec (`207abc5`).

**HECHO 2026-09-02:** fork en `alexortizi/Ultimate2CbluetoothFix` (owner type `User`, parent
`bezelye404/Ultimate2CbluetoothFix`). `origin` = fork, `upstream` = bezelye404,
`branch.main.remote` = `upstream` a propósito. Push verificado contra `origin`.

- [x] **Step 5: Verificar el destino del push**

```bash
git rev-parse --abbrev-ref feature/tray-autostart-multipad@{upstream}
```

Expected: `origin/feature/tray-autostart-multipad`. **Si dice `upstream/...`, parar y corregir antes de seguir.**

---

## Task 2: Solución, proyecto de tests y props de publish

**Files:**
- Create: `8bitdofixer.sln`
- Create: `tests/8bitdofixer.Tests/8bitdofixer.Tests.csproj`
- Create: `tests/8bitdofixer.Tests/SolutionSanityTests.cs`
- Modify: `8bitdofixer.csproj` (mover props de publish a un PropertyGroup condicional; agregar `InternalsVisibleTo`)

**Interfaces:**
- Consumes: nada
- Produces: `dotnet test` funcionando; el assembly de tests `8bitdofixer.Tests` puede ver los tipos `internal` de la app

**El problema que resuelve, y por qué no es opcional:** el csproj de la app tiene hoy `<SelfContained>true</SelfContained>` y `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` en el PropertyGroup principal. Un proyecto de tests que referencie un proyecto self-contained falla con **NETSDK1150**. La corrección es que esas propiedades apliquen solo al publicar.

**HECHO 2026-09-02 (escrito, SIN verificar: falta correr los Steps 5-7 en Windows).**

- [x] **Step 1: Mover las props de publish a un grupo condicional**

En `8bitdofixer.csproj`, borrar estas cuatro líneas del `<PropertyGroup>` principal:

```xml
    <!-- Publish Configuration -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

y agregar, después del cierre de ese `</PropertyGroup>`:

```xml
  <!-- Solo al publicar: un build normal no es self-contained, para que el proyecto
       de tests pueda referenciar este proyecto (NETSDK1150).
       Publicar con: dotnet publish -c Release -r win-x64 -->
  <PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>8bitdofixer.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

- [x] **Step 2: Crear el csproj de tests**

`tests/8bitdofixer.Tests/8bitdofixer.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>BitDoFixer.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\8bitdofixer.csproj" />
  </ItemGroup>
</Project>
```

**Si el restore falla por versiones de paquete** (no pude verificarlas offline contra el SDK de .NET 10): correr `dotnet new xunit -o /tmp/probe` en Windows y copiar de ahí las versiones que el SDK genere.

- [x] **Step 3: Crear el archivo de solución**

`8bitdofixer.sln`:

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "8bitdofixer", "8bitdofixer.csproj", "{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E01}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "8bitdofixer.Tests", "tests\8bitdofixer.Tests\8bitdofixer.Tests.csproj", "{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E02}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E01}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E01}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E01}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E01}.Release|Any CPU.Build.0 = Release|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E02}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E02}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E02}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{6F1A0C21-4E7B-4D3A-9E51-0A1B2C3D4E02}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
```

Los tabs dentro de `GlobalSection` son obligatorios en el formato `.sln`.

- [x] **Step 4: Escribir un test de sanidad que falle por la razón correcta**

`tests/8bitdofixer.Tests/SolutionSanityTests.cs`:

```csharp
using Xunit;

namespace BitDoFixer.Tests;

public class SolutionSanityTests
{
    [Fact]
    public void TestProjectCanSeeInternalTypesOfTheApp()
    {
        // Localization es un tipo public de la app; si esto compila y corre,
        // el ProjectReference y el TargetFramework están bien.
        Assert.NotNull(Localization.Instance);
    }

    [Fact]
    public void LocalizationDefaultsToEnglish()
    {
        Assert.True(Localization.Instance.IsEnglish);
    }
}
```

- [ ] **Step 5: [WINDOWS] Build y test**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
```

Expected: build sin errores (en particular **sin NETSDK1150**) y 2 tests en verde.

Si aparece NETSDK1150 igual, el fallback es no usar `ProjectReference` y linkear los archivos fuente en el csproj de tests:

```xml
  <ItemGroup>
    <Compile Include="..\..\Services\*.cs" LinkBase="Linked" />
    <Compile Include="..\..\Localization.cs" LinkBase="Linked" />
  </ItemGroup>
```

- [ ] **Step 6: [WINDOWS] Verificar que el publish single-file sigue funcionando**

```bash
dotnet publish 8bitdofixer.csproj -c Release -r win-x64
```

Expected: un único `8bitdofixer.exe` en `bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`. Este paso es el que prueba que mover las props a un grupo condicional no rompió el empaquetado.

- [ ] **Step 7: Commit**

```bash
git add 8bitdofixer.sln 8bitdofixer.csproj tests/
git commit -m "build: add solution and xunit test project

Move PublishSingleFile/SelfContained/IncludeNativeLibraries to a
RuntimeIdentifier-conditional group so a normal build is not
self-contained: a test project cannot reference a self-contained
project (NETSDK1150). Publishing still produces a single file via
dotnet publish -r win-x64.

Grant InternalsVisibleTo to the test assembly so internal types stay
internal instead of being widened to public for testability."
```

---

## Task 3: Medición del hardware real

**Files:**
- Modify: `BluetoothRemapper.cs:34-44` (logging temporal, se revierte en la Tarea 7)

**Interfaces:**
- Consumes: nada
- Produces: los valores reales de `ProductGuid`, VID, PID e `InstanceName` de los mandos del usuario, que fijan las constantes de `DeviceFilter` en la Tarea 5

**Por qué existe esta tarea:** en modo Bluetooth el Ultimate 2C puede reportar VID/PID distintos que por USB. El spec §5 define el filtro por `VID == 0x2DC8`, pero eso es una hipótesis, no un dato. Escribir el filtro antes de medir es adivinar.

- [ ] **Step 1: Agregar el logging de enumeración**

En `BluetoothRemapper.cs`, reemplazar el bloque de enumeración actual (líneas 34-44):

```csharp
            var devices = directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly);
            if (devices.Count == 0) devices = directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly);

            if (devices.Count == 0)
            {
                Log(loc.LogMapperNotFound);
                statusCallback?.Invoke(RemapperStatus.NotFound);
                return;
            }

            var chosen = devices[0];
```

por:

```csharp
            // TEMPORAL (Tarea 3 del plan multipad): instrumentación para fijar
            // las constantes de DeviceFilter contra hardware real. Se revierte
            // en la Tarea 7.
            foreach (var t in new[] { DeviceType.Gamepad, DeviceType.Joystick })
            {
                foreach (var d in directInput.GetDevices(t, DeviceEnumerationFlags.AttachedOnly))
                {
                    var bytes = d.ProductGuid.ToByteArray();
                    uint data1 = BitConverter.ToUInt32(bytes, 0);
                    Log($"[PROBE] type={t} name='{d.InstanceName}' product='{d.ProductName}' " +
                        $"productGuid={d.ProductGuid} instanceGuid={d.InstanceGuid} " +
                        $"vid=0x{(ushort)(data1 & 0xFFFF):X4} pid=0x{(ushort)(data1 >> 16):X4}");
                }
            }

            var devices = directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly);
            if (devices.Count == 0) devices = directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly);

            if (devices.Count == 0)
            {
                Log(loc.LogMapperNotFound);
                statusCallback?.Invoke(RemapperStatus.NotFound);
                return;
            }

            var chosen = devices[0];
```

- [ ] **Step 2: [WINDOWS] Build y correr con UN mando**

```bash
dotnet build 8bitdofixer.sln
dotnet run --project 8bitdofixer.csproj
```

Emparejar un Ultimate 2C en modo Bluetooth, pulsar **START SERVICE**, y copiar del panel de logs todas las líneas `[PROBE]`.

- [ ] **Step 3: [WINDOWS] Repetir con DOS mandos emparejados a la vez**

Mismo procedimiento con dos Ultimate 2C prendidos. Copiar las líneas `[PROBE]`.

Esto responde tres preguntas que no se pueden deducir: si el mando aparece en la enumeración `Gamepad` o solo en `Joystick`; si dos mandos idénticos tienen `InstanceGuid` distintos (necesario, es la clave del diff del supervisor) y `ProductGuid` iguales; y cuál es el VID/PID real en modo Bluetooth.

- [ ] **Step 4: Checkpoint — fijar las constantes**

El usuario pega la salida `[PROBE]`. Con eso se confirman o corrigen, en la Tarea 5:

- `DeviceFilter.VidEightBitDo` (hipótesis: `0x2DC8`)
- si el `InstanceName` contiene "8BitDo" o "Ultimate", o algo distinto
- el `DeviceType` con el que enumerar en el supervisor

**No avanzar a la Tarea 5 sin esta salida.** Las Tareas 4 y 6 no dependen de ella y pueden hacerse en paralelo.

- [ ] **Step 5: Commit de la instrumentación**

```bash
git add BluetoothRemapper.cs
git commit -m "chore: log DirectInput device identity to pin filter constants

Temporary instrumentation, reverted in the supervisor extraction. The
8BitDo Ultimate 2C may report a different VID/PID over Bluetooth than
over USB, so the device filter constants are measured against real
hardware instead of assumed."
```

---

## Task 4: `Xbox360Mapping` — extraer las funciones puras

**Files:**
- Create: `Services/Xbox360Mapping.cs`
- Create: `tests/8bitdofixer.Tests/Xbox360MappingTests.cs`
- Modify: `BluetoothRemapper.cs` (borrar los helpers privados que se mudan)

**Interfaces:**
- Consumes: nada
- Produces:
  - `BitDoFixer.Services.Xbox360Mapping.Deadzone` → `const int` = 4000
  - `Xbox360Mapping.NormalizeAxis(int v)` → `short`
  - `Xbox360Mapping.ApplyDeadzone(short v)` → `short`
  - `Xbox360Mapping.NegateAxis(short v)` → `short`
  - `Xbox360Mapping.GetBtn(bool[]? buttons, int index)` → `bool`
  - `Xbox360Mapping.PovToDpad(int[]? povs)` → `DpadState`
  - `BitDoFixer.Services.DpadState` → `readonly record struct (bool Up, bool Right, bool Down, bool Left)`

**Este es un refactor de extracción, no un cambio de comportamiento.** Los tests fijan lo que el código hace hoy, incluidas sus rarezas. Si un test parece describir algo raro (los diagonales en los bordes del POV, por ejemplo), es porque el código actual hace eso y lo queremos preservar.

**HECHO 2026-09-02 (escrito). Pendiente: Steps 2, 4 y 6, que se corren en Windows.**

- [x] **Step 1: Escribir los tests que fallan**

`tests/8bitdofixer.Tests/Xbox360MappingTests.cs`:

```csharp
using BitDoFixer.Services;
using Xunit;

namespace BitDoFixer.Tests;

public class Xbox360MappingTests
{
    // --- NormalizeAxis: DirectInput entrega 0..65535 con centro en 32767 ---

    [Theory]
    [InlineData(32767, 0)]          // centro
    [InlineData(0, -32767)]         // extremo bajo
    [InlineData(65534, 32767)]      // extremo alto representable
    [InlineData(65535, 32767)]      // 32768 no cabe en short: se clampea
    [InlineData(40000, 7233)]
    public void NormalizeAxis_CentersAndClamps(int raw, short expected)
    {
        Assert.Equal(expected, Xbox360Mapping.NormalizeAxis(raw));
    }

    // --- ApplyDeadzone: banda muerta abierta de +-4000 ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3999, 0)]
    [InlineData(-3999, 0)]
    [InlineData(4000, 4000)]        // el borde NO está en la banda muerta
    [InlineData(-4000, -4000)]
    [InlineData(30000, 30000)]
    public void ApplyDeadzone_ZeroesOnlyInsideTheBand(short input, short expected)
    {
        Assert.Equal(expected, Xbox360Mapping.ApplyDeadzone(input));
    }

    [Fact]
    public void Deadzone_ConstantIsUnchangedFromTheOriginalRemapper()
    {
        Assert.Equal(4000, Xbox360Mapping.Deadzone);
    }

    // --- NegateAxis: short.MinValue no tiene negativo representable ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, -100)]
    [InlineData(-100, 100)]
    [InlineData(short.MaxValue, -32767)]
    public void NegateAxis_Negates(short input, short expected)
    {
        Assert.Equal(expected, Xbox360Mapping.NegateAxis(input));
    }

    [Fact]
    public void NegateAxis_ClampsMinValueInsteadOfOverflowing()
    {
        Assert.Equal(short.MaxValue, Xbox360Mapping.NegateAxis(short.MinValue));
    }

    // --- GetBtn: tolera null y fuera de rango ---

    [Fact]
    public void GetBtn_ReturnsFalseForNullArray()
    {
        Assert.False(Xbox360Mapping.GetBtn(null, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void GetBtn_ReturnsFalseOutOfRange(int index)
    {
        Assert.False(Xbox360Mapping.GetBtn(new[] { true, true, true }, index));
    }

    [Fact]
    public void GetBtn_ReturnsTheValueInRange()
    {
        var buttons = new[] { false, true, false };
        Assert.False(Xbox360Mapping.GetBtn(buttons, 0));
        Assert.True(Xbox360Mapping.GetBtn(buttons, 1));
        Assert.False(Xbox360Mapping.GetBtn(buttons, 2));
    }

    // --- PovToDpad: el POV viene en centesimas de grado, -1 = centrado ---

    [Fact]
    public void PovToDpad_CenteredWhenNull()
    {
        Assert.Equal(new DpadState(false, false, false, false), Xbox360Mapping.PovToDpad(null));
    }

    [Fact]
    public void PovToDpad_CenteredWhenEmpty()
    {
        Assert.Equal(new DpadState(false, false, false, false), Xbox360Mapping.PovToDpad(Array.Empty<int>()));
    }

    [Fact]
    public void PovToDpad_CenteredWhenNegative()
    {
        Assert.Equal(new DpadState(false, false, false, false), Xbox360Mapping.PovToDpad(new[] { -1 }));
    }

    [Theory]
    // pov,          up,    right, down,  left
    [InlineData(0,     true,  false, false, false)]  // arriba
    [InlineData(9000,  false, true,  false, false)]  // derecha
    [InlineData(18000, false, false, true,  false)]  // abajo
    [InlineData(27000, false, false, false, true)]   // izquierda
    public void PovToDpad_CardinalDirections(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.Equal(new DpadState(up, right, down, left), Xbox360Mapping.PovToDpad(new[] { pov }));
    }

    [Theory]
    // Los bordes de los rangos son inclusivos en los dos lados, así que las
    // diagonales exactas activan las dos direcciones. Es intencional: 4500 son
    // 45 grados, o sea arriba-derecha.
    [InlineData(4500,  true,  true,  false, false)]
    [InlineData(13500, false, true,  true,  false)]
    [InlineData(22500, false, false, true,  true)]
    [InlineData(31500, true,  false, false, true)]
    public void PovToDpad_ExactDiagonalsSetBothDirections(int pov, bool up, bool right, bool down, bool left)
    {
        Assert.Equal(new DpadState(up, right, down, left), Xbox360Mapping.PovToDpad(new[] { pov }));
    }

    [Fact]
    public void PovToDpad_WrapsPastTheTop()
    {
        // 35999 (~360 grados) sigue siendo "arriba"
        Assert.Equal(new DpadState(true, false, false, false), Xbox360Mapping.PovToDpad(new[] { 35999 }));
    }

    [Fact]
    public void PovToDpad_OnlyReadsTheFirstHat()
    {
        Assert.Equal(new DpadState(true, false, false, false), Xbox360Mapping.PovToDpad(new[] { 0, 18000 }));
    }
}
```

- [ ] **Step 2: [WINDOWS] Correr y verificar que falla por no compilar**

```bash
dotnet test 8bitdofixer.sln
```

Expected: FAIL en compilación — `CS0246: The type or namespace name 'Xbox360Mapping' could not be found`. Esa es la falla correcta en este punto.

- [x] **Step 3: Escribir la implementación**

`Services/Xbox360Mapping.cs`:

```csharp
namespace BitDoFixer.Services;

/// <summary>Estado del D-pad derivado del hat switch. Inmutable y comparable.</summary>
internal readonly record struct DpadState(bool Up, bool Right, bool Down, bool Left);

/// <summary>
/// Traducción pura DirectInput -> Xbox360. Sin estado, sin COM, sin hardware:
/// es la única parte del mapeo que se puede testear sin un mando enchufado.
/// Extraído de BluetoothRemapper sin cambios de comportamiento.
/// </summary>
internal static class Xbox360Mapping
{
    public const int Deadzone = 4000;

    /// <summary>Los ejes de SharpDX llegan como 0..65535 con el centro en 32767.</summary>
    public static short NormalizeAxis(int v)
    {
        int centered = v - 32767;
        if (centered < short.MinValue) centered = short.MinValue;
        if (centered > short.MaxValue) centered = short.MaxValue;
        return (short)centered;
    }

    public static short ApplyDeadzone(short v)
    {
        if (v > -Deadzone && v < Deadzone) return 0;
        return v;
    }

    /// <summary>El negativo de short.MinValue no es representable: se clampea.</summary>
    public static short NegateAxis(short v)
    {
        if (v == short.MinValue) return short.MaxValue;
        return (short)-v;
    }

    public static bool GetBtn(bool[]? buttons, int index)
    {
        if (buttons is null) return false;
        if (index < 0 || index >= buttons.Length) return false;
        return buttons[index];
    }

    /// <summary>
    /// El POV llega en centésimas de grado (0 = arriba, sentido horario); negativo
    /// significa centrado. Los rangos son inclusivos en los dos extremos, así que
    /// una diagonal exacta activa las dos direcciones que la componen.
    /// </summary>
    public static DpadState PovToDpad(int[]? povs)
    {
        if (povs is null || povs.Length == 0) return default;

        int pov = povs[0];
        if (pov < 0) return default;

        bool up    = pov >= 31500 || pov <= 4500;
        bool right = pov >= 4500  && pov <= 13500;
        bool down  = pov >= 13500 && pov <= 22500;
        bool left  = pov >= 22500 && pov <= 31500;

        return new DpadState(up, right, down, left);
    }
}
```

- [ ] **Step 4: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~Xbox360MappingTests
```

Expected: PASS, 35 tests.

- [x] **Step 5: Borrar los helpers viejos de `BluetoothRemapper.cs`**

Eliminar de `BluetoothRemapper.cs` los métodos privados `GetBtn`, `NormalizeAxis`, `ApplyDeadzone`, `NegateAxis`, `ToTrigger` (sin uso) y `ApplyDpad`, más la constante `Deadzone`, y hacer que el poll loop use `Xbox360Mapping`. Agregar `using BitDoFixer.Services;` arriba. En el loop, reemplazar la llamada a `ApplyDpad(controller, state.PointOfViewControllers)` por:

```csharp
                var dpad = Xbox360Mapping.PovToDpad(state.PointOfViewControllers);
                controller.SetButtonState(Xbox360Button.Up, dpad.Up);
                controller.SetButtonState(Xbox360Button.Right, dpad.Right);
                controller.SetButtonState(Xbox360Button.Down, dpad.Down);
                controller.SetButtonState(Xbox360Button.Left, dpad.Left);
```

y prefijar con `Xbox360Mapping.` cada llamada a `NormalizeAxis`, `ApplyDeadzone`, `NegateAxis` y `GetBtn`. Este paso mantiene la app compilando y funcionando; el traslado completo al worker es la Tarea 7.

- [ ] **Step 6: [WINDOWS] Build, test y prueba manual**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
dotnet run --project 8bitdofixer.csproj
```

Expected: todo verde, y con un mando conectado el remapeo sigue funcionando igual que antes — ejes, deadzone y D-pad incluidos. **Esta prueba manual es el punto del refactor de extracción:** si el D-pad cambió de comportamiento, la extracción introdujo un bug.

- [x] **Step 7: Commit**

```bash
git add Services/Xbox360Mapping.cs tests/8bitdofixer.Tests/Xbox360MappingTests.cs BluetoothRemapper.cs
git commit -m "refactor: extract pure DirectInput to Xbox360 mapping

Move NormalizeAxis, ApplyDeadzone, NegateAxis, GetBtn and the hat-switch
conversion out of BluetoothRemapper into a stateless Xbox360Mapping
class, with ApplyDpad reshaped into a pure PovToDpad returning a
DpadState so the hat math is testable without a controller.

Behavior is unchanged: the tests lock in what the code already does,
including the inclusive range boundaries that make exact diagonals set
both component directions."
```

---

## Task 5: `DeviceFilter` — decidir qué mandos reclamar

**Files:**
- Create: `Services/DeviceDescriptor.cs`
- Create: `Services/DeviceFilter.cs`
- Create: `tests/8bitdofixer.Tests/DeviceFilterTests.cs`

**Interfaces:**
- Consumes: la salida `[PROBE]` de la Tarea 3 (fija las constantes)
- Produces:
  - `BitDoFixer.Services.DeviceDescriptor` → `readonly record struct (Guid InstanceGuid, Guid ProductGuid, string InstanceName)`
  - `DeviceFilter.ShouldClaim(DeviceDescriptor d)` → `bool`
  - `DeviceFilter.TryGetVidPid(Guid productGuid, out ushort vid, out ushort pid)` → `bool`
  - constantes `DeviceFilter.VidEightBitDo`, `VidMicrosoft`, `PidXbox360Pad`

**Por qué existe `DeviceDescriptor`:** el `DeviceInstance` de SharpDX no se puede construir en un test. El descriptor es un snapshot de los tres campos que importan, así que la lógica de decisión queda 100% testeable y el supervisor solo hace un mapeo trivial de tres campos.

**El formato del `ProductGuid`:** DirectInput arma el GUID de producto de un dispositivo HID como `Data1 = (PID << 16) | VID`, con la cola `Data4` fija en `00 00 50 49 44 56 49 44`, que en ASCII es `PIDVID`. Verificar esa cola es lo que distingue un GUID con VID/PID embebidos de uno arbitrario.

- [ ] **Step 1: Confirmar las constantes contra la salida de la Tarea 3**

Antes de escribir los tests, revisar las líneas `[PROBE]`:

- Si el `vid=` de los mandos es `0x2DC8`, las constantes de abajo quedan como están.
- Si es otro valor, cambiar `VidEightBitDo` por el medido **en el test y en la implementación**, y anotar el valor real en el commit.
- Si el `InstanceName` no contiene "8BitDo" ni "Ultimate", ajustar `NameHints` al nombre real.

- [ ] **Step 2: Escribir los tests que fallan**

`tests/8bitdofixer.Tests/DeviceFilterTests.cs`:

```csharp
using BitDoFixer.Services;
using Xunit;

namespace BitDoFixer.Tests;

public class DeviceFilterTests
{
    /// <summary>
    /// Arma un ProductGuid con el layout de DirectInput para HID:
    /// Data1 = (PID &lt;&lt; 16) | VID, y la cola fija "PIDVID".
    /// </summary>
    private static Guid HidProductGuid(ushort vid, ushort pid)
    {
        uint data1 = ((uint)pid << 16) | vid;
        return new Guid(
            (int)data1, 0, 0,
            0x00, 0x00, 0x50, 0x49, 0x44, 0x56, 0x49, 0x44);
    }

    private static DeviceDescriptor Device(ushort vid, ushort pid, string name)
        => new(Guid.NewGuid(), HidProductGuid(vid, pid), name);

    // --- TryGetVidPid ---

    [Fact]
    public void TryGetVidPid_ExtractsFromTheHidLayout()
    {
        Assert.True(DeviceFilter.TryGetVidPid(HidProductGuid(0x2DC8, 0x3106), out var vid, out var pid));
        Assert.Equal(0x2DC8, vid);
        Assert.Equal(0x3106, pid);
    }

    [Fact]
    public void TryGetVidPid_RejectsAGuidWithoutThePidVidTail()
    {
        Assert.False(DeviceFilter.TryGetVidPid(Guid.Empty, out _, out _));
        Assert.False(DeviceFilter.TryGetVidPid(new Guid("31062dc8-0000-0000-0000-000000000000"), out _, out _));
    }

    // --- El guard que impide el loop de pads virtuales. Si este test se cae,
    //     la app puede volver a enumerar sus propios pads y crear pads sin fin. ---

    [Fact]
    public void ShouldClaim_RejectsOurOwnVirtualPad()
    {
        var virtualPad = Device(0x045E, 0x028E, "Xbox 360 Controller for Windows");
        Assert.False(DeviceFilter.ShouldClaim(virtualPad));
    }

    [Fact]
    public void ShouldClaim_RejectsTheVirtualPadEvenIfTheNameLooksLikeAn8BitDo()
    {
        // Red de seguridad: el rechazo del par 045E/028E gana sobre el allow-list
        // por nombre. Sin esto, un pad virtual con un nombre confuso reabre el loop.
        var impostor = Device(0x045E, 0x028E, "8BitDo Ultimate 2C");
        Assert.False(DeviceFilter.ShouldClaim(impostor));
    }

    // --- Allow-list ---

    [Fact]
    public void ShouldClaim_AcceptsBy8BitDoVidEvenWithAnUnhelpfulName()
    {
        Assert.True(DeviceFilter.ShouldClaim(Device(0x2DC8, 0x3106, "Wireless Controller")));
    }

    [Theory]
    [InlineData("8BitDo Ultimate 2C")]
    [InlineData("8bitdo ultimate 2c wireless")]   // el match es case-insensitive
    [InlineData("Ultimate 2C")]
    public void ShouldClaim_AcceptsByNameWhenTheVidIsUnknown(string name)
    {
        var unknownVid = new DeviceDescriptor(Guid.NewGuid(), Guid.Empty, name);
        Assert.True(DeviceFilter.ShouldClaim(unknownVid));
    }

    // --- Rechazos ---

    [Fact]
    public void ShouldClaim_RejectsARealXboxOnePad()
    {
        // VID de Microsoft pero otro PID: no pasa el allow-list, así que queda afuera
        // sin necesidad de rechazar el VID entero.
        Assert.False(DeviceFilter.ShouldClaim(Device(0x045E, 0x02FF, "Xbox One Controller")));
    }

    [Fact]
    public void ShouldClaim_RejectsAWheel()
    {
        Assert.False(DeviceFilter.ShouldClaim(Device(0x046D, 0xC262, "Logitech G920 Driving Force")));
    }

    [Fact]
    public void ShouldClaim_RejectsADeviceWithNoIdentityAtAll()
    {
        Assert.False(DeviceFilter.ShouldClaim(new DeviceDescriptor(Guid.NewGuid(), Guid.Empty, "")));
    }

    [Fact]
    public void ShouldClaim_ToleratesANullName()
    {
        // InstanceName viene de COM: tratarlo como no-nulo es un crash esperando pasar.
        var noName = new DeviceDescriptor(Guid.NewGuid(), Guid.Empty, null!);
        Assert.False(DeviceFilter.ShouldClaim(noName));
    }
}
```

- [ ] **Step 3: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~DeviceFilterTests
```

Expected: FAIL en compilación — `CS0246` por `DeviceFilter` y `DeviceDescriptor`.

- [ ] **Step 4: Escribir la implementación**

`Services/DeviceDescriptor.cs`:

```csharp
namespace BitDoFixer.Services;

/// <summary>
/// Snapshot de los campos de identidad de un dispositivo DirectInput. Existe para
/// que la decisión del filtro sea testeable: el DeviceInstance de SharpDX no se
/// puede construir en un test.
/// </summary>
internal readonly record struct DeviceDescriptor(
    Guid InstanceGuid,
    Guid ProductGuid,
    string InstanceName);
```

`Services/DeviceFilter.cs`:

```csharp
namespace BitDoFixer.Services;

/// <summary>
/// Decide qué dispositivos DirectInput reclama la app. Es una lista de permitidos
/// con un rechazo explícito como red de seguridad.
/// </summary>
internal static class DeviceFilter
{
    /// <summary>VID de 8BitDo. Medido contra hardware real, no asumido (Tarea 3 del plan).</summary>
    public const ushort VidEightBitDo = 0x2DC8;

    /// <summary>VID de Microsoft: el que la app le pone a sus propios pads virtuales.</summary>
    public const ushort VidMicrosoft = 0x045E;

    /// <summary>PID del pad Xbox 360 cableado, el que emite ViGEm.</summary>
    public const ushort PidXbox360Pad = 0x028E;

    private static readonly string[] NameHints = { "8BitDo", "Ultimate" };

    // Cola fija de un ProductGuid derivado de HID: "\0\0PIDVID" en ASCII.
    private static readonly byte[] PidVidTail = { 0x00, 0x00, 0x50, 0x49, 0x44, 0x56, 0x49, 0x44 };

    public static bool ShouldClaim(DeviceDescriptor d)
    {
        bool hasIds = TryGetVidPid(d.ProductGuid, out ushort vid, out ushort pid);

        // Red de seguridad, antes que cualquier aceptación: nuestros propios pads
        // virtuales también se enumeran en DirectInput. Sin este rechazo, cada pad
        // creado se vuelve un dispositivo a mapear y la app crea pads sin fin.
        if (hasIds && vid == VidMicrosoft && pid == PidXbox360Pad) return false;

        if (hasIds && vid == VidEightBitDo) return true;

        string name = d.InstanceName ?? string.Empty;
        foreach (var hint in NameHints)
        {
            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// DirectInput arma el GUID de producto de un HID como Data1 = (PID &lt;&lt; 16) | VID,
    /// con la cola fija "PIDVID". Devuelve false si el GUID no tiene ese layout.
    /// </summary>
    public static bool TryGetVidPid(Guid productGuid, out ushort vid, out ushort pid)
    {
        vid = 0;
        pid = 0;

        Span<byte> bytes = stackalloc byte[16];
        if (!productGuid.TryWriteBytes(bytes)) return false;

        for (int i = 0; i < PidVidTail.Length; i++)
        {
            if (bytes[8 + i] != PidVidTail[i]) return false;
        }

        uint data1 = BitConverter.ToUInt32(bytes[..4]);
        vid = (ushort)(data1 & 0xFFFF);
        pid = (ushort)(data1 >> 16);
        return true;
    }
}
```

- [ ] **Step 5: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~DeviceFilterTests
```

Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add Services/DeviceDescriptor.cs Services/DeviceFilter.cs tests/8bitdofixer.Tests/DeviceFilterTests.cs
git commit -m "feat: add device filter with virtual-pad loop guard

Allow-list on the 8BitDo VID or an 8BitDo/Ultimate instance name, with
an explicit reject of the exact 045E/028E pair that takes precedence
over the allow-list.

That reject is the load-bearing part: the virtual pads this app creates
also enumerate in DirectInput, so without it every pad created becomes
a device to map and the app spawns pads without end. The test named
RejectsTheVirtualPadEvenIfTheNameLooksLikeAn8BitDo is what keeps that
from regressing.

VID/PID come from the ProductGuid HID layout (Data1 = PID << 16 | VID,
'PIDVID' tail), verified against real hardware rather than assumed."
```

---

## Task 6: Modelos observables y strings nuevos

**Files:**
- Create: `Models/ControllerState.cs`
- Create: `Models/ControllerEntry.cs`
- Create: `Models/BatteryEntry.cs`
- Create: `tests/8bitdofixer.Tests/ControllerEntryTests.cs`
- Modify: `Localization.cs` (strings nuevos)

**Interfaces:**
- Consumes: nada
- Produces:
  - `BitDoFixer.Models.ControllerState` → enum `{ Connecting, Mapped, Lost }`
  - `BitDoFixer.Models.ServiceState` → enum `{ Stopped, Searching, Mapped, DriverMissing }`
  - `BitDoFixer.Models.ControllerEntry` → `sealed class : INotifyPropertyChanged`, con `Guid InstanceGuid { get; }`, `string Name { get; }`, `int PlayerIndex { get; }`, `ControllerState State { get; set; }`, `bool RumbleSupported { get; set; }`, `string StatusText { get; }`, `string PlayerLabel { get; }`, `string RumbleText { get; }`
  - `BitDoFixer.Models.BatteryEntry` → `sealed class : INotifyPropertyChanged`, con `string BleDeviceId { get; }`, `string Name { get; set; }`, `int Level { get; set; }`
  - `BitDoFixer.Models.ControllerEntry` también expone `string RumbleText { get; }` y `void RefreshLocalizedText()`
  - `Localization`: `ControllersTitle`, `SearchingControllers`, `PlayerLabel(int)`, `RumbleOn`, `RumbleOff`, `StateConnecting`, `StateMapped`, `StateLost`, `ServiceStopped`, `DriverMissingTitle`, `DriverMissingHint`, `LogSupervisorStarted`, `LogDeviceFound(string,int)`, `LogDeviceMapped(string)`, `LogDeviceLost(string)`, `LogDeviceReleased(string)`, `LogDriverMissing(string)`, `LogWorkerRetry(string,int)`, `LogNoSlotsLeft(string)`

**`public`, no `internal`, y esto no es cosmético.** WPF resuelve los bindings por
reflexión y **falla en silencio** contra tipos `internal`: el binding no tira excepción,
simplemente no muestra nada. Así que `ControllerState`, `ServiceState`, `ControllerEntry`,
`BatteryEntry` y `ControllerService` son `public`. Los tipos que nunca aparecen en un
binding (`Xbox360Mapping`, `DeviceFilter`, `DeviceDescriptor`, `IControllerSink`,
`ControllerWorker`, `ControllerSupervisor`) siguen `internal` y se testean vía el
`InternalsVisibleTo` de la Tarea 2.

**Nota sobre `StatusText`:** las entries exponen texto ya localizado en vez de que el XAML haga `switch` sobre el enum. Eso mantiene la UI declarativa y deja todo el idioma en `Localization.cs`, como el resto del proyecto. El precio es que un cambio de idioma tiene que refrescar las entries — se resuelve en la Tarea 11.

**HECHO 2026-09-02 (escrito). Pendiente: Steps 2 y 6, que se corren en Windows.**

- [x] **Step 1: Escribir el test que falla**

`tests/8bitdofixer.Tests/ControllerEntryTests.cs`:

```csharp
using System.ComponentModel;
using BitDoFixer.Models;
using Xunit;

namespace BitDoFixer.Tests;

public class ControllerEntryTests
{
    private static ControllerEntry Entry() =>
        new(Guid.NewGuid(), "8BitDo Ultimate 2C", playerIndex: 1);

    [Fact]
    public void NewEntryStartsConnecting()
    {
        Assert.Equal(ControllerState.Connecting, Entry().State);
    }

    [Fact]
    public void ChangingStateRaisesPropertyChangedForStateAndStatusText()
    {
        var entry = Entry();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.State = ControllerState.Mapped;

        // StatusText es derivado: si no notifica, la UI se queda con el texto viejo.
        Assert.Contains(nameof(ControllerEntry.State), raised);
        Assert.Contains(nameof(ControllerEntry.StatusText), raised);
    }

    [Fact]
    public void SettingTheSameStateDoesNotNotify()
    {
        var entry = Entry();
        entry.State = ControllerState.Mapped;

        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        entry.State = ControllerState.Mapped;

        Assert.Empty(raised);
    }

    [Fact]
    public void ChangingRumbleSupportRaisesPropertyChanged()
    {
        var entry = Entry();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.RumbleSupported = true;

        Assert.Contains(nameof(ControllerEntry.RumbleSupported), raised);
    }

    [Fact]
    public void StatusTextIsLocalizedNotAnEnumName()
    {
        var entry = Entry();
        entry.State = ControllerState.Mapped;
        Assert.Equal(Localization.Instance.StateMapped, entry.StatusText);
    }

    [Fact]
    public void IdentityIsImmutable()
    {
        var entry = Entry();
        Assert.Equal("8BitDo Ultimate 2C", entry.Name);
        Assert.Equal(1, entry.PlayerIndex);
        Assert.NotEqual(Guid.Empty, entry.InstanceGuid);
    }

    [Fact]
    public void BatteryEntryNotifiesOnLevelChange()
    {
        var battery = new BatteryEntry("BLE#abc123", "8BitDo Ultimate 2C");
        var raised = new List<string?>();
        ((INotifyPropertyChanged)battery).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        battery.Level = 42;

        Assert.Contains(nameof(BatteryEntry.Level), raised);
        Assert.Equal(42, battery.Level);
    }
}
```

- [ ] **Step 2: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~ControllerEntryTests
```

Expected: FAIL en compilación — `CS0246` por `ControllerEntry`, `ControllerState`, `BatteryEntry`.

- [x] **Step 3: Escribir los enums**

`Models/ControllerState.cs`:

```csharp
namespace BitDoFixer.Models;

/// <summary>Estado de un mando individual.</summary>
// public, no internal: WPF bindea por reflexión y falla en silencio contra
// tipos internal. Todo lo que aparece en un Binding del XAML tiene que ser public.
public enum ControllerState
{
    /// <summary>Detectado, todavía adquiriendo el dispositivo y conectando el pad virtual.</summary>
    Connecting,

    /// <summary>Mapeando: el loop de polling está corriendo.</summary>
    Mapped,

    /// <summary>El dispositivo físico desapareció. El pad virtual sigue vivo durante la ventana de gracia.</summary>
    Lost
}

/// <summary>
/// Estado agregado del servicio. DriverMissing vive acá y no en ControllerState porque
/// el ViGEmClient se crea una sola vez a nivel de servicio: sin cliente no hay ninguna
/// entry, así que un mando individual nunca puede estar en ese estado.
/// </summary>
public enum ServiceState
{
    Stopped,

    /// <summary>Corriendo, cero mandos encontrados. Es el estado de reposo normal, no un error.</summary>
    Searching,

    /// <summary>Uno o más mandos mapeados.</summary>
    Mapped,

    /// <summary>No se pudo crear el ViGEmClient: falta ViGEmBus.</summary>
    DriverMissing
}
```

- [x] **Step 4: Escribir `ControllerEntry` y `BatteryEntry`**

`Models/ControllerEntry.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BitDoFixer.Models;

/// <summary>
/// Un mando, observado por la UI. La identidad es inmutable; el estado cambia.
/// StatusText y PlayerLabel son derivados y notifican junto con su fuente, para que
/// el XAML no tenga que hacer switch sobre el enum.
/// </summary>
public sealed class ControllerEntry : INotifyPropertyChanged
{
    public ControllerEntry(Guid instanceGuid, string name, int playerIndex)
    {
        InstanceGuid = instanceGuid;
        Name = name;
        PlayerIndex = playerIndex;
    }

    public Guid InstanceGuid { get; }
    public string Name { get; }

    /// <summary>Slot XInput 1..4, o 0 si no se pudo asignar ninguno.</summary>
    public int PlayerIndex { get; }

    private ControllerState _state = ControllerState.Connecting;
    public ControllerState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private bool _rumbleSupported;
    public bool RumbleSupported
    {
        get => _rumbleSupported;
        set
        {
            if (_rumbleSupported == value) return;
            _rumbleSupported = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RumbleText));
        }
    }

    public string StatusText => State switch
    {
        ControllerState.Connecting => Localization.Instance.StateConnecting,
        ControllerState.Mapped => Localization.Instance.StateMapped,
        ControllerState.Lost => Localization.Instance.StateLost,
        _ => Localization.Instance.StateConnecting
    };

    public string PlayerLabel => Localization.Instance.PlayerLabel(PlayerIndex);

    public string RumbleText => RumbleSupported
        ? Localization.Instance.RumbleOn
        : Localization.Instance.RumbleOff;

    /// <summary>Refresca todo el texto derivado tras un cambio de idioma.</summary>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PlayerLabel));
        OnPropertyChanged(nameof(RumbleText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

`Models/BatteryEntry.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BitDoFixer.Models;

/// <summary>
/// Un dispositivo BLE que reporta batería. Se keyea por BleDeviceId y no por nombre:
/// dos Ultimate 2C idénticos comparten nombre, y keyear por nombre hace que el segundo
/// sobreescriba al primero.
///
/// Deliberadamente NO se ata a un ControllerEntry: no existe join confiable entre un
/// InstanceGuid de DirectInput y un device id de BLE (ver spec §7.1).
/// </summary>
public sealed class BatteryEntry : INotifyPropertyChanged
{
    public BatteryEntry(string bleDeviceId, string name)
    {
        BleDeviceId = bleDeviceId;
        _name = name;
    }

    public string BleDeviceId { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); }
    }

    private int _level;
    public int Level
    {
        get => _level;
        set { if (_level == value) return; _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelText)); }
    }

    public string LevelText => $"{Level}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [x] **Step 5: Agregar los strings a `Localization.cs`**

Insertar antes de la línea `public event PropertyChangedEventHandler? PropertyChanged;`:

```csharp
        // --- Multi-mando (plan supervisor) ---
        // NOTA: las traducciones al turco necesitan revisión de un hablante nativo.
        public string ControllersTitle => IsEnglish ? "CONTROLLERS" : "KUMANDALAR";
        public string SearchingControllers => IsEnglish ? "Searching for 8BitDo controllers…" : "8BitDo kumandaları aranıyor…";
        public string StateConnecting => IsEnglish ? "Connecting…" : "Bağlanıyor…";
        public string StateMapped => IsEnglish ? "Mapped" : "Eşlendi";
        public string StateLost => IsEnglish ? "Lost" : "Bağlantı Koptu";
        public string ServiceStopped => IsEnglish ? "Service stopped" : "Servis durduruldu";
        public string RumbleOn => IsEnglish ? "Rumble" : "Titreşim";
        public string RumbleOff => IsEnglish ? "No rumble" : "Titreşim yok";
        public string PlayerLabel(int index) => index <= 0
            ? (IsEnglish ? "No slot" : "Yuva yok")
            : (IsEnglish ? $"Player {index}" : $"Oyuncu {index}");

        public string DriverMissingTitle => IsEnglish ? "ViGEmBus not installed" : "ViGEmBus kurulu değil";
        public string DriverMissingHint => IsEnglish
            ? "Install the ViGEmBus driver, then the app reconnects on its own."
            : "ViGEmBus sürücüsünü kurun, uygulama kendiliğinden yeniden bağlanır.";

        // Log del supervisor
        public string LogSupervisorStarted => IsEnglish ? "Supervisor started. Watching for controllers." : "Süpervizör başlatıldı. Kumandalar izleniyor.";
        public string LogDeviceFound(string name, int player) => IsEnglish
            ? $"Found '{name}' → player {player}"
            : $"'{name}' bulundu → oyuncu {player}";
        public string LogDeviceMapped(string name) => IsEnglish ? $"'{name}' mapped and ready." : $"'{name}' eşlendi ve hazır.";
        public string LogDeviceLost(string name) => IsEnglish
            ? $"'{name}' lost. Holding its virtual pad for {Services.ControllerSupervisor.VirtualPadGraceSeconds}s."
            : $"'{name}' bağlantısı koptu. Sanal pad {Services.ControllerSupervisor.VirtualPadGraceSeconds}s tutuluyor.";
        public string LogDeviceReleased(string name) => IsEnglish
            ? $"'{name}' did not come back; virtual pad released."
            : $"'{name}' geri dönmedi; sanal pad bırakıldı.";
        public string LogWorkerRetry(string name, int seconds) => IsEnglish
            ? $"'{name}' failed; retrying in {seconds}s."
            : $"'{name}' başarısız; {seconds}s içinde yeniden denenecek.";
        public string LogDriverMissing(string detail) => IsEnglish
            ? $"ViGEmBus driver unavailable ({detail}). Retrying every 60s."
            : $"ViGEmBus sürücüsü kullanılamıyor ({detail}). Her 60s tekrar denenecek.";
        public string LogNoSlotsLeft(string name) => IsEnglish
            ? $"'{name}' mapped without an XInput slot: all 4 are taken."
            : $"'{name}' XInput yuvası olmadan eşlendi: 4 yuvanın tamamı dolu.";
```

**Ojo con el orden de tareas:** `LogDeviceLost` referencia `Services.ControllerSupervisor.VirtualPadGraceSeconds`, que se crea en la Tarea 8. Hasta entonces, dejar el literal `15` en su lugar y cambiarlo a la constante en la Tarea 8. Anotarlo con un comentario `// TODO Tarea 8` es aceptable acá porque el plan dice exactamente cuándo se resuelve.

- [ ] **Step 6: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~ControllerEntryTests
```

Expected: PASS, 7 tests.

- [x] **Step 7: Commit**

```bash
git add Models/ tests/8bitdofixer.Tests/ControllerEntryTests.cs Localization.cs
git commit -m "feat: add observable controller and battery models

ControllerEntry and BatteryEntry expose already-localized derived text
so the XAML stays declarative and every string stays in Localization.

BatteryEntry keys on the BLE device id rather than the device name:
two identical Ultimate 2C pads report the same name, and the current
monitor lets the second overwrite the first.

ServiceState carries DriverMissing; ControllerState does not, because
the ViGEmClient is created once per service and no entry exists without
it. Turkish strings need a native review."
```

---

## Task 7: `IControllerSink` y `ControllerWorker`

**Files:**
- Create: `Services/IControllerSink.cs`
- Create: `Services/ControllerWorker.cs`
- Delete: `BluetoothRemapper.cs` (su contenido queda repartido entre `Xbox360Mapping` y `ControllerWorker`)
- Modify: `MainWindow.xaml.cs` (queda roto a propósito hasta la Tarea 11 — ver Step 6)

**Interfaces:**
- Consumes: `Xbox360Mapping` (Tarea 4), `DeviceDescriptor` (Tarea 5)
- Produces:
  - `BitDoFixer.Services.IControllerSink` con `OnLog(string)`, `OnDeviceFound(Guid, string, int)`, `OnDeviceMapped(Guid, bool)`, `OnDeviceLost(Guid)`, `OnDeviceRemoved(Guid)`, `OnDriverMissing(bool, string?)`
  - `ControllerWorker.RunAsync(DirectInput, DeviceDescriptor, IntPtr, IXbox360Controller, Action<Action<byte,byte>?>, IControllerSink, CancellationToken)` → `Task`

**Tres decisiones de diseño en el worker, y el por qué de cada una:**

1. **El worker no es dueño del pad virtual.** No lo crea, no lo conecta y no lo desconecta. El supervisor lo hace, porque la ventana de gracia exige que el pad sobreviva a la muerte del worker. Si el worker llamara a `Connect()`, un reenganche dentro de la gracia lo llamaría sobre un pad ya conectado.
2. **El rumble se enrutra por un target mutable, no por suscripción al evento.** El supervisor suscribe `FeedbackReceived` **una sola vez** al crear el pad y lo reenvía a un `Action<byte,byte>?` que el worker setea al arrancar y limpia al terminar. La alternativa —que cada worker se suscriba y se desuscriba— necesita nombrar el tipo del delegate de ViGEm, que no pude verificar offline, y si se olvida la desuscripción cada reconexión deja un handler más colgado apuntando a un `Effect` ya dispuesto.
3. **El worker no es dueño del `DirectInput`.** Lo recibe. Los `Joystick` lo referencian, así que disponerlo mientras hay workers vivos rompe todo; su vida la maneja el supervisor.

- [ ] **Step 1: Escribir `IControllerSink`**

`Services/IControllerSink.cs`:

```csharp
namespace BitDoFixer.Services;

/// <summary>
/// Cómo el supervisor y sus workers reportan hacia arriba. Existe para que el
/// supervisor no sepa nada de WPF: ControllerService implementa esta interfaz y
/// marshalea cada llamada al Dispatcher.
/// </summary>
internal interface IControllerSink
{
    void OnLog(string message);

    /// <summary>Dispositivo detectado y con pad virtual asignado. playerIndex 0 = sin slot.</summary>
    void OnDeviceFound(Guid instanceGuid, string name, int playerIndex);

    /// <summary>El loop de polling arrancó.</summary>
    void OnDeviceMapped(Guid instanceGuid, bool rumbleSupported);

    /// <summary>El dispositivo físico desapareció. El pad virtual sigue vivo por la gracia.</summary>
    void OnDeviceLost(Guid instanceGuid);

    /// <summary>Venció la gracia sin que el dispositivo volviera: pad liberado, entry fuera.</summary>
    void OnDeviceRemoved(Guid instanceGuid);

    /// <summary>No se pudo crear el ViGEmClient (o se recuperó).</summary>
    void OnDriverMissing(bool missing, string? detail);
}
```

- [ ] **Step 2: Escribir `ControllerWorker`**

`Services/ControllerWorker.cs`:

```csharp
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.DirectInput;

namespace BitDoFixer.Services;

/// <summary>
/// Mapea UN mando físico a UN pad virtual ya creado. No es dueño del pad ni del
/// DirectInput: los recibe. Termina cuando se cancela el token o cuando el
/// dispositivo se cae, y en los dos casos el supervisor decide qué hacer.
/// </summary>
internal static class ControllerWorker
{
    private const int PollIntervalMs = 5;
    private const int BufferSize = 128;

    public static async Task RunAsync(
        DirectInput directInput,
        DeviceDescriptor device,
        IntPtr hwnd,
        IXbox360Controller pad,
        Action<Action<byte, byte>?> setRumbleTarget,
        IControllerSink sink,
        CancellationToken token)
    {
        Joystick? joystick = null;
        Effect? forceFeedbackEffect = null;
        EffectParameters? effectParams = null;

        try
        {
            joystick = new Joystick(directInput, device.InstanceGuid);
            joystick.SetCooperativeLevel(hwnd, CooperativeLevel.Exclusive | CooperativeLevel.Background);
            joystick.Properties.BufferSize = BufferSize;
            joystick.Acquire();

            bool rumbleSupported = false;
            try
            {
                var actuators = joystick.GetObjects(DeviceObjectTypeFlags.ForceFeedbackActuator)
                                        .Select(x => (int)x.ObjectId)
                                        .ToArray();

                if (actuators.Length > 0)
                {
                    // Un solo actuador: la mayoría de los DInput tienen un motor, y una
                    // dirección cartesiana de un eje ("1") es la configuración que funciona
                    // de forma más confiable.
                    effectParams = new EffectParameters
                    {
                        Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds,
                        StartDelay = 0,
                        SamplePeriod = 0,
                        Duration = -1, // infinito
                        TriggerButton = -1,
                        TriggerRepeatInterval = 0,
                        Axes = new[] { actuators[0] },
                        Directions = new[] { 1 },
                        Envelope = null,
                        Parameters = new ConstantForce { Magnitude = 0 }
                    };

                    forceFeedbackEffect = new Effect(joystick, EffectGuid.ConstantForce, effectParams);
                    forceFeedbackEffect.Download();
                    rumbleSupported = true;
                }
            }
            catch (Exception ex)
            {
                sink.OnLog($"[{device.InstanceName}] vibration setup failed: {ex.Message} (continuing without rumble)");
            }

            if (rumbleSupported && forceFeedbackEffect is not null && effectParams is not null)
            {
                var effect = forceFeedbackEffect;
                var parameters = effectParams;

                setRumbleTarget((largeMotor, smallMotor) =>
                {
                    try
                    {
                        // ViGEm entrega 0..255; DInput espera magnitud 0..10000.
                        int maxMotor = Math.Max(largeMotor, smallMotor);
                        int magnitude = (maxMotor * 10000) / 255;

                        parameters.Parameters = new ConstantForce { Magnitude = magnitude };
                        effect.SetParameters(parameters, EffectParameterFlags.TypeSpecificParameters);

                        if (magnitude > 0) effect.Start(1, EffectPlayFlags.NoDownload);
                        else effect.Stop();
                    }
                    catch { } // Un error de FFB en runtime no puede tirar el mapper
                });
            }

            sink.OnDeviceMapped(device.InstanceGuid, rumbleSupported);

            var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PollIntervalMs));
            while (await timer.WaitForNextTickAsync(token))
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();
                if (state is null) continue;

                var buttons = state.Buttons;

                short lx = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.X));
                short ly = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.Y));
                short rx = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.Z));
                short ry = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.RotationZ));

                pad.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
                pad.SetAxisValue(Xbox360Axis.LeftThumbY, Xbox360Mapping.NegateAxis(ly));
                pad.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                pad.SetAxisValue(Xbox360Axis.RightThumbY, Xbox360Mapping.NegateAxis(ry));

                // Los gatillos del Ultimate 2C en Bluetooth son digitales (limitación de hardware).
                pad.SetSliderValue(Xbox360Slider.LeftTrigger, Xbox360Mapping.GetBtn(buttons, 8) ? (byte)255 : (byte)0);
                pad.SetSliderValue(Xbox360Slider.RightTrigger, Xbox360Mapping.GetBtn(buttons, 9) ? (byte)255 : (byte)0);

                pad.SetButtonState(Xbox360Button.A, Xbox360Mapping.GetBtn(buttons, 0));
                pad.SetButtonState(Xbox360Button.B, Xbox360Mapping.GetBtn(buttons, 1));
                pad.SetButtonState(Xbox360Button.X, Xbox360Mapping.GetBtn(buttons, 3));
                pad.SetButtonState(Xbox360Button.Y, Xbox360Mapping.GetBtn(buttons, 4));

                pad.SetButtonState(Xbox360Button.LeftShoulder, Xbox360Mapping.GetBtn(buttons, 6));
                pad.SetButtonState(Xbox360Button.RightShoulder, Xbox360Mapping.GetBtn(buttons, 7));

                pad.SetButtonState(Xbox360Button.Back, Xbox360Mapping.GetBtn(buttons, 10));
                pad.SetButtonState(Xbox360Button.Start, Xbox360Mapping.GetBtn(buttons, 11));

                pad.SetButtonState(Xbox360Button.LeftThumb, Xbox360Mapping.GetBtn(buttons, 13));
                pad.SetButtonState(Xbox360Button.RightThumb, Xbox360Mapping.GetBtn(buttons, 14));

                var dpad = Xbox360Mapping.PovToDpad(state.PointOfViewControllers);
                pad.SetButtonState(Xbox360Button.Up, dpad.Up);
                pad.SetButtonState(Xbox360Button.Right, dpad.Right);
                pad.SetButtonState(Xbox360Button.Down, dpad.Down);
                pad.SetButtonState(Xbox360Button.Left, dpad.Left);

                pad.SubmitReport();
            }
        }
        catch (OperationCanceledException)
        {
            // Esperado al detener el servicio
        }
        catch (Exception ex)
        {
            sink.OnLog($"[{device.InstanceName}] {ex.Message}");
        }
        finally
        {
            // Cortar el rumble ANTES de disponer el effect: el handler del supervisor
            // sigue suscripto al pad y dispararía sobre un Effect muerto.
            setRumbleTarget(null);
            forceFeedbackEffect?.Dispose();
            joystick?.Dispose();
        }
    }
}
```

- [ ] **Step 3: Borrar `BluetoothRemapper.cs`**

```bash
git rm BluetoothRemapper.cs
```

Su contenido quedó repartido: las funciones puras en `Xbox360Mapping` (Tarea 4), el acquire/FFB/poll loop en `ControllerWorker`, la enumeración en `ControllerSupervisor` (Tarea 8). El enum `RemapperStatus` desaparece: lo reemplaza `ControllerState`.

- [ ] **Step 4: Verificar que no quedaron referencias**

```bash
grep -rn "BluetoothRemapper\|RemapperStatus" --include=*.cs --include=*.xaml .
```

Expected: solo apariciones en `MainWindow.xaml.cs`, que se arregla en la Tarea 11.

- [ ] **Step 5: [WINDOWS] Verificar que compila salvo `MainWindow`**

```bash
dotnet build 8bitdofixer.sln
```

Expected: **falla**, con errores acotados a `MainWindow.xaml.cs` por `BluetoothRemapper` y `RemapperStatus`. Cualquier error en otro archivo hay que arreglarlo antes de seguir.

- [ ] **Step 6: Silenciar `MainWindow` provisoriamente para poder correr los tests**

El árbol tiene que compilar para que `dotnet test` corra. En `MainWindow.xaml.cs`, comentar el cuerpo de `StartService()` dejando solo `UpdateUiState(true); _isRunning = true;`, borrar el campo `_lastRemapperStatus` y el método `ApplyRemapperStatus`, y en `StopService()` sacar la referencia a `_lastRemapperStatus`. La UI queda temporalmente sin funcionalidad: la Tarea 11 la reconstruye contra el servicio. Marcar cada corte con `// TEMPORAL: reconectado en la Tarea 11 del plan`.

- [ ] **Step 7: [WINDOWS] Build y test**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
```

Expected: build limpio y todos los tests de las Tareas 2, 4, 5 y 6 en verde. **La app está intencionalmente sin funcionalidad de mapeo en este commit** — es el único punto del plan donde eso pasa, y las Tareas 8 a 11 lo cierran.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: replace single-device remapper with a per-device worker

ControllerWorker maps one physical pad to one already-created virtual
pad. It deliberately owns neither the virtual pad nor the DirectInput
instance: the grace window requires the pad to outlive the worker, and
Joystick objects reference the DirectInput, so both belong to the
supervisor.

Rumble routes through a mutable target that the supervisor forwards to,
rather than each worker subscribing to FeedbackReceived. A worker that
forgets to unsubscribe leaves a handler pointing at a disposed Effect,
and every reconnect adds another one.

MainWindow is temporarily inert; tasks 8-11 wire it to the service."
```

---

## Task 8: `ControllerSupervisor`

**Files:**
- Create: `Services/ControllerSupervisor.cs`
- Modify: `Localization.cs` (cambiar el literal `15` por la constante, ver Tarea 6 Step 5)

**Interfaces:**
- Consumes: `DeviceFilter`, `DeviceDescriptor`, `ControllerWorker`, `IControllerSink`
- Produces:
  - `ControllerSupervisor.VirtualPadGraceSeconds` → `const int` = 15
  - `ControllerSupervisor(IControllerSink sink)` → constructor
  - `ControllerSupervisor.RunAsync(IntPtr hwnd, CancellationToken token)` → `Task`
  - implementa `IDisposable`

**Qué hace cada tick, en orden:**

1. Asegura el `ViGEmClient`. Si no se puede crear, es `DriverMissing`: reporta y no reintenta hasta 60 s después. No corta el loop — instalar ViGEmBus con la app corriendo tiene que recuperarla sola.
2. Enumera DirectInput (Gamepad y Joystick), filtra con `DeviceFilter`, arma descriptores.
3. Dispositivos nuevos: asigna slot del free-list, crea y conecta el pad virtual, suscribe el forward de rumble una única vez, levanta el worker.
4. Slots cuyo worker terminó: si el dispositivo sigue enumerado, reintenta con backoff. Si no está, marca `LostSince` y espera la gracia; vencida, desconecta el pad, devuelve el slot al free-list y quita la entry.

**Sobre el free-list de slots:** XInput expone 4 slots. Se asignan del más bajo libre, y se devuelven solo cuando el pad se libera de verdad (vencida la gracia). Eso es exactamente lo que preserva el número de jugador en una reconexión rápida. Si los 4 están tomados, el mando se mapea igual con `PlayerIndex = 0` y se loguea.

- [ ] **Step 1: Escribir el supervisor**

`Services/ControllerSupervisor.cs`:

```csharp
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using SharpDX.DirectInput;

namespace BitDoFixer.Services;

/// <summary>
/// Enumera, levanta un worker por mando y los recoge cuando mueren. Es dueño del
/// DirectInput, del ViGEmClient compartido y de los pads virtuales.
/// No sabe nada de WPF: todo sale por IControllerSink.
/// </summary>
internal sealed class ControllerSupervisor : IDisposable
{
    /// <summary>Cuánto sobrevive un pad virtual a la pérdida del mando físico.
    /// Preserva el slot XInput en una reconexión rápida.</summary>
    public const int VirtualPadGraceSeconds = 15;

    private const int EnumerationIntervalMs = 2000;
    private const int WorkerBackoffStartMs = 2000;
    private const int WorkerBackoffCapMs = 30000;
    private const int DriverRetryMs = 60000;
    private const int MaxXInputSlots = 4;

    private sealed class Slot
    {
        public required DeviceDescriptor Device { get; set; }
        public required IXbox360Controller Pad { get; init; }
        public required int PlayerIndex { get; init; }

        /// <summary>Target de rumble del worker vivo. El forward del pad lee de acá.</summary>
        public Action<byte, byte>? RumbleTarget;

        public Task? Worker { get; set; }
        public CancellationTokenSource? WorkerCts { get; set; }
        public DateTime? LostSince { get; set; }
        public int BackoffMs { get; set; } = WorkerBackoffStartMs;
        public DateTime NextAttemptUtc { get; set; } = DateTime.MinValue;
    }

    private readonly IControllerSink _sink;
    private readonly Dictionary<Guid, Slot> _slots = new();
    private readonly SortedSet<int> _freeSlots = new(Enumerable.Range(1, MaxXInputSlots));

    private DirectInput? _directInput;
    private ViGEmClient? _vigem;
    private bool _driverMissing;
    private DateTime _driverRetryUtc = DateTime.MinValue;

    public ControllerSupervisor(IControllerSink sink) => _sink = sink;

    public async Task RunAsync(IntPtr hwnd, CancellationToken token)
    {
        _directInput = new DirectInput();
        _sink.OnLog(Localization.Instance.LogSupervisorStarted);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(EnumerationIntervalMs));
            do
            {
                try
                {
                    Tick(hwnd, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Un tick que falla no puede matar el supervisor: el próximo reintenta.
                    _sink.OnLog($"[SUPERVISOR] {ex.Message}");
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException)
        {
            // Esperado al detener
        }
        finally
        {
            await ShutdownAsync();
        }
    }

    private void Tick(IntPtr hwnd, CancellationToken token)
    {
        if (!EnsureClient()) return;

        var present = Enumerate();
        SpawnNewDevices(present, hwnd, token);
        ReapAndRetry(present, hwnd, token);
    }

    /// <summary>
    /// Crea el ViGEmClient si falta. Un fallo se trata como "falta ViGEmBus": es la
    /// causa dominante, y no depende de acertar el nombre del tipo de excepción de la
    /// librería. Reintenta cada 60 s para que instalar el driver recupere la app sin
    /// reiniciarla.
    /// </summary>
    private bool EnsureClient()
    {
        if (_vigem is not null) return true;
        if (DateTime.UtcNow < _driverRetryUtc) return false;

        try
        {
            _vigem = new ViGEmClient();
            if (_driverMissing)
            {
                _driverMissing = false;
                _sink.OnDriverMissing(false, null);
            }
            return true;
        }
        catch (Exception ex)
        {
            _driverRetryUtc = DateTime.UtcNow.AddMilliseconds(DriverRetryMs);
            if (!_driverMissing)
            {
                _driverMissing = true;
                _sink.OnDriverMissing(true, ex.Message);
                _sink.OnLog(Localization.Instance.LogDriverMissing(ex.Message));
            }
            return false;
        }
    }

    private Dictionary<Guid, DeviceDescriptor> Enumerate()
    {
        var found = new Dictionary<Guid, DeviceDescriptor>();
        if (_directInput is null) return found;

        foreach (var type in new[] { DeviceType.Gamepad, DeviceType.Joystick })
        {
            foreach (var instance in _directInput.GetDevices(type, DeviceEnumerationFlags.AttachedOnly))
            {
                var descriptor = new DeviceDescriptor(
                    instance.InstanceGuid,
                    instance.ProductGuid,
                    instance.InstanceName ?? string.Empty);

                if (!DeviceFilter.ShouldClaim(descriptor)) continue;

                // Un mando puede aparecer en las dos enumeraciones; el dict deduplica.
                found[descriptor.InstanceGuid] = descriptor;
            }
        }

        return found;
    }

    private void SpawnNewDevices(Dictionary<Guid, DeviceDescriptor> present, IntPtr hwnd, CancellationToken token)
    {
        foreach (var (guid, device) in present)
        {
            if (_slots.ContainsKey(guid)) continue;
            if (_vigem is null) return;

            int playerIndex = 0;
            if (_freeSlots.Count > 0)
            {
                playerIndex = _freeSlots.Min;
                _freeSlots.Remove(playerIndex);
            }
            else
            {
                _sink.OnLog(Localization.Instance.LogNoSlotsLeft(device.InstanceName));
            }

            var pad = _vigem.CreateXbox360Controller(0x045E, 0x028E);

            var slot = new Slot { Device = device, Pad = pad, PlayerIndex = playerIndex };

            // Suscripción ÚNICA por pad. El worker sólo cambia RumbleTarget, así que
            // reconectar no acumula handlers ni apunta a un Effect ya dispuesto.
            pad.FeedbackReceived += (_, args) => slot.RumbleTarget?.Invoke(args.LargeMotor, args.SmallMotor);

            pad.Connect();

            _slots[guid] = slot;
            _sink.OnDeviceFound(guid, device.InstanceName, playerIndex);
            _sink.OnLog(Localization.Instance.LogDeviceFound(device.InstanceName, playerIndex));

            StartWorker(slot, hwnd, token);
        }
    }

    private void ReapAndRetry(Dictionary<Guid, DeviceDescriptor> present, IntPtr hwnd, CancellationToken token)
    {
        foreach (var guid in _slots.Keys.ToList())
        {
            var slot = _slots[guid];
            bool workerAlive = slot.Worker is { IsCompleted: false };
            bool devicePresent = present.ContainsKey(guid);

            if (workerAlive)
            {
                // El dispositivo volvió mientras el worker seguía vivo: nada que hacer.
                if (devicePresent) slot.LostSince = null;
                continue;
            }

            if (devicePresent)
            {
                // El worker murió pero el dispositivo sigue ahí: reintento con backoff.
                if (DateTime.UtcNow < slot.NextAttemptUtc) continue;

                slot.Device = present[guid];
                slot.LostSince = null;
                _sink.OnLog(Localization.Instance.LogWorkerRetry(slot.Device.InstanceName, slot.BackoffMs / 1000));
                slot.BackoffMs = Math.Min(slot.BackoffMs * 2, WorkerBackoffCapMs);
                StartWorker(slot, hwnd, token);
                continue;
            }

            // El dispositivo no está. Arranca (o corre) la ventana de gracia.
            if (slot.LostSince is null)
            {
                slot.LostSince = DateTime.UtcNow;
                _sink.OnDeviceLost(guid);
                _sink.OnLog(Localization.Instance.LogDeviceLost(slot.Device.InstanceName));
                continue;
            }

            if (DateTime.UtcNow - slot.LostSince.Value < TimeSpan.FromSeconds(VirtualPadGraceSeconds)) continue;

            ReleaseSlot(guid, slot);
        }
    }

    private void StartWorker(Slot slot, IntPtr hwnd, CancellationToken token)
    {
        slot.WorkerCts?.Dispose();
        slot.WorkerCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var cts = slot.WorkerCts;
        var device = slot.Device;

        slot.Worker = Task.Run(() => ControllerWorker.RunAsync(
            _directInput!,
            device,
            hwnd,
            slot.Pad,
            target => slot.RumbleTarget = target,
            _sink,
            cts.Token), CancellationToken.None);
    }

    private void ReleaseSlot(Guid guid, Slot slot)
    {
        slot.RumbleTarget = null;

        try { slot.Pad.Disconnect(); } catch { }

        slot.WorkerCts?.Cancel();
        slot.WorkerCts?.Dispose();
        slot.WorkerCts = null;

        if (slot.PlayerIndex > 0) _freeSlots.Add(slot.PlayerIndex);

        _slots.Remove(guid);
        _sink.OnDeviceRemoved(guid);
        _sink.OnLog(Localization.Instance.LogDeviceReleased(slot.Device.InstanceName));
    }

    private async Task ShutdownAsync()
    {
        foreach (var slot in _slots.Values)
        {
            slot.RumbleTarget = null;
            slot.WorkerCts?.Cancel();
        }

        // Esperar a los workers ANTES de disponer el DirectInput: los Joystick lo referencian.
        var workers = _slots.Values.Select(s => s.Worker).Where(t => t is not null).Cast<Task>().ToArray();
        if (workers.Length > 0)
        {
            try { await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { }
        }

        foreach (var (guid, slot) in _slots.ToList())
        {
            try { slot.Pad.Disconnect(); } catch { }
            slot.WorkerCts?.Dispose();
            _sink.OnDeviceRemoved(guid);
        }

        _slots.Clear();
        _freeSlots.Clear();
        foreach (var i in Enumerable.Range(1, MaxXInputSlots)) _freeSlots.Add(i);

        _vigem?.Dispose();
        _vigem = null;
        _directInput?.Dispose();
        _directInput = null;
    }

    public void Dispose()
    {
        _vigem?.Dispose();
        _directInput?.Dispose();
    }
}
```

- [ ] **Step 2: Cerrar el pendiente de la Tarea 6**

En `Localization.cs`, cambiar el literal `15` de `LogDeviceLost` por `Services.ControllerSupervisor.VirtualPadGraceSeconds` y borrar el comentario `// TODO Tarea 8`.

- [ ] **Step 3: [WINDOWS] Verificar que compila**

```bash
dotnet build 8bitdofixer.sln
```

Expected: build limpio. Los tests siguen en verde pero el supervisor todavía no tiene quién lo arranque — eso es la Tarea 9.

Si `pad.FeedbackReceived += (_, args) => ...` no compila por inferencia del delegate, agregar el tipo explícito que indique el error del compilador; no pude verificar la firma exacta de Nefarius.ViGEm.Client 1.x offline.

- [ ] **Step 4: Commit**

```bash
git add Services/ControllerSupervisor.cs Localization.cs
git commit -m "feat: add supervisor with infinite retry and pad grace window

Enumerates DirectInput every 2s, diffs against live workers by
InstanceGuid, and spawns one worker per claimed device against a shared
ViGEmClient.

Virtual pads outlive their physical device by 15s and XInput slots are
only returned to the free list when a pad is really released, so a pad
that drops and comes back keeps its player number instead of
reshuffling mid-session.

A failure to create the ViGEmClient is treated as a missing ViGEmBus and
retried every 60s rather than being fatal, so installing the driver
while the app runs recovers it without a restart. A failing tick logs
and lets the next tick retry; one bad device cannot kill the service."
```

---

## Task 9: `ControllerService`

**Files:**
- Create: `Services/ControllerService.cs`
- Create: `tests/8bitdofixer.Tests/ControllerServiceStateTests.cs`

**Interfaces:**
- Consumes: `ControllerSupervisor`, `IControllerSink`, `ControllerEntry`, `BatteryEntry`, `ServiceState`
- Produces:
  - `ControllerService.Instance` → singleton
  - `ControllerService.Controllers` → `ObservableCollection<ControllerEntry>`
  - `ControllerService.Batteries` → `ObservableCollection<BatteryEntry>`
  - `ControllerService.State` → `ServiceState`
  - `ControllerService.IsRunning` → `bool`
  - `ControllerService.StateChanged` → `event EventHandler`
  - `ControllerService.LogWritten` → `event Action<string>`
  - `ControllerService.Start(IntPtr hwnd)` / `Stop()`
  - `ControllerService.ComputeState(bool running, bool driverMissing, bool anyMapped)` → `static ServiceState` (pura, testeable)

**El trabajo real de esta clase es el marshalling.** El supervisor corre en un `Task` de background y toca `ObservableCollection`, que WPF sólo tolera desde el hilo de UI. Cada método de `IControllerSink` se despacha con `Dispatcher.Invoke`. Sin eso, la primera detección de un mando tira `NotSupportedException` en el binding.

**Nota de orden:** `Start()` resuelve `Application.Current.Dispatcher`, así que sólo puede llamarse después de que la `Application` existe. En esta fase eso siempre se cumple: lo llama `MainWindow`.

- [ ] **Step 1: Escribir el test de la máquina de estados**

`tests/8bitdofixer.Tests/ControllerServiceStateTests.cs`:

```csharp
using BitDoFixer.Models;
using BitDoFixer.Services;
using Xunit;

namespace BitDoFixer.Tests;

public class ControllerServiceStateTests
{
    [Fact]
    public void StoppedWinsOverEverything()
    {
        // Detenido es detenido, incluso si el driver faltaba cuando paró.
        Assert.Equal(ServiceState.Stopped, ControllerService.ComputeState(running: false, driverMissing: true, anyMapped: true));
        Assert.Equal(ServiceState.Stopped, ControllerService.ComputeState(running: false, driverMissing: false, anyMapped: false));
    }

    [Fact]
    public void DriverMissingWinsOverSearching()
    {
        Assert.Equal(ServiceState.DriverMissing, ControllerService.ComputeState(running: true, driverMissing: true, anyMapped: false));
    }

    [Fact]
    public void MappedWhenAtLeastOneControllerIsMapped()
    {
        Assert.Equal(ServiceState.Mapped, ControllerService.ComputeState(running: true, driverMissing: false, anyMapped: true));
    }

    [Fact]
    public void SearchingIsTheRestingStateNotAnError()
    {
        // Corriendo con cero mandos es normal, no un fallo.
        Assert.Equal(ServiceState.Searching, ControllerService.ComputeState(running: true, driverMissing: false, anyMapped: false));
    }

    [Fact]
    public void SingletonIsStable()
    {
        Assert.Same(ControllerService.Instance, ControllerService.Instance);
    }

    [Fact]
    public void StartsStoppedAndEmpty()
    {
        var service = ControllerService.Instance;
        Assert.Equal(ServiceState.Stopped, service.State);
        Assert.False(service.IsRunning);
        Assert.Empty(service.Controllers);
    }
}
```

- [ ] **Step 2: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~ControllerServiceStateTests
```

Expected: FAIL en compilación — `CS0246` por `ControllerService`.

- [ ] **Step 3: Escribir el servicio**

`Services/ControllerService.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using BitDoFixer.Models;

namespace BitDoFixer.Services;

/// <summary>
/// Dueño del ciclo de vida del servicio, independiente de cualquier ventana.
/// Implementa IControllerSink y marshalea cada callback del supervisor al hilo de UI:
/// el supervisor corre en background y ObservableCollection sólo tolera mutaciones
/// desde el Dispatcher.
/// </summary>
public sealed class ControllerService : IControllerSink
{
    public static ControllerService Instance { get; } = new();

    private ControllerService() { }

    public ObservableCollection<ControllerEntry> Controllers { get; } = new();
    public ObservableCollection<BatteryEntry> Batteries { get; } = new();

    public ServiceState State { get; private set; } = ServiceState.Stopped;
    public event EventHandler? StateChanged;
    public event Action<string>? LogWritten;

    public bool IsRunning => _cts is not null;

    private CancellationTokenSource? _cts;
    private ControllerSupervisor? _supervisor;
    private Dispatcher? _dispatcher;
    private bool _driverMissing;

    /// <summary>
    /// Decide el estado agregado. Pura y estática a propósito: es la única lógica
    /// de esta clase que se puede testear sin un Dispatcher.
    /// </summary>
    public static ServiceState ComputeState(bool running, bool driverMissing, bool anyMapped)
    {
        if (!running) return ServiceState.Stopped;
        if (driverMissing) return ServiceState.DriverMissing;
        return anyMapped ? ServiceState.Mapped : ServiceState.Searching;
    }

    public void Start(IntPtr hwnd)
    {
        if (IsRunning) return;

        _dispatcher = Application.Current.Dispatcher;
        _cts = new CancellationTokenSource();
        _driverMissing = false;
        _supervisor = new ControllerSupervisor(this);

        var token = _cts.Token;
        var supervisor = _supervisor;

        _ = Task.Run(() => supervisor.RunAsync(hwnd, token), CancellationToken.None);

        _ = Task.Run(() => BatteryService.RunAsync(
            initialDelaySeconds: 3,
            intervalSeconds: 300,
            token,
            log: msg => OnLog($"[BATTERY] {msg}"),
            batteryCallback: UpsertBattery), CancellationToken.None);

        RecomputeState();
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts!.Cancel();
        _cts.Dispose();
        _cts = null;
        _supervisor = null;

        Marshal(() =>
        {
            Controllers.Clear();
            Batteries.Clear();
        });

        _driverMissing = false;
        RecomputeState();
    }

    // --- IControllerSink. Todo entra desde un hilo de background. ---

    public void OnLog(string message) => Marshal(() => LogWritten?.Invoke(message));

    public void OnDeviceFound(Guid instanceGuid, string name, int playerIndex) => Marshal(() =>
    {
        if (Find(instanceGuid) is not null) return;
        Controllers.Add(new ControllerEntry(instanceGuid, name, playerIndex));
        RecomputeStateOnUiThread();
    });

    public void OnDeviceMapped(Guid instanceGuid, bool rumbleSupported) => Marshal(() =>
    {
        var entry = Find(instanceGuid);
        if (entry is null) return;
        entry.RumbleSupported = rumbleSupported;
        entry.State = ControllerState.Mapped;
        LogWritten?.Invoke(Localization.Instance.LogDeviceMapped(entry.Name));
        RecomputeStateOnUiThread();
    });

    public void OnDeviceLost(Guid instanceGuid) => Marshal(() =>
    {
        var entry = Find(instanceGuid);
        if (entry is null) return;
        entry.State = ControllerState.Lost;
        RecomputeStateOnUiThread();
    });

    public void OnDeviceRemoved(Guid instanceGuid) => Marshal(() =>
    {
        var entry = Find(instanceGuid);
        if (entry is null) return;
        Controllers.Remove(entry);
        RecomputeStateOnUiThread();
    });

    public void OnDriverMissing(bool missing, string? detail) => Marshal(() =>
    {
        _driverMissing = missing;
        RecomputeStateOnUiThread();
    });

    private void UpsertBattery(string bleDeviceId, string name, int level) => Marshal(() =>
    {
        var existing = Batteries.FirstOrDefault(b => b.BleDeviceId == bleDeviceId);
        if (existing is null)
        {
            Batteries.Add(new BatteryEntry(bleDeviceId, name) { Level = level });
            return;
        }

        existing.Name = name;
        existing.Level = level;
    });

    private ControllerEntry? Find(Guid instanceGuid)
        => Controllers.FirstOrDefault(c => c.InstanceGuid == instanceGuid);

    private void RecomputeState() => Marshal(RecomputeStateOnUiThread);

    private void RecomputeStateOnUiThread()
    {
        var next = ComputeState(
            running: IsRunning,
            driverMissing: _driverMissing,
            anyMapped: Controllers.Any(c => c.State == ControllerState.Mapped));

        if (next == State) return;
        State = next;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Corre la acción en el hilo de UI. Si no hay Dispatcher todavía (tests), corre
    /// inline: así los tests de estado no necesitan una Application viva.
    /// </summary>
    private void Marshal(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
```

- [ ] **Step 4: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~ControllerServiceStateTests
```

Expected: PASS, 6 tests. El build va a fallar hasta la Tarea 10 por la referencia a `BatteryService` — hacer la Tarea 10 y volver a este paso.

- [ ] **Step 5: Commit** (después de la Tarea 10, cuando compile)

```bash
git add Services/ControllerService.cs tests/8bitdofixer.Tests/ControllerServiceStateTests.cs
git commit -m "feat: add ControllerService owning service lifetime

State lives in the service, not in the window, so the service can run
without a visible window and any consumer can drive it.

Every IControllerSink callback is marshalled to the Dispatcher: the
supervisor runs on a background task and ObservableCollection only
tolerates mutation from the UI thread. Without it the first device
detection throws NotSupportedException inside the binding.

ComputeState is static and pure so the aggregate state machine is
testable without a Dispatcher."
```

---

## Task 10: `BatteryService`

**Files:**
- Create: `Services/BatteryService.cs`
- Delete: `BluetoothBatteryMonitor.cs`

**Interfaces:**
- Consumes: nada de las tareas previas
- Produces: `BatteryService.RunAsync(int initialDelaySeconds, int intervalSeconds, CancellationToken, Action<string> log, Action<string,string,int> batteryCallback)` → `Task`, donde el callback recibe `(bleDeviceId, name, level)`

**Dos cambios respecto de `BluetoothBatteryMonitor`:**

1. **Keyea por `devInfo.Id`, no por `service.Device.Name`.** Hoy el callback es `Action<string,int>` con el nombre, y dos Ultimate 2C idénticos comparten nombre: el segundo sobreescribe al primero.
2. **Los errores por dispositivo se loguean una vez, no en cada poll.** Con poll cada 300 s y la app corriendo todo el día, el `catch` silencioso actual (`BluetoothBatteryMonitor.cs:86-88`) se convierte, cuando empieza a loguear, en 288 líneas por día por mando.

- [ ] **Step 1: Escribir `BatteryService`**

`Services/BatteryService.cs`:

```csharp
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BitDoFixer.Services;

/// <summary>
/// Lee el nivel de batería por GATT de los dispositivos BLE 8BitDo.
///
/// Deliberadamente NO intenta atar cada batería a un mando mapeado: no hay join
/// confiable entre un InstanceGuid de DirectInput y un device id de BLE (spec §7.1).
/// Reporta una lista propia, keyeada por device id.
/// </summary>
internal static class BatteryService
{
    private static readonly Guid BatteryServiceUuid = GattServiceUuids.Battery;
    private static readonly Guid BatteryLevelUuid = GattCharacteristicUuids.BatteryLevel;

    public static async Task RunAsync(
        int initialDelaySeconds,
        int intervalSeconds,
        CancellationToken token,
        Action<string> log,
        Action<string, string, int> batteryCallback)
    {
        log(Localization.Instance.LogBatteryStart(initialDelaySeconds, intervalSeconds));

        // Errores ya reportados, por device id: evita repetir el mismo fallo en cada poll.
        var reportedErrors = new HashSet<string>();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(initialDelaySeconds), token);

            await PollAsync(log, batteryCallback, reportedErrors, token);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
            while (await timer.WaitForNextTickAsync(token))
            {
                await PollAsync(log, batteryCallback, reportedErrors, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Esperado al detener
        }
        catch (Exception ex)
        {
            log(Localization.Instance.LogBatteryFatal(ex.Message));
        }
    }

    private static async Task PollAsync(
        Action<string> log,
        Action<string, string, int> batteryCallback,
        HashSet<string> reportedErrors,
        CancellationToken token)
    {
        try
        {
            string selector = GattDeviceService.GetDeviceSelectorFromUuid(BatteryServiceUuid);
            var devices = await DeviceInformation.FindAllAsync(selector);

            foreach (var devInfo in devices)
            {
                token.ThrowIfCancellationRequested();

                if (devInfo.Name is null ||
                    !devInfo.Name.Contains("8BitDo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var service = await GattDeviceService.FromIdAsync(devInfo.Id);
                    if (service?.Device is null) continue;

                    var characteristics = await service.GetCharacteristicsForUuidAsync(BatteryLevelUuid);
                    if (characteristics.Status != GattCommunicationStatus.Success ||
                        characteristics.Characteristics.Count == 0)
                    {
                        continue;
                    }

                    var result = await characteristics.Characteristics[0].ReadValueAsync();
                    if (result.Status != GattCommunicationStatus.Success) continue;

                    byte level = DataReader.FromBuffer(result.Value).ReadByte();
                    string name = service.Device.Name ?? devInfo.Name;

                    // La clave es el device id, no el nombre: dos Ultimate 2C idénticos
                    // comparten nombre y uno sobreescribiría al otro.
                    batteryCallback(devInfo.Id, name, level);
                    log(Localization.Instance.LogBatteryLevel(name, level));

                    reportedErrors.Remove(devInfo.Id);
                }
                catch (Exception ex)
                {
                    if (reportedErrors.Add(devInfo.Id))
                    {
                        log($"[{devInfo.Name}] {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (reportedErrors.Add("__scan__"))
            {
                log(Localization.Instance.LogBatteryScanError(ex.Message));
            }
        }
    }
}
```

- [ ] **Step 2: Borrar el monitor viejo**

```bash
git rm BluetoothBatteryMonitor.cs
grep -rn "BluetoothBatteryMonitor" --include=*.cs .
```

Expected del grep: sólo `MainWindow.xaml.cs`, que se arregla en la Tarea 11.

- [ ] **Step 3: [WINDOWS] Build y test**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
```

Expected: build con errores sólo en `MainWindow.xaml.cs`. Comentar ahí la llamada a `BluetoothBatteryMonitor.RunAsync` con `// TEMPORAL: Tarea 11`, rebuildear, y entonces todos los tests (Tareas 2, 4, 5, 6, 9) en verde.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: key battery readings by BLE device id

The old monitor passed the device name to its callback, so two identical
Ultimate 2C pads reported the same key and the second overwrote the
first in the single battery card.

Per-device errors are now logged once instead of on every poll: at a
300s interval with the app running all day, the silent catch it replaces
would otherwise become 288 lines per day per pad."
```

---

## Task 11: Reconectar la UI

**Files:**
- Modify: `MainWindow.xaml:52-92` (las dos tarjetas del dashboard)
- Modify: `MainWindow.xaml.cs` (reescritura del code-behind)

**Interfaces:**
- Consumes: `ControllerService.Instance`, `ControllerEntry`, `BatteryEntry`, `ServiceState`
- Produces: la app funcionando de nuevo, con multi-mando y reconexión

**Requisito previo:** los tipos bindeados tienen que ser `public` (Tarea 6). Un binding
a un tipo `internal` no falla con error: falla mostrando la celda vacía, que es mucho
peor de diagnosticar.

**Dos decisiones de binding:**

1. **`DataContext` en code-behind, no `x:Static` en XAML.** `ControllerService` es `internal`; apuntarle desde XAML con `x:Static` mete fricción innecesaria con el loader de BAML. Asignar `DataContext = ControllerService.Instance` en el constructor y bindear con rutas relativas es equivalente y sin riesgo.
2. **El texto de estado se actualiza desde code-behind, no por binding.** `ControllerService` no implementa `INotifyPropertyChanged`, así que un binding a `State` o `IsRunning` nunca refrescaría. El evento `StateChanged` maneja ese texto; el resto de la UI sí se bindea, porque `ObservableCollection` y `ControllerEntry` sí notifican.

- [ ] **Step 1: Reemplazar las dos tarjetas en `MainWindow.xaml`**

Sustituir todo el bloque `<UniformGrid Grid.Row="1" ...>` … `</UniformGrid>` por:

```xml
        <!-- Status Dashboard -->
        <UniformGrid Grid.Row="1" Columns="2" Margin="20,20,20,10" MaxHeight="260">
            <!-- Card 1: Controllers -->
            <materialDesign:Card Margin="0,0,10,0" Padding="25" UniformCornerRadius="8" Background="{StaticResource CardBgBrush}" materialDesign:ElevationAssist.Elevation="Dp2">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <StackPanel Orientation="Horizontal" Margin="0,0,0,15">
                        <materialDesign:PackIcon x:Name="IconConnectionStatus" Kind="BluetoothConnect" Width="24" Height="24" Foreground="{StaticResource AccentTealBrush}" Margin="0,0,8,0"/>
                        <TextBlock Text="{Binding ControllersTitle, Source={x:Static local:Localization.Instance}}" FontSize="14" FontWeight="Bold" Foreground="{StaticResource AccentTealBrush}" VerticalAlignment="Center"/>
                    </StackPanel>

                    <Grid Grid.Row="1">
                        <!-- Estado vacío: el texto lo escribe el code-behind desde StateChanged -->
                        <TextBlock x:Name="TxtServiceState" FontSize="16" Opacity="0.72" VerticalAlignment="Top" TextWrapping="Wrap">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Controllers.Count}" Value="0">
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>

                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <ItemsControl ItemsSource="{Binding Controllers}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Grid Margin="0,0,0,12">
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="Auto"/>
                                            </Grid.ColumnDefinitions>

                                            <StackPanel>
                                                <TextBlock Text="{Binding Name}" FontSize="16" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                                <StackPanel Orientation="Horizontal" Margin="0,3,0,0">
                                                    <TextBlock Text="{Binding StatusText}" FontSize="13" Foreground="{StaticResource AccentTealBrush}"/>
                                                    <TextBlock Text=" • " FontSize="13" Opacity="0.5"/>
                                                    <TextBlock Text="{Binding RumbleText}" FontSize="13" Opacity="0.72"/>
                                                </StackPanel>
                                            </StackPanel>

                                            <Border Grid.Column="1" Background="{StaticResource AccentTealDarkBrush}" CornerRadius="4" Padding="8,4" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding PlayerLabel}" FontSize="12" FontWeight="Bold" Foreground="{StaticResource HeaderTextBrush}"/>
                                            </Border>
                                        </Grid>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </ScrollViewer>
                    </Grid>
                </Grid>
            </materialDesign:Card>

            <!-- Card 2: Batteries -->
            <materialDesign:Card Margin="10,0,0,0" Padding="25" UniformCornerRadius="8" Background="{StaticResource CardBgBrush}" materialDesign:ElevationAssist.Elevation="Dp2">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>

                    <StackPanel Orientation="Horizontal" Margin="0,0,0,15">
                        <materialDesign:PackIcon Kind="BatteryMedium" Width="24" Height="24" Foreground="{StaticResource AccentAmberBrush}" Margin="0,0,8,0"/>
                        <TextBlock Text="{Binding BatteryLevelTitle, Source={x:Static local:Localization.Instance}}" FontSize="14" FontWeight="Bold" Foreground="{StaticResource AccentAmberBrush}" VerticalAlignment="Center"/>
                    </StackPanel>

                    <Grid Grid.Row="1">
                        <TextBlock Text="{Binding NoDevice, Source={x:Static local:Localization.Instance}}" FontSize="16" Opacity="0.72" VerticalAlignment="Top">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding Batteries.Count}" Value="0">
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>

                        <ScrollViewer VerticalScrollBarVisibility="Auto">
                            <ItemsControl ItemsSource="{Binding Batteries}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Margin="0,0,0,14">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*"/>
                                                    <ColumnDefinition Width="Auto"/>
                                                </Grid.ColumnDefinitions>
                                                <TextBlock Text="{Binding Name}" FontSize="14" TextTrimming="CharacterEllipsis"/>
                                                <TextBlock Grid.Column="1" Text="{Binding LevelText}" FontSize="18" FontWeight="SemiBold" Foreground="{StaticResource AccentAmberBrush}"/>
                                            </Grid>
                                            <ProgressBar Value="{Binding Level, Mode=OneWay}" Maximum="100" Height="6" Margin="0,5,0,0"
                                                         Foreground="{StaticResource AccentAmberBrush}"/>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </ScrollViewer>
                    </Grid>
                </Grid>
            </materialDesign:Card>
        </UniformGrid>
```

Los nombres `TxtRemapperStatus`, `TxtDeviceInfo`, `BatteryProgress`, `TxtBatteryLevel`, `TxtBatteryDevice` e `IconBatteryBolt` desaparecen: no hay más campos escalares. `IconConnectionStatus` sobrevive porque el code-behind le cambia el color según el estado.

- [ ] **Step 2: Reescribir `MainWindow.xaml.cs`**

Reemplazar el archivo completo por:

```csharp
using System.Windows;
using System.Windows.Media;
using BitDoFixer.Models;
using BitDoFixer.Services;

namespace BitDoFixer
{
    public partial class MainWindow : Window
    {
        private const int MaxLogLines = 500;

        private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly Brush ConnectedBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly Brush ScanningBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

        private readonly ControllerService _service = ControllerService.Instance;
        private readonly List<string> _logLines = new();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _service;
            _service.StateChanged += (_, _) => ApplyServiceState();
            _service.LogWritten += Log;

            Log(Localization.Instance.LogAppInit);
            ApplyServiceState();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // EnsureHandle en lugar de Handle: el acquire de DirectInput necesita un HWND
            // válido, y en la fase de bandeja la ventana puede no haberse mostrado nunca.
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            Log(Localization.Instance.LogServicesStarting);
            _service.Start(hwnd);
            UpdateUiState();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            Log(Localization.Instance.LogServicesStopping);
            _service.Stop();
            UpdateUiState();
        }

        private void BtnLang_Click(object sender, RoutedEventArgs e)
        {
            var loc = Localization.Instance;
            loc.IsEnglish = !loc.IsEnglish;
            BtnLang.Content = loc.IsEnglish ? "TR" : "EN";

            // Las entries cachean texto derivado ya localizado, así que hay que
            // pedirles que lo recalculen: Localization notifica sus propias
            // propiedades, no las de las entries.
            foreach (var entry in _service.Controllers) entry.RefreshLocalizedText();
            ApplyServiceState();
        }

        private void ApplyServiceState()
        {
            var loc = Localization.Instance;

            switch (_service.State)
            {
                case ServiceState.Mapped:
                    TxtServiceState.Text = string.Empty;
                    IconConnectionStatus.Foreground = ConnectedBrush;
                    break;
                case ServiceState.Searching:
                    TxtServiceState.Text = loc.SearchingControllers;
                    IconConnectionStatus.Foreground = ScanningBrush;
                    break;
                case ServiceState.DriverMissing:
                    TxtServiceState.Text = $"{loc.DriverMissingTitle}\n{loc.DriverMissingHint}";
                    IconConnectionStatus.Foreground = ErrorBrush;
                    break;
                default:
                    TxtServiceState.Text = loc.ServiceStopped;
                    IconConnectionStatus.Foreground = IdleBrush;
                    break;
            }

            UpdateUiState();
        }

        private void UpdateUiState()
        {
            BtnStart.IsEnabled = !_service.IsRunning;
            BtnStop.IsEnabled = _service.IsRunning;
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _logLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

                // Cap circular: con la app corriendo todo el día, sin techo esto crece
                // sin límite y se come la memoria del proceso.
                if (_logLines.Count > MaxLogLines)
                {
                    _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
                }

                TxtLogs.Text = string.Join(Environment.NewLine, _logLines);
                LogScroller.ScrollToBottom();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _service.Stop();
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}
```

`OnClosed` sigue matando la app: la bandeja es la Fase 2, y hasta entonces cerrar la ventana debe seguir cerrando la app.

- [ ] **Step 3: [WINDOWS] Build y test**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
```

Expected: build limpio, todos los tests en verde.

- [ ] **Step 4: [WINDOWS] Checklist manual — el checkpoint de la fase**

```bash
dotnet run --project 8bitdofixer.csproj
```

Marcar cada uno:

- [ ] **Arranque sin mandos.** START con ningún mando prendido. Esperado: "Searching for 8BitDo controllers…", icono ámbar, **sin errores en el log**. Antes esto reportaba "Not Found" y se rendía.
- [ ] **Conexión en caliente.** Prender un mando con el servicio ya corriendo. Esperado: aparece en la lista en ≤2 s, pasa a "Mapped", badge "Player 1".
- [ ] **El mapeo sigue bien.** Probar los dos sticks, D-pad (incluidas diagonales), los 4 botones de cara, bumpers, gatillos, Back/Start y clics de stick, con `joy.cpl` o el probador de mando de Steam.
- [ ] **Desconexión.** Apagar el mando. Esperado: pasa a "Lost" en ≤2 s y el log dice que retiene el pad virtual 15 s.
- [ ] **Reconexión dentro de la gracia.** Volver a prenderlo antes de los 15 s. Esperado: vuelve a "Mapped" **con el mismo número de jugador**.
- [ ] **Reconexión pasada la gracia.** Apagarlo, esperar >20 s, prenderlo. Esperado: el log dice que liberó el pad, y al volver reaparece (puede tomar otro slot: es el comportamiento correcto).
- [ ] **Dos mandos a la vez.** Prender los dos. Esperado: dos filas, Player 1 y Player 2, y los dos responden **de forma independiente** — verificar que mover el stick de uno no mueve el del otro.
- [ ] **CPU con dos mandos.** Administrador de tareas → CPU del proceso con dos mandos mapeados. Anotar el número. Si supera ~5% en una máquina de escritorio, subir `ControllerWorker.PollIntervalMs` de 5 a 10 y volver a medir (el spec §14 anticipa que 5 ms probablemente no rinda 200 Hz reales de todos modos).
- [ ] **Sin loop de pads virtuales.** Con el servicio corriendo un rato, abrir `joy.cpl`. Esperado: **exactamente** un "Xbox 360 Controller for Windows" por mando físico, y que ese número no crezca. Si crece, el rechazo de `DeviceFilter` no está funcionando contra el hardware real.
- [ ] **STOP limpia todo.** Pulsar STOP. Esperado: las listas se vacían, los pads virtuales desaparecen de `joy.cpl`, y START vuelve a funcionar.
- [ ] **Cambio de idioma en caliente.** Con mandos mapeados, pulsar TR. Esperado: los estados y los badges de jugador cambian de idioma sin reiniciar.

- [ ] **Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: show a list of controllers and batteries

Replaces the single connection card and single battery gauge with
ItemsControls over the service's observable collections: with two pads
the old UI had one card that the second device overwrote.

The service state text is driven from code-behind via StateChanged
because ControllerService does not implement INotifyPropertyChanged, so
a binding to State would never refresh. Entries do notify, so the lists
themselves bind normally.

The log gets a 500-line circular cap: once the app runs all day, an
uncapped TextBox grows without limit."
```

- [ ] **Step 6: Push**

```bash
git push origin feature/tray-autostart-multipad
```

---

## Cierre de la fase

Al terminar la Tarea 11 la app funciona con multi-mando y reconexión automática, y todavía se arranca a mano. Es un punto de parada válido: se puede usar así por un tiempo antes de encarar la bandeja.

**Lo que queda pendiente y va en el plan siguiente** (`2026-09-02-tray-autostart.md`):

- `crash.log` sigue escribiéndose a ruta relativa en `Program.cs:24` y `App.xaml.cs:28` — **sigue roto para el arranque desde el registro**, pero en esta fase no importa porque la app siempre se lanza a mano.
- `OnClosed` sigue llamando a `Application.Current.Shutdown()`.
- `ShutdownMode` sigue en el default `OnLastWindowClose`.
- No hay persistencia: el idioma se resetea en cada arranque.

**Riesgos abiertos que sólo se cierran con hardware:**

1. Las constantes de `DeviceFilter` dependen de la medición de la Tarea 3. Si el Ultimate 2C en Bluetooth no reporta el VID de 8BitDo ni un nombre reconocible, hay que ajustar el filtro con el PID medido.
2. La firma de `pad.FeedbackReceived` y el nombre del tipo de excepción de ViGEm no se pudieron verificar offline. Las Tareas 8 y 9 los evitan a propósito (forward por target mutable, catch genérico), pero si el compilador pide un tipo explícito, hay que agregarlo.
3. Si `dotnet test` no resuelve las versiones de xunit contra el SDK de .NET 10, la Tarea 2 Step 2 tiene el procedimiento para obtener las que el SDK genera.
