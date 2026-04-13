using AutoSignals.Services.LemonSqueezy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoSignals.Controllers
{
    [ApiController]
    [Route("api/lemonsqueezy")]
    public class LemonSqueezyWebhookController : ControllerBase
    {
        private readonly LemonSqueezyWebhookService _webhookService;
        private readonly ILogger<LemonSqueezyWebhookController> _logger;

        public LemonSqueezyWebhookController(
            LemonSqueezyWebhookService webhookService,
            ILogger<LemonSqueezyWebhookController> logger)
        {
            _webhookService = webhookService;
            _logger = logger;
        }

        /// <summary>
        /// LemonSqueezy webhook endpoint.
        /// Must be reachable anonymously — LemonSqueezy calls this server-to-server.
        /// Raw body must be read before any model-binding touches it for HMAC verification.
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

            // 2. Validate HMAC-SHA256 signature
            var signature = Request.Headers["X-Signature"].FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(signature) || !_webhookService.IsValidSignature(rawBody, signature))
            {
                _logger.LogWarning("LemonSqueezy webhook received with invalid signature.");
                return Unauthorized();
            }

            // 3. Dispatch to webhook service
            try
            {
                await _webhookService.HandleEventAsync(rawBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LemonSqueezy webhook processing threw an exception.");
                // Return 500 so LemonSqueezy retries
                return StatusCode(500);
            }

            // 4. Always return 200 — LemonSqueezy retries on non-2xx
            return Ok();
        }
    }
}
