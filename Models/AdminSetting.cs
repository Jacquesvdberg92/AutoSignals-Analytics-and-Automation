using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models
{
    public class AdminSetting
    {
        [Key]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Value { get; set; } = string.Empty;
    }
}
