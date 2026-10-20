using System.Collections.ObjectModel;
using System.Diagnostics;
using SttConsole.Models;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace SttConsole.UI;

public partial class MainWindow
{
    private void ShowSpeechServiceDialog()
    {
        // Layout:
        //  Y=1  Endpoint label   Y=2  Endpoint field
        //  Y=4  Region label     Y=5  Region field
        //  Y=7  2-Pass checkbox
        //  Y=9  Authentication label
        //  Y=11 auth ListView (H=2)
        //  Y=14 dynamic section:
        //    API Key:        Y=14 label, Y=15 field
        //    Managed ID:     Y=14-15 Tenant, Y=17-18 Client, Y=20-21 RedirectUri,
        //                    Y=23-24 ResourceId, Y=26 DeviceCode, Y=28 sign-in status + button
        //  Y=30 OK / Cancel buttons
        var settings = _settingsRepo.Settings;
        var authMethods = new ObservableCollection<string> { "API Key", "Managed Identity" };
        var authIndex = (int)settings.SpeechAuth;

        var dialog = new Dialog { Title = "Speech Service Settings", Width = 65, Height = 35 };

        var endpointLabel = new Label { Text = "Endpoint URL (optional):", X = 1, Y = 1 };
        var endpointField = new TextField { Text = settings.SpeechEndpoint, X = 1, Y = 2, Width = Dim.Fill() - 2 };

        var regionLabel = new Label { Text = "Region:", X = 1, Y = 4 };
        var regionField = new TextField { Text = settings.SpeechRegion, X = 1, Y = 5, Width = 30 };

        var twoPassCheckBox = new CheckBox
        {
            Text = "2-Pass Transcription",
            X = 1,
            Y = 7,
            Value = settings.UseTwoPass ? CheckState.Checked : CheckState.UnChecked,
        };

        var authLabel = new Label { Text = "Authentication (↑↓ select):", X = 1, Y = 9 };
        var authList = new ListView { X = 1, Y = 11, Width = 28, Height = 2, CanFocus = true };
        authList.SetSource(authMethods);
        authList.SelectedItem = authIndex;

        // API Key section
        var apiKeyLabel = new Label { Text = "API Key:", X = 1, Y = 14 };
        var apiKeyField = new TextField { Text = settings.SpeechApiKey, X = 1, Y = 15, Width = Dim.Fill() - 2, Secret = true };

        // Managed Identity section
        var tenantLabel = new Label { Text = "Tenant ID:", X = 1, Y = 14 };
        var tenantField = new TextField { Text = settings.TenantId, X = 1, Y = 15, Width = Dim.Fill() - 2 };
        var clientLabel = new Label { Text = "Client ID:", X = 1, Y = 17 };
        var clientField = new TextField { Text = settings.ClientId, X = 1, Y = 18, Width = Dim.Fill() - 2 };
        var redirectUriLabel = new Label { Text = "Redirect URI:", X = 1, Y = 20 };
        var redirectUriField = new TextField
        {
            Text = string.IsNullOrWhiteSpace(settings.RedirectUri) ? AppSettings.DefaultRedirectUri : settings.RedirectUri,
            X = 1,
            Y = 21,
            Width = Dim.Fill() - 2,
        };
        var resourceIdLabel = new Label { Text = "Speech Resource ID (Entra ID):", X = 1, Y = 23 };
        var resourceIdField = new TextField { Text = settings.SpeechResourceId, X = 1, Y = 24, Width = Dim.Fill() - 2 };
        var deviceCodeBox = new CheckBox
        {
            Text = "Device Code Flow",
            X = 1,
            Y = 26,
            Value = settings.DeviceCodeFlow ? CheckState.Checked : CheckState.UnChecked,
        };

        // Sign-in status row (Managed Identity only)
        var isSignedIn = _authService.IsSignedIn;
        string SignInStatusText() => isSignedIn ? "● Signed in" : "○ Not signed in";
        string SignInBtnText() => isSignedIn ? "Sign Out" : "Sign In";

        var signInStatusLabel = new Label { Text = SignInStatusText(), X = 1, Y = 28 };
        var signInBtn = new Button { Text = SignInBtnText(), X = Pos.Right(signInStatusLabel) + 2, Y = 28 };

        signInBtn.Accepting += (_, e) =>
        {
            if (isSignedIn)
            {
                _authService.SignOutAsync().GetAwaiter().GetResult();
                isSignedIn = false;
            }
            else
            {
                // AuthService reads these from settings, and the dialog's OK hasn't been pressed yet
                settings.TenantId = tenantField.Text;
                settings.ClientId = clientField.Text;
                settings.RedirectUri = redirectUriField.Text;
                settings.DeviceCodeFlow = deviceCodeBox.Value == CheckState.Checked;
                isSignedIn = RunSignIn(settings.DeviceCodeFlow);
            }

            signInStatusLabel.Text = SignInStatusText();
            signInBtn.Text = SignInBtnText();
            dialog.Layout();
            e.Handled = true;
        };

        void UpdateVisibility(int index)
        {
            var isApiKey = index == 0;
            apiKeyLabel.Visible = isApiKey;
            apiKeyField.Visible = isApiKey;
            tenantLabel.Visible = !isApiKey;
            tenantField.Visible = !isApiKey;
            clientLabel.Visible = !isApiKey;
            clientField.Visible = !isApiKey;
            redirectUriLabel.Visible = !isApiKey;
            redirectUriField.Visible = !isApiKey;
            resourceIdLabel.Visible = !isApiKey;
            resourceIdField.Visible = !isApiKey;
            deviceCodeBox.Visible = !isApiKey;
            signInStatusLabel.Visible = !isApiKey;
            signInBtn.Visible = !isApiKey;
            dialog.Layout();
        }

        authList.ValueChanged += (_, e) => UpdateVisibility(e.NewValue ?? 0);

        // Prevent Enter in any field / list item from bubbling Command.Accept up to the dialog
        static void SuppressAccept(object? sender, CommandEventArgs e) => e.Handled = true;
        endpointField.Accepting += SuppressAccept;
        regionField.Accepting += SuppressAccept;
        apiKeyField.Accepting += SuppressAccept;
        tenantField.Accepting += SuppressAccept;
        clientField.Accepting += SuppressAccept;
        redirectUriField.Accepting += SuppressAccept;
        resourceIdField.Accepting += SuppressAccept;
        authList.Accepting += SuppressAccept;
        deviceCodeBox.Accepting += SuppressAccept;
        twoPassCheckBox.Accepting += SuppressAccept;

        var okBtn = new Button { Text = "OK", X = Pos.Center() - 14, Y = 30 };
        var cancelBtn = new Button { Text = "Cancel", X = Pos.Center() + 1, Y = 30 };

        okBtn.Accepting += (_, _) =>
        {
            settings.SpeechEndpoint = endpointField.Text;
            settings.SpeechRegion = regionField.Text;
            settings.SpeechAuth = (authList.SelectedItem ?? 0) == 0 ? AuthMethod.ApiKey : AuthMethod.ManagedIdentity;
            if (settings.SpeechAuth == AuthMethod.ApiKey)
            {
                settings.SpeechApiKey = apiKeyField.Text;
            }
            else
            {
                settings.TenantId = tenantField.Text;
                settings.ClientId = clientField.Text;
                settings.RedirectUri = redirectUriField.Text;
                settings.SpeechResourceId = resourceIdField.Text.Trim();
                settings.DeviceCodeFlow = deviceCodeBox.Value == CheckState.Checked;
            }

            settings.UseTwoPass = twoPassCheckBox.Value == CheckState.Checked;
            _settingsRepo.Save();
            _app.RequestStop();
        };
        cancelBtn.Accepting += (_, _) => _app.RequestStop();

        dialog.Add(
            endpointLabel, endpointField, regionLabel, regionField,
            authLabel, authList,
            apiKeyLabel, apiKeyField,
            tenantLabel, tenantField, clientLabel, clientField,
            redirectUriLabel, redirectUriField, resourceIdLabel, resourceIdField, deviceCodeBox,
            signInStatusLabel, signInBtn,
            twoPassCheckBox,
            okBtn, cancelBtn);

        UpdateVisibility(authIndex);
        _app.Run(dialog);
        dialog.Dispose();
    }

    // Runs sign-in on a background task and keeps the TUI responsive while waiting.
    // The dialog closes itself when sign-in finishes; Cancel aborts the pending sign-in.
    private bool RunSignIn(bool deviceCode)
    {
        // Layout:
        //  Y=1  instruction
        //  Y=3  "URL:" label       (device code only)
        //  Y=4  url field + [Open]
        //  Y=6  "Code:" label
        //  Y=7  code field
        //  Y=10 [Cancel]
        var closedByUser = false;
        var verificationUrl = string.Empty;
        Action<string, string>? onDeviceCode = null;

        using var cts = new CancellationTokenSource();
        var dialog = new Dialog
        {
            Title = deviceCode ? "Device Code Authentication" : "Sign In",
            Width = 70,
            Height = deviceCode ? 14 : 8,
        };

        var instruction = new Label
        {
            Text = deviceCode ? "Requesting device code..." : "Complete the sign-in in your browser...",
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
        };
        var cancelBtn = new Button { Text = "Cancel", X = Pos.Center(), Y = deviceCode ? 10 : 4 };
        cancelBtn.Accepting += (_, _) =>
        {
            closedByUser = true;
            cts.Cancel();
            _app.RequestStop();
        };
        dialog.Add(instruction, cancelBtn);

        if (deviceCode)
        {
            var urlLabel = new Label { Text = "URL:", X = 1, Y = 3 };
            var urlField = new TextField { Text = string.Empty, X = 1, Y = 4, Width = Dim.Fill() - 10, ReadOnly = true };
            var openBtn = new Button { Text = "Open", X = Pos.Right(urlField) + 1, Y = 4 };
            var codeLabel = new Label { Text = "Code:  (Shift+← → to select  /  Ctrl+C to copy)", X = 1, Y = 6 };
            var codeField = new TextField { Text = string.Empty, X = 1, Y = 7, Width = 20, ReadOnly = true };

            openBtn.Accepting += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(verificationUrl) { UseShellExecute = true });
                }
                catch (Exception)
                {
                    // The URL stays visible in the field, so the user can open it manually.
                }

                e.Handled = true;
            };
            urlField.Accepting += (_, e) => e.Handled = true;
            codeField.Accepting += (_, e) => e.Handled = true;
            dialog.Add(urlLabel, urlField, openBtn, codeLabel, codeField);

            onDeviceCode = (url, code) => _app.Invoke(() =>
            {
                verificationUrl = url;
                instruction.Text = "Open the URL in your browser and enter the code to sign in:";
                urlField.Text = url;
                codeField.Text = code;
                dialog.Layout();
            });
        }

        // Task.Run keeps a synchronous part of the credential (e.g. launching the browser) off the UI thread
        var authTask = Task.Run(() => _authService.SignInAsync(deviceCode, onDeviceCode, cts.Token));
        _ = authTask.ContinueWith(
            _ => _app.Invoke(() =>
            {
                // Skip when the user already closed the dialog, or RequestStop would close the parent dialog
                if (!closedByUser)
                {
                    _app.RequestStop();
                }
            }),
            TaskScheduler.Default);

        _app.Run(dialog);
        dialog.Dispose();

        if (closedByUser)
        {
            return false;
        }

        if (authTask.IsFaulted)
        {
            ShowMessage($"Sign-in failed: {authTask.Exception!.GetBaseException().Message}", MessageSeverity.Error);
            return false;
        }

        return authTask.IsCompletedSuccessfully && authTask.Result;
    }
}
