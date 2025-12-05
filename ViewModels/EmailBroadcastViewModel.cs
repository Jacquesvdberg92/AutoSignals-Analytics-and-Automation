using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AutoSignals.ViewModels
{
    public class EmailBroadcastViewModel
    {
        [Required]
        [Display(Name = "Subject")]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Body (HTML)")]
        public string HtmlBody { get; set; } = string.Empty;

        [Display(Name = "Send as test to me only")]
        public bool IsTest { get; set; }

        [Display(Name = "Send to all confirmed users")]
        public bool SendToAll { get; set; }

        public List<RecipientViewModel> Recipients { get; set; } = new();

        public List<string> SelectedRecipientIds { get; set; } = new();
    }

    public class RecipientViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
