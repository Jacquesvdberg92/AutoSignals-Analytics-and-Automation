using AutoSignals.Models;
using Microsoft.Extensions.Options;
using TL;

namespace AutoSignals.Services
{
    /// <summary>
    /// BackgroundService that uses your personal Telegram account (via WTelegramClient / MTProto)
    /// to receive messages from any group or channel — including ones where bots are not allowed.
    /// The bot (<see cref="TelegramBotService"/>) continues to handle outbound notifications.
    /// </summary>
    public class TelegramUserScannerService : BackgroundService
    {
        private readonly ILogger<TelegramUserScannerService> _logger;
        private readonly TelegramUserClientOptions _options;
        private readonly TelegramMessageProcessorService _processor;

        private WTelegram.Client? _client;

        // Async gates used to hand the one-time phone code / 2FA password from the admin UI
        // back into the login loop without blocking any thread.
        private TaskCompletionSource<string>? _verificationCodeTcs;
        private TaskCompletionSource<string>? _passwordTcs;

        /// <summary>Reflects the current authentication / connection state for the admin UI.</summary>
        public string AuthStatus { get; private set; } = "Not started";

        public TelegramUserScannerService(
            ILogger<TelegramUserScannerService> logger,
            IOptions<TelegramUserClientOptions> options,
            TelegramMessageProcessorService processor)
        {
            _logger = logger;
            _options = options.Value;
            _processor = processor;
        }

        // ── Admin helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Called by the admin controller after the user enters the SMS/app verification code.
        /// </summary>
        public void ProvideVerificationCode(string code) =>
            _verificationCodeTcs?.TrySetResult(code);

        /// <summary>
        /// Called by the admin controller to supply the 2FA cloud password when required.
        /// </summary>
        public void ProvidePassword(string password) =>
            _passwordTcs?.TrySetResult(password);

        // ── BackgroundService ────────────────────────────────────────────────────

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.ApiId == 0 || string.IsNullOrWhiteSpace(_options.ApiHash))
            {
                _logger.LogWarning(
                    "TelegramUserScanner: ApiId/ApiHash not configured — user-account scanning is disabled. " +
                    "Add TelegramUserClient:ApiId and TelegramUserClient:ApiHash to your configuration.");
                AuthStatus = "Not configured";
                return;
            }

            try
            {
                // Suppress WTelegramClient's internal console logging; forward warnings upward.
                WTelegram.Helpers.Log = (level, message) =>
                {
                    if (level >= 3)
                        _logger.LogWarning("[WTelegram] {Message}", message);
                };

                _client = new WTelegram.Client(Config);
                _client.OnUpdates += OnUpdatesAsync;

                AuthStatus = "Connecting";
                _logger.LogInformation("TelegramUserScanner: Starting Telegram user login...");

                // Use the async login loop — never blocks a thread while waiting for user input.
                var loginInfo = _options.PhoneNumber;
                while (_client.User == null)
                {
                    switch (await _client.Login(loginInfo))
                    {
                        case "verification_code":
                            AuthStatus = "WaitingForCode";
                            _logger.LogWarning(
                                "TelegramUserScanner: Verification code required. " +
                                "Navigate to /Admin/TelegramUserAuth and enter the code sent to your phone.");
                            _verificationCodeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                            loginInfo = await _verificationCodeTcs.Task.WaitAsync(stoppingToken);
                            break;

                        case "password":
                            if (!string.IsNullOrWhiteSpace(_options.Password))
                            {
                                loginInfo = _options.Password;
                            }
                            else
                            {
                                AuthStatus = "WaitingForPassword";
                                _logger.LogWarning(
                                    "TelegramUserScanner: 2FA password required. " +
                                    "Navigate to /Admin/TelegramUserAuth and enter your cloud password.");
                                _passwordTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                                loginInfo = await _passwordTcs.Task.WaitAsync(stoppingToken);
                            }
                            break;

                        default:
                            loginInfo = null;
                            break;
                    }
                }

                AuthStatus = "Authenticated";
                _logger.LogInformation(
                    "TelegramUserScanner: Logged in as @{Username} (id={UserId}).",
                    _client.User.username, _client.User.id);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TelegramUserScanner: Stopped.");
            }
            catch (Exception ex)
            {
                AuthStatus = "Error";
                _logger.LogError(ex, "TelegramUserScanner: Fatal error — user-account scanning stopped.");
            }
            finally
            {
                _client?.Dispose();
                _client = null;
            }
        }

        // ── WTelegramClient config callback ──────────────────────────────────────

        // Only needs to provide non-interactive config; phone/code/password are
        // handled through the Login() loop above.
        private string? Config(string what) => what switch
        {
            "api_id"           => _options.ApiId.ToString(),
            "api_hash"         => _options.ApiHash,
            "session_pathname" => _options.SessionPath,
            _                  => null   // let WTelegramClient use its defaults
        };

        // ── Update handler ───────────────────────────────────────────────────────

        private async Task OnUpdatesAsync(UpdatesBase updates)
        {
            foreach (var update in updates.UpdateList)
            {
                // Resolve the TL.Message from every relevant update type.
                Message? msg = update switch
                {
                    UpdateNewMessage u         when u.message is Message m => m,
                    UpdateNewChannelMessage u  when u.message is Message m => m,
                    UpdateEditMessage u        when u.message is Message m => m,
                    UpdateEditChannelMessage u when u.message is Message m => m,
                    _ => null
                };

                if (msg == null) continue;

                // Skip messages sent by ourselves.
                if (msg.flags.HasFlag(Message.Flags.out_)) continue;

                // Only handle group / channel messages — skip private chats.
                var chatId = ToBotApiChatId(msg.peer_id);
                if (chatId == null) continue;

                // msg.message holds the text for text messages and the caption for photo/video.
                var messageText = msg.message;
                if (string.IsNullOrWhiteSpace(messageText)) continue;

                try
                {
                    await _processor.ProcessMessageAsync(messageText, chatId, msg.date);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TelegramUserScanner: Error processing message from chat {ChatId}.", chatId);
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a WTelegramClient <see cref="Peer"/> to the Bot API chat-ID string format
        /// (negative for groups/channels) so it matches the <c>TelegramGroupId</c> stored on
        /// <see cref="AutoSignals.Models.SignalProvider"/> records.
        /// Returns <c>null</c> for private (user) chats — those are intentionally ignored.
        /// </summary>
        private static string? ToBotApiChatId(Peer? peer) => peer switch
        {
            PeerChannel ch => $"-100{ch.channel_id}",   // supergroup or broadcast channel
            PeerChat c     => $"-{c.chat_id}",           // legacy basic group
            _              => null                        // PeerUser → private DM, skip
        };
    }
}

