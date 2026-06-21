using Microsoft.Win32;
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ReqNRollTestConsole
{
    public partial class MainWindow : Window
    {
        // =====================================================
        // TEST PROJECT LOCATION
        // =====================================================

        private readonly string _testProjectDirectory =
            @"C:\Users\abdis\OneDrive\Desktop\SDET Challenge-AHussein\";

        // =====================================================
        // EXECUTION DOMAIN
        // =====================================================

        private enum ExecutionDomain
        {
            Web,
            Api
        }

        private ExecutionDomain _currentDomain = ExecutionDomain.Web;

        // =====================================================
        // WEB/API TESTING STATS
        // =====================================================

        private int _totalScenarios;
        private int _passed;
        private int _failed;
        private int _singleRunCount;
        private int _singleRunPassed;
        private int _singleRunFailed;

        // =====================================================
        // RUN STATE
        // =====================================================

        private CancellationTokenSource? _runCts;
        private Process? _currentProcess;
        private readonly List<string> _detailedLogLines = new();
        private readonly object _logLock = new();
        private DateTime? _runStartedAt;

        // =====================================================
        // ANIMATIONS
        // =====================================================

        private CancellationTokenSource? _typewriterCts;
        private DispatcherTimer? _guideArrowTimer;
        private int _activeGuideArrowIndex;
        private TextBlock[]? _guideArrows;

        // =====================================================
        // TEST AREA MODEL
        // =====================================================

        private sealed class TestArea
        {
            public string Name { get; init; } = string.Empty;
            public string Path { get; init; } = string.Empty;
            public List<string> Scenarios { get; init; } = new();

            public bool IsConfigured => !string.IsNullOrWhiteSpace(Path);
        }

        // =====================================================
        // WEB TEST STYLE → AREA → SCENARIO MAPPINGS
        // =====================================================

        private readonly Dictionary<string, List<TestArea>> _webTestMap = new()
        {
            {
                "Functional",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "Landing Discovery",
                        Path = "tests/UI/1-landing-discovery-ui.spec.ts",
                        Scenarios = new()
                        {
                            "View Product List",
                            "Search Functionality",
                            "Filter Products by Category",
                            "Category Filtering and Reset"
                        }
                    },
                    new()
                    {
                        Name = "Product Details",
                        Path = "tests/UI/2-product-description-ui.spec.ts",
                        Scenarios = new()
                        {
                            "Positive: Display correct product information",
                            "User Story 2: View Product Details - Negative: Visitor cannot add product to favorites as a visitor"
                        }
                    },
                    new()
                    {
                        Name = "Authentication",
                        Path = "tests/UI/3-login-ui.spec.ts",
                        Scenarios = new()
                        {
                            "Positive: Valid credentials allow successful login",
                            "Negative: Invalid credentials display error message",
                            "Persistence: Login persists during navigation"
                        }
                    },
                    new()
                    {
                        Name = "Cart & Checkout",
                        Path = "tests/UI/4-cart-checkout-ui.spec.ts",
                        Scenarios = new()
                        {
                            "Add, Update, and Remove items from Cart",
                            "User Stories 8 & 9: Full Purchase Journey"
                        }
                    }
                }
            },
            {
                "End To End",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "Authentication",
                        Path = "tests/UI/3-login-ui.spec.ts",
                        Scenarios = new()
                        {
                            "Positive: Valid credentials allow successful login",
                            "Persistence: Login persists during navigation"
                        }
                    },
                    new()
                    {
                        Name = "Cart & Checkout",
                        Path = "tests/UI/4-cart-checkout-ui.spec.ts",
                        Scenarios = new()
                        {
                            "Add, Update, and Remove items from Cart",
                            "User Stories 8 & 9: Full Purchase Journey"
                        }
                    }
                }
            },
            {
                "Smoke",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "Landing Discovery",
                        Path = "tests/UI/1-landing-discovery-ui.spec.ts",
                        Scenarios = new()
                        {
                            "View Product List"
                        }
                    },
                    new()
                    {
                        Name = "Authentication",
                        Path = "tests/UI/3-login-ui.spec.ts",
                        Scenarios = new()
                        {
                            "Positive: Valid credentials allow successful login"
                        }
                    }
                }
            },
            {
                "Regression",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "All UI Tests",
                        Path = "tests/UI",
                        Scenarios = new()
                        {
                            "Run All"
                        }
                    }
                }
            }
        };

        // =====================================================
        // API TEST STYLE → AREA → SCENARIO MAPPINGS
        // =====================================================

        private readonly Dictionary<string, List<TestArea>> _apiTestMap = new()
        {
            {
                "API - Standard",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "Product Discovery API",
                        Path = "tests/API/1-product-discovery-api.spec.ts",
                        Scenarios = new()
                        {
                            "GET /products returns a populated product list",
                            "GET /products supports price range filtering",
                            "GET /products/{id} returns product details"
                        }
                    },
                    new()
                    {
                        Name = "Login API",
                        Path = "tests/API/2-login-api.spec.ts",
                        Scenarios = new()
                        {
                            "POST /users/login returns access token for valid credentials",
                            "POST /users/login rejects invalid credentials"
                        }
                    },
                    new()
                    {
                        Name = "Cart / Checkout API",
                        Path = "tests/API/3-cart-checkout-api.spec.ts",
                        Scenarios = new()
                        {
                            "GET /products provides product data required for cart flow",
                            "Authenticated user can access their invoices/orders endpoint",
                            "Unauthenticated request to invoices/orders endpoint is rejected"
                        }
                    },
                    new()
                    {
                        Name = "All API Tests",
                        Path = "tests/API",
                        Scenarios = new()
                        {
                            "Run All"
                        }
                    }
                }
            },
            {
                "API - Load",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "Load Testing",
                        Path = string.Empty,
                        Scenarios = new()
                        {
                            "Product catalogue load test",
                            "Login endpoint load test",
                            "Checkout endpoint load test"
                        }
                    }
                }
            },
            {
                "API - Performance",
                new List<TestArea>
                {
                    new()
                    {
                        Name = "Performance Thresholds",
                        Path = string.Empty,
                        Scenarios = new()
                        {
                            "Response time threshold validation",
                            "P95 response time validation",
                            "P99 response time validation"
                        }
                    }
                }
            }
        };

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;

            EnvironmentCombo.SelectionChanged += TestStyleCombo_SelectionChanged;
            BrowserCombo.SelectionChanged += BrowserCombo_SelectionChanged;

            InitialiseUi();
        }

        // =====================================================
        // INITIALISATION
        // =====================================================

        private void InitialiseUi()
        {
            SetExecutionDomain(ExecutionDomain.Web, writeLog: false);

            ApplyTheme(GetSelectedContent(SideEnvironmentCombo));
            UpdateStats();

            if (!Directory.Exists(_testProjectDirectory))
            {
                AppendLog("⚠ Test project directory not found:");
                AppendLog($"  {_testProjectDirectory}");
                AppendLog("  Update _testProjectDirectory in MainWindow.xaml.cs.");
                AppendLog(string.Empty);
            }
            else
            {
                AppendLog("Automation console initialised.");
                AppendLog($"Test project: {_testProjectDirectory}");
                AppendLog("Ready.");
                AppendLog(string.Empty);
            }
        }

        private Dictionary<string, List<TestArea>> GetCurrentTestMap()
        {
            return _currentDomain == ExecutionDomain.Api
                ? _apiTestMap
                : _webTestMap;
        }

        private void SetExecutionDomain(ExecutionDomain domain, bool writeLog)
        {
            _currentDomain = domain;

            PlatformCombo.Items.Clear();

            PlatformCombo.Items.Add(new ComboBoxItem
            {
                Content = domain == ExecutionDomain.Api
                    ? "Practice Software Testing API"
                    : "Practice Software Testing"
            });

            PlatformCombo.SelectedIndex = 0;

            PopulateTestStyles();

            if (writeLog)
            {
                AppendLog(domain == ExecutionDomain.Api
                    ? "API execution domain selected."
                    : "Web execution domain selected.");
            }

            UpdateTestPreview();
        }

        private void PopulateTestStyles()
        {
            EnvironmentCombo.Items.Clear();

            foreach (var testStyle in GetCurrentTestMap().Keys)
            {
                EnvironmentCombo.Items.Add(new ComboBoxItem { Content = testStyle });
            }

            EnvironmentCombo.SelectedIndex = 0;

            PopulateAreasForSelectedTestStyle();
        }

        private void PopulateAreasForSelectedTestStyle()
        {
            SuiteCombo.Items.Clear();

            var testStyle = GetSelectedContent(EnvironmentCombo);
            var map = GetCurrentTestMap();

            if (!map.TryGetValue(testStyle, out var areas))
                return;

            foreach (var area in areas)
            {
                SuiteCombo.Items.Add(new ComboBoxItem { Content = area.Name });
            }

            SuiteCombo.SelectedIndex = 0;

            PopulateScenariosForSelectedArea();
        }

        private void PopulateScenariosForSelectedArea()
        {
            BrowserCombo.Items.Clear();

            var selectedArea = GetSelectedArea();

            if (selectedArea == null)
                return;

            foreach (var scenario in selectedArea.Scenarios)
            {
                BrowserCombo.Items.Add(new ComboBoxItem { Content = scenario });
            }

            BrowserCombo.SelectedIndex = 0;

            UpdateTestPreview();
        }

        // =====================================================
        // ANIMATIONS
        // =====================================================

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            var duration = TimeSpan.FromSeconds(0.7);
            var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };

            var storyboard = new Storyboard();

            var navOpacity = new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = easing
            };

            Storyboard.SetTarget(navOpacity, SidePanel);
            Storyboard.SetTargetProperty(navOpacity, new PropertyPath("Opacity"));
            storyboard.Children.Add(navOpacity);

            var navSlide = new DoubleAnimation(-40, 0, duration)
            {
                EasingFunction = easing
            };

            Storyboard.SetTarget(navSlide, SidePanel);
            Storyboard.SetTargetProperty(
                navSlide,
                new PropertyPath("RenderTransform.(TranslateTransform.X)"));

            storyboard.Children.Add(navSlide);

            var mainOpacity = new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = easing
            };

            Storyboard.SetTarget(mainOpacity, FrontEndPanel);
            Storyboard.SetTargetProperty(mainOpacity, new PropertyPath("Opacity"));
            storyboard.Children.Add(mainOpacity);

            var mainSlide = new DoubleAnimation(40, 0, duration)
            {
                EasingFunction = easing
            };

            Storyboard.SetTarget(mainSlide, FrontEndPanel);
            Storyboard.SetTargetProperty(
                mainSlide,
                new PropertyPath("RenderTransform.(TranslateTransform.X)"));

            storyboard.Children.Add(mainSlide);

            storyboard.Begin();

            StartExecutionGuideArrowAnimation();
        }

        private void StartExecutionGuideArrowAnimation()
        {
            _guideArrows = new[]
            {
                GuideArrowEnvironment,
                GuideArrowTestStyle,
                GuideArrowArea,
                GuideArrowRunTests
            };

            foreach (var arrow in _guideArrows)
            {
                arrow.Opacity = 0.35;
            }

            _guideArrowTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };

            _guideArrowTimer.Tick += (_, _) =>
            {
                if (_guideArrows == null || _guideArrows.Length == 0)
                    return;

                foreach (var arrow in _guideArrows)
                {
                    arrow.Opacity = 0.35;

                    if (arrow.RenderTransform is TranslateTransform transform)
                        transform.X = 0;

                    if (arrow.Effect is DropShadowEffect effect)
                        effect.Opacity = 0;
                }

                var activeArrow = _guideArrows[_activeGuideArrowIndex];

                AnimateGuideArrow(activeArrow);

                _activeGuideArrowIndex++;

                if (_activeGuideArrowIndex >= _guideArrows.Length)
                    _activeGuideArrowIndex = 0;
            };

            _guideArrowTimer.Start();
        }

        private void AnimateGuideArrow(TextBlock arrow)
        {
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.35,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(260),
                AutoReverse = true,
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            arrow.BeginAnimation(OpacityProperty, opacityAnimation);

            if (arrow.RenderTransform is TranslateTransform transform)
            {
                var slideAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 5,
                    Duration = TimeSpan.FromMilliseconds(260),
                    AutoReverse = true,
                    EasingFunction = new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                };

                transform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
            }

            if (arrow.Effect is DropShadowEffect glow)
            {
                var glowAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 0.9,
                    Duration = TimeSpan.FromMilliseconds(260),
                    AutoReverse = true,
                    EasingFunction = new QuadraticEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
                };

                glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnimation);
            }
        }

        // =====================================================
        // BUTTON HANDLERS
        // =====================================================

        private async void RunScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleScenarioAsync();
        }

        private async void RunSuiteButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedAreaAsync();
        }

        private void StopRunButton_Click(object sender, RoutedEventArgs e)
        {
            StopRunButton.IsEnabled = false;
            StatusText.Text = "Status: Cancelling...";
            AppendLog("⏹ Cancellation requested.");

            _runCts?.Cancel();
        }

        private void OpenReportButton_Click(object sender, RoutedEventArgs e)
        {
            OpenPlaywrightReport();
        }

        private void ExportDetailedLogButton_Click(object sender, RoutedEventArgs e)
        {
            ExportDetailedLog();
        }

        private void ExportPackageButton_Click(object sender, RoutedEventArgs e)
        {
            ExportTestPackage();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            StatusText.Text = "Status: Idle";
        }

        private void ClearErrorLogButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorLogTextBox.Clear();
        }

        // =====================================================
        // CORE RUNNERS
        // =====================================================

        private async Task RunSingleScenarioAsync()
        {
            var selectedArea = GetSelectedArea();
            var selectedScenario = GetSelectedContent(BrowserCombo);

            if (selectedArea == null)
            {
                MessageBox.Show("Please select an Area first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedScenario))
            {
                MessageBox.Show("Please select a Scenario first.");
                return;
            }

            if (!selectedArea.IsConfigured)
            {
                ShowNotConfiguredMessage(selectedArea.Name, selectedScenario);
                return;
            }

            _runCts = new CancellationTokenSource();
            ToggleRunning(true);

            try
            {
                ClearPreviousRunState(selectedArea.Name, selectedScenario);

                var command = BuildPlaywrightCommand(
                    selectedArea.Path,
                    selectedScenario == "Run All" ? null : selectedScenario);

                AppendLog($"▶ Running scenario: {selectedScenario}");
                AppendLog($"   Domain: {_currentDomain}");
                AppendLog($"   Area: {selectedArea.Name}");
                AppendLog($"   Environment: {GetSelectedContent(SideEnvironmentCombo)}");
                AppendLog($"   Command: {command}");
                AppendLog(string.Empty);

                var result = await RunCommandAsync(command, _runCts.Token);

                ApplyRunResult(result, selectedArea.Name, selectedScenario, incrementSingleRunCounter: true);
            }
            finally
            {
                ToggleRunning(false);

                _runCts.Dispose();
                _runCts = null;
                _currentProcess = null;
            }
        }

        private async Task RunSelectedAreaAsync()
        {
            var selectedArea = GetSelectedArea();

            if (selectedArea == null)
            {
                MessageBox.Show("Please select an Area first.");
                return;
            }

            if (!selectedArea.IsConfigured)
            {
                ShowNotConfiguredMessage(selectedArea.Name, "Run All");
                return;
            }

            _runCts = new CancellationTokenSource();
            ToggleRunning(true);

            try
            {
                ClearPreviousRunState(selectedArea.Name, "All scenarios in area");

                var command = BuildPlaywrightCommand(selectedArea.Path, null);

                AppendLog($"▶ Running area: {selectedArea.Name}");
                AppendLog($"   Domain: {_currentDomain}");
                AppendLog($"   Environment: {GetSelectedContent(SideEnvironmentCombo)}");
                AppendLog($"   Command: {command}");
                AppendLog(string.Empty);

                var result = await RunCommandAsync(command, _runCts.Token);

                ApplyRunResult(result, selectedArea.Name, "All scenarios in area", incrementSingleRunCounter: false);
            }
            finally
            {
                ToggleRunning(false);

                _runCts.Dispose();
                _runCts = null;
                _currentProcess = null;
            }
        }

        // =====================================================
        // COMMAND BUILDING
        // =====================================================

        private string BuildPlaywrightCommand(string testPath, string? scenarioName)
        {
            var environment = GetSelectedContent(SideEnvironmentCombo);

            var npmScript = environment.Equals("Buggy", StringComparison.OrdinalIgnoreCase)
                ? "test:buggy"
                : "test:main";

            var command = $"npm run {npmScript} -- {testPath}";

            if (!string.IsNullOrWhiteSpace(scenarioName))
            {
                command += $" -g \"{scenarioName}\"";
            }

            command += " --headed";

            return command;
        }

        private sealed class TestRunResult
        {
            public bool Success { get; init; }
            public bool Cancelled { get; init; }
            public int ExitCode { get; init; }
            public int Passed { get; init; }
            public int Failed { get; init; }
            public int Total => Passed + Failed;
            public TimeSpan Duration { get; init; }
        }

        private async Task<TestRunResult> RunCommandAsync(string command, CancellationToken token)
        {
            if (!Directory.Exists(_testProjectDirectory))
            {
                AppendLog("⚠ Cannot run tests. Test project directory does not exist.");
                AppendError($"Missing directory: {_testProjectDirectory}");

                return new TestRunResult
                {
                    Success = false,
                    ExitCode = -1
                };
            }

            var outputLines = new List<string>();
            var startedAt = DateTime.Now;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = _testProjectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            _currentProcess = process;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                lock (outputLines)
                {
                    outputLines.Add(e.Data);
                }

                AppendLog(e.Data);

                if (IsErrorLine(e.Data))
                    AppendError(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                lock (outputLines)
                {
                    outputLines.Add(e.Data);
                }

                AppendLog(e.Data);

                if (IsErrorLine(e.Data))
                    AppendError(e.Data);
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(token);

                // Flush async output handlers.
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                KillCurrentProcessTree();

                AppendLog(string.Empty);
                AppendLog("⏹ Test run cancelled by user.");
                AppendLog(string.Empty);

                return new TestRunResult
                {
                    Success = false,
                    Cancelled = true,
                    ExitCode = -2,
                    Duration = DateTime.Now - startedAt
                };
            }
            catch (Exception ex)
            {
                AppendLog("⚠ Failed to start or monitor Playwright command.");
                AppendError(ex.ToString());

                return new TestRunResult
                {
                    Success = false,
                    ExitCode = -1,
                    Duration = DateTime.Now - startedAt
                };
            }

            AppendLog(string.Empty);
            AppendLog($"⮞ Process exited with code {process.ExitCode}");
            AppendLog(string.Empty);

            List<string> snapshot;
            lock (outputLines)
            {
                snapshot = new List<string>(outputLines);
            }

            var parsed = ParsePlaywrightSummary(snapshot);

            return new TestRunResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Passed = parsed.Passed,
                Failed = parsed.Failed,
                Duration = DateTime.Now - startedAt
            };
        }

        private void KillCurrentProcessTree()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                    _currentProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                AppendError($"Failed to kill process tree: {ex.Message}");
            }
        }

        // =====================================================
        // PLAYWRIGHT OUTPUT PARSING
        // =====================================================

        private static (int Passed, int Failed) ParsePlaywrightSummary(List<string> outputLines)
        {
            var passed = 0;
            var failed = 0;

            foreach (var line in outputLines)
            {
                var passedMatch = Regex.Match(line, @"(?<count>\d+)\s+passed", RegexOptions.IgnoreCase);
                if (passedMatch.Success)
                    passed = int.Parse(passedMatch.Groups["count"].Value);

                var failedMatch = Regex.Match(line, @"(?<count>\d+)\s+failed", RegexOptions.IgnoreCase);
                if (failedMatch.Success)
                    failed = int.Parse(failedMatch.Groups["count"].Value);
            }

            return (passed, failed);
        }

        private static bool IsErrorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var trimmed = line.Trim();
            var lower = trimmed.ToLowerInvariant();

            if (Regex.IsMatch(trimmed, @"^\d+\s+passed", RegexOptions.IgnoreCase))
                return false;

            if (Regex.IsMatch(trimmed, @"^0\s+failed", RegexOptions.IgnoreCase))
                return false;

            if (lower.StartsWith("at ") ||
                lower.Contains("node_modules") ||
                lower.Contains("internal/process") ||
                lower.Contains("async ") ||
                lower.Contains("object.<anonymous>"))
            {
                return false;
            }

            return lower.Contains("failed")
                   || lower.Contains("error")
                   || lower.Contains("timeout")
                   || lower.Contains("expect(")
                   || lower.Contains("locator")
                   || lower.Contains("call log")
                   || lower.Contains("test failed")
                   || lower.Contains("×");
        }

        // =====================================================
        // COMBO EVENTS
        // =====================================================

        private void SideEnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            ApplyTheme(GetSelectedContent(SideEnvironmentCombo));
            UpdateTestPreview();
        }

        private void TestStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateAreasForSelectedTestStyle();
        }

        private void SuiteCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateScenariosForSelectedArea();
        }

        private void BrowserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTestPreview();
        }

        // =====================================================
        // UI HELPERS
        // =====================================================

        private TestArea? GetSelectedArea()
        {
            var testStyle = GetSelectedContent(EnvironmentCombo);
            var areaName = GetSelectedContent(SuiteCombo);
            var map = GetCurrentTestMap();

            if (!map.TryGetValue(testStyle, out var areas))
                return null;

            foreach (var area in areas)
            {
                if (area.Name.Equals(areaName, StringComparison.OrdinalIgnoreCase))
                    return area;
            }

            return null;
        }

        private string GetSelectedContent(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Content is string text)
                return text;

            return comboBox.Text;
        }

        private void ClearPreviousRunState(string areaName, string scenarioName)
        {
            LogTextBox.Clear();
            ErrorLogTextBox.Clear();
            lock (_logLock)
            {
                _detailedLogLines.Clear();
            }

            _runStartedAt = DateTime.Now;

            LastRunStatusText.Text = areaName;
            LastRunTimeText.Text = scenarioName;

            StatusText.Text = "Status: Running...";
        }

        private void ApplyRunResult(
            TestRunResult result,
            string areaName,
            string scenarioName,
            bool incrementSingleRunCounter)
        {
            LastRunStatusText.Text = areaName;
            LastRunTimeText.Text = scenarioName;

            if (incrementSingleRunCounter)
                _singleRunCount++;

            if (result.Cancelled)
            {
                StatusText.Text = $"Status: Cancelled ({FormatDuration(result.Duration)})";
                UpdateStats();
                return;
            }

            if (incrementSingleRunCounter)
            {
                if (result.Success)
                    _singleRunPassed++;
                else
                    _singleRunFailed++;
            }

            ApplyParsedCounts(result);

            StatusText.Text = result.Success
                ? $"Status: Passed ({FormatDuration(result.Duration)})"
                : $"Status: Failed ({FormatDuration(result.Duration)})";

            UpdateStats();
        }

        private void ApplyParsedCounts(TestRunResult result)
        {
            if (result.Total > 0)
            {
                _passed = result.Passed;
                _failed = result.Failed;
                _totalScenarios = result.Total;
                return;
            }

            // Fallback for commands that do not produce a standard Playwright summary.
            _passed = result.Success ? 1 : 0;
            _failed = result.Success ? 0 : 1;
            _totalScenarios = 1;
        }

        private void UpdateStats()
        {
            TotalScenariosText.Text = _totalScenarios.ToString();
            PassedCountText.Text = _passed.ToString();
            FailedCountText.Text = _failed.ToString();

            SingleRunCountText.Text = _singleRunCount.ToString();
            SingleRunPassedText.Text = _singleRunPassed.ToString();
            SingleRunFailedText.Text = _singleRunFailed.ToString();
        }

        private void ToggleRunning(bool isRunning)
        {
            RunScenarioButton.IsEnabled = !isRunning;
            RunSuiteButton.IsEnabled = !isRunning;
            StopRunButton.IsEnabled = isRunning;

            SideEnvironmentCombo.IsEnabled = !isRunning;
            EnvironmentCombo.IsEnabled = !isRunning;
            SuiteCombo.IsEnabled = !isRunning;
            BrowserCombo.IsEnabled = !isRunning;
            PlatformCombo.IsEnabled = !isRunning;

            if (isRunning)
                StatusText.Text = "Status: Running...";
        }

        private void AppendLog(string message)
        {
            AppendToTextBox(LogTextBox, message);
        }

        private void AppendError(string message)
        {
            AppendToTextBox(ErrorLogTextBox, message);
        }

        private void AppendToTextBox(TextBox textBox, string message)
        {
            var stamped = $"[{DateTime.Now:HH:mm:ss}] {message}";

            lock (_logLock)
            {
                _detailedLogLines.Add(stamped);
            }

            Dispatcher.Invoke(() =>
            {
                textBox.AppendText(message + Environment.NewLine);
                textBox.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        private void UpdateTestPreview()
        {
            var selectedArea = GetSelectedArea();
            var scenario = GetSelectedContent(BrowserCombo);
            var environment = GetSelectedContent(SideEnvironmentCombo);
            var testStyle = GetSelectedContent(EnvironmentCombo);
            var domain = _currentDomain.ToString();

            var previewText =
                $"> Domain: {domain}{Environment.NewLine}" +
                $"> Environment: {environment}{Environment.NewLine}" +
                $"> Test Style: {testStyle}{Environment.NewLine}" +
                $"> Area: {selectedArea?.Name ?? "N/A"}{Environment.NewLine}" +
                $"> Scenario: {scenario}{Environment.NewLine}" +
                $"> Path: {(string.IsNullOrWhiteSpace(selectedArea?.Path) ? "Not configured yet" : selectedArea.Path)}";

            if (FindName("TestPreviewTextBlock") is not TextBlock preview)
                return;

            _typewriterCts?.Cancel();
            _typewriterCts = new CancellationTokenSource();

            _ = TypeWriterAsync(preview, previewText, _typewriterCts.Token);
        }

        private async Task TypeWriterAsync(TextBlock target, string text, CancellationToken token)
        {
            try
            {
                target.Text = string.Empty;

                foreach (var character in text)
                {
                    token.ThrowIfCancellationRequested();

                    target.Text += character;

                    await Task.Delay(12, token);
                }
            }
            catch (OperationCanceledException)
            {
                // User selected another test before the animation finished.
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalSeconds < 60
                ? $"{duration.TotalSeconds:0.0}s"
                : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        private void ShowNotConfiguredMessage(string area, string scenario)
        {
            var message =
                $"'{area}' / '{scenario}' is not wired to a runnable test file yet.\n\n" +
                "This slot is reserved for future API load/performance automation.";

            MessageBox.Show(
                message,
                "Test Not Configured",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            AppendLog($"⚠ Not configured: {area} / {scenario}");
        }

        // =====================================================
        // REPORTING / EXPORTS
        // =====================================================

        private void OpenPlaywrightReport()
        {
            var reportIndex = Path.Combine(_testProjectDirectory, "playwright-report", "index.html");

            try
            {
                if (File.Exists(reportIndex))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = reportIndex,
                        UseShellExecute = true
                    });

                    AppendLog($"Opened Playwright report: {reportIndex}");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c npx playwright show-report",
                    WorkingDirectory = _testProjectDirectory,
                    UseShellExecute = true
                });

                AppendLog("Launching Playwright report server...");
            }
            catch (Exception ex)
            {
                AppendError($"Failed to open Playwright report: {ex.Message}");
                MessageBox.Show(
                    "Unable to open the Playwright report. Check the error log for details.",
                    "Report Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportDetailedLog()
        {
            try
            {
                var exportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AutomationConsole",
                    "Logs");

                Directory.CreateDirectory(exportDir);

                var filePath = Path.Combine(
                    exportDir,
                    $"automation-console-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                List<string> lines;

                lock (_logLock)
                {
                    lines = _detailedLogLines.Count > 0
                        ? new List<string>(_detailedLogLines)
                        : new List<string> { "No detailed log entries were captured yet." };
                }

                File.WriteAllLines(filePath, lines);

                AppendLog($"Detailed log exported: {filePath}");

                MessageBox.Show(
                    $"Detailed log exported:\n\n{filePath}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendError($"Failed to export detailed log: {ex.Message}");
            }
        }

        private void ExportTestPackage()
        {
            try
            {
                var exportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AutomationConsole",
                    "Packages");

                Directory.CreateDirectory(exportDir);

                var zipPath = Path.Combine(
                    exportDir,
                    $"automation-test-package-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                List<string> logSnapshot;

                lock (_logLock)
                {
                    logSnapshot = new List<string>(_detailedLogLines);
                }

                AddTextToZip(zip, "console-log.txt", logSnapshot);

                AddDirectoryToZip(
                    zip,
                    Path.Combine(_testProjectDirectory, "playwright-report"),
                    "playwright-report");

                AddDirectoryToZip(
                    zip,
                    Path.Combine(_testProjectDirectory, "test-results"),
                    "test-results");

                AppendLog($"Test package exported: {zipPath}");

                MessageBox.Show(
                    $"Test package exported:\n\n{zipPath}",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendError($"Failed to export test package: {ex.Message}");
                MessageBox.Show(
                    "Unable to export the test package. Check the error log for details.",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void AddTextToZip(ZipArchive zip, string entryName, List<string> lines)
        {
            var entry = zip.CreateEntry(entryName);

            using var writer = new StreamWriter(entry.Open());

            if (lines.Count == 0)
            {
                writer.WriteLine("No detailed log entries were captured yet.");
                return;
            }

            foreach (var line in lines)
                writer.WriteLine(line);
        }

        private static void AddDirectoryToZip(ZipArchive zip, string sourceDirectory, string entryRoot)
        {
            if (!Directory.Exists(sourceDirectory))
                return;

            foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                var entryName = Path.Combine(entryRoot, relativePath).Replace("\\", "/");

                zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.Fastest);
            }
        }

        // =====================================================
        // THEME
        // =====================================================

        private void ApplyTheme(string environment)
        {
            var isBuggy = environment.Equals("Buggy", StringComparison.OrdinalIgnoreCase);

            if (isBuggy)
            {
                UpdateBrushResource("AppBackgroundBrush", "#0B0B0B");
                UpdateBrushResource("SurfaceBrush", "#141010");
                UpdateBrushResource("SurfaceAltBrush", "#1A1214");
                UpdateBrushResource("PrimaryBrush", "#211316");
                UpdateBrushResource("AccentBrush", "#6F2A35");
                UpdateBrushResource("BorderBrushColor", "#3A2428");
                UpdateBrushResource("TextPrimaryBrush", "#D86A7A");
                UpdateBrushResource("TextSecondaryBrush", "#8A7377");
            }
            else
            {
                UpdateBrushResource("AppBackgroundBrush", "#000000");
                UpdateBrushResource("SurfaceBrush", "#101010");
                UpdateBrushResource("SurfaceAltBrush", "#0C0C0C");
                UpdateBrushResource("PrimaryBrush", "#181818");
                UpdateBrushResource("AccentBrush", "#127500");
                UpdateBrushResource("BorderBrushColor", "#1F2937");
                UpdateBrushResource("TextPrimaryBrush", "#1FC100");
                UpdateBrushResource("TextSecondaryBrush", "#707070");
            }
        }

        private void UpdateBrushResource(string key, string hexColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);

            var brush = new SolidColorBrush(color);
            brush.Freeze();

            Application.Current.Resources[key] = brush;
        }

        // =====================================================
        // SECTION NAVIGATION
        // =====================================================

        private void ShowSection(string section)
        {
            FrontEndPanel.Visibility = section == "frontend"
                ? Visibility.Visible
                : Visibility.Collapsed;

            DesktopPanel.Visibility = section == "desktop"
                ? Visibility.Visible
                : Visibility.Collapsed;

            HhtPanel.Visibility = section == "hht"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void FrontEndNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSection("frontend");
            SetExecutionDomain(ExecutionDomain.Web, writeLog: true);
        }

        private void SqlNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSection("frontend");
            SetExecutionDomain(ExecutionDomain.Api, writeLog: true);
        }

        private void DataNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSection("frontend");
        }

        // =====================================================
        // LEGACY EVENT HANDLERS
        // These exist so old XAML event bindings do not break.
        // =====================================================

        private async void DesktopRunSuiteButton_Click(object sender, RoutedEventArgs e)
        {
            SetExecutionDomain(ExecutionDomain.Api, writeLog: true);
            await RunSelectedAreaAsync();
        }

        private async void DesktopRunScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            SetExecutionDomain(ExecutionDomain.Api, writeLog: true);
            await RunSingleScenarioAsync();
        }

        private void DesktopClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        private void DesktopClearErrorLogButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorLogTextBox.Clear();
        }

        private void DesktopModuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Legacy API panel no longer used.
        }

        private async void HhtRunSuiteButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedAreaAsync();
        }

        private async void HhtRunScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleScenarioAsync();
        }

        private void HhtClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        private void HhtClearErrorLogButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorLogTextBox.Clear();
        }

        private void HhtModuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Legacy HHT panel no longer used.
        }
    }
}
