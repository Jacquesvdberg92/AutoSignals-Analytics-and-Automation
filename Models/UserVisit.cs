using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models
{
    public class UserVisit
    {
        public long Id { get; set; }

        [MaxLength(450)]
        public string? UserId { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [MaxLength(256)]
        public string? PagePath { get; set; }

        public DateTime Timestamp { get; set; }

        public long BytesSent { get; set; }
    }
}
