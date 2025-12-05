using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models
{
    /// <summary>
    /// Stores analytical data per page.
    /// </summary>
    public class Analytics
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(256)]
        public string PageName { get; set; }

        public DateTime Date { get; set; }

        public int Views { get; set; }

        // Optional: Track unique users or sessions
        // public string UserId { get; set; }
        // public string SessionId { get; set; }
    }
}
