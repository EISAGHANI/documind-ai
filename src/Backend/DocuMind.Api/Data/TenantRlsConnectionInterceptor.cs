using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DocuMind.Api.Data;

public class RowLevelSecurityOptions
{
    // Off by default. Turn on only after applying Sql/row-level-security.sql (see docs/architecture.md).
    public bool Enabled { get; set; }
}

// Defence in depth behind the EF query filters: tells Postgres which tenant a connection serves
// (`app.tenant_id`) so row-level-security policies can reject cross-tenant rows even if a query
// forgets its filter. Connections with no tenant (workers, migrations) leave the setting empty.
public class TenantRlsConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await Apply(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection, eventData, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static async Task Apply(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken)
    {
        var tenantId = (eventData.Context as ApplicationDbContext)?.TenantIdForConnection ?? Guid.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantId == Guid.Empty ? "" : tenantId.ToString();
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
