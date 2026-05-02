using System;
using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models
{
    public class UserFeedback
    {
        [Key]
        public int Id { get; set; }
        /// <summary>Human-readable ticket reference, e.g. TKT-00042</summary>
        public string TicketNumber { get; set; } = string.Empty;
        [Required]
        public string UserId { get; set; }
        [Required, StringLength(200)]
        public string Subject { get; set; }
        [Required, StringLength(2000)]
        public string Message { get; set; }
        public DateTime SubmittedAt { get; set; }
        /// <summary>New | Open | In Progress | Resolved | Closed</summary>
        public string Status { get; set; } = "New";
        /// <summary>Normal or Important. Set to Important automatically for VIP users.</summary>
        public string Priority { get; set; } = "Normal";
        [StringLength(2000)]
        public string? AdminNotes { get; set; }
        /// <summary>UserId of the admin this ticket is assigned to, or null if unassigned.</summary>
        public string? AssignedTo { get; set; }
        public virtual ICollection<UserFeedbackImage> Images { get; set; } = new List<UserFeedbackImage>();
        public virtual ICollection<UserFeedbackReply> Replies { get; set; } = new List<UserFeedbackReply>();
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

    public class UserFeedbackReply
    {
        [Key]
        public int Id { get; set; }
        public int UserFeedbackId { get; set; }
        [Required]
        public string AuthorId { get; set; }
        [Required, StringLength(4000)]
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>True when the reply was written by an admin.</summary>
        public bool IsAdminReply { get; set; }
        public virtual UserFeedback UserFeedback { get; set; }
    }
}
