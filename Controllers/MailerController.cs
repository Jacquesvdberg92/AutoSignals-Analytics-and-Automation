using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net.Mime;
using System.Text;

[Authorize(Roles = "Admin")]
public class MailerController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public MailerController(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _env = env;
    }

    [HttpPost]
    public ActionResult SendEmail(string receiver, string subject, string message)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var emailSettings = _configuration.GetSection("EmailSettings");
                var senderEmailConfig = emailSettings["SenderEmail"];
                if (string.IsNullOrEmpty(senderEmailConfig))
                    throw new ArgumentException("Sender email address cannot be null or empty", nameof(senderEmailConfig));

                var senderEmail = new MailAddress(senderEmailConfig, "AutoSignals (No-reply)");
                var receiverEmail = new MailAddress(receiver, receiver);
                var password = emailSettings["Password"];
                var smtp = new SmtpClient
                {
                    Host = emailSettings["Host"],
                    Port = int.Parse(emailSettings["Port"]),
                    EnableSsl = bool.Parse(emailSettings["EnableSsl"]),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(senderEmail.Address, password)
                };

                using (var mess = new MailMessage(senderEmail, receiverEmail)
                {
                    Subject = subject,
                    BodyEncoding = Encoding.UTF8
                })
                {
                    // Build HTML view and embed logo as CID
                    var htmlView = AlternateView.CreateAlternateViewFromString(message, Encoding.UTF8, MediaTypeNames.Text.Html);

                    const string logoCid = "logo-header";
                    var logoPhysicalPath = System.IO.Path.Combine(_env.WebRootPath, "assets", "images", "brand-logos", "signal-header.jpeg");

                    if (System.IO.File.Exists(logoPhysicalPath) && message.Contains($"cid:{logoCid}", StringComparison.OrdinalIgnoreCase))
                    {
                        var logoResource = new LinkedResource(logoPhysicalPath, MediaTypeNames.Image.Jpeg)
                        {
                            ContentId = logoCid,
                            TransferEncoding = TransferEncoding.Base64,
                            ContentType = new ContentType(MediaTypeNames.Image.Jpeg)
                        };
                        htmlView.LinkedResources.Add(logoResource);
                    }

                    mess.AlternateViews.Add(htmlView);
                    mess.IsBodyHtml = true; // harmless when AlternateViews exist

                    smtp.Send(mess);
                }
                return View();
            }
        }
        catch (Exception ex)
        {
            ViewBag.Error = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}";
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
        return View();
    }
}
