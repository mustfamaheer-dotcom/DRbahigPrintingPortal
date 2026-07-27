using Microsoft.EntityFrameworkCore;
using PrintingBooksPortal.Data;
using PrintingBooksPortal.Models;

namespace PrintingBooksPortal.Services;

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;

    private const string DefaultWatermarkText = "LICENSED TO: {shopName}\nUSER: {userName}\nDATE: {date}\nDO NOT DISTRIBUTE";

    public SettingsService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsWatermarkEnabledAsync()
    {
        try
        {
            return await GetBoolSettingAsync(SystemSettingKeys.WatermarkEnabled, true);
        }
        catch
        {
            return true;
        }
    }

    public async Task SetWatermarkEnabledAsync(bool enabled)
    {
        try
        {
            await SetBoolSettingAsync(SystemSettingKeys.WatermarkEnabled, enabled);
        }
        catch
        {
            // Silently fail — settings will be retried next time
        }
    }

    public async Task<string> GetWatermarkTextAsync()
    {
        try
        {
            var setting = await _db.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == SystemSettingKeys.WatermarkText);

            if (setting == null || string.IsNullOrWhiteSpace(setting.ValueString))
            {
                return DefaultWatermarkText;
            }

            return setting.ValueString;
        }
        catch
        {
            return DefaultWatermarkText;
        }
    }

    public async Task SetWatermarkTextAsync(string text)
    {
        try
        {
            var setting = await _db.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == SystemSettingKeys.WatermarkText);

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    Key = SystemSettingKeys.WatermarkText,
                    ValueString = text
                };
                _db.SystemSettings.Add(setting);
            }
            else
            {
                setting.ValueString = text;
            }

            await _db.SaveChangesAsync();
        }
        catch
        {
            // Silently fail — settings will be retried next time
        }
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue)
    {
        try
        {
            var setting = await _db.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                setting = new SystemSetting { Key = key, ValueBool = defaultValue };
                _db.SystemSettings.Add(setting);
                await _db.SaveChangesAsync();
            }

            return setting.ValueBool;
        }
        catch
        {
            await EnsureTableCreatedAsync();
            try
            {
                var setting = new SystemSetting { Key = key, ValueBool = defaultValue };
                _db.SystemSettings.Add(setting);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // Table creation also failed — return default
            }
            return defaultValue;
        }
    }

    private async Task SetBoolSettingAsync(string key, bool value)
    {
        try
        {
            var setting = await _db.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                setting = new SystemSetting { Key = key, ValueBool = value };
                _db.SystemSettings.Add(setting);
            }
            else
            {
                setting.ValueBool = value;
            }

            await _db.SaveChangesAsync();
        }
        catch
        {
            await EnsureTableCreatedAsync();
            try
            {
                var setting = new SystemSetting { Key = key, ValueBool = value };
                _db.SystemSettings.Add(setting);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // Table creation also failed — silently fail
            }
        }
    }

    private async Task EnsureTableCreatedAsync()
    {
        try
        {
            // Try pending migrations first
            if ((await _db.Database.GetPendingMigrationsAsync()).Any())
            {
                await _db.Database.MigrateAsync();
                return;
            }
        }
        catch
        {
            // Migration failed, try DDL below
        }

        try
        {
            // Check if table already exists
            await _db.SystemSettings.AnyAsync();

            // Table exists — ensure the ValueString column is present (incomplete migration)
            if (_db.Database.IsSqlServer())
            {
                await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SystemSettings') AND name = 'ValueString')
    ALTER TABLE [SystemSettings] ADD [ValueString] nvarchar(2000) NULL");
            }
            return;
        }
        catch
        {
            // Table doesn't exist — create it below
        }

        try
        {
            if (_db.Database.IsSqlServer())
            {
                await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemSettings')
BEGIN
    CREATE TABLE [SystemSettings] (
        [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Key] nvarchar(100) NOT NULL,
        [ValueBool] bit NOT NULL DEFAULT 1,
        [ValueString] nvarchar(2000) NULL
    );
    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
END");
            }
            else
            {
                await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [SystemSettings] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_SystemSettings] PRIMARY KEY AUTOINCREMENT,
    [Key] TEXT NOT NULL,
    [ValueBool] INTEGER NOT NULL DEFAULT 1,
    [ValueString] TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);");
            }
        }
        catch
        {
            // DDL also failed — will return defaults gracefully
        }
    }
}
