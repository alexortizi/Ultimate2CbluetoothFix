using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BitDoFixer
{
    public class Localization : INotifyPropertyChanged
    {
        public static Localization Instance { get; } = new Localization();

        private bool _isEnglish = true;
        public bool IsEnglish
        {
            get => _isEnglish;
            set
            {
                if (_isEnglish != value)
                {
                    _isEnglish = value;
                    OnPropertyChanged(null); // Notify all properties
                }
            }
        }

        // XAML Bound Properties
        public string AppTitle => IsEnglish ? "8BitDo Controller Fixer" : "8BitDo Kontrolcü Düzeltici";
        public string ConnectionStatusTitle => IsEnglish ? "CONNECTION STATUS" : "BAĞLANTI DURUMU";
        public string BatteryLevelTitle => IsEnglish ? "BATTERY LEVEL" : "PİL SEVİYESİ";
        public string StartServiceBtn => IsEnglish ? "START SERVICE" : "BAŞLAT";
        public string StopServiceBtn => IsEnglish ? "STOP SERVICE" : "DURDUR";
        public string FooterText => "v0.1.0 • github.com/bezelye404";
        
        // Dynamic Texts (Used in Code-Behind)
        public string SearchingDInput => IsEnglish ? "Waiting for D-Input Device..." : "D-Input Cihazı Bekleniyor...";
        public string NoDevice => IsEnglish ? "No Device Connected" : "Cihaz Bağlı Değil";
        public string Scanning => IsEnglish ? "Scanning..." : "Taranıyor...";
        public string Connected => IsEnglish ? "Connection Established" : "Bağlantı Sağlandı";
        public string Stopped => IsEnglish ? "Stopped" : "Durduruldu";
        public string Idle => IsEnglish ? "Idle" : "Boşta";

        // Log messages
        public string LogAppInit => IsEnglish ? "Application Initialized." : "Uygulama başlatıldı.";
        public string LogServicesStarting => IsEnglish ? "Starting Services..." : "Servisler başlatılıyor...";
        public string LogServicesStopping => IsEnglish ? "Stopping Services..." : "Servisler durduruluyor...";
        
        public string LogMapperStart => IsEnglish ? "Bluetooth (DInput) -> Virtual Xbox 360 Remapper Started" : "Bluetooth (DInput) -> Sanal Xbox 360 Kontrolcüsü Başlatıldı";
        public string LogMapperNotFound => IsEnglish ? "ERROR: DInput gamepad/joystick not found!" : "HATA: DInput gamepad/joystick bulunamadı!";
        public string MapperNotFoundStatus => IsEnglish ? "Not Found" : "Bulunamadı";
        public string LogMapperSource(string name) => IsEnglish ? $"Source Device: {name}" : $"Kaynak Cihaz: {name}";
        public string MapperConnectedStatus => IsEnglish ? "Connected" : "Bağlandı";
        public string LogMapperReady => IsEnglish ? "Virtual Xbox Controller Connected. Ready!" : "Sanal Xbox Kontrolcüsü Bağlandı. Hazır!";
        public string LogMapperError(string ex) => IsEnglish ? $"Error or disconnected: {ex}" : $"Hata veya bağlantı koptu: {ex}";
        public string MapperDisconnectedStatus => IsEnglish ? "Disconnected" : "Bağlantı Koptu";

        public string LogBatteryStart(int init, int interval) => IsEnglish ? $"BLE Battery Monitor Started (Initial: {init}s, Interval: {interval}s)" : $"BLE Batarya Monitörü Başlatıldı (İlk Gecikme: {init}s, Aralık: {interval}s)";
        public string LogBatteryFatal(string ex) => IsEnglish ? $"[BLE Monitor Fatal Error]: {ex}" : $"[BLE Monitör Kritik Hata]: {ex}";
        public string LogBatteryLevel(string name, int level) => IsEnglish ? $"{name} Battery: {level}%" : $"{name} Pil: %{level}";
        public string LogBatteryScanError(string ex) => IsEnglish ? $"[BLE Scan Error]: {ex}" : $"[BLE Tarama Hatası]: {ex}";

        // --- Multi-mando (plan supervisor) ---
        // NOTA: las traducciones al turco necesitan revision de un hablante nativo.
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
        // TODO Tarea 8: reemplazar el 15 literal por Services.ControllerSupervisor.VirtualPadGraceSeconds
        public string LogDeviceLost(string name) => IsEnglish
            ? $"'{name}' lost. Holding its virtual pad for 15s."
            : $"'{name}' bağlantısı koptu. Sanal pad 15s tutuluyor.";
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
