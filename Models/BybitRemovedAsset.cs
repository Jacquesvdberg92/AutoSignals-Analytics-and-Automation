using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models
{
	public class BybitRemovedAsset
	{
		public int Id { get; set; }
		public string Symbol { get; set; }
		public DateTime Time { get; set; }
	}
}

