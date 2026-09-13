using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Dokpod.Bff.Authentication;

public sealed class ServerSideTicketStore(IMemoryCache cache) : ITicketStore
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Save(key, new TicketEntry(ticket), ticket);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (cache.TryGetValue(key, out TicketEntry? entry) && entry is not null)
        {
            lock (entry.Gate)
            {
                if (!entry.Removed)
                {
                    entry.Ticket = ticket;
                    Save(key, entry, ticket);
                }
            }
        }
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var entry = cache.Get<TicketEntry>(key);
        if (entry is null) return Task.FromResult<AuthenticationTicket?>(null);
        lock (entry.Gate) return Task.FromResult(entry.Removed ? null : entry.Ticket);
    }

    public Task RemoveAsync(string key)
    {
        if (cache.TryGetValue(key, out TicketEntry? entry) && entry is not null)
        {
            lock (entry.Gate)
            {
                entry.Removed = true;
                entry.Ticket = null;
                cache.Set(key, entry, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = DefaultLifetime,
                    Size = 1
                });
            }
        }
        return Task.CompletedTask;
    }

    private void Save(string key, TicketEntry entry, AuthenticationTicket ticket) => cache.Set(key, entry,
        new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.Add(DefaultLifetime),
            Size = 1
        });

    private sealed class TicketEntry(AuthenticationTicket ticket)
    {
        public object Gate { get; } = new();
        public AuthenticationTicket? Ticket { get; set; } = ticket;
        public bool Removed { get; set; }
    }
}