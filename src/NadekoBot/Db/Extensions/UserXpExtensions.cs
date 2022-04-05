﻿#nullable disable
using LinqToDB;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Services.Database;
using NadekoBot.Services.Database.Models;

namespace NadekoBot.Db;

public static class UserXpExtensions
{
    public static UserXpStats GetOrCreateUserXpStats(this NadekoContext ctx, ulong guildId, ulong userId)
    {
        var usr = ctx.UserXpStats.FirstOrDefault(x => x.UserId == userId && x.GuildId == guildId);

        if (usr is null)
        {
            ctx.Add(usr = new()
            {
                Xp = 0,
                UserId = userId,
                NotifyOnLevelUp = XpNotificationLocation.None,
                GuildId = guildId
            });
        }

        return usr;
    }

    public static List<UserXpStats> GetUsersFor(this DbSet<UserXpStats> xps, ulong guildId, int page)
        => xps.AsQueryable()
              .AsNoTracking()
              .Where(x => x.GuildId == guildId)
              .OrderByDescending(x => x.Xp + x.AwardedXp)
              .Skip(page * 9)
              .Take(9)
              .ToList();

    public static List<UserXpStats> GetTopUserXps(this DbSet<UserXpStats> xps, ulong guildId, int count)
        => xps.AsQueryable()
              .AsNoTracking()
              .Where(x => x.GuildId == guildId)
              .OrderByDescending(x => x.Xp + x.AwardedXp)
              .Take(count)
              .ToList();

    public static int GetUserGuildRanking(this DbSet<UserXpStats> xps, ulong userId, ulong guildId)
        => xps.AsQueryable()
              .AsNoTracking()
              .Where(x => x.GuildId == guildId
                          && x.Xp + x.AwardedXp
                          > xps.AsQueryable()
                               .Where(y => y.UserId == userId && y.GuildId == guildId)
                               .Select(y => y.Xp + y.AwardedXp)
                               .FirstOrDefault())
              .Count()
           + 1;

    public static void ResetGuildUserXp(this DbSet<UserXpStats> xps, ulong userId, ulong guildId)
        => xps.Delete(x => x.UserId == userId && x.GuildId == guildId);

    public static void ResetGuildXp(this DbSet<UserXpStats> xps, ulong guildId)
        => xps.Delete(x => x.GuildId == guildId);
}