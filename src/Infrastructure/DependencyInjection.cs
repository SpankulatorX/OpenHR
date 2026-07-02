using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegionHR.Core.Contracts;
using RegionHR.Core.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Payroll.Engine;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Infrastructure.Persistence.Repositories;
using RegionHR.Infrastructure.Authorization;
using RegionHR.Infrastructure.BackgroundJobs;
using RegionHR.Infrastructure.Export;
using RegionHR.Infrastructure.Storage;
using RegionHR.Infrastructure.GDPR;
using RegionHR.Infrastructure.Notifications;
using RegionHR.Infrastructure.Documents;
using RegionHR.Infrastructure.Integrations;
using RegionHR.Infrastructure.Reporting;
using RegionHR.Infrastructure.Payroll;
using RegionHR.Infrastructure.Scheduling;
using RegionHR.Infrastructure.Events;
using RegionHR.Infrastructure.Services;
using RegionHR.Automation.Domain;
using RegionHR.Migration.Adapters;
using RegionHR.HalsoSAM.Services;
using RegionHR.Infrastructure.HalsoSAM;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace RegionHR.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, bool useInMemory = false)
    {
        // Domain event infrastructure
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<DomainEventInterceptor>();

        // Audit interceptor — scoped så att ICurrentUser (registreras i webblagret) kan
        // injiceras och granskningsposter stämplas med den faktiska användaren.
        // När ICurrentUser inte är registrerad (API:t, tester) använder DI
        // konstruktorns default (null) → stämpeln blir "system" som tidigare.
        services.AddScoped<AuditInterceptor>();

        // DbContext
        if (useInMemory)
        {
            services.AddDbContext<RegionHRDbContext>((sp, options) =>
                options.UseInMemoryDatabase("RegionHR-Dev")
                    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>(), sp.GetRequiredService<DomainEventInterceptor>()));

            // Factory-contexterna (de flesta Blazor-sidorna) MÅSTE ha samma interceptors,
            // annars auditloggas/dispatchas inget för ändringar gjorda via IDbContextFactory.
            services.AddDbContextFactory<RegionHRDbContext>((sp, options) =>
                options.UseInMemoryDatabase("RegionHR-Dev")
                    .AddInterceptors(sp.GetRequiredService<AuditInterceptor>(), sp.GetRequiredService<DomainEventInterceptor>()), ServiceLifetime.Scoped);
        }
        else
        {
            // List<string>-kolumner mappas till jsonb — Npgsql kräver explicit
            // EnableDynamicJson(), annars kastar varje write InvalidCastException.
            var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            services.AddDbContext<RegionHRDbContext>((sp, options) =>
                options.UseNpgsql(dataSource, npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                })
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>(), sp.GetRequiredService<DomainEventInterceptor>()));

            // Factory-contexterna (de flesta Blazor-sidorna) MÅSTE ha samma interceptors,
            // annars auditloggas/dispatchas inget för ändringar gjorda via IDbContextFactory.
            services.AddDbContextFactory<RegionHRDbContext>((sp, options) =>
                options.UseNpgsql(dataSource, npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                })
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>(), sp.GetRequiredService<DomainEventInterceptor>()), ServiceLifetime.Scoped);
        }

        // Repositories
        services.AddScoped<IRepository<Employee, EmployeeId>, EmployeeRepository>();
        services.AddScoped<IRepository<PayrollRun, PayrollRunId>, PayrollRunRepository>();
        services.AddScoped<EmployeeRepository>();
        services.AddScoped<PayrollRunRepository>();

        // Module contracts
        services.AddScoped<ICoreHRModule, CoreHRModuleService>();

        // LAS — skrivväg + repo (våg 2)
        services.AddScoped<RegionHR.LAS.Services.ILASRepository, RegionHR.Infrastructure.LAS.LASRepository>();
        services.AddScoped<RegionHR.LAS.Services.LASService>();
        // LAS auto-kedja (våg 6): anställningsevents → LAS-ackumulering automatiskt
        services.AddScoped<RegionHR.LAS.Services.IEmploymentLookup, RegionHR.Infrastructure.LAS.EmploymentLookup>();
        services.AddScoped<RegionHR.LAS.Services.LASAutoChainService>();
        services.AddScoped<IDomainEventHandler<RegionHR.Core.Domain.EmploymentCreatedEvent>, RegionHR.Infrastructure.LAS.EmploymentCreatedLASHandler>();
        services.AddScoped<IDomainEventHandler<RegionHR.Core.Domain.EmploymentEndedEvent>, RegionHR.Infrastructure.LAS.EmploymentEndedLASHandler>();

        // HSA-katalogen (Inera) — DEMO/sandbox. Byt SandboxHsaCatalogAdapter mot skarp
        // adapter (Inera-avtal + SITHS-cert + WS/LDAP-endpoint) när det finns. (våg 2)
        services.AddSingleton<RegionHR.Infrastructure.Integrations.HSA.IHsaCatalogAdapter,
                              RegionHR.Infrastructure.Integrations.HSA.SandboxHsaCatalogAdapter>();
        services.AddScoped<RegionHR.Infrastructure.Integrations.HSA.HsaCatalogSyncService>();

        // Payroll services
        services.AddScoped<ITaxTableProvider, TaxTableRepository>();
        services.AddScoped<ICollectiveAgreementRulesEngine, CollectiveAgreementRulesEngine>();
        services.AddScoped<PayrollCalculationEngine>();

        // Scheduling services
        services.AddSingleton<ConstraintScheduleSolver>();

        // UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<RegionHRDbContext>()));

        // Export services
        services.AddSingleton<ExportService>();
        services.AddSingleton<PdfPayslipGenerator>();
        services.AddSingleton<PdfGenerator>();

        // Authorization services
        services.AddScoped<UnitScopeService>();
        services.AddScoped<UnitAccessScopeService>();

        // Notifications (EmailNotificationSender is the production MailKit impl)
        services.AddSingleton<EmailNotificationSender>();
        services.AddSingleton<SmsNotificationSender>();
        // EmailSender removed — use EmailNotificationSender instead

        // File storage (LocalFileStorageService implements IFileStorageService)
        services.AddSingleton<IFileStorageService>(new LocalFileStorageService());

        // GDPR
        services.AddScoped<RegisterutdragGenerator>();

        // Reporting
        services.AddScoped<ReportGenerator>();
        services.AddScoped<ReportExecutionService>();

        // E-arkiv (arkivlagen) — våg 7
        services.AddScoped<RegionHR.Infrastructure.Documents.IArchiveService, RegionHR.Infrastructure.Documents.ArchiveService>();

        // Web Push (VAPID + sändning) — våg 7. VapidKeyProvider läser "WebPush"-config, demo-fallback annars.
        services.AddSingleton<RegionHR.Infrastructure.Notifications.VapidKeyProvider>();
        services.AddSingleton<RegionHR.Infrastructure.Notifications.WebPushSender>();
        services.AddScoped<RegionHR.Infrastructure.Notifications.PushDispatchService>();

        // Integrationsramverk (Health Connect-kompatibelt) — våg 8. Lokal fil-drop; skarp SFTP config-ready.
        services.AddSingleton(new RegionHR.IntegrationHub.Framework.SftpTransportOptions
        {
            LokalDropKatalog = System.IO.Path.Combine(AppContext.BaseDirectory, "integration-drop")
        });
        services.AddSingleton<RegionHR.IntegrationHub.Framework.ISftpTransport, RegionHR.Infrastructure.Integrations.Framework.LocalFileDropSftpTransport>();
        services.AddScoped<RegionHR.IntegrationHub.Framework.IIntegrationRunLogStore, RegionHR.Infrastructure.Integrations.Framework.EfIntegrationRunLogStore>();
        services.AddScoped<RegionHR.IntegrationHub.Framework.IIntegrationJob, RegionHR.Infrastructure.Integrations.Framework.HealthConnectManifestJob>();
        services.AddScoped(sp => new RegionHR.Infrastructure.Integrations.Framework.IntegrationJobRunner(
            sp.GetServices<RegionHR.IntegrationHub.Framework.IIntegrationJob>(),
            sp.GetRequiredService<RegionHR.IntegrationHub.Framework.ISftpTransport>(),
            sp.GetRequiredService<RegionHR.IntegrationHub.Framework.IIntegrationRunLogStore>()));

        // HälsoSAM — rehabkedja + automatisk triggning (våg 1)
        services.AddScoped<IRehabRepository, RehabRepository>();
        services.AddScoped<RehabService>();
        services.AddScoped<SickLeaveMonitor>();
        services.AddScoped<SickLeaveNotificationDataProvider>();
        services.AddScoped<ISickLeaveDataProvider, SickLeaveNotificationDataProvider>();

        // Competence — gap-analys + utvecklingsplan (våg 1)
        services.AddSingleton<RegionHR.Competence.Services.CompetenceGapAnalyzer>();
        services.AddSingleton<RegionHR.Competence.Services.UtvecklingsplanGenerator>();

        // Document template engine & e-signing
        services.AddSingleton<DocumentTemplateEngine>();
        services.AddSingleton<ISigningService, SimpleConfirmationSigningService>();

        // Payroll batch-orkestrering (våg 2 slice: lonekorning)
        services.AddSingleton<RegionHR.Infrastructure.Payroll.PayrollInputBuilder>();
        services.AddScoped<RegionHR.Payroll.Domain.RetroactiveRecalculationEngine>();
        services.AddScoped<RegionHR.Infrastructure.Payroll.PayrollBatchService>();

        // Swedish payroll engine
        services.AddSingleton<SwedishTaxCalculator>();
        services.AddSingleton<KollektivavtalEngine>();
        services.AddSingleton<TraktamentsCalculator>();

        // Integration adapters
        services.AddSingleton<Integrations.AGIXmlGenerator>();
        services.AddSingleton<NordeaPainGenerator>();

        // Schema optimization
        services.AddSingleton<SchemaOptimizer>();

        // Analytics
        services.AddScoped<Analytics.FlightRiskService>();
        services.AddScoped<KPICalculationService>();
        services.AddScoped<PayEquityCalculationService>();
        services.AddScoped<ScenarioCalculationService>();

        // Provisioning (lokal registrering — inga externa anrop i v1)
        services.AddScoped<Provisioning.IIdentityProvider, Provisioning.LocalRecordingProvider>();
        services.AddScoped<Provisioning.ProvisioningService>();

        // Automation engine
        services.AddScoped<ConditionEvaluator>();
        services.AddScoped<AutomationActionExecutor>();
        services.AddScoped<IAutomationEngine, AutomationEngineService>();

        // Extension package service (marketplace)
        services.AddScoped<ExtensionPackageService>();

        // Migration engine
        services.AddScoped<IMigrationAdapter, PAXmlAdapter>();
        services.AddScoped<IMigrationAdapter, HeromaAdapter>();
        services.AddScoped<IMigrationAdapter, GenericCSVAdapter>();
        services.AddScoped<MigrationEngineService>();

        // Webhook delivery (HttpClient for outbound webhook calls)
        services.AddHttpClient<WebhookDeliveryService>();

        // Knowledge base
        services.AddScoped<KnowledgeBaseService>();

        // Background services
        services.AddHostedService<NotificationReminderService>();
        services.AddHostedService<RetentionCleanupService>();
        services.AddHostedService<ScheduledReportService>();
        services.AddHostedService<CertificationReminderService>();
        services.AddHostedService<LASAlertService>();
        services.AddHostedService<RehabAutoTriggerService>();

        // OpenTelemetry
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("RegionHR"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation());

        return services;
    }
}

internal class UnitOfWork : IUnitOfWork
{
    private readonly RegionHRDbContext _db;
    public UnitOfWork(RegionHRDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
