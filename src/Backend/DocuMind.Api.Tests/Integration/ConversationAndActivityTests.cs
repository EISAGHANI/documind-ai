using DocuMind.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocuMind.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ConversationAndActivityTests(PostgresFixture postgres)
{
    [SkippableFact]
    public async Task Conversations_messages_and_activity_are_tenant_scoped()
    {
        Skip.IfNot(postgres.Available, "Docker is not available");
        var (tenantA, _) = await postgres.SeedTenantAsync("Cyberdyne", 0.5f);
        var (tenantB, _) = await postgres.SeedTenantAsync("Tyrell", 0.5f);
        var user = Guid.NewGuid();

        await using (var seedA = postgres.CreateContext(tenantA))
        {
            var conversation = new ChatConversation { TenantId = tenantA, UserId = user, Title = "Hello" };
            conversation.Messages.Add(new ChatMessageEntity { TenantId = tenantA, Role = "user", Content = "Hi" });
            seedA.ChatConversations.Add(conversation);
            seedA.ActivityLog.Add(new ActivityLogEntry { TenantId = tenantA, Action = "document.upload" });
            await seedA.SaveChangesAsync();
        }

        await using var asA = postgres.CreateContext(tenantA);
        await using var asB = postgres.CreateContext(tenantB);

        Assert.Single(await asA.ChatConversations.Where(c => c.UserId == user).ToListAsync());
        Assert.Single(await asA.ChatMessages.ToListAsync());
        Assert.NotEmpty(await asA.ActivityLog.ToListAsync());

        Assert.Empty(await asB.ChatConversations.ToListAsync());
        Assert.Empty(await asB.ChatMessages.ToListAsync());
        Assert.Empty(await asB.ActivityLog.ToListAsync());
    }
}
