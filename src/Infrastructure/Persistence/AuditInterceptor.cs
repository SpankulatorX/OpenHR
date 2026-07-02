using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RegionHR.Audit.Domain;
using RegionHR.IntegrationHub.Infrastructure;

namespace RegionHR.Infrastructure.Persistence;

/// <summary>
/// Abstraktion över "vem är inloggad just nu?" så att infrastrukturlagret kan
/// stämpla granskningsposter med den faktiska användaren utan att känna till
/// webblagret. Interfacet ligger i Infrastructure (inte Web) eftersom
/// beroenderiktningen är Web → Infrastructure; den HttpContext-baserade
/// implementationen finns i <c>src/Web/Services/HttpContextCurrentUser.cs</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Stabil identifierare för den inloggade (t.ex. employee_id), eller null om okänd.</summary>
    string? Id { get; }

    /// <summary>Visningsnamn för den inloggade, eller null om okänd.</summary>
    string? Namn { get; }
}

/// <summary>
/// EF Core SaveChangesInterceptor that automatically creates AuditEntry records
/// when entities are created, updated, or deleted.
///
/// Granskningsposter stämplas med den inloggade användaren via
/// <see cref="ICurrentUser"/>. När ingen användare kan härledas (bakgrundsjobb,
/// seeding, migrationer) faller stämpeln tillbaka på "system".
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private const string SystemUser = "system";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ICurrentUser? _currentUser;

    /// <summary>
    /// <paramref name="currentUser"/> är avsiktligt valfri: bakgrundstjänster och
    /// tester utan användarkontext kan skapa interceptorn utan argument och får
    /// då "system"-stämpling som tidigare.
    /// </summary>
    public AuditInterceptor(ICurrentUser? currentUser = null)
    {
        _currentUser = currentUser;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is RegionHRDbContext dbContext)
        {
            CreateAuditEntries(dbContext);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is RegionHRDbContext dbContext)
        {
            CreateAuditEntries(dbContext);
        }

        return base.SavingChanges(eventData, result);
    }

    private void CreateAuditEntries(RegionHRDbContext dbContext)
    {
        // Härled användarstämpel EN gång per SaveChanges. Tomt/whitespace
        // behandlas som okänt → "system" (spårbarheten får aldrig bli "").
        var currentId = _currentUser?.Id;
        var currentNamn = _currentUser?.Namn;
        var userId = string.IsNullOrWhiteSpace(currentId) ? SystemUser : currentId;
        var userName = string.IsNullOrWhiteSpace(currentNamn) ? SystemUser : currentNamn;

        var entries = dbContext.ChangeTracker
            .Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditEntry)
            .Where(e => e.Entity is not OutboxMessage)
            .ToList();

        foreach (var entry in entries)
        {
            var entityType = entry.Entity.GetType().Name;
            var entityId = GetEntityId(entry);
            var action = MapAction(entry.State);
            string? oldValues = null;
            string? newValues = null;

            switch (entry.State)
            {
                case EntityState.Added:
                    newValues = SerializeProperties(entry, EntityState.Added);
                    break;

                case EntityState.Modified:
                    oldValues = SerializeOldValues(entry);
                    newValues = SerializeNewValues(entry);
                    break;

                case EntityState.Deleted:
                    oldValues = SerializeProperties(entry, EntityState.Deleted);
                    break;
            }

            var auditEntry = AuditEntry.Create(
                entityType: entityType,
                entityId: entityId,
                action: action,
                oldValues: oldValues,
                newValues: newValues,
                userId: userId,
                userName: userName,
                ipAddress: null);

            dbContext.AuditEntries.Add(auditEntry);
        }
    }

    private static string GetEntityId(EntityEntry entry)
    {
        // Try to get the "Id" property via reflection
        var idProperty = entry.Entity.GetType().GetProperty("Id");
        if (idProperty is not null)
        {
            var value = idProperty.GetValue(entry.Entity);
            return value?.ToString() ?? "unknown";
        }

        // Fallback: try to get the primary key from EF metadata
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties is not null && keyProperties.Count > 0)
        {
            var keyValues = keyProperties
                .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "null");
            return string.Join(",", keyValues);
        }

        return "unknown";
    }

    private static AuditAction MapAction(EntityState state) => state switch
    {
        EntityState.Added => AuditAction.Create,
        EntityState.Modified => AuditAction.Update,
        EntityState.Deleted => AuditAction.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unexpected entity state for auditing")
    };

    private static string SerializeProperties(EntityEntry entry, EntityState state)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty())
                continue;

            var value = state == EntityState.Deleted
                ? property.OriginalValue
                : property.CurrentValue;

            dict[property.Metadata.Name] = value;
        }

        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    private static string SerializeOldValues(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in entry.Properties.Where(p => p.IsModified))
        {
            dict[property.Metadata.Name] = property.OriginalValue;
        }

        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    private static string SerializeNewValues(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in entry.Properties.Where(p => p.IsModified))
        {
            dict[property.Metadata.Name] = property.CurrentValue;
        }

        return JsonSerializer.Serialize(dict, JsonOptions);
    }
}
