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
        /// </summary>
        private async Task<bool> DoProbe(char axis, int dir, double dist, int feed, string ctx)
        {
            bool querierWasOn = _controller.CentralStatusQuerierEnabled;

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

                // Probe komutu
                string cmd = $"G38.2 {axis}{(dir * dist).ToString("F3", CultureInfo.InvariantCulture)} F{feed}";
                Log($"📤 {ctx}: {cmd}");

                await _controller.SendGCodeCommandAsync(cmd);

                // Idle bekle
                if (!await WaitIdle(30000, ctx))
                {
                    Log($"❌ {ctx}: Timeout");
                    return false;
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
        /// Idle durumunu bekle
        /// </summary>
        private async Task<bool> WaitIdle(int timeoutMs, string ctx)
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
                    Log($"⚠️ {ctx}: Alarm - $X gönderiliyor");
                    await _controller.SendGCodeCommandAsync("$X");
                    await Task.Delay(100);
                }

                await Task.Delay(50);
            }

            return false;
        }

        /// <summary>
        /// Temas koordinatını oku (PRB cache veya MStatus'tan)
        /// </summary>
        private async Task<double> ReadContact(char axis, DateTime since)
        {
            // PRB cache'den dene
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                if (ProbeContactCache.TryGetAfter(since, out double x, out double y, out double z, out _))
                {
                    return axis == 'X' ? x : axis == 'Y' ? y : z;
                }
                await Task.Delay(50);
            }

            // Fallback: MStatus
            await Task.Delay(100);
            var st = _controller.MStatus;
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
}
