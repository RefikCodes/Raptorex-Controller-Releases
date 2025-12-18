using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CncControlApp.Managers;
using CncControlApp.Helpers;

namespace CncControlApp.Services
{
    /// <summary>
    /// Probe Manager - Temiz ve basit probe sistemi
    /// 
    /// AKIŞ (tüm eksenler için aynı):
    /// 1. Coarse probe (hızlı yaklaşma)
    /// 2. Retract (geri çekil)
    /// 3. Fine probe x N (yavaş, hassas - doğrulama için)
    /// 4. Sonuç hesapla ve dön
    /// </summary>
    public class ProbeManager
    {
        private readonly MainControll _controller;
        
        // Sabitler
        private const int MaxProbeFeed = 300;

        // Son tespit edilen köşe (X,Y,Z)
        public (double X, double Y, double Z)? LastCorner { get; private set; }
        private const double FineToleranceThreshold = 0.06; // mm - iki ölçüm arası max fark
        private const int MaxFineAttempts = 6;

        public ProbeManager(MainControll controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        #region Public API

        /// <summary>Z probe - aşağı doğru</summary>
        public Task<ProbeResult> ProbeZAsync(bool manageSession = true)
        {
            return ExecuteProbeAsync('Z', -1, 30.0, 6.0, 2.0, 1.0, 10.0, manageSession);
        }

        /// <summary>X+ probe - sağa doğru</summary>
        public Task<ProbeResult> ProbeXPlusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return ExecuteProbeAsync('X', +1, maxDistance, 6.0, 2.0, 1.0, 10.0, manageSession);
        }

        /// <summary>X- probe - sola doğru</summary>
        public Task<ProbeResult> ProbeXMinusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return ExecuteProbeAsync('X', -1, maxDistance, 6.0, 2.0, 1.0, 10.0, manageSession);
        }

        /// <summary>Y+ probe - öne doğru</summary>
        public Task<ProbeResult> ProbeYPlusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return ExecuteProbeAsync('Y', +1, maxDistance, 6.0, 2.0, 1.0, 10.0, manageSession);
        }

        /// <summary>Y- probe - arkaya doğru</summary>
        public Task<ProbeResult> ProbeYMinusAsync(double maxDistance = 30.0, bool manageSession = true)
        {
            return ExecuteProbeAsync('Y', -1, maxDistance, 6.0, 2.0, 1.0, 10.0, manageSession);
        }

        /// <summary>
        /// Basit Z probe - Fine tuning YOK, sadece tek probe
        /// Center X/Y için kullanılır (kenar tespiti)
        /// </summary>
        /// <param name="maxDistance">Max iniş mesafesi (mm) - bu mesafede temas yoksa BAŞARISIZ</param>
        /// <param name="retractAfter">Probe sonrası geri çekilme mesafesi (0 = retract yok)</param>
        /// <returns>Temas pozisyonu veya hata</returns>
        public async Task<SimpleProbeResult> SimpleZProbeAsync(double maxDistance = 10.0, double retractAfter = 5.0, bool requirePrbContact = false)
        {
            try
            {
                Log($"🔍 Basit Z probe: max {maxDistance}mm");
                var probeStart = DateTime.UtcNow;

                // Feed hesapla - rapid/5 = daha hızlı (eski: /8)
                double rapid = GetAxisRapid('Z');
                int feed = Clamp((int)(rapid / 5), 1, MaxProbeFeed);

                // G91 - relative mode
                await SendCmd("G91");

                // Tek probe
                if (!await DoProbe('Z', -1, maxDistance, feed, "SimpleZ", requirePrbContact))
                {
                    await SendCmd("G90");
                    return SimpleProbeResult.Failed("Z probe temas etmedi - mesafe aşıldı");
                }

                // Koordinat oku
                await Task.Delay(200);
                double contact = await ReadContact('Z', probeStart);

                if (double.IsNaN(contact))
                {
                    await SendCmd("G90");
                    return SimpleProbeResult.Failed("Z koordinat okunamadı");
                }

                Log($"📍 Z temas: {contact:F3} mm");

                // Retract
                if (retractAfter > 0.1)
                {
                    await DoRetract('Z', 1, retractAfter, rapid);
                }

                await SendCmd("G90");
                return SimpleProbeResult.CreateSuccess(contact);
            }
            catch (Exception ex)
            {
                Log($"❌ SimpleZProbe hata: {ex.Message}");
                try { await SendCmd("G90"); } catch { }
                return SimpleProbeResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Center X - Parçanın X kenarını bul (şimdilik sadece sol kenar)
        /// 
        /// AKIŞ:
        /// 1. İlk coarse Z probe (referans)
        /// 2. 10mm geri çekil
        /// 3. 15mm sola (-X) git
        /// 4. Z probe (mesafe: 12mm = retract + 2mm)
        /// 5. Temas yoksa = parça dışına çıktık → DUR
        /// 6. Temas varsa → 3'e dön
        /// </summary>
        public async Task<CenterResult> CenterXAsync()
        {
            bool sessionStarted = false;
            IDisposable fastScope = null;
            Controls.StreamingPopup popup = null;

            const double retractHeight = 10.0;  // Geri çekilme mesafesi
            const double stepSize = 15.0;       // Yana kayma mesafesi
            const double probeDepth = 12.0;     // Probe mesafesi (retract + 2mm)
            const int maxSteps = 20;            // Güvenlik limiti

            // Cancel kontrolü için helper
            bool CheckCancelled()
            {
                bool cancelled = false;
                try { popup?.Dispatcher?.Invoke(() => cancelled = popup?.IsCancelled == true); } catch { }
                return cancelled;
            }

            // Helper: popup'a log yaz
            void PopupLog(string msg)
            {
                Log(msg);
                try { popup?.Dispatcher?.Invoke(() => popup.Append(msg)); } catch { }
            }

            try
            {
                Log("🎯 Center X başlıyor...");
                
                // Önceki probe marker'larını temizle
                Managers.GCodeOverlayManager.ClearProbeEdgeMarkers();

                // Session yönetimi
                if (!RunUiLocker.IsProbeSessionActive())
                {
                    RunUiLocker.BeginProbeSession();
                    sessionStarted = true;
                }

                fastScope = _controller?.BeginScopedCentralStatusOverride(100);

                if (_controller?.IsConnected != true)
                    return CenterResult.Failed("Bağlantı yok");

                // StreamingPopup aç
                try
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        popup = new Controls.StreamingPopup();
                        popup.ConfigureForProbe(); // Dar mod, iptal butonu görünür
                        popup.SetTitle("Center X - Kenar Tespit");
                        popup.SetSubtitle("Z probe ile parça kenarı bulunuyor...");
                        popup.Show();
                    });
                }
                catch { }

                // Başlangıç pozisyonunu kaydet
                double startX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                PopupLog($"📍 Başlangıç: X={startX:F3}");

                // ===== ADIM 1: İlk coarse Z probe =====
                PopupLog("🔍 ADIM 1: İlk Z probe (referans)...");
                var refProbe = await SimpleZProbeAsync(maxDistance: 30.0, retractAfter: 0, requirePrbContact: false);
                if (!refProbe.Success)
                {
                    PopupLog("❌ İlk Z probe başarısız");
                    await Task.Delay(2000);
                    popup?.ForceClose();
                    return CenterResult.Failed("İlk Z probe başarısız");
                }

                double referenceZ = refProbe.ContactPosition;
                PopupLog($"📏 Referans Z = {referenceZ:F3}");
                try { popup?.Dispatcher?.Invoke(() => popup.SetLiveLine($"Referans Z: {referenceZ:F3} mm")); } catch { }

                // ===== ADIM 2: Geri çekil =====
                PopupLog($"🔼 ADIM 2: {retractHeight}mm geri çekiliyor...");
                await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));

                // ===== ADIM 3-5: Döngü - sola git, probe, kontrol =====
                double currentX = startX;
                int step = 0;
                double lastContactZ = referenceZ;

                while (step < maxSteps)
                {
                    // Cancel kontrolü
                    if (CheckCancelled())
                    {
                        PopupLog("❌ İptal edildi");
                        popup?.ForceClose();
                        return CenterResult.Failed("Kullanıcı tarafından iptal edildi");
                    }
                    
                    step++;

                    // ADIM 3: Sola (-X) kay
                    currentX -= stepSize;
                    PopupLog($"◀️ Adım {step}: X = {currentX:F1} pozisyonuna gidiliyor...");
                    await SendCmd($"G90 G0 X{currentX.ToString("F3", CultureInfo.InvariantCulture)}");
                    await WaitIdle(10000, $"MoveX_{step}");

                    // ADIM 4: Z probe (mesafe: 12mm)
                    PopupLog($"🔍 Z probe ({probeDepth}mm mesafe)...");
                    var zProbe = await SimpleZProbeAsync(maxDistance: probeDepth, retractAfter: 0, requirePrbContact: false);

                    if (!zProbe.Success)
                    {
                        // ADIM 5: Temas yok = parça dışına çıktık
                        PopupLog($"✅ TEMAS YOK - Parça kenarı bulundu! (X~{currentX:F3})");

                        // === X PROBE ile köşe tespiti ===
                        PopupLog("➡️ X probe ile köşe saptanıyor...");
                        double sideProbeDist = stepSize + 10.0; // parça içine doğru yeterli mesafe
                        // X probe hızı: Z probe hızının yarısı
                        double zRapidForFeed = GetAxisRapid('Z');
                        int zFeedForProbe = Clamp((int)(zRapidForFeed / 5), 1, MaxProbeFeed);
                        int xFeed = Clamp(zFeedForProbe / 2, 1, MaxProbeFeed);
                        var xProbeStart = DateTime.UtcNow;

                        // Parça içine doğru +X yönünde probe (relative)
                        await SendCmd("G91");
                        bool xHit = await DoProbe('X', +1, sideProbeDist, xFeed, "CornerX");
                        await SendCmd("G90");

                        if (!xHit)
                        {
                            PopupLog("❌ X probe başarısız - köşe tespit edilemedi");
                            await Task.Delay(2000);
                            popup?.ForceClose();
                            return CenterResult.Failed("X köşe probe başarısız");
                        }

                        // X temas sonrası 3mm X retract (parçadan uzaklaş, -X yönü)
                        await DoRetract('X', -1, 3.0, GetAxisRapid('X'));

                        // X temas koordinatını al
                        double cornerX = await ReadContact('X', xProbeStart);
                        double cornerY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                        double cornerZ = _controller.MStatus?.Z ?? 0;

                        // Hafızaya kaydet
                        LastCorner = (cornerX, cornerY, cornerZ);
                        PopupLog($"📍 Köşe bulundu: X={cornerX:F3}, Y={cornerY:F3}, Z={cornerZ:F3}");

                        // İstenilen retract: 12mm (retract+2mm)
                        double finalRetract = 12.0;
                        PopupLog($"🔼 Son retract: {finalRetract}mm...");
                        await DoRetract('Z', 1, finalRetract, GetAxisRapid('Z'));

                        try 
                        { 
                            popup?.Dispatcher?.Invoke(() => 
                            {
                                popup.SetTitle("✅ Köşe Bulundu");
                                popup.SetLiveLine($"Köşe X: {cornerX:F3} mm");
                            }); 
                        } 
                        catch { }

                        await Task.Delay(1000);

                        // === SAĞ TARAF İÇİN TERS SEKANS ===
                        PopupLog("↔️ Sağ kenar için ters sekans başlıyor...");

                        // Başlangıç noktasına dön (yakınında)
                        currentX = startX;
                        PopupLog($"➡️ Başlangıca dönüş: X={currentX:F3}");
                        await SendCmd($"G90 G0 X{currentX.ToString("F3", CultureInfo.InvariantCulture)}");
                        await WaitIdle(10000, "BackToStart");

                        // Başlangıçta yeniden referans Z probe (güvenlik) - küçük mesafe
                        PopupLog("🔍 Referans Z kontrol...");
                        var refCheck = await SimpleZProbeAsync(maxDistance: 15.0, retractAfter: 0, requirePrbContact: false);
                        if (!refCheck.Success)
                        {
                            PopupLog("❌ Referans Z kontrol başarısız");
                            await Task.Delay(1500);
                            popup?.ForceClose();
                            return CenterResult.Failed("Referans Z kontrol başarısız");
                        }
                        await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));

                        // Sağa doğru tarama: dışarı çıkana kadar +X yönünde adım + Z probe
                        double leftX = cornerX; // ilk köşe
                        double rightX = double.NaN;
                        step = 0;
                        while (step < maxSteps)
                        {
                            // Cancel kontrolü
                            if (CheckCancelled())
                            {
                                PopupLog("❌ İptal edildi");
                                popup?.ForceClose();
                                return CenterResult.Failed("Kullanıcı tarafından iptal edildi");
                            }
                            
                            step++;
                            currentX += stepSize;
                            PopupLog($"▶️ Sağ tarama {step}: X={currentX:F1}...");
                            await SendCmd($"G90 G0 X{currentX.ToString("F3", CultureInfo.InvariantCulture)}");
                            await WaitIdle(10000, $"MoveX_R_{step}");

                            PopupLog($"🔍 Z probe (+{probeDepth}mm mesafe)...");
                            var zProbeR = await SimpleZProbeAsync(maxDistance: probeDepth, retractAfter: 0, requirePrbContact: false);
                            if (!zProbeR.Success)
                            {
                                PopupLog($"✅ Sağda dışarı çıkıldı: X~{currentX:F3}");

                                // -X yönüne X probe ile sağ kenarı bul
                                // X probe hızı: Z probe hızının yarısı
                                double zRapidForFeedR = GetAxisRapid('Z');
                                int zFeedForProbeR = Clamp((int)(zRapidForFeedR / 5), 1, MaxProbeFeed);
                                int xFeedR = Clamp(zFeedForProbeR / 2, 1, MaxProbeFeed);
                                var xProbeStartR = DateTime.UtcNow;

                                await SendCmd("G91");
                                bool xHitR = await DoProbe('X', -1, sideProbeDist, xFeedR, "CornerX_R");
                                await SendCmd("G90");

                                if (!xHitR)
                                {
                                    PopupLog("❌ Sağ X probe başarısız");
                                    await Task.Delay(1500);
                                    popup?.ForceClose();
                                    return CenterResult.Failed("Sağ X probe başarısız");
                                }

                                // X temas sonrası 3mm X retract (parçadan uzaklaş, +X yönü)
                                await DoRetract('X', +1, 3.0, GetAxisRapid('X'));

                                rightX = await ReadContact('X', xProbeStartR);
                                PopupLog($"📍 Sağ köşe: X={rightX:F3}");

                                // Son retract 12mm
                                PopupLog("🔼 Son retract: 12.0mm...");
                                await DoRetract('Z', 1, 12.0, GetAxisRapid('Z'));
                                break;
                            }

                            // Temas varsa retract ve devam
                            await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));
                        }

                        if (double.IsNaN(rightX))
                        {
                            PopupLog($"❌ Sağ kenar {maxSteps} adımda bulunamadı");
                            await Task.Delay(1500);
                            popup?.ForceClose();
                            return CenterResult.Failed("Sağ kenar bulunamadı");
                        }

                        // Merkez hesapla ve git
                        // Güvenlik: min/max üzerinden hesapla
                        double minX = Math.Min(leftX, rightX);
                        double maxX = Math.Max(leftX, rightX);
                        double centerX = (minX + maxX) / 2.0;
                        double width = maxX - minX;
                        PopupLog($"🎯 Merkez X hesaplandı: leftX={leftX:F3}, rightX={rightX:F3}, center={centerX:F3}, genişlik={width:F3}");
                        
                        // Canvas üzerine kenar marker'larını ekle
                        double currentY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                        Managers.GCodeOverlayManager.AddProbeEdgeMarker(minX, currentY, 
                            Managers.GCodeOverlayManager.ProbeEdgeType.LeftEdge, $"Sol:{minX:F1}");
                        Managers.GCodeOverlayManager.AddProbeEdgeMarker(maxX, currentY, 
                            Managers.GCodeOverlayManager.ProbeEdgeType.RightEdge, $"Sağ:{maxX:F1}");
                        
                        // Mevcut pozisyonu al (polling ile zaten güncel)
                        double currWX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                        double deltaX = centerX - currWX;
                        PopupLog($"📍 Mevcut X={currWX:F3}, Merkez={centerX:F3}, ΔX={deltaX:F3}");
                        PopupLog($"↔️ Merkeze hareket: Mevcut X={currWX:F3}, Hedef={centerX:F3}, ΔX={deltaX:F3}");
                        await SendCmd("G91");
                        await SendCmd($"G0 X{deltaX.ToString("F3", CultureInfo.InvariantCulture)}");
                        await SendCmd("G90");
                        await WaitIdle(10000, "MoveCenterX");
                        
                        // Merkez marker'ını ekle (X=0 ayarlandıktan sonra merkez 0,currentY olacak)
                        Managers.GCodeOverlayManager.AddProbeEdgeMarker(centerX, currentY, 
                            Managers.GCodeOverlayManager.ProbeEdgeType.Center, "Merkez");

                        // X=0 ayarla (kalıcı WCS)
                        try
                        {
                            bool zres = await _controller.SetZeroAxisAsync("X", permanent: true);
                            PopupLog(zres ? "✅ X=0 ayarlandı (G10 L20)" : "⚠️ X=0 ayarı başarısız");
                        }
                        catch { PopupLog("⚠️ X=0 ayarı sırasında hata"); }

                        try 
                        { 
                            popup?.Dispatcher?.Invoke(() => 
                            {
                                popup.SetTitle("✅ Orta Nokta Ayarlandı");
                                popup.SetLiveLine($"Merkez: X={(centerX):F3} (X=0)");
                            }); 
                        } 
                        catch { }

                        await Task.Delay(2000);
                        popup?.ForceClose();

                        return CenterResult.CreateSuccess(centerX, leftX, rightX, width);
                    }

                    // Temas var - farkı göster
                    double diff = zProbe.ContactPosition - lastContactZ;
                    PopupLog($"📍 Temas: Z = {zProbe.ContactPosition:F3}  (fark: {diff:+0.000;-0.000;0.000})");
                    lastContactZ = zProbe.ContactPosition;

                    try 
                    { 
                        popup?.Dispatcher?.Invoke(() => 
                            popup.SetLiveLine($"Z: {zProbe.ContactPosition:F3}  |  Fark: {diff:+0.000;-0.000;0.000}")
                        ); 
                    } 
                    catch { }

                    // Geri çekil ve tekrarla
                    PopupLog($"🔼 Retract {retractHeight}mm...");
                    await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));
                }

                PopupLog($"❌ {maxSteps} adımda kenar bulunamadı");
                await Task.Delay(2000);
                popup?.ForceClose();
                return CenterResult.Failed($"{maxSteps} adımda kenar bulunamadı");
            }
            catch (Exception ex)
            {
                Log($"❌ Hata: {ex.Message}");
                popup?.ForceClose();
                return CenterResult.Failed(ex.Message);
            }
            finally
            {
                try { await SendCmd("G90"); } catch { }
                try { popup?.ForceClose(); } catch { }
                if (sessionStarted) RunUiLocker.EndProbeSession();
                fastScope?.Dispose();
            }
        }

        /// <summary>
        /// Y ekseni orta nokta tespiti - parçanın ön ve arka kenarını bulur
        /// </summary>
        public async Task<CenterResult> CenterYAsync()
        {
            bool sessionStarted = false;
            IDisposable fastScope = null;
            Controls.StreamingPopup popup = null;

            const double retractHeight = 10.0;  // Geri çekilme mesafesi
            const double stepSize = 15.0;       // Öne/arkaya kayma mesafesi
            const double probeDepth = 12.0;     // Probe mesafesi (retract + 2mm)
            const int maxSteps = 20;            // Güvenlik limiti

            // Cancel kontrolü için helper
            bool CheckCancelled()
            {
                bool cancelled = false;
                try { popup?.Dispatcher?.Invoke(() => cancelled = popup?.IsCancelled == true); } catch { }
                return cancelled;
            }

            // Helper: popup'a log yaz
            void PopupLog(string msg)
            {
                Log(msg);
                try { popup?.Dispatcher?.Invoke(() => popup.Append(msg)); } catch { }
            }

            try
            {
                Log("🎯 Center Y başlıyor...");

                // Session yönetimi
                if (!RunUiLocker.IsProbeSessionActive())
                {
                    RunUiLocker.BeginProbeSession();
                    sessionStarted = true;
                }

                fastScope = _controller?.BeginScopedCentralStatusOverride(100);

                if (_controller?.IsConnected != true)
                    return CenterResult.Failed("Bağlantı yok");

                // StreamingPopup aç
                try
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        popup = new Controls.StreamingPopup();
                        popup.ConfigureForProbe(); // Dar mod, iptal butonu görünür
                        popup.SetTitle("Center Y - Kenar Tespit");
                        popup.SetSubtitle("Z probe ile parça kenarı bulunuyor...");
                        popup.Show();
                    });
                }
                catch { }

                // Başlangıç pozisyonunu kaydet
                double startY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                PopupLog($"📍 Başlangıç: Y={startY:F3}");

                // ===== ADIM 1: İlk coarse Z probe =====
                PopupLog("🔍 ADIM 1: İlk Z probe (referans)...");
                var refProbe = await SimpleZProbeAsync(maxDistance: 30.0, retractAfter: 0, requirePrbContact: false);
                if (!refProbe.Success)
                {
                    PopupLog("❌ İlk Z probe başarısız");
                    await Task.Delay(2000);
                    popup?.ForceClose();
                    return CenterResult.Failed("İlk Z probe başarısız");
                }

                double referenceZ = refProbe.ContactPosition;
                PopupLog($"📏 Referans Z = {referenceZ:F3}");
                try { popup?.Dispatcher?.Invoke(() => popup.SetLiveLine($"Referans Z: {referenceZ:F3} mm")); } catch { }

                // ===== ADIM 2: Geri çekil =====
                PopupLog($"🔼 ADIM 2: {retractHeight}mm geri çekiliyor...");
                await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));

                // ===== ADIM 3-5: Döngü - öne git (-Y), probe, kontrol =====
                double currentY = startY;
                int step = 0;
                double lastContactZ = referenceZ;

                while (step < maxSteps)
                {
                    // Cancel kontrolü
                    if (CheckCancelled())
                    {
                        PopupLog("❌ İptal edildi");
                        popup?.ForceClose();
                        return CenterResult.Failed("Kullanıcı tarafından iptal edildi");
                    }
                    
                    step++;

                    // ADIM 3: Öne (-Y) kay
                    currentY -= stepSize;
                    PopupLog($"▼ Adım {step}: Y = {currentY:F1} pozisyonuna gidiliyor...");
                    await SendCmd($"G90 G0 Y{currentY.ToString("F3", CultureInfo.InvariantCulture)}");
                    await WaitIdle(10000, $"MoveY_{step}");

                    // ADIM 4: Z probe (mesafe: 12mm)
                    PopupLog($"🔍 Z probe ({probeDepth}mm mesafe)...");
                    var zProbe = await SimpleZProbeAsync(maxDistance: probeDepth, retractAfter: 0, requirePrbContact: false);

                    if (!zProbe.Success)
                    {
                        // ADIM 5: Temas yok = parça dışına çıktık
                        PopupLog($"✅ TEMAS YOK - Parça kenarı bulundu! (Y~{currentY:F3})");

                        // === Y PROBE ile köşe tespiti ===
                        PopupLog("▲ Y probe ile köşe saptanıyor...");
                        double sideProbeDist = stepSize + 10.0; // parça içine doğru yeterli mesafe
                        // Y probe hızı: Z probe hızının yarısı
                        double zRapidForFeed = GetAxisRapid('Z');
                        int zFeedForProbe = Clamp((int)(zRapidForFeed / 5), 1, MaxProbeFeed);
                        int yFeed = Clamp(zFeedForProbe / 2, 1, MaxProbeFeed);
                        var yProbeStart = DateTime.UtcNow;

                        // Parça içine doğru +Y yönünde probe (relative)
                        await SendCmd("G91");
                        bool yHit = await DoProbe('Y', +1, sideProbeDist, yFeed, "CornerY");
                        await SendCmd("G90");

                        if (!yHit)
                        {
                            PopupLog("❌ Y probe başarısız - köşe tespit edilemedi");
                            await Task.Delay(2000);
                            popup?.ForceClose();
                            return CenterResult.Failed("Y köşe probe başarısız");
                        }

                        // Y temas sonrası 3mm Y retract (parçadan uzaklaş, -Y yönü)
                        await DoRetract('Y', -1, 3.0, GetAxisRapid('Y'));

                        // Y temas koordinatını al
                        double cornerY = await ReadContact('Y', yProbeStart);
                        double cornerX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                        double cornerZ = _controller.MStatus?.Z ?? 0;

                        // Hafızaya kaydet
                        LastCorner = (cornerX, cornerY, cornerZ);
                        PopupLog($"📍 Ön köşe bulundu: Y={cornerY:F3}, X={cornerX:F3}, Z={cornerZ:F3}");

                        // İstenilen retract: 12mm (retract+2mm)
                        double finalRetract = 12.0;
                        PopupLog($"🔼 Son retract: {finalRetract}mm...");
                        await DoRetract('Z', 1, finalRetract, GetAxisRapid('Z'));

                        try 
                        { 
                            popup?.Dispatcher?.Invoke(() => 
                            {
                                popup.SetTitle("✅ Ön Köşe Bulundu");
                                popup.SetLiveLine($"Ön Köşe Y: {cornerY:F3} mm");
                            }); 
                        } 
                        catch { }

                        await Task.Delay(1000);

                        // === ARKA TARAF İÇİN TERS SEKANS ===
                        PopupLog("↕️ Arka kenar için ters sekans başlıyor...");

                        // Başlangıç noktasına dön (yakınında)
                        currentY = startY;
                        PopupLog($"▲ Başlangıca dönüş: Y={currentY:F3}");
                        await SendCmd($"G90 G0 Y{currentY.ToString("F3", CultureInfo.InvariantCulture)}");
                        await WaitIdle(10000, "BackToStart");

                        // Başlangıçta yeniden referans Z probe (güvenlik) - küçük mesafe
                        PopupLog("🔍 Referans Z kontrol...");
                        var refCheck = await SimpleZProbeAsync(maxDistance: 15.0, retractAfter: 0, requirePrbContact: false);
                        if (!refCheck.Success)
                        {
                            PopupLog("❌ Referans Z kontrol başarısız");
                            await Task.Delay(1500);
                            popup?.ForceClose();
                            return CenterResult.Failed("Referans Z kontrol başarısız");
                        }
                        await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));

                        // Arkaya doğru tarama: dışarı çıkana kadar +Y yönünde adım + Z probe
                        double frontY = cornerY; // ilk köşe (ön)
                        double backY = double.NaN;
                        step = 0;
                        while (step < maxSteps)
                        {
                            // Cancel kontrolü
                            if (CheckCancelled())
                            {
                                PopupLog("❌ İptal edildi");
                                popup?.ForceClose();
                                return CenterResult.Failed("Kullanıcı tarafından iptal edildi");
                            }
                            
                            step++;
                            currentY += stepSize;
                            PopupLog($"▲ Arka tarama {step}: Y={currentY:F1}...");
                            await SendCmd($"G90 G0 Y{currentY.ToString("F3", CultureInfo.InvariantCulture)}");
                            await WaitIdle(10000, $"MoveY_R_{step}");

                            PopupLog($"🔍 Z probe (+{probeDepth}mm mesafe)...");
                            var zProbeR = await SimpleZProbeAsync(maxDistance: probeDepth, retractAfter: 0, requirePrbContact: false);
                            if (!zProbeR.Success)
                            {
                                PopupLog($"✅ Arkada dışarı çıkıldı: Y~{currentY:F3}");

                                // -Y yönüne Y probe ile arka kenarı bul
                                // Y probe hızı: Z probe hızının yarısı
                                double zRapidForFeedR = GetAxisRapid('Z');
                                int zFeedForProbeR = Clamp((int)(zRapidForFeedR / 5), 1, MaxProbeFeed);
                                int yFeedR = Clamp(zFeedForProbeR / 2, 1, MaxProbeFeed);
                                var yProbeStartR = DateTime.UtcNow;

                                await SendCmd("G91");
                                bool yHitR = await DoProbe('Y', -1, sideProbeDist, yFeedR, "CornerY_R");
                                await SendCmd("G90");

                                if (!yHitR)
                                {
                                    PopupLog("❌ Arka Y probe başarısız");
                                    await Task.Delay(1500);
                                    popup?.ForceClose();
                                    return CenterResult.Failed("Arka Y probe başarısız");
                                }

                                // Y temas sonrası 3mm Y retract (parçadan uzaklaş, +Y yönü)
                                await DoRetract('Y', +1, 3.0, GetAxisRapid('Y'));

                                backY = await ReadContact('Y', yProbeStartR);
                                PopupLog($"📍 Arka köşe: Y={backY:F3}");

                                // Son retract 12mm
                                PopupLog("🔼 Son retract: 12.0mm...");
                                await DoRetract('Z', 1, 12.0, GetAxisRapid('Z'));
                                break;
                            }

                            // Temas varsa retract ve devam
                            await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));
                        }

                        if (double.IsNaN(backY))
                        {
                            PopupLog($"❌ Arka kenar {maxSteps} adımda bulunamadı");
                            await Task.Delay(1500);
                            popup?.ForceClose();
                            return CenterResult.Failed("Arka kenar bulunamadı");
                        }

                        // Merkez hesapla ve git
                        // Güvenlik: min/max üzerinden hesapla
                        double minY = Math.Min(frontY, backY);
                        double maxY = Math.Max(frontY, backY);
                        double centerY = (minY + maxY) / 2.0;
                        double depth = maxY - minY;
                        PopupLog($"🎯 Merkez Y hesaplandı: frontY={frontY:F3}, backY={backY:F3}, center={centerY:F3}, derinlik={depth:F3}");
                        
                        // Canvas üzerine kenar marker'larını ekle
                        double currentX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                        Managers.GCodeOverlayManager.AddProbeEdgeMarker(currentX, minY, 
                            Managers.GCodeOverlayManager.ProbeEdgeType.FrontEdge, $"Ön:{minY:F1}");
                        Managers.GCodeOverlayManager.AddProbeEdgeMarker(currentX, maxY, 
                            Managers.GCodeOverlayManager.ProbeEdgeType.BackEdge, $"Arka:{maxY:F1}");
                        
                        // Mevcut pozisyonu al (polling ile zaten güncel)
                        double currWY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                        double deltaY = centerY - currWY;
                        PopupLog($"📍 Mevcut Y={currWY:F3}, Merkez={centerY:F3}, ΔY={deltaY:F3}");
                        PopupLog($"↕️ Merkeze hareket: Mevcut Y={currWY:F3}, Hedef={centerY:F3}, ΔY={deltaY:F3}");
                        await SendCmd("G91");
                        await SendCmd($"G0 Y{deltaY.ToString("F3", CultureInfo.InvariantCulture)}");
                        await SendCmd("G90");
                        await WaitIdle(10000, "MoveCenterY");
                        
                        // Merkez marker'ını ekle
                        Managers.GCodeOverlayManager.AddProbeEdgeMarker(currentX, centerY, 
                            Managers.GCodeOverlayManager.ProbeEdgeType.Center, "Merkez");

                        // Y=0 ayarla (kalıcı WCS)
                        try
                        {
                            bool zres = await _controller.SetZeroAxisAsync("Y", permanent: true);
                            PopupLog(zres ? "✅ Y=0 ayarlandı (G10 L20)" : "⚠️ Y=0 ayarı başarısız");
                        }
                        catch { PopupLog("⚠️ Y=0 ayarı sırasında hata"); }

                        try 
                        { 
                            popup?.Dispatcher?.Invoke(() => 
                            {
                                popup.SetTitle("✅ Orta Nokta Ayarlandı");
                                popup.SetLiveLine($"Merkez: Y={(centerY):F3} (Y=0)");
                            }); 
                        } 
                        catch { }

                        await Task.Delay(2000);
                        popup?.ForceClose();

                        return CenterResult.CreateSuccess(centerY, frontY, backY, depth);
                    }

                    // Temas var - farkı göster
                    double diff = zProbe.ContactPosition - lastContactZ;
                    PopupLog($"📍 Temas: Z = {zProbe.ContactPosition:F3}  (fark: {diff:+0.000;-0.000;0.000})");
                    lastContactZ = zProbe.ContactPosition;

                    try 
                    { 
                        popup?.Dispatcher?.Invoke(() => 
                            popup.SetLiveLine($"Z: {zProbe.ContactPosition:F3}  |  Fark: {diff:+0.000;-0.000;0.000}")
                        ); 
                    } 
                    catch { }

                    // Geri çekil ve tekrarla
                    PopupLog($"🔼 Retract {retractHeight}mm...");
                    await DoRetract('Z', 1, retractHeight, GetAxisRapid('Z'));
                }

                PopupLog($"❌ {maxSteps} adımda kenar bulunamadı");
                await Task.Delay(2000);
                popup?.ForceClose();
                return CenterResult.Failed($"{maxSteps} adımda kenar bulunamadı");
            }
            catch (Exception ex)
            {
                Log($"❌ Hata: {ex.Message}");
                popup?.ForceClose();
                return CenterResult.Failed(ex.Message);
            }
            finally
            {
                try { await SendCmd("G90"); } catch { }
                try { popup?.ForceClose(); } catch { }
                if (sessionStarted) RunUiLocker.EndProbeSession();
                fastScope?.Dispose();
            }
        }

        /// <summary>
        /// Center XY Outer: Önce X, sonra Y merkezleme yapar
        /// </summary>
        public async Task<CenterResult> CenterXYAsync()
        {
            Log("═══════════════════════════════════════════════════════════════");
            Log("🎯 CENTER XY OUTER BAŞLIYOR");
            Log("═══════════════════════════════════════════════════════════════");

            // Önce CenterX çalıştır
            Log("📐 AŞAMA 1: X Merkezleme...");
            var xResult = await CenterXAsync();
            
            if (!xResult.Success)
            {
                Log($"❌ Center X başarısız: {xResult.ErrorMessage}");
                return CenterResult.Failed($"X merkezleme hatası: {xResult.ErrorMessage}");
            }

            Log($"✅ X merkez bulundu. Genişlik: {xResult.Width:F3}mm");

            // Kısa bekleme
            await Task.Delay(500);

            // Sonra CenterY çalıştır
            Log("📐 AŞAMA 2: Y Merkezleme...");
            var yResult = await CenterYAsync();
            
            if (!yResult.Success)
            {
                Log($"❌ Center Y başarısız: {yResult.ErrorMessage}");
                return CenterResult.Failed($"Y merkezleme hatası: {yResult.ErrorMessage}");
            }

            Log($"✅ Y merkez bulundu. Derinlik: {yResult.Width:F3}mm");

            Log("═══════════════════════════════════════════════════════════════");
            Log($"🎯 CENTER XY TAMAMLANDI - X Genişlik: {xResult.Width:F3}mm, Y Derinlik: {yResult.Width:F3}mm");
            Log("═══════════════════════════════════════════════════════════════");

            // Her iki sonucu birleştirerek döndür
            return CenterResult.CreateXY(xResult, yResult);
        }

        /// <summary>
        /// Center X Inner: İç kenardan X merkezleme (Z probe YOK)
        /// Manuel Z konumundan başlayarak X ekseninde iki tarafa probe yapar
        /// </summary>
        public async Task<CenterResult> CenterXInnerAsync()
        {
            bool sessionStarted = false;
            IDisposable fastScope = null;
            Controls.StreamingPopup popup = null;

            const double maxProbeDistance = 50.0; // Tek yönde maksimum probe mesafesi

            // Cancel kontrolü için helper
            bool CheckCancelled()
            {
                bool cancelled = false;
                try { popup?.Dispatcher?.Invoke(() => cancelled = popup?.IsCancelled == true); } catch { }
                return cancelled;
            }

            // Helper: popup'a log yaz
            void PopupLog(string msg)
            {
                Log(msg);
                try { popup?.Dispatcher?.Invoke(() => popup.Append(msg)); } catch { }
            }

            try
            {
                Log("🎯 Center X Inner başlıyor...");
                
                // Önceki probe marker'larını temizle
                Managers.GCodeOverlayManager.ClearProbeEdgeMarkers();

                // Session yönetimi
                if (!RunUiLocker.IsProbeSessionActive())
                {
                    RunUiLocker.BeginProbeSession();
                    sessionStarted = true;
                }

                fastScope = _controller?.BeginScopedCentralStatusOverride(100);

                if (_controller?.IsConnected != true)
                    return CenterResult.Failed("Bağlantı yok");

                // StreamingPopup aç
                try
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        popup = new Controls.StreamingPopup();
                        popup.ConfigureForProbe();
                        popup.SetTitle("İç Merkez X - İç Kenar Tespit");
                        popup.SetSubtitle("X probe ile iç kenarlar bulunuyor...");
                        popup.Show();
                    });
                }
                catch { }

                // Başlangıç pozisyonunu kaydet
                double startX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                double startY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                PopupLog($"📍 Başlangıç: X={startX:F3}, Y={startY:F3}");

                // X probe feed hesapla (Z feed'in yarısı)
                double zRapid = GetAxisRapid('Z');
                int zFeed = Clamp((int)(zRapid / 5), 1, MaxProbeFeed);
                int xFeed = Clamp(zFeed / 2, 1, MaxProbeFeed);
                
                PopupLog($"⚙️ X Probe Hızı: {xFeed} mm/min");

                // ===== SOL KENAR (Negatif X yönünde probe) =====
                PopupLog("◀️ Sol kenar araması başlıyor (-X yönü)...");
                
                double leftX = double.NaN;
                var leftProbeStart = DateTime.UtcNow;
                
                // Relative mode
                await SendCmd("G91");
                bool leftHit = await DoProbe('X', -1, maxProbeDistance, xFeed, "InnerLeftX");
                await SendCmd("G90");

                if (!leftHit)
                {
                    PopupLog("❌ Sol kenar bulunamadı - temas yok");
                    await Task.Delay(2000);
                    popup?.ForceClose();
                    return CenterResult.Failed("Sol iç kenar probe başarısız");
                }

                // Sol kenar koordinatını oku
                leftX = await ReadContact('X', leftProbeStart);
                PopupLog($"✅ Sol kenar bulundu: X={leftX:F3}");

                // 3mm geri çekil (parçadan uzaklaş, +X yönü)
                PopupLog("➡️ Sol kenardan 3mm geri çekiliyor...");
                await DoRetract('X', +1, 3.0, GetAxisRapid('X'));

                // Cancel kontrolü
                if (CheckCancelled())
                {
                    PopupLog("❌ İptal edildi");
                    popup?.ForceClose();
                    return CenterResult.Failed("Kullanıcı tarafından iptal edildi");
                }

                // Başlangıç noktasına dön
                PopupLog($"🔄 Başlangıç noktasına dönülüyor: X={startX:F3}");
                await SendCmd($"G90 G0 X{startX.ToString("F3", CultureInfo.InvariantCulture)}");
                await WaitIdle(10000, "BackToStart");

                // ===== SAĞ KENAR (Pozitif X yönünde probe) =====
                PopupLog("▶️ Sağ kenar araması başlıyor (+X yönü)...");
                
                double rightX = double.NaN;
                var rightProbeStart = DateTime.UtcNow;
                
                // Relative mode
                await SendCmd("G91");
                bool rightHit = await DoProbe('X', +1, maxProbeDistance, xFeed, "InnerRightX");
                await SendCmd("G90");

                if (!rightHit)
                {
                    PopupLog("❌ Sağ kenar bulunamadı - temas yok");
                    await Task.Delay(2000);
                    popup?.ForceClose();
                    return CenterResult.Failed("Sağ iç kenar probe başarısız");
                }

                // Sağ kenar koordinatını oku
                rightX = await ReadContact('X', rightProbeStart);
                PopupLog($"✅ Sağ kenar bulundu: X={rightX:F3}");

                // 3mm geri çekil (parçadan uzaklaş, -X yönü)
                PopupLog("◀️ Sağ kenardan 3mm geri çekiliyor...");
                await DoRetract('X', -1, 3.0, GetAxisRapid('X'));

                // Merkez hesapla
                double minX = Math.Min(leftX, rightX);
                double maxX = Math.Max(leftX, rightX);
                double centerX = (minX + maxX) / 2.0;
                double width = maxX - minX;
                
                PopupLog($"🎯 Merkez hesaplandı:");
                PopupLog($"   Sol: {minX:F3} mm");
                PopupLog($"   Sağ: {maxX:F3} mm");
                PopupLog($"   Merkez: {centerX:F3} mm");
                PopupLog($"   İç Genişlik: {width:F3} mm");

                // Canvas üzerine marker'ları ekle
                Managers.GCodeOverlayManager.AddProbeEdgeMarker(minX, startY, 
                    Managers.GCodeOverlayManager.ProbeEdgeType.LeftEdge, $"Sol:{minX:F1}");
                Managers.GCodeOverlayManager.AddProbeEdgeMarker(maxX, startY, 
                    Managers.GCodeOverlayManager.ProbeEdgeType.RightEdge, $"Sağ:{maxX:F1}");

                // Merkeze git
                double currWX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                double deltaX = centerX - currWX;
                PopupLog($"↔️ Merkeze hareket: Mevcut={currWX:F3}, Hedef={centerX:F3}, ΔX={deltaX:F3}");
                
                await SendCmd("G91");
                await SendCmd($"G0 X{deltaX.ToString("F3", CultureInfo.InvariantCulture)}");
                await SendCmd("G90");
                await WaitIdle(10000, "MoveCenterX");
                
                // Merkez marker'ı ekle
                Managers.GCodeOverlayManager.AddProbeEdgeMarker(centerX, startY, 
                    Managers.GCodeOverlayManager.ProbeEdgeType.Center, "Merkez");

                // X=0 ayarla (kalıcı WCS)
                try
                {
                    bool zres = await _controller.SetZeroAxisAsync("X", permanent: true);
                    PopupLog(zres ? "✅ X=0 ayarlandı (G10 L20)" : "⚠️ X=0 ayarı başarısız");
                }
                catch { PopupLog("⚠️ X=0 ayarı sırasında hata"); }

                try 
                { 
                    popup?.Dispatcher?.Invoke(() => 
                    {
                        popup.SetTitle("✅ İç Merkez Bulundu");
                        popup.SetLiveLine($"Merkez: X={centerX:F3} (X=0), Genişlik: {width:F3}mm");
                    }); 
                } 
                catch { }

                await Task.Delay(2000);
                popup?.ForceClose();

                return CenterResult.CreateSuccess(centerX, minX, maxX, width);
            }
            catch (Exception ex)
            {
                Log($"❌ Hata: {ex.Message}");
                popup?.ForceClose();
                return CenterResult.Failed(ex.Message);
            }
            finally
            {
                try { await SendCmd("G90"); } catch { }
                try { popup?.ForceClose(); } catch { }
                if (sessionStarted) RunUiLocker.EndProbeSession();
                fastScope?.Dispose();
            }
        }

        /// <summary>
        /// Center Y Inner: İç kenardan Y merkezleme (Z probe YOK)
        /// Manuel Z konumundan başlayarak Y ekseninde iki tarafa probe yapar
        /// </summary>
        public async Task<CenterResult> CenterYInnerAsync()
        {
            bool sessionStarted = false;
            IDisposable fastScope = null;
            Controls.StreamingPopup popup = null;

            const double maxProbeDistance = 50.0; // Tek yönde maksimum probe mesafesi

            bool CheckCancelled()
            {
                bool cancelled = false;
                try { popup?.Dispatcher?.Invoke(() => cancelled = popup?.IsCancelled == true); } catch { }
                return cancelled;
            }

            void PopupLog(string msg)
            {
                Log(msg);
                try { popup?.Dispatcher?.Invoke(() => popup.Append(msg)); } catch { }
            }

            try
            {
                Log("🎯 Center Y Inner başlıyor...");
                Managers.GCodeOverlayManager.ClearProbeEdgeMarkers();

                if (!RunUiLocker.IsProbeSessionActive())
                {
                    RunUiLocker.BeginProbeSession();
                    sessionStarted = true;
                }

                fastScope = _controller?.BeginScopedCentralStatusOverride(100);
                if (_controller?.IsConnected != true)
                    return CenterResult.Failed("Bağlantı yok");

                // Popup
                try
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        popup = new Controls.StreamingPopup();
                        popup.ConfigureForProbe();
                        popup.SetTitle("İç Merkez Y - İç Kenar Tespit");
                        popup.SetSubtitle("Y probe ile iç kenarlar bulunuyor...");
                        popup.Show();
                    });
                }
                catch { }

                double startX = _controller.MStatus?.WorkX ?? _controller.MStatus?.X ?? 0;
                double startY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                PopupLog($"📍 Başlangıç: X={startX:F3}, Y={startY:F3}");

                // Y feed (Z rapid/5, yarısı)
                double zRapid = GetAxisRapid('Z');
                int zFeed = Clamp((int)(zRapid / 5), 1, MaxProbeFeed);
                int yFeed = Clamp(zFeed / 2, 1, MaxProbeFeed);
                PopupLog($"⚙️ Y Probe Hızı: {yFeed} mm/min");

                // ===== ÖN KENAR (Y- yönünde) =====
                PopupLog("⬇️ Ön kenar araması başlıyor (Y- yönü)...");
                double frontY = double.NaN;
                var frontProbeStart = DateTime.UtcNow;

                await SendCmd("G91");
                bool frontHit = await DoProbe('Y', -1, maxProbeDistance, yFeed, "InnerFrontY");
                await SendCmd("G90");
                if (!frontHit)
                {
                    PopupLog("❌ Ön kenar bulunamadı - temas yok");
                    await Task.Delay(2000);
                    popup?.ForceClose();
                    return CenterResult.Failed("Ön iç kenar probe başarısız");
                }
                frontY = await ReadContact('Y', frontProbeStart);
                PopupLog($"✅ Ön kenar bulundu: Y={frontY:F3}");
                PopupLog("🔼 Ön kenardan 3mm geri çekiliyor (+Y)...");
                await DoRetract('Y', +1, 3.0, GetAxisRapid('Y'));

                if (CheckCancelled())
                {
                    PopupLog("❌ İptal edildi");
                    popup?.ForceClose();
                    return CenterResult.Failed("Kullanıcı tarafından iptal edildi");
                }

                // Başlangıç noktasına dön
                PopupLog($"🔄 Başlangıç noktasına dönülüyor: Y={startY:F3}");
                await SendCmd($"G90 G0 Y{startY.ToString("F3", CultureInfo.InvariantCulture)}");
                await WaitIdle(10000, "BackToStartY");

                // ===== ARKA KENAR (Y+ yönünde) =====
                PopupLog("⬆️ Arka kenar araması başlıyor (Y+ yönü)...");
                double backY = double.NaN;
                var backProbeStart = DateTime.UtcNow;

                await SendCmd("G91");
                bool backHit = await DoProbe('Y', +1, maxProbeDistance, yFeed, "InnerBackY");
                await SendCmd("G90");
                if (!backHit)
                {
                    PopupLog("❌ Arka kenar bulunamadı - temas yok");
                    await Task.Delay(2000);
                    popup?.ForceClose();
                    return CenterResult.Failed("Arka iç kenar probe başarısız");
                }
                backY = await ReadContact('Y', backProbeStart);
                PopupLog($"✅ Arka kenar bulundu: Y={backY:F3}");
                PopupLog("🔽 Arka kenardan 3mm geri çekiliyor (-Y)...");
                await DoRetract('Y', -1, 3.0, GetAxisRapid('Y'));

                // Merkez ve derinlik
                double minY = Math.Min(frontY, backY);
                double maxY = Math.Max(frontY, backY);
                double centerY = (minY + maxY) / 2.0;
                double depth = maxY - minY;
                PopupLog("🎯 Merkez hesaplandı:");
                PopupLog($"   Ön: {minY:F3} mm");
                PopupLog($"   Arka: {maxY:F3} mm");
                PopupLog($"   Merkez: {centerY:F3} mm");
                PopupLog($"   İç Derinlik: {depth:F3} mm");

                // Marker'lar
                Managers.GCodeOverlayManager.AddProbeEdgeMarker(startX, minY,
                    Managers.GCodeOverlayManager.ProbeEdgeType.FrontEdge, $"Ön:{minY:F1}");
                Managers.GCodeOverlayManager.AddProbeEdgeMarker(startX, maxY,
                    Managers.GCodeOverlayManager.ProbeEdgeType.BackEdge, $"Arka:{maxY:F1}");

                // Merkeze hareket
                double currWY = _controller.MStatus?.WorkY ?? _controller.MStatus?.Y ?? 0;
                double deltaY = centerY - currWY;
                PopupLog($"↕️ Merkeze hareket: Mevcut={currWY:F3}, Hedef={centerY:F3}, ΔY={deltaY:F3}");
                await SendCmd("G91");
                await SendCmd($"G0 Y{deltaY.ToString("F3", CultureInfo.InvariantCulture)}");
                await SendCmd("G90");
                await WaitIdle(10000, "MoveCenterY");

                // Merkez marker
                Managers.GCodeOverlayManager.AddProbeEdgeMarker(startX, centerY,
                    Managers.GCodeOverlayManager.ProbeEdgeType.Center, "Merkez");

                // Y=0 ayarla
                try
                {
                    bool yres = await _controller.SetZeroAxisAsync("Y", permanent: true);
                    PopupLog(yres ? "✅ Y=0 ayarlandı (G10 L20)" : "⚠️ Y=0 ayarı başarısız");
                }
                catch { PopupLog("⚠️ Y=0 ayarı sırasında hata"); }

                try
                {
                    popup?.Dispatcher?.Invoke(() =>
                    {
                        popup.SetTitle("✅ İç Merkez Bulundu");
                        popup.SetLiveLine($"Merkez: Y={centerY:F3} (Y=0), Derinlik: {depth:F3}mm");
                    });
                }
                catch { }

                await Task.Delay(2000);
                popup?.ForceClose();

                return CenterResult.CreateSuccess(centerY, minY, maxY, depth);
            }
            catch (Exception ex)
            {
                Log($"❌ Hata: {ex.Message}");
                popup?.ForceClose();
                return CenterResult.Failed(ex.Message);
            }
            finally
            {
                try { await SendCmd("G90"); } catch { }
                try { popup?.ForceClose(); } catch { }
                if (sessionStarted) RunUiLocker.EndProbeSession();
                fastScope?.Dispose();
            }
        }

        #endregion

        #region Ana Probe Akışı

        /// <summary>
        /// Tek bir probe sekansı çalıştır
        /// </summary>
        private async Task<ProbeResult> ExecuteProbeAsync(
            char axis, int dir,
            double coarseDist, double fineDist,
            double retractCoarse, double retractFine, double retractFinal,
            bool manageSession)
        {
            bool sessionStarted = false;
            IDisposable fastScope = null;

            try
            {
                Log($"🔧 {axis} Probe başlıyor (yön: {(dir > 0 ? "+" : "-")})...");

                // Session yönetimi
                if (manageSession && !RunUiLocker.IsProbeSessionActive())
                {
                    RunUiLocker.BeginProbeSession();
                    sessionStarted = true;
                }

                // Hızlı status güncellemesi
                fastScope = _controller?.BeginScopedCentralStatusOverride(100);

                // Bağlantı kontrolü
                if (_controller?.IsConnected != true)
                    return ProbeResult.Failed("Bağlantı yok");

                // Feed hesapla
                double rapid = GetAxisRapid(axis);
                int coarseFeed = Clamp((int)(rapid / 8), 1, MaxProbeFeed);
                int fineFeed = Clamp((int)(rapid / 15), 1, MaxProbeFeed);

                Log($"📏 Rapid: {rapid:F0}, Coarse: {coarseFeed}, Fine: {fineFeed} mm/min");

                // G91 - relative mode
                await SendCmd("G91");
                Log("⚙️ G91 aktif");

                // ========== COARSE PROBE ==========
                Log($"🔍 Coarse probe başlıyor: {coarseDist}mm");
                
                if (!await DoProbe(axis, dir, coarseDist, coarseFeed, "Coarse"))
                    return Fail("Coarse probe başarısız");

                Log("✅ Coarse temas");

                // ========== RETRACT ==========
                await DoRetract(axis, -dir, retractCoarse, rapid);

                // ========== FINE PROBES ==========
                var readings = new List<double>();

                for (int i = 0; i < MaxFineAttempts; i++)
                {
                    int step = i + 1;
                    Log($"🎯 Fine#{step} başlıyor...");

                    // Retract before fine probe
                    if (i > 0) // İlk fine probe için coarse retract yeterli
                        await DoRetract(axis, -dir, retractFine, rapid);

                    // Fine probe
                    DateTime probeStart = DateTime.UtcNow;
                    if (!await DoProbe(axis, dir, fineDist, fineFeed, $"Fine#{step}"))
                        return Fail($"Fine#{step} başarısız");

                    // Koordinat oku
                    await Task.Delay(400);
                    double contact = await ReadContact(axis, probeStart);
                    
                    if (double.IsNaN(contact))
                        return Fail($"Fine#{step} koordinat okunamadı");

                    readings.Add(contact);
                    Log($"📍 Fine#{step}: {contact:F3} mm");

                    // Doğrulama - en az 2 ölçüm lazım
                    if (readings.Count >= 2)
                    {
                        var (validated, avg, tol, idxA, idxB) = ValidateReadings(readings);
                        if (validated)
                        {
                            Log($"✅ Doğrulama OK: #{idxA + 1} ve #{idxB + 1}, fark={tol:F3}mm");

                            // Final retract
                            await DoRetract(axis, -dir, retractFinal, rapid);
                            
                            // G90 - absolute mode
                            await SendCmd("G90");

                            return ProbeResult.CreateSuccess(avg, tol, readings, idxA, idxB);
                        }
                    }
                }

                // Doğrulama başarısız
                string vals = string.Join(", ", readings.Select(r => r.ToString("F3")));
                return Fail($"Doğrulama başarısız: {vals}");
            }
            catch (Exception ex)
            {
                Log($"❌ Hata: {ex.Message}");
                return Fail(ex.Message);
            }
            finally
            {
                try { await SendCmd("G90"); } catch { }
                if (sessionStarted) RunUiLocker.EndProbeSession();
                fastScope?.Dispose();
            }
        }

        #endregion

        #region Temel İşlemler

        /// <summary>
        /// Probe komutu gönder ve tamamlanmasını bekle
        /// PRB success flag = 0 ise temas yok → false döner
        /// </summary>
        private async Task<bool> DoProbe(char axis, int dir, double dist, int feed, string ctx, bool requirePrbForSuccess = false)
        {
            bool querierWasOn = _controller.CentralStatusQuerierEnabled;
            DateTime probeStartTime = DateTime.UtcNow;
            double beforePos = double.NaN;

            try
            {
                // Alarm temizle
                await _controller.SendGCodeCommandAsync("$X");
                await Task.Delay(100);

                // Querier durdur (probe sırasında karışmasın)
                if (querierWasOn)
                {
                    _controller.StopCentralStatusQuerier();
                    await Task.Delay(50);
                }

                // Ölçüm öncesi konumu tazele
                try { await _controller.SendGCodeCommandAsync("?"); await Task.Delay(80); } catch { }
                beforePos = GetAxisPosition(axis);

                // Probe komutu
                string cmd = $"G38.2 {axis}{(dir * dist).ToString("F3", CultureInfo.InvariantCulture)} F{feed}";
                Log($"📤 {ctx}: {cmd}");

                await _controller.SendGCodeCommandAsync(cmd);

                // Idle bekle - Alarm = temas yok = başarısız
                if (!await WaitIdle(30000, ctx, alarmMeansFailure: true))
                {
                    Log($"❌ {ctx}: Probe başarısız (alarm veya timeout)");
                    return false;
                }

                // Read position after motion for fallback decision
                try { await _controller.SendGCodeCommandAsync("?"); await Task.Delay(120); } catch { }
                double afterPos = GetAxisPosition(axis);

                // PRB mesajını bekle (max 700ms) - varsa success flag'ı değerlendir
                Log($"🔍 {ctx}: PRB mesajı bekleniyor (opsiyonel)...");
                var deadline = DateTime.UtcNow.AddMilliseconds(700);
                bool prbSeen = false;

                while (DateTime.UtcNow < deadline)
                {
                    if (CncControlApp.Managers.ProbeContactCache.LastProbeTime > probeStartTime)
                    {
                        prbSeen = true;
                        break;
                    }
                    await Task.Delay(30);
                }

                if (prbSeen)
                {
                    bool success = CncControlApp.Managers.ProbeContactCache.LastProbeSuccess;
                    Log($"📍 {ctx}: PRB success={success}");
                    if (!success)
                    {
                        Log($"⚠️ {ctx}: PRB success=0 - Temas YOK");
                        return false;
                    }
                    Log($"✅ {ctx}: PRB success=1 - Temas VAR");
                }
                else
                {
                    // Fallback: movement-based contact inference
                    // If traveled nearly full distance, assume NO CONTACT; otherwise CONTACT
                    if (!double.IsNaN(beforePos) && !double.IsNaN(afterPos))
                    {
                        double traveled = Math.Abs(afterPos - beforePos);
                        double target = Math.Abs(dist);
                        double tol = 0.15; // mm tolerance
                        bool fullTravel = traveled >= (target - tol);
                        Log($"ℹ️ {ctx}: PRB yok, hareket={traveled:F3} (hedef {target:F3}) → {(fullTravel ? "NO-CONTACT" : "CONTACT")}");
                        if (fullTravel) return false; // no contact
                        return true; // contact
                    }

                    if (requirePrbForSuccess)
                    {
                        Log($"⚠️ {ctx}: PRB yok ve konum okunamadı → başarısız");
                        return false;
                    }

                    // PRB yok, pozisyon da yok → başarı varsay (eski davranış)
                    Log($"ℹ️ {ctx}: PRB/konum yok → başarı varsayıldı");
                }
                return true;
            }
            finally
            {
                // Querier'ı geri başlat
                if (querierWasOn)
                {
                    await Task.Delay(50);
                    _controller.StartCentralStatusQuerier();
                }
            }
        }

        private double GetAxisPosition(char axis)
        {
            try
            {
                var st = _controller.MStatus;
                if (st == null) return double.NaN;
                if (axis == 'X') return double.IsNaN(st.WorkX) ? st.X : st.WorkX;
                if (axis == 'Y') return double.IsNaN(st.WorkY) ? st.Y : st.WorkY;
                return st.Z;
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// JOG ile geri çekil ($J komutu alarm durumunda bile çalışır)
        /// </summary>
        private async Task DoRetract(char axis, int dir, double dist, double rapid)
        {
            Log($"🔼 Retract: {dist:F1}mm");

            // Alarm temizle
            await _controller.SendGCodeCommandAsync("$X");
            await Task.Delay(100);

            // JOG komutu
            string jog = $"$J=G91 {axis}{(dir * dist).ToString("F3", CultureInfo.InvariantCulture)} F{rapid:F0}";
            await _controller.SendGCodeCommandAsync(jog);

            // Tamamlanmasını bekle
            await WaitIdle(5000, "Retract");
        }

        /// <summary>
        /// Idle durumunu bekle (Probe sırasında alarm = başarısız probe demek)
        /// </summary>
        private async Task<bool> WaitIdle(int timeoutMs, string ctx, bool alarmMeansFailure = false)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                // Status sorgula
                await _controller.SendGCodeCommandAsync("?");
                await Task.Delay(80);

                string status = _controller.MachineStatus ?? "";

                if (status.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (status.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase))
                {
                    if (alarmMeansFailure)
                    {
                        Log($"⚠️ {ctx}: Alarm tespit edildi - Probe BAŞARISIZ");
                        // Alarm temizle ama false dön
                        await _controller.SendGCodeCommandAsync("$X");
                        await Task.Delay(100);
                        return false;
                    }
                    else
                    {
                        Log($"⚠️ {ctx}: Alarm - $X gönderiliyor");
                        await _controller.SendGCodeCommandAsync("$X");
                        await Task.Delay(100);
                    }
                }

                await Task.Delay(50);
            }

            return false;
        }

        /// <summary>
        /// Temas koordinatını oku (PRB cache veya MStatus'tan)
        /// PRB koordinatları MPos (makine koordinatı) olarak gelir, Work koordinatına dönüştürülür
        /// </summary>
        private async Task<double> ReadContact(char axis, DateTime since)
        {
            // Mevcut status'u al (polling ile zaten güncel)
            var st = _controller.MStatus;
            
            // PRB cache'den dene
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                if (ProbeContactCache.TryGetAfter(since, out double mx, out double my, out double mz, out _))
                {
                    // PRB değerleri MPos (makine koordinatı) - Work koordinatına dönüştür
                    // WCS offset = MPos - WorkPos → WorkPos = MPos - offset
                    if (st != null)
                    {
                        double offsetX = st.X - (double.IsNaN(st.WorkX) ? st.X : st.WorkX);
                        double offsetY = st.Y - (double.IsNaN(st.WorkY) ? st.Y : st.WorkY);
                        // Z için offset yok (genelde)
                        
                        double workX = mx - offsetX;
                        double workY = my - offsetY;
                        double workZ = mz;
                        
                        System.Diagnostics.Debug.WriteLine($"[ReadContact] PRB MPos: X={mx:F3}, Y={my:F3}, Z={mz:F3}");
                        System.Diagnostics.Debug.WriteLine($"[ReadContact] WCS offset: X={offsetX:F3}, Y={offsetY:F3}");
                        System.Diagnostics.Debug.WriteLine($"[ReadContact] Work coord: X={workX:F3}, Y={workY:F3}, Z={workZ:F3}");
                        
                        return axis == 'X' ? workX : axis == 'Y' ? workY : workZ;
                    }
                    
                    // Fallback: offset hesaplanamadıysa MPos döndür (eski davranış)
                    return axis == 'X' ? mx : axis == 'Y' ? my : mz;
                }
                await Task.Delay(10);
            }

            // Fallback: PRB gelmedi, MStatus'tan Work koordinat al
            st = _controller.MStatus;
            if (st == null) return double.NaN;

            // X/Y için Work koordinat tercih et
            if (axis == 'X') return double.IsNaN(st.WorkX) ? st.X : st.WorkX;
            if (axis == 'Y') return double.IsNaN(st.WorkY) ? st.Y : st.WorkY;
            return st.Z;
        }

        /// <summary>
        /// Ölçümleri doğrula - birbirine yakın iki değer bul
        /// </summary>
        private (bool ok, double avg, double tol, int a, int b) ValidateReadings(List<double> r)
        {
            double minDiff = double.MaxValue;
            int bestA = -1, bestB = -1;

            for (int i = 0; i < r.Count - 1; i++)
            {
                for (int j = i + 1; j < r.Count; j++)
                {
                    double diff = Math.Abs(r[i] - r[j]);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        bestA = i;
                        bestB = j;
                    }
                }
            }

            if (minDiff < FineToleranceThreshold)
            {
                double avg = (r[bestA] + r[bestB]) / 2.0;
                return (true, avg, minDiff, bestA, bestB);
            }

            return (false, 0, 0, -1, -1);
        }

        #endregion

        #region Yardımcılar

        private void Log(string msg) => _controller?.AddLogMessage($"> {msg}");

        private async Task<bool> SendCmd(string cmd)
        {
            return await _controller.SendGCodeAndWaitAsync(cmd, 2000);
        }

        private double GetAxisRapid(char axis)
        {
            int id = axis == 'X' ? 110 : axis == 'Y' ? 111 : 112;
            try
            {
                var s = _controller?.Settings?.FirstOrDefault(x => x.Id == id);
                if (s != null && double.TryParse(s.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    return Math.Max(1, v);
            }
            catch { }
            return 1000;
        }

        private static int Clamp(int val, int min, int max)
        {
            return val < min ? min : val > max ? max : val;
        }

        private ProbeResult Fail(string msg)
        {
            Log($"❌ {msg}");
            return ProbeResult.Failed(msg);
        }

        #endregion
    }

    #region Probe Result

    public class ProbeResult
    {
        public bool Success { get; set; }
        public double ContactPosition { get; set; }
        public double Tolerance { get; set; }
        public List<double> FineReadings { get; set; }
        public int UsedIndexA { get; set; }
        public int UsedIndexB { get; set; }
        public string ErrorMessage { get; set; }

        public static ProbeResult CreateSuccess(double pos, double tol, List<double> readings, int a, int b)
        {
            return new ProbeResult
            {
                Success = true,
                ContactPosition = pos,
                Tolerance = tol,
                FineReadings = readings,
                UsedIndexA = a,
                UsedIndexB = b
            };
        }

        public static ProbeResult Failed(string error, List<double> readings = null)
        {
            return new ProbeResult
            {
                Success = false,
                ErrorMessage = error,
                ContactPosition = double.NaN,
                FineReadings = readings
            };
        }
    }

    #endregion

    #region Center Result

    public class CenterResult
    {
        public bool Success { get; set; }
        public double CenterPosition { get; set; }
        public double LeftEdge { get; set; }
        public double RightEdge { get; set; }
        public double Width { get; set; }
        public double Depth { get; set; }  // Y için derinlik
        public string ErrorMessage { get; set; }

        public static CenterResult CreateSuccess(double center, double left, double right, double width)
        {
            return new CenterResult
            {
                Success = true,
                CenterPosition = center,
                LeftEdge = left,
                RightEdge = right,
                Width = width
            };
        }

        /// <summary>
        /// CenterXY için her iki sonucu birleştir
        /// </summary>
        public static CenterResult CreateXY(CenterResult xResult, CenterResult yResult)
        {
            return new CenterResult
            {
                Success = true,
                CenterPosition = 0,  // X ve Y sıfırlandı
                LeftEdge = xResult.LeftEdge,
                RightEdge = xResult.RightEdge,
                Width = xResult.Width,
                Depth = yResult.Width  // Y'nin width'i aslında depth
            };
        }

        public static CenterResult Failed(string error)
        {
            return new CenterResult
            {
                Success = false,
                ErrorMessage = error,
                CenterPosition = double.NaN
            };
        }
    }

    #endregion

    #region Simple Probe Result

    /// <summary>
    /// Basit probe sonucu (fine tuning yok)
    /// </summary>
    public class SimpleProbeResult
    {
        public bool Success { get; set; }
        public double ContactPosition { get; set; }
        public string ErrorMessage { get; set; }

        public static SimpleProbeResult CreateSuccess(double position)
        {
            return new SimpleProbeResult
            {
                Success = true,
                ContactPosition = position
            };
        }

        public static SimpleProbeResult Failed(string error)
        {
            return new SimpleProbeResult
            {
                Success = false,
                ErrorMessage = error,
                ContactPosition = double.NaN
            };
        }
    }

    #endregion
}
