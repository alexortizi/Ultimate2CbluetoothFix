# Bandeja del sistema y autoarranque — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que la app arranque con Windows sin ventana, directo a la bandeja del sistema, con los servicios ya activos.

**Architecture:** Un `TrayIconHost` y `MainWindow` pasan a ser dos consumidores del `ControllerService` que ya existe; la ventana se oculta en vez de cerrarse y `ShutdownMode` pasa a `OnExplicitShutdown`, así el proceso sobrevive sin ventana visible. Un `SettingsStore` en `%APPDATA%` persiste dos switches, y `StartupRegistration` registra el `.exe` en la clave `Run` de HKCU con el argumento `--minimized`. Una guardia de instancia única evita que el arranque automático y un doble click convivan peleando por el mismo mando.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`), WPF, H.NotifyIcon.Wpf (nuevo), `System.Text.Json` con contexto source-generated, `Microsoft.Win32.Registry`.

**Spec:** `docs/superpowers/specs/2026-09-02-tray-autostart-multipad-design.md`

**Plan previo, requerido:** `docs/superpowers/plans/2026-09-02-multipad-supervisor.md`. Este plan asume que ya existen `ControllerService`, `ControllerSupervisor` y la UI de listas. No empezar antes de terminar ese.

## Global Constraints

- **El entorno de desarrollo es macOS y este proyecto NO compila ahí.** Todo paso marcado **[WINDOWS]** lo ejecuta el usuario en su máquina Windows y pega la salida.
- Target framework: `net10.0-windows10.0.19041.0` en app y tests.
- `RootNamespace` es `BitDoFixer`. Archivos nuevos en subcarpetas usan sub-namespace (`BitDoFixer.Infrastructure`, `BitDoFixer.Settings`, `BitDoFixer.Tray`).
- Todo string visible al usuario pasa por `Localization.cs`, en inglés y turco. Las traducciones al turco quedan marcadas para revisión.
- **Los tipos que aparecen en un `Binding` del XAML tienen que ser `public`.** WPF resuelve bindings por reflexión y contra un tipo `internal` falla en silencio: muestra la celda vacía, sin error.
- **La ruta del `.exe` sale de `Environment.ProcessPath`, nunca de `Assembly.Location`.** Con `PublishSingleFile=true`, `Assembly.Location` devuelve string vacío en runtime.
- Carpeta de datos: `%APPDATA%\8BitDoFixer\` (`settings.json`, `crash.log`).
- Clave de registro: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `8BitDoFixer`.
- Argumento de arranque silencioso: `--minimized`.
- Nombres de sincronización: `Local\8BitDoFixer.SingleInstance` y `Local\8BitDoFixer.ShowWindow`. Prefijo `Local\`, no `Global\`.
- Commits frecuentes, uno por tarea como mínimo. Rama `feature/tray-autostart-multipad`.

---

## Estructura de archivos

| Archivo | Responsabilidad | Tarea |
|---|---|---|
| `Infrastructure/AppPaths.cs` | Dónde vive `%APPDATA%\8BitDoFixer\` | 1 |
| `Infrastructure/CrashLogger.cs` | Log de crashes a ruta absoluta, sin `MessageBox` | 1 |
| `Settings/AppSettings.cs` | El record de configuración + defaults | 2 |
| `Settings/SettingsStore.cs` | Load/save JSON atómico, tolerante a corrupción | 2 |
| `Settings/StartupRegistration.cs` | Clave `Run` + lectura de `StartupApproved` | 3 |
| `Infrastructure/SingleInstanceGuard.cs` | Mutex + evento "mostrá la ventana" | 4 |
| `Tray/TrayIconHost.cs` | `TaskbarIcon`, menú, tooltip, balloons | 5 |
| `App.xaml` / `App.xaml.cs` | Orquestación del arranque, `ShutdownMode` | 5 |
| `Program.cs` | Parseo de args, instancia única | 5 |
| `MainWindow.xaml` / `.cs` | Ocultar en X, los dos switches | 5, 6 |
| `README.md`, `8bitdofixer.csproj`, `Localization.cs` | Docs, versión 0.2.0, footer | 7 |

---

## Task 1: `AppPaths` y `CrashLogger`

**Files:**
- Create: `Infrastructure/AppPaths.cs`
- Create: `Infrastructure/CrashLogger.cs`
- Create: `tests/8bitdofixer.Tests/AppPathsTests.cs`
- Modify: `Program.cs` (usar `CrashLogger`)
- Modify: `App.xaml.cs` (usar `CrashLogger`, sacar el `MessageBox`)

**Interfaces:**
- Consumes: nada
- Produces:
  - `BitDoFixer.Infrastructure.AppPaths.Root` → `string`
  - `AppPaths.SettingsFile` → `string`
  - `AppPaths.CrashLogFile` → `string`
  - `AppPaths.EnsureRoot()` → `void`
  - `BitDoFixer.Infrastructure.CrashLogger.Install()` → `void`
  - `CrashLogger.SetNotifier(Action<string,string>)` → `void`
  - `CrashLogger.Log(Exception?)` → `void`

**El bug que arregla, y por qué es la primera tarea:** `Program.cs:24` y `App.xaml.cs:28` hacen `File.AppendAllText("crash.log", …)` con **ruta relativa**. Lanzado desde la clave `Run` del registro, el directorio de trabajo es `C:\Windows\System32`, así que el handler de crash tira `UnauthorizedAccessException` y **se come el crash original**. Va primero porque a partir de la Tarea 5 la app se lanza desde el registro, y si algo falla ahí sin este arreglo no hay diagnóstico posible.

Y el `MessageBox` del handler es igual de dañino en ese contexto: un modal en un arranque de logon, sin ventana visible, cuelga el proceso sin que se vea nada.

- [ ] **Step 1: Escribir el test que falla**

`tests/8bitdofixer.Tests/AppPathsTests.cs`:

```csharp
using BitDoFixer.Infrastructure;
using Xunit;

namespace BitDoFixer.Tests;

public class AppPathsTests
{
    [Fact]
    public void RootLivesUnderRoamingAppData()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(appData, AppPaths.Root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootIsNamedAfterTheApp()
    {
        Assert.Equal("8BitDoFixer", Path.GetFileName(AppPaths.Root));
    }

    [Fact]
    public void PathsAreAbsolute()
    {
        // El punto de toda la clase: una ruta relativa se resuelve contra
        // C:\Windows\System32 cuando el arranque viene de la clave Run.
        Assert.True(Path.IsPathFullyQualified(AppPaths.Root));
        Assert.True(Path.IsPathFullyQualified(AppPaths.SettingsFile));
        Assert.True(Path.IsPathFullyQualified(AppPaths.CrashLogFile));
    }

    [Fact]
    public void FilesLiveInsideRoot()
    {
        Assert.Equal(AppPaths.Root, Path.GetDirectoryName(AppPaths.SettingsFile));
        Assert.Equal(AppPaths.Root, Path.GetDirectoryName(AppPaths.CrashLogFile));
    }

    [Fact]
    public void FileNamesAreStable()
    {
        Assert.Equal("settings.json", Path.GetFileName(AppPaths.SettingsFile));
        Assert.Equal("crash.log", Path.GetFileName(AppPaths.CrashLogFile));
    }

    [Fact]
    public void EnsureRootIsIdempotent()
    {
        AppPaths.EnsureRoot();
        AppPaths.EnsureRoot();
        Assert.True(Directory.Exists(AppPaths.Root));
    }
}
```

- [ ] **Step 2: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~AppPathsTests
```

Expected: FAIL en compilación — `CS0246: AppPaths`.

- [ ] **Step 3: Escribir `AppPaths`**

`Infrastructure/AppPaths.cs`:

```csharp
namespace BitDoFixer.Infrastructure;

/// <summary>
/// Rutas absolutas de los datos de la app. Absolutas a propósito: lanzada desde la
/// clave Run del registro, el directorio de trabajo es C:\Windows\System32, donde
/// cualquier escritura relativa falla por permisos.
/// </summary>
internal static class AppPaths
{
    private const string FolderName = "8BitDoFixer";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        FolderName);

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string CrashLogFile => Path.Combine(Root, "crash.log");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
```

- [ ] **Step 4: Escribir `CrashLogger`**

`Infrastructure/CrashLogger.cs`:

```csharp
using System.Text;

namespace BitDoFixer.Infrastructure;

/// <summary>
/// Punto único de logging de crashes. Reemplaza la lógica duplicada de Program.cs
/// y App.xaml.cs, y deliberadamente NO muestra un MessageBox: en un arranque de
/// logon sin ventana visible, un modal cuelga el proceso sin que se vea nada.
/// El aviso al usuario se delega a un notificador (el balloon de la bandeja).
/// </summary>
internal static class CrashLogger
{
    private static Action<string, string>? _notify;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);
    }

    /// <summary>La bandeja se registra acá una vez creada (Tarea 5).</summary>
    public static void SetNotifier(Action<string, string> notify) => _notify = notify;

    public static void Log(Exception? ex)
    {
        if (ex is null) return;

        try
        {
            AppPaths.EnsureRoot();

            var log = new StringBuilder()
                .AppendLine($"CRASH [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]: {ex.Message}")
                .AppendLine(ex.StackTrace);

            var inner = ex.InnerException;
            int depth = 0;
            while (inner is not null && depth++ < 5)
            {
                log.AppendLine($"INNER: {inner.Message}").AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }

            log.AppendLine();
            File.AppendAllText(AppPaths.CrashLogFile, log.ToString());
        }
        catch
        {
            // Un logger de crashes que puede tirar excepciones no sirve para nada.
        }

        try
        {
            _notify?.Invoke("8BitDo Fixer", $"{ex.Message}\n{AppPaths.CrashLogFile}");
        }
        catch { }
    }
}
```

- [ ] **Step 5: Sacar el logging duplicado de `Program.cs`**

Reemplazar `Program.cs` completo por:

```csharp
using BitDoFixer.Infrastructure;

namespace BitDoFixer
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            CrashLogger.Install();

            try
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex);
            }
        }
    }
}
```

- [ ] **Step 6: Sacar el `MessageBox` de `App.xaml.cs`**

Reemplazar `App.xaml.cs` completo por:

```csharp
using System.Windows;
using BitDoFixer.Infrastructure;

namespace BitDoFixer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                CrashLogger.Log(args.Exception);
                args.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}
```

El handler de `AppDomain.CurrentDomain.UnhandledException` se fue a `CrashLogger.Install()`, llamado desde `Program.Main` antes de que exista la `Application` — que es antes de lo que estaba, no después.

- [ ] **Step 7: [WINDOWS] Build, test y verificación del archivo**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~AppPathsTests
dir "%APPDATA%\8BitDoFixer"
```

Expected: 6 tests en verde y la carpeta creada.

- [ ] **Step 8: [WINDOWS] Verificar que el crash log realmente se escribe**

Agregar temporalmente al final de `MainWindow`'s constructor `throw new InvalidOperationException("probe");`, correr la app, confirmar que aparece `%APPDATA%\8BitDoFixer\crash.log` con el stack, y **borrar la línea**.

Este paso existe porque el camino de crash es el único que no se puede testear unitariamente y es exactamente el que estaba roto.

- [ ] **Step 9: Commit**

```bash
git add Infrastructure/ tests/8bitdofixer.Tests/AppPathsTests.cs Program.cs App.xaml.cs
git commit -m "fix: write crash log to an absolute path without a modal

Both crash handlers wrote to the relative path 'crash.log'. Launched
from the registry Run key the working directory is C:\Windows\System32,
so the handler threw UnauthorizedAccessException and swallowed the
original crash — precisely when a crash log matters most.

The MessageBox is gone too: a modal during a logon start, with no
visible window, hangs the process silently. User-facing notification is
delegated to a notifier that the tray icon registers later."
```

---

## Task 2: `AppSettings` y `SettingsStore`

**Files:**
- Create: `Settings/AppSettings.cs`
- Create: `Settings/SettingsStore.cs`
- Create: `tests/8bitdofixer.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `AppPaths`
- Produces:
  - `BitDoFixer.Settings.AppSettings` → `sealed record` con `bool AutoStartServices`, `bool StartWithWindows`, `bool HasShownTrayHint`, `bool IsEnglish`, todos `{ get; init; }`
  - `AppSettings.Defaults(string uiLanguage)` → `static AppSettings`
  - `AppSettings.Defaults()` → `static AppSettings` (usa `CultureInfo.CurrentUICulture`)
  - `BitDoFixer.Settings.SettingsStore(string path)` → constructor
  - `SettingsStore.Load()` → `AppSettings`
  - `SettingsStore.Save(AppSettings)` → `void`

**Por qué el path es inyectable:** `SettingsStore` recibe la ruta en el constructor en vez de leer `AppPaths.SettingsFile` adentro. Sin eso los tests escribirían en el `%APPDATA%` real del usuario, y no se podría testear el caso de archivo corrupto.

**Escritura atómica:** `Save` escribe a `.tmp` y hace `File.Move(overwrite: true)`. Sin eso, un corte de energía o un kill durante la escritura deja un JSON truncado, y la app arranca sin configuración la próxima vez.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/8bitdofixer.Tests/SettingsStoreTests.cs`:

```csharp
using BitDoFixer.Settings;
using Xunit;

namespace BitDoFixer.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "8bdf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // --- Defaults ---

    [Fact]
    public void DefaultsAreConservative()
    {
        var defaults = AppSettings.Defaults("en");

        // Nada se activa solo: el usuario decide si la app arranca con Windows.
        Assert.False(defaults.AutoStartServices);
        Assert.False(defaults.StartWithWindows);
        Assert.False(defaults.HasShownTrayHint);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("es", true)]
    [InlineData("tr", false)]
    [InlineData("TR", false)]
    public void DefaultsDetectTurkish(string uiLanguage, bool expectedEnglish)
    {
        Assert.Equal(expectedEnglish, AppSettings.Defaults(uiLanguage).IsEnglish);
    }

    // --- Load ---

    [Fact]
    public void LoadReturnsDefaultsWhenTheFileIsMissing()
    {
        var store = new SettingsStore(_path);
        var settings = store.Load();

        Assert.False(settings.AutoStartServices);
        Assert.False(File.Exists(_path)); // Load no debe crear el archivo
    }

    [Fact]
    public void LoadReturnsDefaultsAndQuarantinesACorruptFile()
    {
        File.WriteAllText(_path, "{ this is not json");
        var store = new SettingsStore(_path);

        var settings = store.Load();

        Assert.False(settings.AutoStartServices);
        Assert.True(File.Exists(_path + ".bad"));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void LoadReturnsDefaultsWhenTheJsonIsValidButNull()
    {
        File.WriteAllText(_path, "null");
        var store = new SettingsStore(_path);
        Assert.False(store.Load().AutoStartServices);
    }

    // --- Roundtrip ---

    [Fact]
    public void SaveThenLoadPreservesEveryField()
    {
        var store = new SettingsStore(_path);
        var written = new AppSettings
        {
            AutoStartServices = true,
            StartWithWindows = true,
            HasShownTrayHint = true,
            IsEnglish = false
        };

        store.Save(written);
        var read = store.Load();

        Assert.Equal(written, read); // record: comparación por valor
    }

    [Fact]
    public void SaveLeavesNoTempFileBehind()
    {
        var store = new SettingsStore(_path);
        store.Save(AppSettings.Defaults("en"));

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void SaveOverwritesAnExistingFile()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings { AutoStartServices = true });
        store.Save(new AppSettings { AutoStartServices = false });

        Assert.False(store.Load().AutoStartServices);
    }

    [Fact]
    public void SaveCreatesTheDirectoryIfItIsMissing()
    {
        var nested = Path.Combine(_dir, "sub", "dir", "settings.json");
        new SettingsStore(nested).Save(AppSettings.Defaults("en"));
        Assert.True(File.Exists(nested));
    }
}
```

- [ ] **Step 2: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~SettingsStoreTests
```

Expected: FAIL en compilación — `CS0246` por `AppSettings` y `SettingsStore`.

- [ ] **Step 3: Escribir `AppSettings`**

`Settings/AppSettings.cs`:

```csharp
using System.Globalization;
using System.Text.Json.Serialization;

namespace BitDoFixer.Settings;

/// <summary>
/// Configuración persistida. Es un record para que la comparación por valor haga
/// trivial detectar "no cambió nada, no hace falta escribir".
/// public porque los switches de la UI se bindean contra esto.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Arrancar los servicios solo al iniciar la app.</summary>
    public bool AutoStartServices { get; init; }

    /// <summary>Registrar el .exe en la clave Run de HKCU.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Si ya se avisó una vez que la X esconde en la bandeja.</summary>
    public bool HasShownTrayHint { get; init; }

    public bool IsEnglish { get; init; } = true;

    /// <summary>
    /// Defaults conservadores: nada se auto-activa. El idioma se detecta, porque
    /// con la app reiniciándose en cada logon un idioma que no persiste molesta.
    /// </summary>
    public static AppSettings Defaults(string uiLanguage) => new()
    {
        IsEnglish = !uiLanguage.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
    };

    public static AppSettings Defaults()
        => Defaults(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
}

/// <summary>
/// Contexto source-generated: evita la serialización por reflexión, así que la
/// configuración sigue funcionando si algún día se activa PublishTrimmed.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: Escribir `SettingsStore`**

`Settings/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace BitDoFixer.Settings;

/// <summary>
/// Persiste AppSettings como JSON. La ruta se inyecta para que los tests no escriban
/// en el %APPDATA% real y para poder testear el caso de archivo corrupto.
/// Nunca tira: un JSON roto degrada a defaults.
/// </summary>
internal sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return AppSettings.Defaults();

            string json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            return loaded ?? AppSettings.Defaults();
        }
        catch (Exception)
        {
            // Cuarentena en vez de borrado: si el archivo tenía algo recuperable,
            // queda a mano, y la app arranca igual.
            Quarantine();
            return AppSettings.Defaults();
        }
    }

    public void Save(AppSettings settings)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tmp = _path + ".tmp";
        string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

        // Escritura atómica: sin esto, morir a mitad de la escritura deja un JSON
        // truncado y la app arranca sin configuración la próxima vez.
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private void Quarantine()
    {
        try
        {
            if (File.Exists(_path)) File.Move(_path, _path + ".bad", overwrite: true);
        }
        catch { }
    }
}
```

- [ ] **Step 5: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~SettingsStoreTests
```

Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add Settings/AppSettings.cs Settings/SettingsStore.cs tests/8bitdofixer.Tests/SettingsStoreTests.cs
git commit -m "feat: persist settings atomically in %APPDATA%

Writes go through a .tmp plus File.Move so dying mid-write cannot leave
a truncated JSON that costs the user their configuration. A corrupt file
is quarantined as .bad and the app starts on defaults rather than
failing to start.

The path is injected rather than read from AppPaths so tests do not
write into the real %APPDATA% and the corrupt-file path is testable.

Language is now detected and persisted: with the app restarting at every
logon, an unpersisted language choice becomes a daily annoyance."
```

---

## Task 3: `StartupRegistration`

**Files:**
- Create: `Settings/StartupRegistration.cs`
- Create: `tests/8bitdofixer.Tests/StartupRegistrationTests.cs`

**Interfaces:**
- Consumes: nada
- Produces:
  - `BitDoFixer.Settings.StartupRegistration.MinimizedArgument` → `const string` = `"--minimized"`
  - `StartupRegistration.ValueName` → `const string` = `"8BitDoFixer"`
  - `StartupRegistration.BuildRunCommand(string exePath)` → `string` (pura)
  - `StartupRegistration.IsDisabledBlob(byte[]? blob)` → `bool` (pura)
  - `StartupRegistration.CurrentCommand()` → `string?`
  - `StartupRegistration.IsRegistered()` → `bool`
  - `StartupRegistration.IsBlockedByWindows()` → `bool`
  - `StartupRegistration.Enable(string exePath)` → `void`
  - `StartupRegistration.Disable()` → `void`
  - `StartupRegistration.NeedsRepair(string exePath)` → `bool`

**Tres cosas que esta clase resuelve y que no son obvias:**

1. **La ruta va entre comillas.** Si el `.exe` está en una carpeta con espacios (`C:\Program Files\…`, o el Escritorio de un usuario con nombre compuesto), un valor sin comillas hace que Windows intente ejecutar el primer token. `BuildRunCommand` es pura precisamente para poder testear el quoting.

2. **La clave `Run` no alcanza para saber si está activo.** Cuando el usuario lo desactiva desde Administrador de tareas → Inicio, Windows escribe en `StartupApproved\Run` y **deja `Run` intacta**. Leyendo solo `Run`, el switch de la UI diría "activado" mientras Windows lo bloquea. `IsBlockedByWindows()` lee la segunda clave.

3. **Autocura de la ruta.** Es un `.exe` single-file que el usuario puede mover de carpeta; si se movió, el valor del registro apunta a un archivo que ya no está. `NeedsRepair` compara el valor guardado con `Environment.ProcessPath`.

**Sobre el blob de `StartupApproved`:** son 12 bytes; el bit 0 del primer byte marca deshabilitado (`0x02`/`0x06` habilitado, `0x03`/`0x07` deshabilitado). Es una regla empírica, no documentada por Microsoft, así que `IsDisabledBlob` la aísla en una función pura con tests y el Step 7 la verifica contra el comportamiento real de Windows.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/8bitdofixer.Tests/StartupRegistrationTests.cs`:

```csharp
using BitDoFixer.Settings;
using Xunit;

namespace BitDoFixer.Tests;

public class StartupRegistrationTests
{
    // --- BuildRunCommand: el quoting es lo que se rompe en producción ---

    [Fact]
    public void BuildRunCommandQuotesThePath()
    {
        var command = StartupRegistration.BuildRunCommand(@"C:\Tools\8bitdofixer.exe");
        Assert.Equal(@"""C:\Tools\8bitdofixer.exe"" --minimized", command);
    }

    [Fact]
    public void BuildRunCommandSurvivesSpacesInThePath()
    {
        // Sin comillas, Windows intentaría ejecutar "C:\Program".
        var command = StartupRegistration.BuildRunCommand(@"C:\Program Files\8BitDo Fixer\8bitdofixer.exe");
        Assert.StartsWith(@"""C:\Program Files\8BitDo Fixer\8bitdofixer.exe""", command);
    }

    [Fact]
    public void BuildRunCommandIncludesTheMinimizedFlag()
    {
        var command = StartupRegistration.BuildRunCommand(@"C:\a\b.exe");
        Assert.EndsWith(StartupRegistration.MinimizedArgument, command);
    }

    [Fact]
    public void MinimizedArgumentIsStable()
    {
        // Program.cs parsea este literal exacto; cambiarlo rompe el arranque silencioso.
        Assert.Equal("--minimized", StartupRegistration.MinimizedArgument);
    }

    // --- IsDisabledBlob: el bit 0 del primer byte ---

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)] // habilitado
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)] // habilitado
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]  // el usuario lo apagó
    [InlineData(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]  // el usuario lo apagó
    public void IsDisabledBlobReadsTheLowBit(byte[] blob, bool expected)
    {
        Assert.Equal(expected, StartupRegistration.IsDisabledBlob(blob));
    }

    [Fact]
    public void IsDisabledBlobTreatsAbsenceAsNotDisabled()
    {
        // Sin entrada en StartupApproved, Windows no lo está bloqueando.
        Assert.False(StartupRegistration.IsDisabledBlob(null));
        Assert.False(StartupRegistration.IsDisabledBlob(Array.Empty<byte>()));
    }

    // --- Ciclo real contra HKCU. Escribe en el registro del usuario y limpia. ---

    [Fact]
    public void EnableThenDisableLeavesNoValueBehind()
    {
        bool wasRegistered = StartupRegistration.IsRegistered();
        string? previous = StartupRegistration.CurrentCommand();

        try
        {
            StartupRegistration.Enable(@"C:\Tools\probe.exe");
            Assert.True(StartupRegistration.IsRegistered());
            Assert.Equal(@"""C:\Tools\probe.exe"" --minimized", StartupRegistration.CurrentCommand());

            StartupRegistration.Disable();
            Assert.False(StartupRegistration.IsRegistered());
            Assert.Null(StartupRegistration.CurrentCommand());
        }
        finally
        {
            // Restaurar el estado previo del usuario: un test no puede dejarle
            // la app registrada al arranque, ni desregistrada si la tenía puesta.
            if (wasRegistered && previous is not null) StartupRegistration.EnableRaw(previous);
            else StartupRegistration.Disable();
        }
    }

    [Fact]
    public void NeedsRepairDetectsAMovedExecutable()
    {
        bool wasRegistered = StartupRegistration.IsRegistered();
        string? previous = StartupRegistration.CurrentCommand();

        try
        {
            StartupRegistration.Enable(@"C:\OldFolder\8bitdofixer.exe");
            Assert.True(StartupRegistration.NeedsRepair(@"C:\NewFolder\8bitdofixer.exe"));
            Assert.False(StartupRegistration.NeedsRepair(@"C:\OldFolder\8bitdofixer.exe"));
        }
        finally
        {
            if (wasRegistered && previous is not null) StartupRegistration.EnableRaw(previous);
            else StartupRegistration.Disable();
        }
    }

    [Fact]
    public void NeedsRepairIsFalseWhenNotRegistered()
    {
        bool wasRegistered = StartupRegistration.IsRegistered();
        string? previous = StartupRegistration.CurrentCommand();

        try
        {
            StartupRegistration.Disable();
            Assert.False(StartupRegistration.NeedsRepair(@"C:\anything\app.exe"));
        }
        finally
        {
            if (wasRegistered && previous is not null) StartupRegistration.EnableRaw(previous);
        }
    }
}
```

- [ ] **Step 2: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~StartupRegistrationTests
```

Expected: FAIL en compilación — `CS0246: StartupRegistration`.

- [ ] **Step 3: Escribir `StartupRegistration`**

`Settings/StartupRegistration.cs`:

```csharp
using Microsoft.Win32;

namespace BitDoFixer.Settings;

/// <summary>
/// Registra el ejecutable en el arranque del usuario. HKCU, no HKLM: no necesita
/// permisos de administrador y aparece en Administrador de tareas → Inicio, donde
/// el usuario puede desactivarlo.
/// </summary>
internal static class StartupRegistration
{
    public const string ValueName = "8BitDoFixer";
    public const string MinimizedArgument = "--minimized";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// Pura para poder testear el quoting: sin comillas, un .exe en una carpeta con
    /// espacios hace que Windows intente ejecutar solo el primer token de la ruta.
    /// </summary>
    public static string BuildRunCommand(string exePath) => $"\"{exePath}\" {MinimizedArgument}";

    /// <summary>
    /// Windows marca la entrada como deshabilitada en el bit 0 del primer byte del
    /// blob de StartupApproved (0x02/0x06 habilitado, 0x03/0x07 deshabilitado).
    /// Regla empírica, aislada acá para tenerla bajo test.
    /// </summary>
    public static bool IsDisabledBlob(byte[]? blob)
    {
        if (blob is null || blob.Length == 0) return false;
        return (blob[0] & 0x01) != 0;
    }

    public static string? CurrentCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public static bool IsRegistered() => CurrentCommand() is not null;

    /// <summary>
    /// True si la entrada existe pero Windows la tiene desactivada desde
    /// Administrador de tareas. Sin este chequeo, el switch de la UI mostraría
    /// "activado" mientras el arranque no ocurre.
    /// </summary>
    public static bool IsBlockedByWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
        return IsDisabledBlob(key?.GetValue(ValueName) as byte[]);
    }

    public static void Enable(string exePath) => EnableRaw(BuildRunCommand(exePath));

    /// <summary>Escribe un valor ya armado. Para restaurar estado previo.</summary>
    public static void EnableRaw(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// True si está registrado pero apuntando a otra ruta que la actual. Es un .exe
    /// single-file que el usuario puede mover; sin autocura, el arranque queda
    /// apuntando a un archivo que ya no existe.
    /// </summary>
    public static bool NeedsRepair(string exePath)
    {
        string? current = CurrentCommand();
        if (current is null) return false;
        return !string.Equals(current, BuildRunCommand(exePath), StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~StartupRegistrationTests
```

Expected: PASS, 12 tests.

- [ ] **Step 5: [WINDOWS] Verificar que los tests no dejaron basura**

```bash
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v 8BitDoFixer
```

Expected: `ERROR: The system was unable to find the specified registry key or value` — los tests restauran el estado previo en su `finally`. Si aparece `C:\Tools\probe.exe`, un test no limpió: borrarlo con `reg delete` y arreglar el `finally`.

- [ ] **Step 6: Commit**

```bash
git add Settings/StartupRegistration.cs tests/8bitdofixer.Tests/StartupRegistrationTests.cs
git commit -m "feat: register the executable for user logon startup

HKCU rather than HKLM: no admin rights, and it shows up in Task Manager
> Startup where the user can turn it off.

Reads StartupApproved as well as Run, because disabling the entry from
Task Manager leaves the Run value intact — checking only Run would make
the UI switch claim 'on' while Windows blocks the launch.

BuildRunCommand and IsDisabledBlob are pure so the two things that
actually break are under test: quoting a path with spaces, and the
undocumented low-bit convention in the StartupApproved blob."
```

---

## Task 4: `SingleInstanceGuard`

**Files:**
- Create: `Infrastructure/SingleInstanceGuard.cs`
- Create: `tests/8bitdofixer.Tests/SingleInstanceGuardTests.cs`

**Interfaces:**
- Consumes: nada
- Produces:
  - `BitDoFixer.Infrastructure.SingleInstanceGuard()` → constructor
  - `SingleInstanceGuard.TryAcquire()` → `bool` (false = ya corre otra instancia)
  - `SingleInstanceGuard.SignalExistingInstance()` → `void`
  - `SingleInstanceGuard.ListenForShowRequests(Action showWindow, CancellationToken)` → `void`
  - implementa `IDisposable`

**Por qué recién ahora hace falta:** con autoarranque, el `.exe` **ya está corriendo** cuando el usuario hace doble click en él. Sin guardia habría dos procesos peleando por el acquire `Exclusive` de DirectInput sobre el mismo mando, y dos pads virtuales por cada mando físico.

**`Local\` y no `Global\`:** con prefijo `Local\` el namespace de sincronización es por sesión de usuario, así que en Fast User Switching cada usuario tiene su propia instancia — que es el comportamiento correcto, no una limitación.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/8bitdofixer.Tests/SingleInstanceGuardTests.cs`:

```csharp
using BitDoFixer.Infrastructure;
using Xunit;

namespace BitDoFixer.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void FirstGuardAcquires()
    {
        using var guard = new SingleInstanceGuard();
        Assert.True(guard.TryAcquire());
    }

    [Fact]
    public void SecondGuardIsRefusedWhileTheFirstHoldsIt()
    {
        using var first = new SingleInstanceGuard();
        Assert.True(first.TryAcquire());

        using var second = new SingleInstanceGuard();
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void ReleasingLetsTheNextOneIn()
    {
        var first = new SingleInstanceGuard();
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = new SingleInstanceGuard();
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void SignallingWakesTheListener()
    {
        using var owner = new SingleInstanceGuard();
        Assert.True(owner.TryAcquire());

        using var woken = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();
        owner.ListenForShowRequests(() => woken.Set(), cts.Token);

        using var second = new SingleInstanceGuard();
        Assert.False(second.TryAcquire());
        second.SignalExistingInstance();

        Assert.True(woken.Wait(TimeSpan.FromSeconds(5)));
        cts.Cancel();
    }

    [Fact]
    public void SignallingWithNoListenerDoesNotThrow()
    {
        // La segunda instancia señala y se va; que nadie escuche no es un error.
        using var guard = new SingleInstanceGuard();
        guard.SignalExistingInstance();
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var guard = new SingleInstanceGuard();
        guard.TryAcquire();
        guard.Dispose();
        guard.Dispose();
    }
}
```

- [ ] **Step 2: [WINDOWS] Verificar que falla**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~SingleInstanceGuardTests
```

Expected: FAIL en compilación — `CS0246: SingleInstanceGuard`.

- [ ] **Step 3: Escribir `SingleInstanceGuard`**

`Infrastructure/SingleInstanceGuard.cs`:

```csharp
namespace BitDoFixer.Infrastructure;

/// <summary>
/// Garantiza una sola instancia por sesión de usuario, y le da a la segunda una
/// forma de pedirle a la primera que muestre su ventana.
///
/// Prefijo Local\ y no Global\: el namespace es por sesión, así que en Fast User
/// Switching cada usuario tiene su propia instancia. Eso es lo correcto.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\8BitDoFixer.SingleInstance";
    private const string ShowEventName = @"Local\8BitDoFixer.ShowWindow";

    private Mutex? _mutex;
    private bool _owned;
    private Thread? _listener;

    /// <summary>False si ya hay otra instancia viva en esta sesión.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName);

        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // La instancia previa murió sin liberar: el mutex es nuestro.
            _owned = true;
        }

        return _owned;
    }

    /// <summary>Le pide a la instancia viva que muestre su ventana.</summary>
    public void SignalExistingInstance()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(ShowEventName);
            handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Nadie escuchando: la otra instancia está arrancando o cerrando.
            // No es un error; la segunda instancia se va igual.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Arranca un hilo de background que espera pedidos de "mostrá la ventana".
    /// Es un hilo dedicado y no un Task porque bloquea indefinidamente en un
    /// WaitHandle, que es exactamente lo que no conviene hacer en el thread pool.
    /// </summary>
    public void ListenForShowRequests(Action showWindow, CancellationToken token)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        _listener = new Thread(() =>
        {
            try
            {
                using (handle)
                using (var cancel = new ManualResetEventSlim(false))
                using (token.Register(cancel.Set))
                {
                    var handles = new WaitHandle[] { handle, cancel.WaitHandle };

                    while (!token.IsCancellationRequested)
                    {
                        if (WaitHandle.WaitAny(handles) != 0) return;
                        showWindow();
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex);
            }
        })
        {
            IsBackground = true,
            Name = "8BitDoFixer.ShowWindowListener"
        };

        _listener.Start();
    }

    public void Dispose()
    {
        if (_mutex is null) return;

        try
        {
            if (_owned) _mutex.ReleaseMutex();
        }
        catch { }

        _mutex.Dispose();
        _mutex = null;
        _owned = false;
    }
}
```

- [ ] **Step 4: [WINDOWS] Correr los tests**

```bash
dotnet test 8bitdofixer.sln --filter FullyQualifiedName~SingleInstanceGuardTests
```

Expected: PASS, 6 tests.

Si `SignallingWakesTheListener` es inestable, la causa es que el hilo listener todavía no llegó a crear el `EventWaitHandle` cuando la segunda instancia señala. Es una condición real que en producción no importa (la app crea el listener durante el arranque, mucho antes de que exista una segunda instancia); en el test se arregla esperando a que el handle exista antes de señalar.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/SingleInstanceGuard.cs tests/8bitdofixer.Tests/SingleInstanceGuardTests.cs
git commit -m "feat: allow only one instance per user session

With logon autostart the executable is already running when the user
double-clicks it. Without a guard, two processes fight over the same
Exclusive DirectInput acquire and create two virtual pads per physical
controller.

The second instance signals a named event and exits; the first shows its
window instead. Local\ rather than Global\ so Fast User Switching gives
each user their own instance."
```

---

## Task 5: Bandeja, orquestación de arranque y ocultar en X

**Files:**
- Modify: `8bitdofixer.csproj` (agregar `H.NotifyIcon.Wpf`)
- Create: `Tray/TrayIconHost.cs`
- Modify: `App.xaml` (`ShutdownMode`, sacar `StartupUri`)
- Modify: `App.xaml.cs` (orquestación completa)
- Modify: `Program.cs` (args + instancia única)
- Modify: `MainWindow.xaml.cs` (`OnClosing` oculta; `OnClosed` deja de matar la app)
- Modify: `Localization.cs` (strings de la bandeja)

**Interfaces:**
- Consumes: `AppPaths`, `CrashLogger`, `SettingsStore`, `AppSettings`, `StartupRegistration`, `SingleInstanceGuard`, `ControllerService`, `ServiceState`
- Produces:
  - `BitDoFixer.Tray.TrayIconHost(Action showWindow, Action startService, Action stopService, Action exit, Func<bool> isStartWithWindowsEnabled, Action<bool> setStartWithWindows)` → constructor
  - `TrayIconHost.UpdateState(ServiceState state, int controllerCount)` → `void`
  - `TrayIconHost.Notify(string title, string message)` → `void`
  - `TrayIconHost` implementa `IDisposable`
  - `App.Current` → `static App`
  - `App.Settings` → `AppSettings`
  - `App.UpdateSettings(Func<AppSettings, AppSettings>)` → `void`
  - `App.StartMinimized` / `App.Guard` → `{ get; init; }`
  - `App.ShowMainWindow()` / `App.ExitApplication()` → `void`
  - `MainWindow.RefreshStartupSwitch()` → `void` (stub; la Tarea 6 lo implementa)
  - `Localization`: `TrayOpen`, `TrayStart`, `TrayStop`, `TrayStartWithWindows`, `TrayExit`, `TrayHintTitle`, `TrayHintMessage`, `TrayTooltip(ServiceState, int)`

**Los cuatro cambios que hacen que la app sobreviva sin ventana:**

1. **`ShutdownMode="OnExplicitShutdown"`.** El default es `OnLastWindowClose`. Con la ventana oculta el proceso sobreviviría igual (ocultar no es cerrar), pero cualquier `Close()` futuro lo mataría. Explícito es explícito.
2. **`StartupUri` se va de `App.xaml`.** `App.OnStartup` crea la ventana a mano, porque necesita decidir si mostrarla y necesita su HWND antes de arrancar el servicio.
3. **`EnsureHandle()`, no `Handle`.** El acquire de DirectInput necesita un HWND válido y con `--minimized` la ventana no se muestra nunca. `Handle` devolvería `IntPtr.Zero`.
4. **`OnClosing` cancela y oculta.** Si la ventana se cerrara de verdad, el HWND se destruye y el acquire `Exclusive` de todos los workers se cae. La única salida real es "Salir" en la bandeja.

- [ ] **Step 1: Agregar la dependencia**

En `8bitdofixer.csproj`, dentro del `<ItemGroup>` de `PackageReference`:

```xml
    <!-- WPF no trae NotifyIcon. Este da un TaskbarIcon con ContextMenu de WPF, que
         hereda los estilos de Material Design; la alternativa (UseWindowsForms +
         System.Windows.Forms.NotifyIcon) mete WinForms entero en el single-file y
         deja el menú con estilo nativo, chocando con el resto de la UI. -->
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.*" />
```

- [ ] **Step 2: Agregar los strings a `Localization.cs`**

```csharp
        // --- Bandeja (plan tray-autostart) ---
        // NOTA: las traducciones al turco necesitan revisión de un hablante nativo.
        public string TrayOpen => IsEnglish ? "Open" : "Aç";
        public string TrayStart => IsEnglish ? "Start services" : "Servisleri başlat";
        public string TrayStop => IsEnglish ? "Stop services" : "Servisleri durdur";
        public string TrayStartWithWindows => IsEnglish ? "Start with Windows" : "Windows ile başlat";
        public string TrayExit => IsEnglish ? "Exit" : "Çıkış";
        public string TrayHintTitle => IsEnglish ? "Still running" : "Çalışmaya devam ediyor";
        public string TrayHintMessage => IsEnglish
            ? "8BitDo Fixer keeps running in the system tray. Use Exit from the tray menu to close it."
            : "8BitDo Fixer sistem tepsisinde çalışmaya devam eder. Kapatmak için tepsi menüsünden Çıkış'ı kullanın.";

        public string AutoStartServicesLabel => IsEnglish ? "Start services automatically" : "Servisleri otomatik başlat";
        public string StartWithWindowsLabel => IsEnglish ? "Start with Windows" : "Windows ile başlat";
        public string BlockedByWindowsNote => IsEnglish
            ? "Windows has this disabled (Task Manager → Startup)"
            : "Windows bunu devre dışı bıraktı (Görev Yöneticisi → Başlangıç)";

        public string TrayTooltip(Models.ServiceState state, int controllerCount) => state switch
        {
            Models.ServiceState.Mapped => IsEnglish
                ? $"8BitDo Fixer — {controllerCount} controller(s) mapped"
                : $"8BitDo Fixer — {controllerCount} kumanda eşlendi",
            Models.ServiceState.Searching => IsEnglish
                ? "8BitDo Fixer — searching for controllers"
                : "8BitDo Fixer — kumandalar aranıyor",
            Models.ServiceState.DriverMissing => IsEnglish
                ? "8BitDo Fixer — ViGEmBus missing"
                : "8BitDo Fixer — ViGEmBus eksik",
            _ => IsEnglish ? "8BitDo Fixer — stopped" : "8BitDo Fixer — durduruldu"
        };
```

- [ ] **Step 3: Escribir `TrayIconHost`**

`Tray/TrayIconHost.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BitDoFixer.Models;
using H.NotifyIcon;

namespace BitDoFixer.Tray;

/// <summary>
/// El icono de bandeja y su menú. No sabe nada del servicio ni de la ventana: recibe
/// las acciones por constructor, así que es un consumidor más de ControllerService,
/// no un intermediario.
/// </summary>
internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _startItem;
    private readonly MenuItem _stopItem;
    private readonly MenuItem _startupItem;
    private readonly MenuItem _openItem;
    private readonly MenuItem _exitItem;
    private readonly Action<bool> _setStartWithWindows;

    public TrayIconHost(
        Action showWindow,
        Action startService,
        Action stopService,
        Action exit,
        Func<bool> isStartWithWindowsEnabled,
        Action<bool> setStartWithWindows)
    {
        _setStartWithWindows = setStartWithWindows;

        var loc = Localization.Instance;

        _openItem = new MenuItem { Header = loc.TrayOpen };
        _openItem.Click += (_, _) => showWindow();

        _startItem = new MenuItem { Header = loc.TrayStart };
        _startItem.Click += (_, _) => startService();

        _stopItem = new MenuItem { Header = loc.TrayStop };
        _stopItem.Click += (_, _) => stopService();

        _startupItem = new MenuItem
        {
            Header = loc.TrayStartWithWindows,
            IsCheckable = true,
            IsChecked = isStartWithWindowsEnabled()
        };
        _startupItem.Click += (_, _) => _setStartWithWindows(_startupItem.IsChecked);

        _exitItem = new MenuItem { Header = loc.TrayExit };
        _exitItem.Click += (_, _) => exit();

        var menu = new ContextMenu();
        menu.Items.Add(_openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_exitItem);

        _icon = new TaskbarIcon
        {
            // Recurso embebido: con PublishSingleFile no hay un .ico en disco al lado del exe.
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.ico")),
            ToolTipText = loc.TrayTooltip(ServiceState.Stopped, 0),
            ContextMenu = menu,
            Visibility = Visibility.Visible
        };

        _icon.TrayMouseDoubleClick += (_, _) => showWindow();
    }

    public void UpdateState(ServiceState state, int controllerCount)
    {
        var loc = Localization.Instance;
        bool running = state != ServiceState.Stopped;

        _icon.ToolTipText = loc.TrayTooltip(state, controllerCount);
        _startItem.IsEnabled = !running;
        _stopItem.IsEnabled = running;
    }

    /// <summary>Refresca el menú tras un cambio de idioma o del estado del registro.</summary>
    public void RefreshLabels(bool startWithWindowsEnabled)
    {
        var loc = Localization.Instance;
        _openItem.Header = loc.TrayOpen;
        _startItem.Header = loc.TrayStart;
        _stopItem.Header = loc.TrayStop;
        _startupItem.Header = loc.TrayStartWithWindows;
        _startupItem.IsChecked = startWithWindowsEnabled;
        _exitItem.Header = loc.TrayExit;
    }

    public void Notify(string title, string message)
    {
        try
        {
            _icon.ShowNotification(title, message);
        }
        catch
        {
            // Un balloon que falla no puede tirar la app, y menos si el que
            // lo pidió fue el logger de crashes.
        }
    }

    public void Dispose()
    {
        _icon.Visibility = Visibility.Collapsed;
        _icon.Dispose();
    }
}
```

**Verificación de API:** no pude comprobar offline la superficie exacta de `H.NotifyIcon.Wpf` 2.x. Si el compilador rechaza `IconSource`, `ToolTipText`, `TrayMouseDoubleClick` o `ShowNotification`, usar el nombre que indique el error (en la generación anterior de la librería eran `Icon`, `ToolTipText`, `TrayMouseDoubleClick` y `ShowBalloonTip`). El resto del diseño no depende de esos nombres.

- [ ] **Step 4: Modificar `App.xaml`**

Cambiar la etiqueta de apertura:

```xml
<Application x:Class="BitDoFixer.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
             ShutdownMode="OnExplicitShutdown">
```

Se fue `StartupUri="MainWindow.xaml"`: la ventana la crea `OnStartup`, que necesita decidir si mostrarla. El resto del archivo queda igual.

- [ ] **Step 5: Reescribir `App.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Interop;
using BitDoFixer.Infrastructure;
using BitDoFixer.Models;
using BitDoFixer.Services;
using BitDoFixer.Settings;
using BitDoFixer.Tray;

namespace BitDoFixer
{
    public partial class App : Application
    {
        public static new App Current => (App)Application.Current;

        /// <summary>Puesto por Program.Main desde el argumento --minimized.</summary>
        public bool StartMinimized { get; init; }

        /// <summary>
        /// Puesta por Program.Main; la app la mantiene viva mientras corre.
        /// internal y no public: App es public y SingleInstanceGuard es internal, así
        /// que una propiedad public de ese tipo es CS0053 (accesibilidad inconsistente).
        /// </summary>
        internal SingleInstanceGuard? Guard { get; init; }

        public AppSettings Settings { get; private set; } = AppSettings.Defaults();

        private SettingsStore? _store;
        private MainWindow? _window;
        private TrayIconHost? _tray;
        private CancellationTokenSource? _listenerCts;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                CrashLogger.Log(args.Exception);
                args.Handled = true;
            };

            AppPaths.EnsureRoot();
            _store = new SettingsStore(AppPaths.SettingsFile);
            Settings = _store.Load();
            Localization.Instance.IsEnglish = Settings.IsEnglish;

            _window = new MainWindow();

            // EnsureHandle y no Handle: con --minimized la ventana no se muestra nunca,
            // y el acquire de DirectInput necesita un HWND válido.
            var hwnd = new WindowInteropHelper(_window).EnsureHandle();

            _tray = new TrayIconHost(
                showWindow: ShowMainWindow,
                startService: () => ControllerService.Instance.Start(hwnd),
                stopService: () => ControllerService.Instance.Stop(),
                exit: ExitApplication,
                isStartWithWindowsEnabled: () => StartupRegistration.IsRegistered(),
                setStartWithWindows: SetStartWithWindows);

            CrashLogger.SetNotifier((title, message) => _tray!.Notify(title, message));

            ControllerService.Instance.StateChanged += (_, _) => RefreshTray();

            _listenerCts = new CancellationTokenSource();
            Guard?.ListenForShowRequests(
                () => Dispatcher.Invoke(ShowMainWindow),
                _listenerCts.Token);

            ReconcileStartupRegistration();

            if (!StartMinimized) _window.Show();

            if (Settings.AutoStartServices) ControllerService.Instance.Start(hwnd);

            RefreshTray();
        }

        public void ShowMainWindow()
        {
            if (_window is null) return;

            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        public void ExitApplication()
        {
            ControllerService.Instance.Stop();
            _listenerCts?.Cancel();
            _tray?.Dispose();
            Shutdown();
        }

        public void UpdateSettings(Func<AppSettings, AppSettings> mutate)
        {
            var next = mutate(Settings);
            if (next == Settings) return; // record: comparación por valor

            Settings = next;
            _store?.Save(next);
        }

        public void SetStartWithWindows(bool enabled)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null) return;

                if (enabled) StartupRegistration.Enable(exePath);
                else StartupRegistration.Disable();

                UpdateSettings(s => s with { StartWithWindows = enabled });
            }
            catch (Exception ex)
            {
                // Políticas corporativas pueden negar la escritura en HKCU. No se
                // persiste un estado que miente: se avisa y el switch vuelve solo.
                CrashLogger.Log(ex);
            }

            _window?.RefreshStartupSwitch();
            RefreshTray();
        }

        /// <summary>
        /// Es un .exe single-file que el usuario puede mover. Si el setting dice que
        /// arranca con Windows pero el registro apunta a otra ruta, se reescribe.
        /// </summary>
        private void ReconcileStartupRegistration()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null) return;

                if (Settings.StartWithWindows)
                {
                    if (!StartupRegistration.IsRegistered() || StartupRegistration.NeedsRepair(exePath))
                    {
                        StartupRegistration.Enable(exePath);
                    }
                }
                else if (StartupRegistration.IsRegistered())
                {
                    // El usuario lo desactivó en la app en una sesión previa pero el
                    // valor sobrevivió: el setting manda.
                    StartupRegistration.Disable();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex);
            }
        }

        public void NotifyTrayHintOnce()
        {
            if (Settings.HasShownTrayHint) return;

            var loc = Localization.Instance;
            _tray?.Notify(loc.TrayHintTitle, loc.TrayHintMessage);
            UpdateSettings(s => s with { HasShownTrayHint = true });
        }

        public void RefreshTray()
        {
            var service = ControllerService.Instance;
            _tray?.UpdateState(service.State, service.Controllers.Count);
        }

        public void RefreshTrayLabels()
            => _tray?.RefreshLabels(StartupRegistration.IsRegistered());
    }
}
```

- [ ] **Step 6: Reescribir `Program.cs`**

```csharp
using BitDoFixer.Infrastructure;
using BitDoFixer.Settings;

namespace BitDoFixer
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            CrashLogger.Install();

            var guard = new SingleInstanceGuard();

            // Con autoarranque el .exe ya está corriendo cuando el usuario hace doble
            // click: sin esto habría dos procesos peleando por el mismo mando.
            if (!guard.TryAcquire())
            {
                guard.SignalExistingInstance();
                guard.Dispose();
                return;
            }

            try
            {
                var app = new App
                {
                    StartMinimized = args.Contains(StartupRegistration.MinimizedArgument),
                    Guard = guard
                };
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex);
            }
            finally
            {
                guard.Dispose();
            }
        }
    }
}
```

- [ ] **Step 7: Cambiar el cierre de `MainWindow`**

En `MainWindow.xaml.cs`, **borrar** el `OnClosed` actual y poner:

```csharp
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Cancelar y ocultar, no cerrar: si la ventana se destruye, su HWND se va
            // con ella y el acquire Exclusive de todos los workers se cae. La única
            // salida real es "Salir" en el menú de la bandeja.
            e.Cancel = true;
            Hide();
            App.Current.NotifyTrayHintOnce();
            base.OnClosing(e);
        }
```

Agregar también el stub de `RefreshStartupSwitch`, porque `App.SetStartWithWindows` ya lo
llama y sin él esta tarea no compila. La Tarea 6 lo reemplaza por la implementación real:

```csharp
        /// <summary>Stub: la Tarea 6 lo implementa cuando existan los checkboxes.</summary>
        public void RefreshStartupSwitch() { }
```

Y en `BtnLang_Click`, agregar al final la persistencia del idioma y el refresco de la bandeja:

```csharp
            App.Current.UpdateSettings(s => s with { IsEnglish = loc.IsEnglish });
            App.Current.RefreshTrayLabels();
```

- [ ] **Step 8: [WINDOWS] Build y test**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
```

Expected: build limpio y todos los tests de las Tareas 1 a 4 más los del plan anterior en verde.

- [ ] **Step 9: [WINDOWS] Checklist manual de la bandeja**

```bash
dotnet run --project 8bitdofixer.csproj
```

- [ ] **El icono aparece** al lado del reloj, con el logo de la app (no un cuadrado en blanco: eso significaría que el pack URI no resolvió).
- [ ] **Tooltip.** Pasar el mouse: dice "stopped". START, y con un mando pasa a "1 controller(s) mapped".
- [ ] **Menú.** Click derecho: Abrir, Iniciar, Detener, Iniciar con Windows, Salir. Iniciar deshabilitado mientras corre y Detener deshabilitado mientras no.
- [ ] **La X oculta.** Cerrar la ventana con la X. Esperado: la ventana desaparece, **el proceso sigue en el Administrador de tareas**, el icono queda, y aparece un balloon "Still running" — **una sola vez**. Cerrar y reabrir de nuevo: el balloon no vuelve.
- [ ] **El servicio sobrevive a la ventana.** Con un mando mapeado, cerrar con la X y **probar el mando en un juego o en `joy.cpl`**. Esperado: sigue funcionando. Este es el punto de toda la fase.
- [ ] **Doble click reabre** la ventana con el log intacto.
- [ ] **Instancia única.** Con la app corriendo, ejecutar el `.exe` de nuevo. Esperado: **no** aparece un segundo proceso; se muestra la ventana de la que ya corría.
- [ ] **Un solo pad virtual.** Después de lo anterior, `joy.cpl` debe seguir mostrando un solo pad virtual por mando físico.
- [ ] **Salir cierra de verdad.** Bandeja → Salir. Esperado: el icono se va, el proceso desaparece del Administrador de tareas, y los pads virtuales desaparecen de `joy.cpl`.
- [ ] **El idioma persiste.** Cambiar a TR, salir por la bandeja, volver a abrir. Esperado: arranca en turco, y el menú de la bandeja también está en turco.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: run from the system tray and survive a hidden window

The window is no longer the app. ShutdownMode becomes OnExplicitShutdown,
StartupUri is gone so OnStartup can decide whether to show the window at
all, and the X cancels the close and hides instead: destroying the window
takes its HWND with it and every worker's Exclusive DirectInput acquire
falls over. The only real exit is Exit in the tray menu.

The HWND comes from EnsureHandle rather than Handle, because with
--minimized the window is never shown and Handle would be IntPtr.Zero.

Closing with the X shows a one-time balloon: without it the user thinks
they closed the app, reopens it, and the single-instance guard hands back
the same window with no explanation."
```

---

## Task 6: Los dos switches en la UI

**Files:**
- Modify: `MainWindow.xaml` (el footer)
- Modify: `MainWindow.xaml.cs` (handlers + `RefreshStartupSwitch`)

**Interfaces:**
- Consumes: `App.Current.Settings`, `App.Current.UpdateSettings`, `App.Current.SetStartWithWindows`, `StartupRegistration.IsBlockedByWindows()`
- Produces: `MainWindow.RefreshStartupSwitch()` → `void` (la implementación real; la Tarea 5 dejó el stub)

**Los dos switches son independientes a propósito.** "Iniciar con Windows" toca el registro; "Iniciar servicios automáticamente" solo toca `settings.json`. Separados podés tener la combinación útil de arrancar con Windows pero no activar el mapeo hasta pedirlo — y sobre todo, desactivar el arranque automático sin perder la configuración de servicios.

**Y el switch no puede mentir.** Si Windows tiene la entrada bloqueada desde Administrador de tareas, el checkbox se muestra activado con una nota debajo. Sin esa nota, el usuario ve "activado", reinicia, no arranca, y no tiene forma de saber por qué.

- [ ] **Step 1: Reemplazar el footer de `MainWindow.xaml`**

Sustituir todo el bloque `<!-- Actions Footer --> <Grid Grid.Row="3" ...>` … `</Grid>` por:

```xml
        <!-- Actions Footer -->
        <Grid Grid.Row="3" Margin="20,10,20,25">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Row="0" Grid.Column="0" Margin="0,0,0,10">
                <CheckBox x:Name="ChkAutoStartServices" Click="ChkAutoStartServices_Click"
                          Content="{Binding AutoStartServicesLabel, Source={x:Static local:Localization.Instance}}"
                          FontSize="13" Cursor="Hand"/>

                <CheckBox x:Name="ChkStartWithWindows" Click="ChkStartWithWindows_Click"
                          Content="{Binding StartWithWindowsLabel, Source={x:Static local:Localization.Instance}}"
                          FontSize="13" Margin="0,6,0,0" Cursor="Hand"/>

                <TextBlock x:Name="TxtStartupBlocked" Margin="26,2,0,0" FontSize="11"
                           Foreground="{StaticResource AccentAmberBrush}" Visibility="Collapsed"
                           Text="{Binding BlockedByWindowsNote, Source={x:Static local:Localization.Instance}}"/>
            </StackPanel>

            <TextBlock Grid.Row="1" Grid.Column="0" VerticalAlignment="Center" Opacity="0.62" FontSize="12"
                       Text="{Binding FooterText, Source={x:Static local:Localization.Instance}}"/>

            <Button Grid.Row="0" Grid.RowSpan="2" Grid.Column="1" x:Name="BtnStop" Click="BtnStop_Click"
                    Style="{DynamicResource MaterialDesignOutlinedButton}"
                    Margin="0,0,15,0" IsEnabled="False" Width="160" Height="40"
                    VerticalAlignment="Center"
                    materialDesign:ButtonAssist.CornerRadius="8">
                <StackPanel Orientation="Horizontal">
                    <materialDesign:PackIcon Kind="StopCircleOutline" Width="20" Height="20" Margin="0,0,8,0" VerticalAlignment="Center"/>
                    <TextBlock Text="{Binding StopServiceBtn, Source={x:Static local:Localization.Instance}}" VerticalAlignment="Center" FontWeight="SemiBold"/>
                </StackPanel>
            </Button>

            <Button Grid.Row="0" Grid.RowSpan="2" Grid.Column="2" x:Name="BtnStart" Click="BtnStart_Click"
                    Style="{DynamicResource MaterialDesignRaisedButton}"
                    Width="160" Height="40" VerticalAlignment="Center"
                    materialDesign:ButtonAssist.CornerRadius="8">
                <StackPanel Orientation="Horizontal">
                    <materialDesign:PackIcon Kind="PlayCircleOutline" Width="20" Height="20" Margin="0,0,8,0" VerticalAlignment="Center"/>
                    <TextBlock Text="{Binding StartServiceBtn, Source={x:Static local:Localization.Instance}}" VerticalAlignment="Center" FontWeight="SemiBold"/>
                </StackPanel>
            </Button>
        </Grid>
```

Verificar contra el archivo real que el `Style` y los atributos de `BtnStart` coincidan con los que ya tenía; lo único que cambia en los dos botones es `Grid.Row`, `Grid.RowSpan` y `VerticalAlignment`.

- [ ] **Step 2: Reemplazar el stub de `RefreshStartupSwitch` y agregar los handlers**

En `MainWindow.xaml.cs`, reemplazar el stub por:

```csharp
        private void ChkAutoStartServices_Click(object sender, RoutedEventArgs e)
        {
            App.Current.UpdateSettings(s => s with { AutoStartServices = ChkAutoStartServices.IsChecked == true });
        }

        private void ChkStartWithWindows_Click(object sender, RoutedEventArgs e)
        {
            // App hace la escritura al registro y después llama de vuelta a
            // RefreshStartupSwitch, así que si la escritura falla el checkbox se
            // corrige solo en lugar de quedar mostrando algo que no pasó.
            App.Current.SetStartWithWindows(ChkStartWithWindows.IsChecked == true);
        }

        /// <summary>
        /// Sincroniza los dos checkboxes con la verdad: los settings y el registro.
        /// Se llama al arrancar y después de cada escritura al registro.
        /// </summary>
        public void RefreshStartupSwitch()
        {
            var settings = App.Current.Settings;

            ChkAutoStartServices.IsChecked = settings.AutoStartServices;

            // El registro manda sobre el setting: si la escritura falló, el checkbox
            // tiene que reflejar lo que realmente quedó.
            ChkStartWithWindows.IsChecked = StartupRegistration.IsRegistered();

            TxtStartupBlocked.Visibility =
                StartupRegistration.IsRegistered() && StartupRegistration.IsBlockedByWindows()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
```

Agregar `using BitDoFixer.Settings;` arriba, y llamar a `RefreshStartupSwitch()` al final del constructor de `MainWindow`.

- [ ] **Step 3: [WINDOWS] Build y test**

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
```

Expected: todo verde.

- [ ] **Step 4: [WINDOWS] Checklist manual de los switches**

- [ ] **Persistencia.** Marcar los dos, salir por la bandeja, reabrir. Esperado: los dos siguen marcados.
- [ ] **El registro se escribió.** `reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v 8BitDoFixer`. Esperado: la ruta del `.exe` **entre comillas**, seguida de `--minimized`.
- [ ] **Auto-start de servicios.** Con "Iniciar servicios automáticamente" marcado, salir y reabrir. Esperado: los servicios arrancan solos, sin tocar START.
- [ ] **Independencia.** Desmarcar solo "Iniciar con Windows". Esperado: el valor del registro desaparece y "Iniciar servicios automáticamente" sigue marcado.
- [ ] **La nota de bloqueo de Windows.** Con "Iniciar con Windows" marcado, ir a Administrador de tareas → Inicio, desactivar 8BitDoFixer, y reabrir la app. Esperado: el checkbox sigue marcado **y aparece la nota ámbar** "Windows has this disabled". Reactivarlo en el Administrador de tareas y confirmar que la nota se va.
- [ ] **Autocura de ruta.** Publicar el `.exe` (`dotnet publish -c Release -r win-x64`), correrlo desde una carpeta, marcar "Iniciar con Windows", cerrar, **mover el `.exe` a otra carpeta**, y correrlo de nuevo. Esperado: el valor del registro ahora apunta a la ruta nueva.

- [ ] **Step 5: [WINDOWS] EL CHECKPOINT DE LA FASE — reiniciar Windows**

Con los dos switches marcados y el `.exe` publicado corriendo desde una ubicación estable:

```bash
shutdown /r /t 0
```

Después del login, sin tocar nada:

- [ ] **No hay ventana.** No aparece la ventana de la app.
- [ ] **El icono está en la bandeja.**
- [ ] **Los servicios están corriendo.** Tooltip: "searching for controllers" (sin mando) o "1 controller(s) mapped".
- [ ] **Prender el mando funciona sin tocar nada.** Este es el requisito original completo.
- [ ] **Sin crash.log nuevo.** `type "%APPDATA%\8BitDoFixer\crash.log"` — si hay entradas con la fecha del arranque, algo falló en el camino de logon y hay que investigarlo antes de dar la fase por terminada.

- [ ] **Step 6: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add start-with-Windows and auto-start switches

The two are independent: one writes the registry, the other only
settings.json, so disabling autostart does not lose the service
preference.

The switch reads the registry rather than the setting, so a failed write
corrects itself instead of displaying something that did not happen, and
an amber note appears when Windows has the entry disabled from Task
Manager — otherwise the user sees 'on', reboots, nothing starts, and has
no way to find out why."
```

---

## Task 7: Documentación y versión

**Files:**
- Modify: `README.md`
- Modify: `8bitdofixer.csproj` (`<Version>0.2.0</Version>`)
- Modify: `Localization.cs` (`FooterText`)

**Interfaces:**
- Consumes: nada
- Produces: nada de código

- [ ] **Step 1: Bump de versión**

En `8bitdofixer.csproj`: `<Version>0.1.0</Version>` → `<Version>0.2.0</Version>`.

- [ ] **Step 2: Actualizar el footer**

En `Localization.cs`, reemplazar `<USUARIO>` por el usuario de GitHub real:

```csharp
        public string FooterText => "v0.2.0 • github.com/<USUARIO>/Ultimate2CbluetoothFix";
```

- [ ] **Step 3: Actualizar el README**

Agregar a la sección `## ✨ Features`:

```markdown
- 🎮 **Multiple Controllers**: Maps several 8BitDo Ultimate 2C pads simultaneously, one virtual Xbox 360 controller each.
- 🔄 **Automatic Reconnection**: The service keeps watching for controllers, so turning a pad on after the app started just works. Virtual pads are held for 15 seconds after a disconnect so a quick reconnect keeps the same player number.
- 🚀 **Start with Windows**: Optionally starts at logon straight into the system tray with services already running.
- 🔔 **System Tray**: Closing the window hides it; the service keeps running. Exit from the tray menu.
```

Reemplazar la sección `## 🚀 Installation & Usage` por:

```markdown
## 🚀 Installation & Usage

1. Download the latest release (`8bitdofixer.exe`) from the [Releases](../../releases) section.
2. Install the [ViGEmBus Driver](https://github.com/nefarius/ViGEmBus/releases) if you haven't already.
3. Turn on your controller in **Bluetooth mode** (Bluetooth/Android mode) and pair it with Windows.
4. Launch `8bitdofixer.exe` and click **Start**.
5. To run it hands-free, tick **Start with Windows** and **Start services automatically**. From then on the
   app starts at logon minimized to the system tray with the remapper already running, and picks up your
   controllers whenever you turn them on.

Settings and the crash log live in `%APPDATA%\8BitDoFixer\`.

### Building from source

```bash
dotnet build 8bitdofixer.sln
dotnet test 8bitdofixer.sln
dotnet publish 8bitdofixer.csproj -c Release -r win-x64
```

A normal build is deliberately not self-contained, so the test project can reference the app.
`dotnet publish -r win-x64` is what produces the single-file self-contained executable.
```

Agregar a `## 🔌 Dependencies`:

```markdown
| [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) | `2.x` | System tray icon and menu |
```

Y en `## 🙏 Credits`, arriba de los demás:

```markdown
* Fork of [bezelye404/Ultimate2CbluetoothFix](https://github.com/bezelye404/Ultimate2CbluetoothFix) — the original 8BitDo Ultimate 2C remapper this builds on.
```

- [ ] **Step 4: Actualizar las limitaciones conocidas**

En `## ⚠️ Known Issues`, agregar:

```markdown
* **Battery and controller identity are not linked:** with two identical pads paired, the battery list shows
  both levels but cannot say which physical pad each one belongs to. There is no reliable way to match a
  DirectInput device instance to a Bluetooth LE device id.
* **Player numbers can shift:** a pad that stays off for more than 15 seconds releases its XInput slot and may
  come back as a different player number.
```

- [ ] **Step 5: [WINDOWS] Verificar que el publish sigue andando**

```bash
dotnet publish 8bitdofixer.csproj -c Release -r win-x64
```

Expected: un único `.exe`, y la UI muestra `v0.2.0` en el footer.

- [ ] **Step 6: Commit y push**

```bash
git add README.md 8bitdofixer.csproj Localization.cs
git commit -m "docs: document tray, autostart and multi-controller support

Bump to 0.2.0, credit the upstream project this is forked from, and
document the two limitations that are design decisions rather than bugs:
battery readings cannot be attributed to a specific pad, and player
numbers can shift after the 15s grace window."
git push origin feature/tray-autostart-multipad
```

---

## Cierre

Al terminar la Tarea 7 el requisito original está completo: la app arranca con Windows, minimizada en la bandeja, con los servicios activos, y toma los mandos cuando los prendés.

**Riesgos que sólo se cierran con hardware y con un reinicio real:**

1. **La superficie de API de `H.NotifyIcon.Wpf` 2.x** (`IconSource`, `ToolTipText`, `TrayMouseDoubleClick`, `ShowNotification`) no se pudo verificar offline. Si el compilador rechaza alguno, el error dice el nombre correcto; el diseño no depende de ellos.
2. **El icono de bandeja bajo `PublishSingleFile`.** El pack URI `pack://application:,,,/Assets/logo.ico` debería resolver porque `logo.ico` es un `<Resource>`, pero se verifica en el Step 9 de la Tarea 5: un cuadrado blanco en la bandeja significa que no resolvió.
3. **La convención del blob de `StartupApproved`** (bit 0 del primer byte) es empírica. El Step 4 de la Tarea 6 la verifica contra el comportamiento real de Windows.
4. **El único checkpoint que no se puede simular es el reinicio** (Tarea 6, Step 5). Es el que prueba de verdad la feature.
