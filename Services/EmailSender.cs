using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System.Threading.Tasks;

namespace AutoSignals.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public EmailSender(IConfiguration configuration, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _env = env;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var mailerController = new MailerController(_configuration, _env);
            mailerController.SendEmail(email, subject, htmlMessage);
            return Task.CompletedTask;
        }
    }
}
