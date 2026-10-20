using SttConsole.Models;
using SttConsole.Services.Audio;
using SttConsole.Services.Auth;
using SttConsole.Services.Settings;
using SttConsole.Services.Speech;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace SttConsole.UI;

/// <summary>
/// Main window.
/// Call UpdateRecognitionText / UpdateTranslationText / ConfirmResult
/// from background threads to update the UI safely.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IApplication _app;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IAuthService _authService;
    private readonly ISpeechService _speechService;
    private readonly IAudioInputFactory _audioFactory;

    private readonly Label _recognitionLabel;
    private readonly Label _translationLabel;

    private readonly List<(string Recognized, string Translated, DateTime Time)> _confirmedData = [];
    private readonly WrappingListSource _wrappingSource;
    private readonly ListView _resultsListView;
    private readonly FrameView _resultsFrame;
    private bool _showTranslation;

    private readonly Button _controlBtn;
    private readonly Button _settingsBtn;
    private readonly FrameView _settingsFrame;
    private readonly Label _elapsedLabel;

    private bool _isRunning;
    private TimeSpan _elapsedAccumulated;
    private DateTime _runStartTime;
    private readonly Lock _logLock = new();
    private bool _logErrorShown;

    public MainWindow(
        IApplication app,
        ISettingsRepository settingsRepo,
        IAuthService authService,
        ISpeechService speechService,
        IAudioInputFactory audioFactory)
    {
        _audioFactory = audioFactory;
        _app = app;
        _settingsRepo = settingsRepo;
        _authService = authService;
        _speechService = speechService;
        _wrappingSource = new WrappingListSource(_app, new string(' ', 12)); // width of "[HH:mm:ss]  "
        Title = string.Empty;
        BorderStyle = LineStyle.None;

        var titleLabel = new Label
        {
            Text = "STT Console  [S: Start/Stop  T: Toggle View  Q: Quit]",
            X = 0,
            Y = 0,
        };
        Add(titleLabel);

        _settingsFrame = new FrameView
        {
            Title = string.Empty,
            X = 0,
            Y = Pos.Bottom(titleLabel),
            Width = Dim.Fill(),
            Height = 3,
        };
        _controlBtn = new Button { Text = ControlBtnText(), X = 1, Y = 0 };
        _elapsedLabel = new Label { Text = ElapsedText(), X = Pos.Right(_controlBtn) + 2, Y = 0 };
        _settingsBtn = new Button { Text = "Settings ▼", X = Pos.Right(_elapsedLabel) + 2, Y = 0 };

        _controlBtn.Accepting += (_, _) => ToggleRunState();
        _settingsBtn.Accepting += (_, _) => ShowSettingsMenu();

        _app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            if (_isRunning)
            {
                _elapsedLabel.Text = ElapsedText();
            }

            return true;
        });
        _settingsFrame.Add(_controlBtn, _elapsedLabel, _settingsBtn);

        var recognitionFrame = new FrameView
        {
            Title = "■ Speech Recognition (Live)",
            X = 0,
            Y = Pos.Bottom(_settingsFrame),
            Width = Dim.Fill(),
            Height = 6,
        };
        _recognitionLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill(),
            Text = "Interim recognition text will appear here once started...",
        };
        recognitionFrame.Add(_recognitionLabel);

        var translationFrame = new FrameView
        {
            Title = "■ Translation (Live)",
            X = 0,
            Y = Pos.Bottom(recognitionFrame),
            Width = Dim.Fill(),
            Height = 6,
        };
        _translationLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill(),
            Text = "Real-time translation will appear here...",
        };
        translationFrame.Add(_translationLabel);

        _resultsFrame = new FrameView
        {
            Title = ResultsHeaderText(),
            X = 0,
            Y = Pos.Bottom(translationFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _resultsListView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false,
            Source = _wrappingSource,
        };
        _resultsFrame.Add(_resultsListView);

        Add(_settingsFrame, recognitionFrame, translationFrame, _resultsFrame);

        _speechService.PartialResultReceived += (_, e) =>
        {
            UpdateRecognitionText(e.Text);
            if (e.Translation is not null)
            {
                UpdateTranslationText(e.Translation);
            }
            else if (e.Text == string.Empty)
            {
                UpdateTranslationText(string.Empty);
            }
        };
        _speechService.FinalResultReceived += (_, e) =>
        {
            ConfirmResult(e.Recognized, e.Translated ?? string.Empty);
            UpdateRecognitionText(string.Empty);
            UpdateTranslationText(string.Empty);
        };
        _speechService.ErrorOccurred += (_, message) =>
            _app.Invoke(() =>
            {
                // The recognizer is dead after an error; reset the UI so Start works again
                if (_isRunning)
                {
                    _ = _speechService.StopAsync();
                    SetRunning(false);
                }

                ShowMessage(message, MessageSeverity.Error);
            });
    }

    public string LogFilePath { get; } =
        Path.Combine(Path.GetTempPath(), "sttconsole", "logs", $"recognition_{DateTime.Now:yyyyMMdd_HHmmss}.log");

    public TimeSpan GetTotalElapsed()
    {
        var total = _elapsedAccumulated;
        if (_isRunning)
        {
            total += DateTime.Now - _runStartTime;
        }

        return total;
    }

    // Safe to call from background threads

    public void UpdateRecognitionText(string text) => _app.Invoke(() => _recognitionLabel.Text = text);

    public void UpdateTranslationText(string text) => _app.Invoke(() => _translationLabel.Text = text);

    public void ConfirmResult(string recognized, string translated)
    {
        var time = DateTime.Now;

        // Written outside the UI thread so a result confirmed while the app is quitting still reaches the log
        AppendLog(recognized, translated, time);
        _app.Invoke(() =>
        {
            _confirmedData.Add((recognized, translated, time));
            var text = _showTranslation ? translated : recognized;
            _wrappingSource.Add($"[{time:HH:mm:ss}]  {text}");
            _resultsListView.ScrollVertical(_wrappingSource.Count);
        });
    }

    // ESC is swallowed so it doesn't exit the app.
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            return true;
        }

        if (key == Key.Q || key == Key.C.WithCtrl)
        {
            HandleQuit();
            return true;
        }

        if (key == Key.S)
        {
            ToggleRunState();
            return true;
        }

        if (key == Key.T)
        {
            ToggleResultsView();
            return true;
        }

        if (key == Key.CursorUp)
        {
            _resultsListView.ScrollVertical(-1);
            return true;
        }

        if (key == Key.CursorDown)
        {
            _resultsListView.ScrollVertical(1);
            return true;
        }

        if (key == Key.PageUp)
        {
            _resultsListView.ScrollVertical(-_resultsListView.GetContentHeight());
            return true;
        }

        if (key == Key.PageDown)
        {
            _resultsListView.ScrollVertical(_resultsListView.GetContentHeight());
            return true;
        }

        return base.OnKeyDown(key);
    }

    // The file is created on the first result, so a session with no results leaves no empty log.
    // A write failure must not interrupt recognition, so it is only surfaced once.
    private void AppendLog(string recognized, string translated, DateTime time)
    {
        try
        {
            var line = $"[{time:HH:mm:ss}] {recognized}";
            if (translated != string.Empty)
            {
                line += $"{Environment.NewLine}           → {translated}";
            }

            lock (_logLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                File.AppendAllText(LogFilePath, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_logErrorShown)
            {
                return;
            }

            _logErrorShown = true;
            _app.Invoke(() => ShowMessage($"Failed to write log file: {ex.Message}", MessageSeverity.Warn));
        }
    }

    private void ToggleResultsView()
    {
        _showTranslation = !_showTranslation;
        _resultsFrame.Title = ResultsHeaderText();

        // Suspend CollectionChanged to avoid scroll position jumps during rebuild
        _wrappingSource.SuspendCollectionChangedEvent = true;
        _wrappingSource.Clear();
        foreach (var (recognized, translated, time) in _confirmedData)
        {
            var text = _showTranslation ? translated : recognized;
            _wrappingSource.Add($"[{time:HH:mm:ss}]  {text}");
        }

        _wrappingSource.SuspendCollectionChangedEvent = false;
        _wrappingSource.RaiseReset();
        if (_confirmedData.Count > 0)
        {
            _resultsListView.ScrollVertical(_wrappingSource.Count);
        }
    }

    private string AudioBtnText() => $"Audio Input: {_settingsRepo.Settings.AudioDevice}";

    private string SpeechBtnText()
    {
        var settings = _settingsRepo.Settings;
        if (settings.SpeechRegion == string.Empty)
        {
            return "Speech Service: (not configured)";
        }

        var auth = settings.SpeechAuth == AuthMethod.ApiKey ? "API Key" : "Managed ID";
        return $"Speech Service: {settings.SpeechRegion} [{auth}]";
    }

    private string LanguageBtnText() =>
        $"Language: {_settingsRepo.Settings.RecognitionLang} → {_settingsRepo.Settings.TranslationLang}";

    private string ControlBtnText() => _isRunning ? "■ Stop" : "▶ Start";

    private string ResultsHeaderText()
    {
        var mode = _showTranslation ? "Translation" : "Recognition";
        return $"■ Confirmed Text ({mode})  [T: Toggle  ↑↓: Scroll]";
    }

    private string ElapsedText()
    {
        var total = _elapsedAccumulated;
        if (_isRunning)
        {
            total += DateTime.Now - _runStartTime;
        }

        if (total == TimeSpan.Zero && !_isRunning)
        {
            return "[--:--:--]";
        }

        return $"[{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}]";
    }

    private void ToggleRunState()
    {
        if (_isRunning)
        {
            _ = _speechService.StopAsync();
            SetRunning(false);
        }
        else
        {
            UpdateRecognitionText(string.Empty);
            UpdateTranslationText(string.Empty);
            _ = _speechService.StartAsync();
            SetRunning(true);
        }
    }

    private void SetRunning(bool running)
    {
        if (running == _isRunning)
        {
            return;
        }

        // The elapsed time of a finished run stays on screen until the next Start resets it
        if (running)
        {
            _elapsedAccumulated = TimeSpan.Zero;
            _runStartTime = DateTime.Now;
        }
        else
        {
            _elapsedAccumulated += DateTime.Now - _runStartTime;
        }

        _isRunning = running;
        _controlBtn.Text = ControlBtnText();
        _elapsedLabel.Text = ElapsedText();

        // Settings are locked while recognition is running
        _settingsBtn.Enabled = !running;
        _settingsFrame.Layout();
    }
}
