using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    public class AdminSettingService
    {
        private readonly AutoSignalsDbContext _context;

        public AdminSettingService(AutoSignalsDbContext context)
        {
            _context = context;
        }

        public async Task<bool> IsEnabledAsync(string key, bool defaultValue = true)
        {
            var setting = await _context.AdminSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == key)
                .ConfigureAwait(false);

            return setting == null ? defaultValue : setting.Value == "true";
        }

        public async Task SetAsync(string key, string value)
        {
            var setting = await _context.AdminSettings
                .FindAsync(key)
                .ConfigureAwait(false);

            if (setting == null)
                _context.AdminSettings.Add(new AdminSetting { Key = key, Value = value });
            else
                setting.Value = value;

            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
