using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AutoSignals.ViewModels;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class EmailBroadcastController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _emailSender;

        public EmailBroadcastController(UserManager<IdentityUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users
                .Where(u => u.EmailConfirmed && u.Email != null)
                .Select(u => new RecipientViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName!,
                    Email = u.Email!
                })
                .ToListAsync();

            var vm = new EmailBroadcastViewModel
            {
                Recipients = users
            };

            return View(vm);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Send(EmailBroadcastViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // repopulate recipients on error
                var users = await _userManager.Users
                    .Where(u => u.EmailConfirmed && u.Email != null)
                    .Select(u => new RecipientViewModel { Id = u.Id, UserName = u.UserName!, Email = u.Email! })
                    .ToListAsync();
                model.Recipients = users;
                return View("Index", model);
            }

            // Always wrap the provided HTML body with our email template
            string wrappedHtml = WrapEmail(model.Subject, model.HtmlBody);

            if (model.IsTest)
            {
                var me = await _userManager.GetUserAsync(User);
                if (me?.Email != null)
                {
                    await _emailSender.SendEmailAsync(me.Email, model.Subject, wrappedHtml);
                }
                TempData["Status"] = "Test email sent to you.";
                return RedirectToAction(nameof(Index));
            }

            var query = _userManager.Users.Where(u => u.EmailConfirmed && u.Email != null);
            if (!model.SendToAll)
            {
                if (model.SelectedRecipientIds == null || model.SelectedRecipientIds.Count == 0)
                {
                    ModelState.AddModelError(string.Empty, "Select at least one recipient or choose Send to all.");
                    var users = await _userManager.Users
                        .Where(u => u.EmailConfirmed && u.Email != null)
                        .Select(u => new RecipientViewModel { Id = u.Id, UserName = u.UserName!, Email = u.Email! })
                        .ToListAsync();
                    model.Recipients = users;
                    return View("Index", model);
                }
                query = query.Where(u => model.SelectedRecipientIds.Contains(u.Id));
            }

            var recipients = await query.Select(u => u.Email!).ToListAsync();
            foreach (var email in recipients)
            {
                await _emailSender.SendEmailAsync(email, model.Subject, wrappedHtml);
            }
            TempData["Status"] = $"Email sent to {recipients.Count} user(s).";
            return RedirectToAction(nameof(Index));
        }

        private static readonly System.Text.RegularExpressions.Regex HtmlTagRegex =
            new System.Text.RegularExpressions.Regex("<\\w+[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string NormalizeBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;

            // If it already looks like HTML, leave it unchanged.
            if (HtmlTagRegex.IsMatch(body)) return body;

            // Otherwise, treat as plain text: HTML-encode and turn newlines into <br>.
            var encoded = System.Net.WebUtility.HtmlEncode(body);
            return encoded.Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
        }

        private string WrapEmail(string subject, string bodyHtml)
        {
            // keep building Terms URL as before (optional)
            var scheme = Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && !string.IsNullOrWhiteSpace(proto)
                ? proto.ToString()
                : Request.Scheme;
            var baseUrl = $"{scheme}://{Request.Host}{Request.PathBase}";
            var termsPath = Url.Page("/terms_conditions") ?? "/terms_conditions";
            var termsUrl = $"{baseUrl}{termsPath}";

            // IMPORTANT: use CID for the logo
            const string logoCid = "logo-header";

            // Normalize body to respect new lines when plain text is entered
            var formattedBody = NormalizeBody(bodyHtml);

            var html = $"""
<!doctype html>
<html>
  <body style="margin:0;padding:0;background:#f6f7fb;">
    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f7fb;">
      <tr>
        <td align="center">
          <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border:1px solid #e5e7eb;">
            <tr>
              <td style="padding:20px 24px;border-bottom:1px solid #e5e7eb;">
                <table role="presentation" width="100%">
                  <tr>
                    <td width="56" valign="middle">
                      <img src="cid:{logoCid}" width="180" height="60" alt="AutoSignals" style="display:block;border:0;border-radius:6px;">
                    </td>
                    <td valign="middle" style="font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
                      <div style="font-size:18px;font-weight:600;color:#111827;">AutoSignals</div>
                      <div style="font-size:12px;color:#6b7280;">Analytics and automation</div>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
            <tr>
              <td style="padding:24px;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#111827;line-height:1.6;font-size:14px;">
                <h2 style="margin:0 0 12px 0;font-size:18px;">{subject}</h2>
                {formattedBody}
              </td>
            </tr>
            <tr>
              <td style="padding:16px 24px;border-top:1px solid #e5e7eb;background:#f9fafb;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
                <div style="font-size:11px;color:#6b7280;line-height:1.5;">
                  Risk disclosure: Trading cryptocurrencies involves substantial risk of loss and is not suitable for every investor. Signals and analytics are provided "as is" and without warranty. Past performance is not indicative of future results.
                  Read our <a href="{termsUrl}" style="color:#2563eb;text-decoration:underline;">Terms &amp; Conditions</a>.
                </div>
              </td>
            </tr>
          </table>
        </td>
      </tr>
    </table>
  </body>
</html>
""";
            return html;
        }
    }
}
