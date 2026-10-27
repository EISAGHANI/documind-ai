-- Optional Postgres row-level security (RLS) for tenant tables. Defence in depth behind the EF query
-- filters; see docs/architecture.md. Apply manually with a privileged role, then set
-- RowLevelSecurity__Enabled=true so each connection sets `app.tenant_id`.
--
-- Semantics: when `app.tenant_id` is set (API requests), only that tenant's rows are visible/writable.
-- When it is empty (sign-in before a tenant is known, workers, migrations) the policy allows access,
-- so those paths keep working and still rely on the application-level filters.
--
-- RLS does not apply to the table owner or superusers: run the application as a separate,
-- non-owner role (GRANT SELECT, INSERT, UPDATE, DELETE) or add FORCE ROW LEVEL SECURITY below.

DO $$
DECLARE
    t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'Documents', 'DocumentChunks', 'TenantApiKeys', 'TenantSettings', 'TenantMemberships',
        'TenantInvitations', 'AuditChecklists', 'AuditRuns', 'ChatConversations', 'ChatMessages', 'ActivityLog']
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %I USING (
                coalesce(current_setting(''app.tenant_id'', true), '''') = ''''
                OR "TenantId" = current_setting(''app.tenant_id'', true)::uuid)', t);
    END LOOP;
END $$;

-- To roll back: ALTER TABLE "<table>" DISABLE ROW LEVEL SECURITY; for each table above.
