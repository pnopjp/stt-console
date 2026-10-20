using System.Collections.ObjectModel;
using SttConsole.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace SttConsole.UI;

public partial class MainWindow
{
    public void ShowMessage(string message, MessageSeverity severity = MessageSeverity.Info)
    {
        var title = severity switch
        {
            MessageSeverity.Warn => "Warning",
            MessageSeverity.Error => "Error",
            _ => "Information",
        };

        const int DialogWidth = 60;
        var messageLines = CountWrappedLines(message, DialogWidth - 4);
        var dialogHeight = Math.Clamp(messageLines + 6, 9, Math.Max(9, Console.WindowHeight - 2));

        var dialog = new Dialog { Title = title, Width = DialogWidth, Height = dialogHeight };
        var label = new Label
        {
            Text = message,
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
        };

        var stdScheme = dialog.GetScheme() ?? new Scheme();
        var borderForeground = severity switch
        {
            MessageSeverity.Error => new Color(ColorName16.BrightRed),
            MessageSeverity.Warn => new Color(255, 165, 0),
            _ => stdScheme.Normal.Foreground,
        };

        // Always set all scheme attributes explicitly so that changing Normal.Foreground
        // for border coloring does not cause derived attributes (which fill the dialog
        // background) to be recalculated and appear transparent.
        dialog.SetScheme(new Scheme(stdScheme)
        {
            Normal = new Terminal.Gui.Drawing.Attribute(borderForeground, stdScheme.Normal.Background),
            Focus = stdScheme.GetAttributeForRole(VisualRole.Focus, null),
            HotNormal = stdScheme.GetAttributeForRole(VisualRole.HotNormal, null),
            HotFocus = stdScheme.GetAttributeForRole(VisualRole.HotFocus, null),
            Disabled = stdScheme.GetAttributeForRole(VisualRole.Disabled, null),
        });

        // Pin label to the original scheme so message text stays standard color
        label.SetScheme(stdScheme);

        var okBtn = new Button { Text = "OK", X = Pos.Center() - 1, Y = Pos.AnchorEnd(1) };
        okBtn.Accepting += (_, _) => _app.RequestStop();

        dialog.Add(label, okBtn);
        _app.Run(dialog);
        dialog.Dispose();
    }

    // Estimates wrapped line count; full-width (CJK) characters take two cells, but wrapping itself is character-based.
    private static int CountWrappedLines(string text, int width)
    {
        var lines = 0;
        foreach (var line in text.Replace("\r", string.Empty).Split('\n'))
        {
            var cells = line.Sum(c => IsFullWidth(c) ? 2 : 1);
            lines += Math.Max(1, (cells + width - 1) / width);
        }

        return lines;
    }

    private static bool IsFullWidth(char c) =>
        c is >= (char)0x1100 and (<= (char)0x115F or >= (char)0x2E80 and <= (char)0xFF60 or >= (char)0xFFE0 and <= (char)0xFFE6);

    private void HandleQuit()
    {
        if (!_isRunning)
        {
            _ = _speechService.StopAsync();
            _app.RequestStop();
            return;
        }

        var doQuit = false;
        var dialog = new Dialog { Title = "Confirm Exit", Width = 50, Height = 8 };
        var message = new Label { Text = "Recognition is running. Quit anyway?", X = 1, Y = 1 };
        var okBtn = new Button { Text = "Quit", X = Pos.Center() - 14, Y = Pos.AnchorEnd(3) };
        var cancelBtn = new Button { Text = "Cancel", X = Pos.Center() + 1, Y = Pos.AnchorEnd(3) };

        okBtn.Accepting += (_, _) =>
        {
            doQuit = true;
            _app.RequestStop();
        };
        cancelBtn.Accepting += (_, _) => _app.RequestStop();

        dialog.Add(message, okBtn, cancelBtn);
        _app.Run(dialog);
        dialog.Dispose();

        if (doQuit)
        {
            _ = _speechService.StopAsync();
            _app.RequestStop();
        }
    }

    private void ShowSettingsMenu()
    {
        Action? nextAction = null;

        var dialog = new Dialog { Title = "Settings", Width = 50, Height = 14 };
        var audioBtn = new Button { Text = AudioBtnText(), X = 1, Y = 1 };
        var speechBtn = new Button { Text = SpeechBtnText(), X = 1, Y = 3 };
        var langBtn = new Button { Text = LanguageBtnText(), X = 1, Y = 5 };
        var closeBtn = new Button { Text = "Close", X = Pos.Center() - 5, Y = Pos.Bottom(langBtn) + 3 };

        // The chosen dialog opens after this one closes, so only one modal is running at a time.
        audioBtn.Accepting += (_, _) =>
        {
            nextAction = ShowAudioSourceDialog;
            _app.RequestStop();
        };
        speechBtn.Accepting += (_, _) =>
        {
            nextAction = ShowSpeechServiceDialog;
            _app.RequestStop();
        };
        langBtn.Accepting += (_, _) =>
        {
            nextAction = ShowLanguageDialog;
            _app.RequestStop();
        };
        closeBtn.Accepting += (_, _) => _app.RequestStop();

        dialog.Add(audioBtn, speechBtn, langBtn, closeBtn);
        _app.Run(dialog);
        dialog.Dispose();

        nextAction?.Invoke();
    }

    private void ShowAudioSourceDialog()
    {
        var audioDevices = new ObservableCollection<string>(_audioFactory.ListSources());
        var initialIndex = Math.Max(0, audioDevices.IndexOf(_settingsRepo.Settings.AudioDevice));

        var listHeight = Math.Clamp(audioDevices.Count, 4, 14);
        var dialog = new Dialog { Title = "Audio Input Source", Width = 75, Height = listHeight + 8 };

        var label = new Label { Text = "Available devices (↑↓ scroll):", X = 1, Y = 1 };
        var listView = new ListView
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill() - 2,
            Height = listHeight,
            CanFocus = true,
        };
        listView.SetSource(audioDevices);
        listView.SelectedItem = initialIndex;

        var okBtn = new Button { Text = "OK", X = Pos.Center() - 14, Y = Pos.Bottom(listView) + 1 };
        var cancelBtn = new Button { Text = "Cancel", X = Pos.Center() + 1, Y = Pos.Bottom(listView) + 1 };

        void ConfirmAudio()
        {
            if (listView.SelectedItem.HasValue)
            {
                _settingsRepo.Settings.AudioDevice = audioDevices[listView.SelectedItem.Value];
            }

            _settingsRepo.Save();
            _app.RequestStop();
        }

        okBtn.Accepting += (_, _) => ConfirmAudio();
        listView.Accepting += (_, e) =>
        {
            ConfirmAudio();
            e.Handled = true;
        };
        cancelBtn.Accepting += (_, _) => _app.RequestStop();

        dialog.Add(label, listView, okBtn, cancelBtn);
        _app.Run(dialog);
        dialog.Dispose();
    }

    private void ShowLanguageDialog()
    {
        var languages = SupportedLanguages.All;
        var languageLabels = new ObservableCollection<string>(languages.Select(l => $"{l.Label} ({l.Code})"));

        var recognitionIndex = Math.Max(0, IndexOfLanguage(_settingsRepo.Settings.RecognitionLang));
        var translationIndex = IndexOfLanguage(_settingsRepo.Settings.TranslationLang);
        if (translationIndex < 0)
        {
            translationIndex = 1;
        }

        // Clamp list height to available terminal space; dialog height derived from it
        var listHeight = Math.Clamp(languages.Count, 4, 14);
        var dialogHeight = listHeight + 9; // Y=1 label + Y=2 blank + Y=3 list + 1 gap + 3 buttons + 2 border

        var dialog = new Dialog { Title = "Language Settings", Width = 65, Height = dialogHeight };

        var recognitionLabel = new Label { Text = "Recognition (↑↓ scroll):", X = 2, Y = 1 };
        var translationLabel = new Label { Text = "Translation  (↑↓ scroll):", X = 34, Y = 1 };

        var recognitionList = new ListView { X = 2, Y = 3, Width = 28, Height = listHeight, CanFocus = true };
        recognitionList.SetSource(languageLabels);
        recognitionList.SelectedItem = recognitionIndex;

        var translationList = new ListView { X = 34, Y = 3, Width = 28, Height = listHeight, CanFocus = true };
        translationList.SetSource(languageLabels);
        translationList.SelectedItem = translationIndex;

        var okBtn = new Button { Text = "OK", X = Pos.Center() - 14, Y = Pos.AnchorEnd(3) };
        var cancelBtn = new Button { Text = "Cancel", X = Pos.Center() + 1, Y = Pos.AnchorEnd(3) };

        void ConfirmLanguage()
        {
            if (recognitionList.SelectedItem.HasValue)
            {
                _settingsRepo.Settings.RecognitionLang = languages[recognitionList.SelectedItem.Value].Code;
            }

            if (translationList.SelectedItem.HasValue)
            {
                _settingsRepo.Settings.TranslationLang = languages[translationList.SelectedItem.Value].Code;
            }

            _settingsRepo.Save();
            _app.RequestStop();
        }

        okBtn.Accepting += (_, _) => ConfirmLanguage();
        recognitionList.Accepting += (_, e) =>
        {
            ConfirmLanguage();
            e.Handled = true;
        };
        translationList.Accepting += (_, e) =>
        {
            ConfirmLanguage();
            e.Handled = true;
        };
        cancelBtn.Accepting += (_, _) => _app.RequestStop();

        dialog.Add(recognitionLabel, recognitionList, translationLabel, translationList, okBtn, cancelBtn);
        _app.Run(dialog);
        dialog.Dispose();
    }

    private static int IndexOfLanguage(string code)
    {
        for (var i = 0; i < SupportedLanguages.All.Count; i++)
        {
            if (SupportedLanguages.All[i].Code == code)
            {
                return i;
            }
        }

        return -1;
    }
}
