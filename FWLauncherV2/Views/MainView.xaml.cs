using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CurseForge.APIClient;
using CurseForge.APIClient.Models.Files;
using FWLauncherV2.Dialogs;
using FWLauncherV2.Models;
using FWLauncherV2.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using File = System.IO.File;

namespace FWLauncherV2.Views
{
    public partial class MainView : UserControl
    {
        // Modpack doğrudan bu klasöre kurulur (ayrı "modpacks/{id}" alt klasörü açılmaz).
        private readonly string launcherPath = SettingsService.LauncherDirectory;
        private const string ModpackUrl = "https://pokewing.com/Versions/modpacks.json";
        private const string CurseForgeApiKey = "$2a$10$VTXmen.R8t9tKx2kjp5IauRMYvSUxXy/dC0BY05JPmB7fgjU5PK5W";

        private readonly HttpClient httpClient = HttpClientProvider.Download;
        private readonly ApiClient cfApiClient;

        private ModpackInfo? currentPack;
        private List<ModpackInfo> allPacks = new();
        private bool _packSelectorReady;   // SelectionChanged'in ilk doldurmada tetiklenmesini önler
        private UserSettings currentUserSettings = new();
        private string currentUsername = "Oyuncu";
        private bool updateAvailable;

        private bool? _serverOnline;          // null = kontrol ediliyor, true/false = sonuç
        private bool _operationInProgress;    // kurulum/oyun başlatma sürerken true
        private readonly DispatcherTimer _serverTimer;

        public MainView()
        {
            InitializeComponent();
            cfApiClient = new ApiClient(CurseForgeApiKey);

            // Sunucu durumunu periyodik yenile (açılırsa OYNA kilidi otomatik açılır).
            _serverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
            _serverTimer.Tick += (s, e) => { if (currentPack != null) _ = CheckServerStatusAsync(); };

            Loaded += MainView_Loaded;
            Unloaded += (s, e) => _serverTimer.Stop();
        }

        public void SetCurrentUser(string username)
        {
            currentUsername = string.IsNullOrWhiteSpace(username) ? "Oyuncu" : username;
            TxtWelcome.Text = $"Hoş geldin, {currentUsername}!";
        }

        private async void MainView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUserSettings();
            await LoadModpackInfo();
            _serverTimer.Start();
        }

        private void LoadUserSettings()
        {
            currentUserSettings = SettingsService.Load();

            if (string.IsNullOrWhiteSpace(currentUserSettings.LastUsername))
                currentUserSettings.LastUsername = currentUsername;

            // Java yolu boşsa, launcher'ın indirdiği yerel Java'lardan birini bulmayı dene.
            if (string.IsNullOrWhiteSpace(currentUserSettings.JavaPath) || !File.Exists(currentUserSettings.JavaPath))
            {
                var javaFolder = Path.Combine(launcherPath, "javalar");
                if (Directory.Exists(javaFolder))
                {
                    var foundJava = Directory.GetFiles(javaFolder, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(foundJava))
                        currentUserSettings.JavaPath = foundJava;
                }
            }
        }

        private async Task LoadModpackInfo()
        {
            UpdateStatus("Paket bilgileri alınıyor...", 0);
            try
            {
                var jsonString = await httpClient.GetStringAsync(ModpackUrl);
                var onlinePacks = JsonSerializer.Deserialize<List<ModpackInfo>>(jsonString);
                allPacks = onlinePacks?.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList() ?? new();

                if (allPacks.Count == 0)
                    throw new Exception("Mod paketi bilgisi bulunamadı.");

                // Son seçilen paketi hatırla (yoksa ilk paket).
                currentPack = allPacks.FirstOrDefault(p => p.Id == currentUserSettings.SelectedPackId) ?? allPacks[0];

                // Sürüm seçici: 2+ paket varsa göster (paketler web editöründeki 'Sürümler' sekmesinden yönetilir).
                _packSelectorReady = false;
                CmbPack.Items.Clear();
                foreach (var p in allPacks)
                {
                    // Aynı ada sahip paketleri Id ile ayırt et ("PokeWing Network (RolePlay)").
                    bool dup = allPacks.Count(x => x.Name == p.Name) > 1;
                    CmbPack.Items.Add(dup && !string.IsNullOrWhiteSpace(p.Id) ? $"{p.Name} ({p.Id})" : p.Name);
                }
                CmbPack.SelectedIndex = allPacks.IndexOf(currentPack);
                CmbPack.Visibility = allPacks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                _packSelectorReady = true;

                TxtPackName.Text = currentPack.Name;
                UpdateBadges();
                UpdateUI();
                UpdateStatus("Hazır.", 100);
                _ = CheckServerStatusAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Mod paketi bilgisi alınamadı.", ex);
                UpdateStatus("Hata: Paket bilgisi alınamadı.", 0);
                FWDialog.Warn(
                    $"Mod paketi bilgisi alınamadı. İnternet bağlantınızı kontrol edin.\n\nAyrıntı: {ex.Message}",
                    "Bağlantı Hatası");
            }
        }

        /// <summary>
        /// Paketin kurulum klasörü. İlk paket eski kurulumları bozmamak için kök klasörde kalır;
        /// diğer paketler packs\{Id} altına kurulur (her paketin dünyası/modları/ayarları ayrıdır).
        /// </summary>
        private string PackDir(ModpackInfo p)
        {
            if (allPacks.Count == 0 || p.Id == allPacks[0].Id) return launcherPath;
            var safe = new string((string.IsNullOrWhiteSpace(p.Id) ? p.Name : p.Id)
                .Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length == 0) safe = "pack";
            return Path.Combine(launcherPath, "packs", safe);
        }

        private string CurrentPackDir => currentPack == null ? launcherPath : PackDir(currentPack);

        /// <summary>Sürüm seçici değişince: paketi kaydet, rozet/durum/kurulum bilgisini tazele.</summary>
        private void CmbPack_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_packSelectorReady || CmbPack.SelectedIndex < 0 || CmbPack.SelectedIndex >= allPacks.Count)
                return;
            currentPack = allPacks[CmbPack.SelectedIndex];
            currentUserSettings.SelectedPackId = currentPack.Id;
            try { SettingsService.Save(currentUserSettings); } catch (Exception ex) { Logger.Warn($"Paket seçimi kaydedilemedi: {ex.Message}"); }
            TxtPackName.Text = currentPack.Name;
            _serverOnline = null;   // yeni paketin sunucusu kontrol edilene dek OYNA bekletilir
            UpdateBadges();
            UpdateUI();
            _ = CheckServerStatusAsync();
            Logger.Info($"Paket seçildi: {currentPack.Name} · klasör={CurrentPackDir}");
        }

        private void UpdateUI()
        {
            if (currentPack == null)
                return;

            updateAvailable = false;

            // Kurulu mu? (manifest + launcher_info var) ve sürüm eski mi?
            var manifestPath = Path.Combine(CurrentPackDir, "manifest.json");
            var localInfoPath = Path.Combine(CurrentPackDir, "launcher_info.json");
            bool installed = File.Exists(manifestPath) && File.Exists(localInfoPath);

            if (installed)
            {
                try
                {
                    var localInfo = JsonSerializer.Deserialize<LocalPackInfo>(File.ReadAllText(localInfoPath));
                    if (localInfo != null && localInfo.Version != currentPack.Version)
                        updateAvailable = true;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"launcher_info.json okunamadı: {ex.Message}");
                }
            }

            // Paket henüz yayında değilse (ZipUrl boş) kurulum/oynama kapalı kalır.
            bool packReady = !string.IsNullOrWhiteSpace(currentPack.ZipUrl);
            BtnUpdate.IsEnabled = packReady;
            BtnUpdate.Content = !packReady ? "HAZIRLANIYOR" : (updateAvailable ? "Güncelle" : "KUR");
            BtnUpdate.ToolTip = packReady ? null : "Bu paket henüz yayınlanmadı — dosyaları eklenince otomatik açılır.";
            ApplyPlayGate();
        }

        /// <summary>
        /// OYNA butonunu sunucu durumuna göre kilitler/açar. GÜNCELLE bundan etkilenmez.
        /// Sunucu çevrimdışıysa oynanamaz; çevrimiçi olunca otomatik açılır.
        /// </summary>
        private void ApplyPlayGate()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ApplyPlayGate);
                return;
            }

            if (_operationInProgress) return;

            switch (_serverOnline)
            {
                case true:
                    BtnPlay.IsEnabled = true;
                    BtnPlay.Content = "OYNA";
                    BtnPlay.ToolTip = null;
                    break;
                case false:
                    BtnPlay.IsEnabled = false;
                    BtnPlay.Content = "SUNUCU KAPALI";
                    BtnPlay.ToolTip = "Sunucu çevrimdışı — oynamak için sunucunun açık olması gerekir.";
                    break;
                default: // null = kontrol ediliyor
                    BtnPlay.IsEnabled = false;
                    BtnPlay.Content = "OYNA";
                    BtnPlay.ToolTip = "Sunucu durumu kontrol ediliyor...";
                    break;
            }
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e) => await HandleInstallation(updateAvailable);

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (currentPack == null)
                return;

            // Paket dosyaları henüz yayınlanmamışsa (ZipUrl boş) kurulum denenemez.
            if (string.IsNullOrWhiteSpace(currentPack.ZipUrl) &&
                !File.Exists(Path.Combine(CurrentPackDir, "manifest.json")))
            {
                FWDialog.Warn("Bu paket henüz hazırlanıyor — dosyaları yayınlanınca oynanabilir olacak.",
                    "Paket Hazır Değil");
                return;
            }

            // Kalıcı kart olduğu için ayarları her tıklamada taze oku (Java/RAM değişmiş olabilir).
            LoadUserSettings();

            // Sunucu çevrimdışıysa oynanamaz (KUR/GÜNCELLE engellenmez).
            if (_serverOnline == false)
            {
                FWDialog.Warn("Sunucu şu anda çevrimdışı. Lütfen sunucu açıldığında tekrar deneyin.",
                    "Sunucu Çevrimdışı");
                return;
            }

            try
            {
                if (!EnsureJavaSelected() || !EnsureRamSelected())
                    return;

                _operationInProgress = true;
                BtnPlay.IsEnabled = false;
                BtnPlay.Content = "Başlatılıyor...";
                BtnPlay.ToolTip = "Oyun hazırlanıyor";

                UpdateStatus("Oyun başlatılıyor...", 0);
                Logger.Info($"OYNA tıklandı · java={currentUserSettings.JavaPath} · ram={currentUserSettings.LastRamInGB}GB");

                int ramMB = currentUserSettings.LastRamInGB * 1024;
                string javaPath = currentUserSettings.JavaPath!;
                string packDir = CurrentPackDir;

                bool started = await AutoCheckAndLaunch(packDir, ramMB, javaPath);

                if (!started)
                {
                    ResetPlayButton();
                    return;
                }

                // Oyun ayrı bir süreç olarak çalışıyor; launcher'ı küçült.
                BtnPlay.Content = "Çalışıyor";
                BtnPlay.ToolTip = "Oyun çalışıyor";
                UpdateStatus("Oyun çalışıyor...", 100);

                var window = Window.GetWindow(this);
                if (window != null)
                    window.WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                Logger.Error("Oyun başlatılırken hata.", ex);
                FWDialog.Error($"Oyun başlatılırken hata oluştu:\n{ex.Message}");
                ResetPlayButton();
            }
        }

        private bool EnsureJavaSelected()
        {
            if (!string.IsNullOrWhiteSpace(currentUserSettings.JavaPath) && File.Exists(currentUserSettings.JavaPath))
                return true;

            // Java seçilmemiş/geçersiz: sistemde otomatik bulmayı dene (kurulumda da bu kullanılıyor).
            string auto = AutoFindJava(GetPackMcVersion());
            if (!string.IsNullOrWhiteSpace(auto) && File.Exists(auto))
            {
                currentUserSettings.JavaPath = auto;
                try { SettingsService.Save(currentUserSettings); } catch (Exception ex) { Logger.Warn($"Java yolu kaydedilemedi: {ex.Message}"); }
                Logger.Info($"Java otomatik bulundu ve ayarlandı: {auto}");
                return true;
            }

            FWDialog.Warn("Sisteminizde uygun bir Java bulunamadı. Lütfen Ayarlardan Java 21 yolunu seçin.",
                "Java Bulunamadı");
            (Window.GetWindow(this) as MainWindow)?.NavigateToSettings();
            return false;
        }

        private bool EnsureRamSelected()
        {
            if (currentUserSettings.LastRamInGB <= 0)
            {
                FWDialog.Warn("Lütfen önce Ayarlardan geçerli bir RAM değeri seçin.", "RAM Ayarlanmamış");
                (Window.GetWindow(this) as MainWindow)?.NavigateToSettings();
                return false;
            }
            return true;
        }

        private void ResetPlayButton()
        {
            _operationInProgress = false;
            BtnPlay.IsEnabled = true;
            BtnPlay.Content = "OYNA";
            BtnPlay.ToolTip = null;
            ApplyPlayGate(); // sunucu çevrimdışıysa tekrar kilitle
        }

        private async Task<bool> AutoCheckAndLaunch(string packDir, int ramMB, string javaPath)
        {
            var manifestPath = Path.Combine(packDir, "manifest.json");

            if (!File.Exists(manifestPath))
            {
                FWDialog.Error("Manifest dosyası eksik, lütfen paketi yeniden yükleyin.");
                return false;
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath));
            if (manifest?.Minecraft == null || string.IsNullOrWhiteSpace(manifest.Minecraft.Version))
            {
                FWDialog.Error("Manifest dosyası geçersiz, lütfen paketi yeniden yükleyin.");
                return false;
            }

            var launcher = new MinecraftLauncher(new MinecraftPath(packDir));
            string? versionName = await ResolveInstalledVersionAsync(launcher, manifest);

            if (versionName == null)
            {
                UpdateStatus("Mod yükleyici bulunamadı, eksikler tamamlanıyor...", 85);
                try
                {
                    // Eksik parçaları tamamla (configleri ve mevcut modları bozmadan).
                    await InstallOrUpdatePackAsync(packDir);
                }
                catch (Exception ex)
                {
                    Logger.Error("Mod yükleyici otomatik kurulumu başarısız.", ex);
                    FWDialog.Error($"Mod yükleyici kurulumu başarısız oldu: {ex.Message}");
                    return false;
                }
            }
            else if (!VanillaLibsComplete())
            {
                // Mod yükleyici kurulu ama vanilla kütüphaneleri eksik (lwjgl vb.) → onar.
                try
                {
                    UpdateStatus("Eksik oyun dosyaları indiriliyor...", 0);
                    await InstallVanillaAsync(launcher, manifest.Minecraft.Version);
                }
                catch (Exception ex)
                {
                    Logger.Error("Eksik oyun dosyaları indirilemedi.", ex);
                    FWDialog.Error($"Oyun dosyaları doğrulanamadı: {ex.Message}");
                    return false;
                }
            }

            return await LaunchGame(packDir);
        }

        /// <summary>Kurulu sürümler arasından paketin mod yükleyicisine (Forge/NeoForge/Vanilla) ait olanı bulur.</summary>
        private async Task<string?> ResolveInstalledVersionAsync(MinecraftLauncher launcher, Manifest manifest)
        {
            var loader = manifest.Minecraft.ModLoaders.FirstOrDefault(m => m.Primary)
                         ?? manifest.Minecraft.ModLoaders.FirstOrDefault();
            var kind = ModLoaderService.Detect(loader?.Id);
            string loaderVersion = ModLoaderService.ExtractVersion(loader?.Id ?? "");
            string mc = manifest.Minecraft.Version;

            var versions = await launcher.GetAllVersionsAsync();
            return ModLoaderService.FindInstalledVersion(kind, mc, loaderVersion, versions.Select(v => v.Name));
        }

        /// <summary>
        /// Vanilla kütüphanelerinin (özellikle lwjgl) indirilmiş olup olmadığını hızlıca kontrol eder.
        /// Eksikse oyun başlarken "org.lwjgl.system.Struct" hatasıyla çöker.
        /// </summary>
        private bool VanillaLibsComplete()
        {
            try
            {
                var lwjgl = Path.Combine(launcherPath, "libraries", "org", "lwjgl");
                return Directory.Exists(lwjgl)
                    && Directory.EnumerateFiles(lwjgl, "*.jar", SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }

        private async Task HandleInstallation(bool isUpdate)
        {
            if (currentPack == null)
                return;

            await RunAction(async () =>
            {
                var packDir = CurrentPackDir;
                UpdateStatus(isUpdate ? "Paket güncelleniyor..." : "Yeni kurulum başlıyor...", 0);
                await InstallOrUpdatePackAsync(packDir);
                UpdateStatus(isUpdate ? "Güncelleme tamamlandı!" : "Kurulum tamamlandı!", 100);
                FWDialog.Success(
                    isUpdate
                        ? "Güncelleme tamamlandı. Mevcut ayarların korundu."
                        : "Kurulum başarıyla tamamlandı. Artık 'OYNA'ya basabilirsiniz.");
                UpdateUI();
            });
        }

        /// <summary>
        /// Paketi kurar veya günceller — klasörü SİLMEDEN. Mevcut configler korunur; yalnızca
        /// eksik modlar indirilir, pakete ait olmayan modlar silinir, kurulu sürümler atlanır.
        /// Hem ilk kurulum hem güncelleme hem onarım için kullanılır.
        /// </summary>
        private async Task InstallOrUpdatePackAsync(string packDirectory)
        {
            if (currentPack == null)
                throw new InvalidOperationException("Mod paketi bilgisi yüklenmedi.");

            Directory.CreateDirectory(packDirectory);
            var modsDirectory = Path.Combine(packDirectory, "mods");
            Directory.CreateDirectory(modsDirectory);

            // 1) Zip'i indir, manifesti oku ve yeni dosyaları configleri bozmadan uygula.
            UpdateStatus("Paket bilgileri indiriliyor...", 0);
            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{currentPack.Id}.zip");
            await DownloadFileAsync(currentPack.ZipUrl, tempZipPath, "Paket indiriliyor", 0, 18);

            UpdateStatus("Paket dosyaları denetleniyor...", 20);
            Manifest manifest;
            var overrideMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var archive = ZipFile.OpenRead(tempZipPath))
            {
                var manifestEntry = archive.GetEntry("manifest.json")
                    ?? throw new Exception("Pakette manifest.json bulunamadı.");

                using (var stream = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream)
                        ?? throw new Exception("manifest.json okunamadı.");
                }

                // manifest.json bir meta veridir -> her zaman güncellenir.
                manifestEntry.ExtractToFile(Path.Combine(packDirectory, "manifest.json"), true);

                foreach (var entry in archive.Entries.Where(en => en.FullName.StartsWith("overrides/")))
                {
                    var relative = entry.FullName.Substring("overrides/".Length);
                    if (string.IsNullOrEmpty(relative) || string.IsNullOrEmpty(entry.Name))
                        continue;

                    // overrides/mods içindeki jar'ları "korunacak" listesine ekle (manifestte olmasalar da silinmesin).
                    var normalized = relative.Replace('\\', '/');
                    if (normalized.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                        && normalized.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        overrideMods.Add(Path.GetFileName(normalized));

                    var destPath = Path.Combine(packDirectory, relative);

                    // KORUMA: dosya zaten varsa üzerine yazma (kullanıcının config ayarları bozulmasın).
                    if (File.Exists(destPath))
                        continue;

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, true);
                }
            }
            TryDeleteFile(tempZipPath);

            if (string.IsNullOrWhiteSpace(manifest.Minecraft.Version))
                throw new Exception("Manifest içinde Minecraft sürümü belirtilmemiş.");

            // 2) Oyun (vanilla + mod yükleyici) zaten kurulu mu? Kuruluysa uzun kurulumları atla.
            var launcher = new MinecraftLauncher(new MinecraftPath(packDirectory));
            var loaderEntry = manifest.Minecraft.ModLoaders.FirstOrDefault(m => m.Primary)
                              ?? manifest.Minecraft.ModLoaders.FirstOrDefault();
            var kind = ModLoaderService.Detect(loaderEntry?.Id);
            var loaderVer = ModLoaderService.ExtractVersion(loaderEntry?.Id ?? "");
            var versionNames = (await launcher.GetAllVersionsAsync()).Select(v => v.Name).ToList();

            bool loaderInstalled = kind == ModLoaderKind.Vanilla
                ? versionNames.Any(n => n == manifest.Minecraft.Version)
                : ModLoaderService.FindInstalledVersion(kind, manifest.Minecraft.Version, loaderVer, versionNames, exactOnly: true) != null;

            // Vanilla'yı HER ZAMAN doğrula (idempotent: yalnızca eksik dosyaları indirir).
            // ÖNEMLİ: NeoForge/Forge --installClient vanilla kütüphanelerini (lwjgl vb.) indirmez;
            // bu yüzden vanilla atlanırsa oyun "org.lwjgl.system.Struct" ile çöker.
            await InstallVanillaAsync(launcher, manifest.Minecraft.Version);

            // 3) Modları akıllıca senkronize et (eksikleri indir, fazlaları sil, mevcutları koru).
            await SyncModsAsync(manifest, modsDirectory, overrideMods);

            // 4) Mod yükleyiciyi yalnızca gerekiyorsa kur.
            if (!loaderInstalled)
                await InstallLoaderIfNeededAsync(manifest, packDirectory);
            else
                UpdateStatus("Mod yükleyici zaten kurulu.", 97);

            // 5) Yerel sürüm bilgisini güncelle.
            await File.WriteAllTextAsync(Path.Combine(packDirectory, "launcher_info.json"),
                JsonSerializer.Serialize(new LocalPackInfo { Id = currentPack.Id, Version = currentPack.Version }));

            UpdateStatus("Hazır!", 100);
        }

        private async Task InstallVanillaAsync(MinecraftLauncher launcher, string mcVersion)
        {
            UpdateStatus("Vanilla Minecraft kuruluyor...", 30);

            // Gerçek indirme ilerlemesini [30, 45] aralığına yansıt.
            var lastReport = DateTime.MinValue;
            var byteProgress = new Progress<ByteProgress>(b =>
            {
                double ratio = b.ToRatio();
                if (double.IsNaN(ratio) || ratio < 0) return;
                if (ratio > 1) ratio = 1;

                if ((DateTime.Now - lastReport).TotalMilliseconds < 100 && ratio < 1) return;
                lastReport = DateTime.Now;

                SetProgress(30 + ratio * 15);
                SetStatusText($"Vanilla Minecraft kuruluyor...  (%{ratio * 100:0})");
            });

            var fileProgress = new Progress<InstallerProgressChangedEventArgs>(_ => { });
            await launcher.InstallAsync(mcVersion, fileProgress, byteProgress, CancellationToken.None);
            UpdateStatus("Vanilla Minecraft kuruldu.", 45);
        }

        /// <summary>
        /// Mod klasörünü manifeste göre akıllıca senkronize eder: pakete ait olmayan modları siler,
        /// eksikleri indirir, zaten var olanları yeniden indirmez. Configleri etkilemez.
        /// </summary>
        private async Task SyncModsAsync(Manifest manifest, string modsDirectory, HashSet<string> keepExtra)
        {
            UpdateStatus("Modlar denetleniyor...", 45);

            // İstenen mod kümesi: manifest dosya kimlikleri -> (dosya adı, indirme adresi)
            var desired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (manifest.Files.Count > 0)
            {
                var body = new GetModFilesRequestBody { FileIds = manifest.Files.Select(f => (int)f.FileID).ToList() };
                var resp = await cfApiClient.GetFilesAsync(body);
                foreach (var mf in resp.Data)
                {
                    if (!string.IsNullOrWhiteSpace(mf.FileName))
                        desired[mf.FileName] = mf.DownloadUrl;
                }
            }

            // 1) Pakete ait olmayan modları sil (manifestte ve override modlarında olmayan .jar'lar).
            int removed = 0;
            foreach (var existing in Directory.GetFiles(modsDirectory, "*.jar"))
            {
                var name = Path.GetFileName(existing);
                if (desired.ContainsKey(name) || keepExtra.Contains(name))
                    continue;

                TryDeleteFile(existing);
                removed++;
                Logger.Info($"Pakete ait olmayan mod silindi: {name}");
            }

            // 2) Eksik modları indir (mevcut olanları atla).
            int total = desired.Count, i = 0, downloaded = 0, skipped = 0;
            foreach (var kv in desired)
            {
                i++;
                var dest = Path.Combine(modsDirectory, kv.Key);
                var progress = 45 + (int)(35 * ((double)i / Math.Max(1, total)));

                if (File.Exists(dest)) { skipped++; continue; }   // zaten var, tekrar indirme

                if (string.IsNullOrWhiteSpace(kv.Value))
                {
                    Logger.Warn($"Mod indirme bağlantısı yok (üçüncü taraf engeli olabilir): {kv.Key}");
                    continue;
                }

                UpdateStatus($"Mod indiriliyor ({i}/{total}): {kv.Key}", progress);
                try { await DownloadFileAsync(kv.Value, dest); downloaded++; }
                catch (Exception ex) { Logger.Warn($"Mod indirilemedi: {kv.Key} - {ex.Message}"); }
            }

            UpdateStatus($"Modlar güncel · {downloaded} indirildi, {skipped} mevcut, {removed} silindi", 80);
            Logger.Info($"Mod senkronizasyonu: {downloaded} indirildi, {skipped} mevcut, {removed} silindi.");
        }

        /// <summary>Paketin mod yükleyicisini (Forge veya NeoForge) headless modda kurar. Vanilla ise atlar.</summary>
        private async Task InstallLoaderIfNeededAsync(Manifest manifest, string packDirectory)
        {
            if (currentPack == null)
                return;

            var loader = manifest.Minecraft.ModLoaders.FirstOrDefault(m => m.Primary)
                         ?? manifest.Minecraft.ModLoaders.FirstOrDefault();
            var kind = ModLoaderService.Detect(loader?.Id);
            if (kind == ModLoaderKind.Vanilla)
                return; // Saf vanilla paketi; ek kurulum gerekmez.

            string loaderVersion = ModLoaderService.ExtractVersion(loader!.Id);
            string displayName = ModLoaderService.DisplayName(kind);

            UpdateStatus($"{displayName} indiriliyor...", 80);

            // ?? yalnızca null'da devreye girer; JSON'da boş string ("") gelirse fallback'e düşmez.
            // Bu yüzden boş/whitespace olan değerleri de atlayıp sıradaki kaynağa geçiyoruz:
            // açık LoaderInstallerUrl -> ForgeInstallerUrl -> manifest loader sürümünden üretilen resmî maven URL'si.
            string url = currentPack.LoaderInstallerUrl ?? "";
            if (string.IsNullOrWhiteSpace(url))
                url = currentPack.ForgeInstallerUrl ?? "";
            if (string.IsNullOrWhiteSpace(url))
                url = ModLoaderService.BuildInstallerUrl(kind, manifest.Minecraft.Version, loaderVersion);

            if (string.IsNullOrWhiteSpace(url))
                throw new Exception($"{displayName} yükleyici adresi belirlenemedi.");

            string installerJar = Path.Combine(Path.GetTempPath(), $"{kind}-installer.jar".ToLowerInvariant());
            await DownloadFileAsync(url, installerJar, $"{displayName} indiriliyor", 80, 88);

            string javaExe = currentUserSettings.JavaPath ?? "";
            if (string.IsNullOrWhiteSpace(javaExe) || !File.Exists(javaExe))
                javaExe = AutoFindJava(manifest.Minecraft.Version);

            if (string.IsNullOrWhiteSpace(javaExe) || !File.Exists(javaExe))
                throw new Exception("Java bulunamadı! Lütfen geçerli bir Java yolu ayarlayın.");

            // Forge/NeoForge yükleyicisi bir launcher_profiles.json bekler.
            EnsureFakeLauncherProfile(packDirectory, manifest.Minecraft.Version);

            UpdateStatus($"{displayName} kuruluyor...", 90);
            int exitCode = await RunJavaInstallerAsync(javaExe, installerJar, packDirectory);
            if (exitCode != 0)
                throw new Exception($"{displayName} kurulumu başarısız oldu (kod: {exitCode}).");

            TryDeleteFile(installerJar);
            UpdateStatus($"{displayName} kurulumu tamamlandı.", 97);
        }

        private static void EnsureFakeLauncherProfile(string gameRoot, string mcVersion)
        {
            string path = Path.Combine(gameRoot, "launcher_profiles.json");
            if (File.Exists(path))
                return;

            string json = $$"""
            {
                "profiles": {
                    "FWLauncherV2": {
                        "created": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
                        "icon": "Furnace",
                        "lastUsed": "{{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}}",
                        "lastVersionId": "{{mcVersion}}",
                        "name": "FWLauncherV2",
                        "type": "custom"
                    }
                },
                "settings": {},
                "version": 3
            }
            """;
            File.WriteAllText(path, json);
        }

        private static async Task<int> RunJavaInstallerAsync(string javaExe, string installerJar, string gameRoot)
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = $"-jar \"{installerJar}\" --installClient \"{gameRoot}\"",
                WorkingDirectory = gameRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Logger.Info("[LOADER] " + e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Logger.Warn("[LOADER-ERR] " + e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(); // UI'yi dondurmaz
            return proc.ExitCode;
        }

        private string AutoFindJava(string minecraftVersion = "")
        {
            try
            {
                int requiredMajor = 17;
                if (!string.IsNullOrWhiteSpace(minecraftVersion))
                {
                    if (minecraftVersion.StartsWith("1.21"))
                        requiredMajor = 21;
                    else if (minecraftVersion.StartsWith("1.20") || minecraftVersion.StartsWith("1.17")
                             || minecraftVersion.StartsWith("1.18") || minecraftVersion.StartsWith("1.19"))
                        requiredMajor = 17;
                    else if (minecraftVersion.StartsWith("1.12") || minecraftVersion.StartsWith("1.16")
                             || minecraftVersion.StartsWith("1.8"))
                        requiredMajor = 8;
                }

                string major = requiredMajor.ToString();

                // 1) Launcher'ın kendi indirdiği Java
                var javaFolder = Path.Combine(launcherPath, "javalar");
                if (Directory.Exists(javaFolder))
                {
                    var found = Directory.GetFiles(javaFolder, "java.exe", SearchOption.AllDirectories)
                        .FirstOrDefault(f => f.Contains(major));
                    if (!string.IsNullOrEmpty(found)) return found;
                }

                // 2) Yaygın JDK kurulum konumları (Oracle, Adoptium, Microsoft, Zulu, Corretto...)
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var roots = new[]
                {
                    Path.Combine(pf, "Java"),
                    Path.Combine(pf, "Eclipse Adoptium"),
                    Path.Combine(pf, "Microsoft"),
                    Path.Combine(pf, "Zulu"),
                    Path.Combine(pf, "Amazon Corretto"),
                    Path.Combine(pf, "BellSoft"),
                    Path.Combine(pf86, "Java"),
                };

                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var jdk in Directory.GetDirectories(root))
                    {
                        if (!Path.GetFileName(jdk).Contains(major)) continue;
                        var p = Path.Combine(jdk, "bin", "java.exe");
                        if (File.Exists(p)) return p;
                    }
                }

                // 3) JAVA_HOME ortam değişkeni
                var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrWhiteSpace(javaHome))
                {
                    var p = Path.Combine(javaHome, "bin", "java.exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Otomatik Java araması başarısız: {ex.Message}");
            }

            return "";
        }

        /// <summary>Kurulu paketin Minecraft sürümünü döndürür (Java major'unu seçmek için).</summary>
        private string GetPackMcVersion()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(currentPack?.McVersion))
                    return currentPack!.McVersion!;

                var manifestPath = Path.Combine(launcherPath, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    var m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
                    if (!string.IsNullOrWhiteSpace(m?.Minecraft?.Version))
                        return m!.Minecraft.Version;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Paket MC sürümü okunamadı: {ex.Message}");
            }
            return "";
        }

        /// <summary>Launcher'ın başlattığı çalışan oyun süreci (logout'ta kapatılır — tek hesap kuralı).</summary>
        public static Process? RunningGame;

        /// <summary>
        /// Launcher'dan çıkış: açık oyun sürecini kapatır + pokewing_auth.json'u siler.
        /// (Mod tarafı da dosyayı izler: silinince oyun kendini kapatır — süreç kapatma başarısız olsa bile çift güvence.)
        /// </summary>
        public static void KillGameAndClearAuth()
        {
            try
            {
                if (RunningGame != null && !RunningGame.HasExited)
                {
                    RunningGame.Kill(entireProcessTree: true);
                    Logger.Info("Çıkış yapıldı: çalışan oyun süreci kapatıldı.");
                }
            }
            catch (Exception ex) { Logger.Warn($"Oyun süreci kapatılamadı: {ex.Message}"); }
            RunningGame = null;
            try
            {
                // Auth dosyası artık her paketin kendi çalışma dizinine yazılıyor.
                // Hangi paketin çalıştığını bilmediğimiz için kök + tüm packs\* alt klasörlerini temizle.
                var root = SettingsService.LauncherDirectory;
                var targets = new List<string> { Path.Combine(root, "pokewing_auth.json") };
                var packsDir = Path.Combine(root, "packs");
                if (Directory.Exists(packsDir))
                    targets.AddRange(Directory.GetDirectories(packsDir)
                        .Select(d => Path.Combine(d, "pokewing_auth.json")));
                foreach (var auth in targets)
                    if (File.Exists(auth)) File.Delete(auth);
            }
            catch (Exception ex) { Logger.Warn($"Auth dosyası silinemedi: {ex.Message}"); }
        }

        private async Task<bool> LaunchGame(string packDir)
        {
            if (currentPack == null)
                return false;

            var launcher = new MinecraftLauncher(new MinecraftPath(packDir));
            var manifest = JsonSerializer.Deserialize<Manifest>(
                await File.ReadAllTextAsync(Path.Combine(packDir, "manifest.json")));

            if (manifest?.Minecraft == null || string.IsNullOrWhiteSpace(manifest.Minecraft.Version))
            {
                FWDialog.Error("Manifest dosyası geçersiz.");
                return false;
            }

            string mc = manifest.Minecraft.Version;
            string? versionName = await ResolveInstalledVersionAsync(launcher, manifest);

            if (versionName == null)
            {
                FWDialog.Error("Mod yükleyici sürümü bulunamadı!");
                return false;
            }

            var jvmArgs = new List<MArgument>
            {
                new MArgument("-Dfile.encoding=UTF-8"),
                new MArgument("-Duser.language=en"),
                new MArgument("-Duser.country=US"),
                new MArgument("-XX:+UseG1GC"),
                new MArgument("-XX:+UnlockExperimentalVMOptions"),
                new MArgument("-XX:+AlwaysPreTouch"),
                new MArgument("-XX:ParallelGCThreads=4"),
                new MArgument("-Dsun.jnu.encoding=UTF-8"),
                new MArgument("-Djava.locale.providers=COMPAT,SPI"),
                new MArgument("-Dusing.aikars.flags=true")
            };

            var gameArgs = new List<MArgument>();

            var opts = new MLaunchOption
            {
                Session = MSession.CreateOfflineSession(currentUsername),
                JavaPath = currentUserSettings.JavaPath,
                MaximumRamMb = currentUserSettings.LastRamInGB * 1024,
                ExtraJvmArguments = jvmArgs
            };

            // ---- Sunucu bağlantısı ----
            // OTOMATİK BAĞLANMA KAPALI: oyun artık doğrudan sunucuya girmez, PokeWing ana menüsüne düşer.
            // Bunun yerine sunucu bilgisini mod'un menüsüne aktarıyoruz: config/pokewing_servers.json →
            // menüdeki "BAĞLAN" ve "Sunucular" doğru sunucuya gider (oyuncu kendisi girer).
            if (!string.IsNullOrWhiteSpace(currentPack.ServerIp))
            {
                try
                {
                    string host = currentPack.ServerIp.Trim();
                    int port = currentPack.ServerPort > 0 ? currentPack.ServerPort : 25565;
                    if (host.Contains(':'))
                    {
                        var parts = host.Split(':');
                        host = parts[0];
                        if (int.TryParse(parts[1], out int p)) port = p;
                    }
                    string addr = $"{host}:{port}";
                    string name = string.IsNullOrWhiteSpace(currentPack.Name) ? "PokeWing Network" : currentPack.Name;
                    string cfgDir = Path.Combine(packDir, "config");
                    Directory.CreateDirectory(cfgDir);
                    var serverCfg = new
                    {
                        primary = addr,
                        servers = new[] { new { name, desc = "PokeWing Network", ip = addr } }
                    };
                    File.WriteAllText(Path.Combine(cfgDir, "pokewing_servers.json"),
                        JsonSerializer.Serialize(serverCfg, new JsonSerializerOptions { WriteIndented = true }));
                    Logger.Info($"PokeWing menü sunucusu yazıldı: {addr} (otomatik bağlanma KAPALI)");
                }
                catch (Exception ex) { Logger.Warn($"pokewing_servers.json yazılamadı: {ex.Message}"); }
            }

            // ---- Sunucu modu (cobblemon/roleplay): paket tanımında Mode doluysa kilitli yaz,
            //      boşsa (ve dosya bizim kilitli yazdığımızsa) sil → mod site ayarını kullanır. ----
            try
            {
                string cfgDir2 = Path.Combine(packDir, "config");
                Directory.CreateDirectory(cfgDir2);
                string modeFile = Path.Combine(cfgDir2, "pokewing_mode.json");
                string? packMode = currentPack?.Mode?.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(packMode))
                {
                    File.WriteAllText(modeFile, $"{{\"mode\":\"{packMode}\",\"lock\":true}}");
                    Logger.Info($"Sunucu modu (paketten) yazıldı: {packMode}");
                }
                else if (File.Exists(modeFile) && File.ReadAllText(modeFile).Contains("\"lock\":true"))
                {
                    File.Delete(modeFile);   // paket modu belirtmiyor → siteden gelsin
                }
            }
            catch (Exception ex) { Logger.Warn($"pokewing_mode.json yazılamadı: {ex.Message}"); }

            opts.ExtraGameArguments = gameArgs;

            // ---- Launcher-token kimlik dosyası (AuthMe yerine): mod bunu okuyup sunucuya doğrulatır ----
            // Sunucu bu token + HWID'yi validate_session.php ile kontrol eder → sahte launcher/token giremez.
            try
            {
                var auth = new
                {
                    username = currentUsername,
                    token = currentUserSettings.SessionToken ?? "",
                    hwid = HardwareId.Get(),
                    launcherPid = Environment.ProcessId,   // mod bekçisi izler: launcher ölürse oyun kapanır
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                // ÖNEMLİ: Auth dosyası oyunun ÇALIŞMA dizinine (packDir) yazılmalı; mod onu orada arar.
                // İlk paket packDir==launcherPath (kök); diğer paketler packs\{Id} altında çalışır.
                // Eskiden hep launcherPath'e yazılıyordu → alt-klasör paketlerinde (ör. RolePlay)
                // mod dosyayı bulamayıp "Doğrulanamadı" ile kick atıyordu.
                File.WriteAllText(Path.Combine(packDir, "pokewing_auth.json"),
                    System.Text.Json.JsonSerializer.Serialize(auth));
            }
            catch (Exception ex) { Logger.Warn($"Auth dosyası yazılamadı: {ex.Message}"); }

            Logger.Info($"Oyun süreci oluşturuluyor · sürüm={versionName}");
            var process = await launcher.BuildProcessAsync(versionName, opts);
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            // NeoForge erken yükleme ekranı: kırmızı Mojang teması yerine KOYU tema (PokeWing'e uygun).
            process.StartInfo.EnvironmentVariables["FML_EARLY_WINDOW_DARK"] = "true";

            // Oyun kapandığında launcher'ı geri getir ve OYNA butonunu sıfırla.
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                RunningGame = null;
                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        var window = Window.GetWindow(this);
                        if (window != null)
                        {
                            window.WindowState = WindowState.Normal;
                            window.Activate();
                        }
                        ResetPlayButton();
                        UpdateStatus("Hazır.", 100);
                    });
                }
                catch (Exception ex)
                {
                    // Launcher kapanmışsa dispatcher erişilemez olabilir; yok say.
                    Logger.Warn($"Oyun kapanış olayı işlenemedi: {ex.Message}");
                }
            };

            process.Start();
            RunningGame = process;   // logout'ta kapatılabilsin (tek hesap kuralı)
            Logger.Info($"Oyun süreci başlatıldı · PID={process.Id}");

            UpdateStatus("Oyun başlatıldı!", 100);
            return true;
        }

        /// <summary>
        /// Dosyayı parça parça (streaming) indirir. progStart/progEnd verilirse indirme yüzdesi
        /// genel ilerleme çubuğuna [progStart, progEnd] aralığında yansıtılır.
        /// </summary>
        private async Task DownloadFileAsync(string url, string dest, string? label = null,
            int progStart = -1, int progEnd = -1)
        {
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            using var resp = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1L;
            bool tracksProgress = progStart >= 0 && progEnd > progStart;

            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            var lastReport = DateTime.MinValue;

            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read));
                readTotal += read;

                if (total > 0 && (DateTime.Now - lastReport).TotalMilliseconds > 120)
                {
                    lastReport = DateTime.Now;
                    double pct = (double)readTotal / total;

                    if (label != null)
                    {
                        double mb = readTotal / 1048576.0;
                        double totMb = total / 1048576.0;
                        SetStatusText($"{label}  ·  {mb:0.0} / {totMb:0.0} MB  (%{pct * 100:0})");
                    }

                    if (tracksProgress)
                        SetProgress(progStart + pct * (progEnd - progStart));
                }
            }

            if (tracksProgress)
                SetProgress(progEnd);
        }

        private async Task RunAction(Func<Task> action)
        {
            // Ayarları taze oku (kalıcı kart) + Java yoksa otomatik bul, o da yoksa uyar.
            LoadUserSettings();
            if (!EnsureJavaSelected())
                return;

            _operationInProgress = true;
            SetButtonsEnabled(false);
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Logger.Error("İşlem sırasında hata.", ex);
                FWDialog.Error($"Hata: {ex.Message}");
                UpdateStatus("İşlem başarısız oldu.", 0);
            }
            finally
            {
                _operationInProgress = false;
                SetButtonsEnabled(true);
            }
        }

        public void OpenCurrentModsFolder()
        {
            try
            {
                if (currentPack == null)
                {
                    FWDialog.Warn("Mod paketi yüklenmedi!");
                    return;
                }

                string packDir = launcherPath;
                string modsDir = Path.Combine(packDir, "mods");

                if (!Directory.Exists(modsDir))
                    Directory.CreateDirectory(modsDir);

                Process.Start("explorer.exe", modsDir);
            }
            catch (Exception ex)
            {
                Logger.Error("Mod klasörü açılamadı.", ex);
                FWDialog.Error("Mod klasörü açılamadı:\n" + ex.Message);
            }
        }

        private void UpdateBadges()
        {
            if (currentPack == null)
                return;

            string loaderText = currentPack.Loader ?? "";
            string versionText = currentPack.McVersion ?? "";

            // Kuruluysa manifestten kesin loader/sürüm bilgisini al.
            try
            {
                var manifestPath = Path.Combine(launcherPath, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
                    if (manifest?.Minecraft != null)
                    {
                        if (string.IsNullOrWhiteSpace(versionText))
                            versionText = manifest.Minecraft.Version;
                        if (string.IsNullOrWhiteSpace(loaderText))
                        {
                            var loader = manifest.Minecraft.ModLoaders.FirstOrDefault(m => m.Primary)
                                         ?? manifest.Minecraft.ModLoaders.FirstOrDefault();
                            loaderText = ModLoaderService.DisplayName(ModLoaderService.Detect(loader?.Id));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Rozet bilgisi okunamadı: {ex.Message}");
            }

            bool hasLoader = !string.IsNullOrWhiteSpace(loaderText);
            bool hasVersion = !string.IsNullOrWhiteSpace(versionText);

            if (hasLoader) TxtLoaderBadge.Text = loaderText;
            if (hasVersion) TxtVersionBadge.Text = versionText;
            BadgesPanel.Visibility = (hasLoader || hasVersion) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task CheckServerStatusAsync()
        {
            if (currentPack == null || string.IsNullOrWhiteSpace(currentPack.ServerIp))
            {
                // Sunucu tanımlı değilse oyna kilidi uygulanmaz.
                SetServerStatus(false, "Sunucu yapılandırılmamış", "");
                _serverOnline = true;
                ApplyPlayGate();
                return;
            }

            string host = currentPack.ServerIp.Trim();
            int port = currentPack.ServerPort > 0 ? currentPack.ServerPort : 25565;
            if (host.Contains(':'))
            {
                var parts = host.Split(':');
                host = parts[0];
                if (int.TryParse(parts[1], out int p)) port = p;
            }

            string ipText = $"{host}:{port}";

            // İlk kontrolde "kontrol ediliyor" durumunu göster (daha önce sonuç yoksa).
            if (_serverOnline == null)
            {
                SetServerStatus(null, "Sunucu durumu kontrol ediliyor...", ipText);
                ApplyPlayGate();
            }

            var status = await MinecraftServerPinger.PingAsync(host, port);

            if (status.Online)
            {
                string text = status.MaxPlayers > 0
                    ? $"Çevrimiçi · {status.OnlinePlayers}/{status.MaxPlayers} oyuncu"
                    : "Sunucu çevrimiçi";
                SetServerStatus(true, text, ipText);
                _serverOnline = true;
            }
            else
            {
                SetServerStatus(false, "Sunucu çevrimdışı", ipText);
                _serverOnline = false;
            }

            ApplyPlayGate();
        }

        private void SetServerStatus(bool? online, string text, string ip)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetServerStatus(online, text, ip));
                return;
            }

            TxtServerStatus.Text = text;
            TxtServerIp.Text = ip;
            ServerDot.Fill = online switch
            {
                true => (Brush)FindResource("OnlineBrush"),
                false => (Brush)FindResource("TextMutedBrush"),
                _ => (Brush)FindResource("WarningBrush")
            };
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Logger.Warn($"Geçici dosya silinemedi ({path}): {ex.Message}"); }
        }

        private void SetButtonsEnabled(bool enable)
        {
            BtnUpdate.IsEnabled = enable;
            if (enable)
            {
                BtnPlay.IsEnabled = true;
                ApplyPlayGate(); // Play, sunucu durumuna göre tekrar kilitlenebilir
            }
            else
            {
                BtnPlay.IsEnabled = false;
            }
        }

        private void UpdateStatus(string text, int progress)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateStatus(text, progress));
                return;
            }

            LblStatus.Text = text;
            ProgressBar.Value = progress;
        }

        private void SetStatusText(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatusText(text));
                return;
            }

            LblStatus.Text = text;
        }

        private void SetProgress(double value)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetProgress(value));
                return;
            }

            ProgressBar.Value = Math.Clamp(value, 0, 100);
        }
    }
}
