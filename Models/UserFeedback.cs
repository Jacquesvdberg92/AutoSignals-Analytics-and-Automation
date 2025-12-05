using System;
using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models
{
    public class UserFeedback
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string UserId { get; set; }
        [Required, StringLength(200)]
        public string Subject { get; set; }
        [Required, StringLength(2000)]
        public string Message { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string Status { get; set; }
        public virtual ICollection<UserFeedbackImage> Images { get; set; } = new List<UserFeedbackImage>();
    }

    public class UserFeedbackImage
    {
        [Key]
        public int Id { get; set; }
        public int UserFeedbackId { get; set; }
        public byte[] Data { get; set; }
        public string FileName { get; set; }
        public virtual UserFeedback UserFeedback { get; set; }
    }
}