using AutoSignals.Services.NOWPayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoSignals.Controllers
{
    [ApiController]
    [Route("api/nowpayments")]
    public class NOWPaymentsWebhookController : ControllerBase
    {
        private readonly NOWPaymentsWebhookService _webhookService;
        private readonly ILogger<NOWPaymentsWebhookController> _logger;

        public NOWPaymentsWebhookController(
            NOWPaymentsWebhookService webhookService,
            ILogger<NOWPaymentsWebhookController> logger)
        {
            _webhookService = webhookService;
            _logger = logger;
        }

        /// <summary>
        /// NOWPayments IPN callback endpoint.
        /// Must be reachable anonymously — NOWPayments calls this server-to-server.
        /// Raw body must be read before model-binding for HMAC-SHA512 verification.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Webhook()
        {
            // 1. Read raw body
            Request.EnableBuffering();
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync();
            Request.Body.Position = 0;

            _logger.LogInformation(
                "NOWPayments IPN received. BodyLength={Len} HasSigHeader={HasSig} RemoteIP={IP}",
                rawBody.Length,
                Request.Headers.ContainsKey("x-nowpayments-sig"),
                HttpContext.Connection.RemoteIpAddress);

            // 2. Validate HMAC-SHA512 signature
            var signature = Request.Headers["x-nowpayments-sig"].FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(signature) || !_webhookService.IsValidSignature(rawBody, signature))
            {
                _logger.LogWarning(
                    "NOWPayments IPN signature validation failed. " +
                    "SignaturePresent={HasSig} IpnSecretConfigured={HasSecret} BodyLength={Len}. " +
                    "Check that NOWPayments:IpnSecret matches the IPN Secret in the NOWPayments dashboard.",
                    !string.IsNullOrWhiteSpace(signature),
                    _webhookService.IsIpnSecretConfigured,
                    rawBody.Length);
                return Unauthorized();
            }

            // 3. Dispatch to webhook service
            try
            {
                await _webhookService.HandleEventAsync(rawBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NOWPayments IPN processing threw an exception.");
                // Return 500 so NOWPayments retries
                return StatusCode(500);
            }

            return Ok();
        }
    }
}
