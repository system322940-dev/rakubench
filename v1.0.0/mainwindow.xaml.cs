using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace RakuBench
{
    public partial class MainWindow : Window
    {
        private bool isGpuBenchmarking = false;
        private Stopwatch gpuSw;
        private int currentFrame = 0;
        private const int TotalTargetFrames = 300;
        private double[] particleXs;
        private double[] particleYs;
        private const int ParticleCount = 30000;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void CpuStartButton_Click(object sender, RoutedEventArgs e)
        {
            LockAllButtons();
            CpuStartButton.Content = "計測中...";
            ProgressIndicator.IsIndeterminate = true;
            ScoreText.Text = "計測中...";
            ResultTextBox.Clear();

            int coreCount = Environment.ProcessorCount;
            ResultTextBox.AppendText($"[System] 論理プロセッサ数: {coreCount}\n");
            ResultTextBox.AppendText($"[System] CPU計測を開始します。\n\n");

            var (score, log) = await Task.Run(() => RunCpuHeavyBenchmark());

            ResultTextBox.AppendText(log);
            ScoreText.Text = score.ToString("N0");
            ProgressIndicator.IsIndeterminate = false;
            UnlockAllButtons();
        }

        private (int score, string log) RunCpuHeavyBenchmark()
        {
            var sb = new StringBuilder();
            var totalSw = Stopwatch.StartNew();

            sb.AppendLine("--- Test 1: Multi-Core Integer Math (Prime Search) ---");
            int maxNumber = 50_000_000;
            int primeCount = 0;
            var sw1 = Stopwatch.StartNew();
            Parallel.For(2, maxNumber, (i) =>
            {
                bool isPrime = true;
                int limit = (int)Math.Sqrt(i);
                for (int j = 2; j <= limit; j++) { if (i % j == 0) { isPrime = false; break; } }
                if (isPrime) { Interlocked.Increment(ref primeCount); }
            });
            sw1.Stop();
            sb.AppendLine($"処理時間: {sw1.ElapsedMilliseconds} ms\n");

            sb.AppendLine("--- Test 2: Multi-Core Floating Point (Trigonometry) ---");
            int floatIterations = 200_000_000;
            var sw2 = Stopwatch.StartNew();
            Parallel.For(0, floatIterations, (i) => { double val = Math.Sin(i) * Math.Cos(i) * Math.Sqrt(i); });
            sw2.Stop();
            sb.AppendLine($"処理時間: {sw2.ElapsedMilliseconds} ms\n");

            totalSw.Stop();

            double baseIntegerScore = 10000000.0 / sw1.ElapsedMilliseconds;
            double baseFloatScore = 15000000.0 / sw2.ElapsedMilliseconds;
            int finalScore = (int)((baseIntegerScore + baseFloatScore) * 100);

            sb.AppendLine("==========================================");
            sb.AppendLine($"総合処理時間: {totalSw.ElapsedMilliseconds} ms");
            sb.AppendLine("==========================================");

            return (finalScore, sb.ToString());
        }

        private async void DramStartButton_Click(object sender, RoutedEventArgs e)
        {
            LockAllButtons();
            DramStartButton.Content = "計測中...";
            ProgressIndicator.IsIndeterminate = true;
            ScoreText.Text = "計測中...";
            ResultTextBox.Clear();
            ResultTextBox.AppendText($"[System] DRAM計測を開始します。\n\n");

            var result = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                int totalBytes = 256 * 1024 * 1024;
                int elementCount = totalBytes / sizeof(long);

                sb.AppendLine("--- Test 3: DRAM Sequential Write ---");
                long[] memoryBuffer = new long[elementCount];
                var dramSw = Stopwatch.StartNew();
                for (int i = 0; i < elementCount; i++) { memoryBuffer[i] = i; }
                dramSw.Stop();

                double speed = ((double)totalBytes / (1024 * 1024)) / dramSw.Elapsed.TotalSeconds;
                sb.AppendLine($"処理時間: {dramSw.ElapsedMilliseconds} ms");
                sb.AppendLine($"転送速度: {speed:F2} MB/s\n");

                memoryBuffer = null;
                GC.Collect();

                return new { Speed = speed, Log = sb.ToString() };
            });

            ResultTextBox.AppendText(result.Log);
            ScoreText.Text = $"{result.Speed:F1}";
            UnlockAllButtons();
        }

        private async void RomStartButton_Click(object sender, RoutedEventArgs e)
        {
            LockAllButtons();
            RomStartButton.Content = "計測中...";
            ProgressIndicator.IsIndeterminate = true;
            ScoreText.Text = "計測中...";
            ResultTextBox.Clear();
            ResultTextBox.AppendText($"[System] ROM 計測を開始します。\n\n");

            string testFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rakubench_test.dat");

            var result = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                long testSize = 1024L * 1024L * 1024L;
                byte[] dummyData = new byte[testSize];
                Random rand = new Random();
                rand.NextBytes(dummyData);

                sb.AppendLine("--- Test 4: ROM Sequential Write & Read (1GB) ---");

                var writeSw = Stopwatch.StartNew();
                File.WriteAllBytes(testFilePath, dummyData);
                writeSw.Stop();

                double writeSpeed = ((double)testSize / (1024 * 1024)) / writeSw.Elapsed.TotalSeconds;
                sb.AppendLine($"書き込み速度: {writeSpeed:F2} MB/s\n");

                var readSw = Stopwatch.StartNew();
                byte[] readData = File.ReadAllBytes(testFilePath);
                readSw.Stop();

                double readSpeed = ((double)testSize / (1024 * 1024)) / readSw.Elapsed.TotalSeconds;
                sb.AppendLine($"読み込み速度: {readSpeed:F2} MB/s\n");

                if (File.Exists(testFilePath)) { File.Delete(testFilePath); }

                return new { Log = sb.ToString(), WriteSpeed = writeSpeed, ReadSpeed = readSpeed };
            });

            ResultTextBox.AppendText(result.Log);
            ScoreText.Text = $"{result.WriteSpeed:F0}/{result.ReadSpeed:F0}";
            UnlockAllButtons();
        }

        private void GpuStartButton_Click(object sender, RoutedEventArgs e)
        {
            LockAllButtons();
            GpuStartButton.Content = "計測中...";
            ProgressIndicator.IsIndeterminate = true;
            ScoreText.Text = "計測中...";
            ResultTextBox.Clear();

            long totalComputations = (long)ParticleCount * TotalTargetFrames;
            ResultTextBox.AppendText($"[System] GPU描画計測を開始します。\n");
            ResultTextBox.AppendText($"[System] すべての描画処理を完遂するまで計測を継続します...\n\n");

            particleXs = new double[ParticleCount];
            particleYs = new double[ParticleCount];
            Random rand = new Random();
            for (int i = 0; i < ParticleCount; i++)
            {
                particleXs[i] = rand.Next(0, 600);
                particleYs[i] = rand.Next(0, 200);
            }

            currentFrame = 0;
            isGpuBenchmarking = true;
            gpuSw = Stopwatch.StartNew();

            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (!isGpuBenchmarking) return;

            for (int i = 0; i < ParticleCount; i++)
            {
                particleXs[i] = (particleXs[i] + 1) % 600;
                particleYs[i] = (particleYs[i] + 0.5) % 200;
            }

            currentFrame++;

            if (currentFrame >= TotalTargetFrames)
            {
                CompositionTarget.Rendering -= OnRendering;
                isGpuBenchmarking = false;
                gpuSw.Stop();

                long elapsedMs = gpuSw.ElapsedMilliseconds;
                double fps = (double)TotalTargetFrames / (elapsedMs / 1000.0);

                var sb = new StringBuilder();
                sb.AppendLine("--- Test 5: GPU Particle Rendering (Complete) ---");
                sb.AppendLine($"完遂までにかかった時間: {elapsedMs} ms");
                sb.AppendLine($"平均フレームレート: {fps:F2} FPS");

                ResultTextBox.AppendText(sb.ToString());
                ScoreText.Text = $"{elapsedMs}ms / {fps:F0}F";

                ProgressIndicator.IsIndeterminate = false;
                UnlockAllButtons();
            }
        }

        private async void NetStartButton_Click(object sender, RoutedEventArgs e)
        {
            LockAllButtons();
            NetStartButton.Content = "計測中...";
            ProgressIndicator.IsIndeterminate = true;
            ScoreText.Text = "計測中...";
            ResultTextBox.Clear();
            ResultTextBox.AppendText("[System] ネット速度計測（ダウンロード）を開始します。\n");
            ResultTextBox.AppendText("[System] テスト用ファイルをダウンロード中...\n\n");

            string testUrl = "https://atlas.microsoft.com/sdk/javascript/mapcontrol/2/atlas.min.js";

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var sw = Stopwatch.StartNew();
                    byte[] data = await client.GetByteArrayAsync(testUrl);
                    sw.Stop();

                    double elapsedSeconds = sw.Elapsed.TotalSeconds;
                    long bytesReceived = data.Length;
                    double megaBits = (bytesReceived * 8.0) / (1000.0 * 1000.0);
                    double mbps = megaBits / elapsedSeconds;

                    var sb = new StringBuilder();
                    sb.AppendLine("--- Test 6: Network Download Speed ---");
                    sb.AppendLine($"データ受信量: {(bytesReceived / 1024.0):F2} KB");
                    sb.AppendLine($"通信時間: {sw.ElapsedMilliseconds} ms");
                    sb.AppendLine($"転送速度: {mbps:F2} Mbps");

                    ResultTextBox.AppendText(sb.ToString());
                    ScoreText.Text = $"{mbps:F1} Mbps";
                }
            }
            catch (Exception ex)
            {
                ResultTextBox.AppendText($"[Error] 計測に失敗しました。ネット接続を確認してください。\n{ex.Message}\n");
                ScoreText.Text = "Error";
            }

            ProgressIndicator.IsIndeterminate = false;
            UnlockAllButtons();
        }

        private void LockAllButtons()
        {
            CpuStartButton.IsEnabled = false;
            DramStartButton.IsEnabled = false;
            RomStartButton.IsEnabled = false;
            GpuStartButton.IsEnabled = false;
            NetStartButton.IsEnabled = false;
        }

        private void UnlockAllButtons()
        {
            CpuStartButton.Content = "CPU";
            DramStartButton.Content = "DRAM";
            RomStartButton.Content = "ROM";
            GpuStartButton.Content = "GPU";
            NetStartButton.Content = "ネット";
            CpuStartButton.IsEnabled = true;
            DramStartButton.IsEnabled = true;
            RomStartButton.IsEnabled = true;
            GpuStartButton.IsEnabled = true;
            NetStartButton.IsEnabled = true;
        }
    }
}
