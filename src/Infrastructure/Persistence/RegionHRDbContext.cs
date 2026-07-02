using Microsoft.EntityFrameworkCore;
using RegionHR.SharedKernel.Domain;
using RegionHR.Core.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Scheduling.Domain;
using RegionHR.CaseManagement.Domain;
using RegionHR.LAS.Domain;
using RegionHR.HalsoSAM.Domain;
using RegionHR.SalaryReview.Domain;
using RegionHR.Travel.Domain;
using RegionHR.Recruitment.Domain;
using RegionHR.IntegrationHub.Infrastructure;
using RegionHR.Audit.Domain;
using RegionHR.Notifications.Domain;
using RegionHR.Leave.Domain;
using RegionHR.Documents.Domain;
using RegionHR.Performance.Domain;
using RegionHR.Reporting.Domain;
using RegionHR.GDPR.Domain;
using RegionHR.Competence.Domain;
using RegionHR.Benefits.Domain;
using RegionHR.LMS.Domain;
using RegionHR.Positions.Domain;
using RegionHR.Offboarding.Domain;
using RegionHR.Analytics.Domain;
using RegionHR.Configuration.Domain;
using RegionHR.Infrastructure.Authorization;
using RegionHR.Infrastructure.Provisioning;
using RegionHR.Infrastructure.Arbetsmiljo;
using RegionHR.Infrastructure.Journeys;
using RegionHR.Automation.Domain;
using RegionHR.Agreements.Domain;
using RegionHR.Migration.Domain;
using RegionHR.Compensation.Domain;
using RegionHR.VMS.Domain;
using RegionHR.Platform.Domain;
using RegionHR.Knowledge.Domain;
using RegionHR.Helpdesk.Domain;

namespace RegionHR.Infrastructure.Persistence;

public class RegionHRDbContext : DbContext
{
    public RegionHRDbContext(DbContextOptions<RegionHRDbContext> options) : base(options) { }

    // Core HR (schema: core_hr)
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();

    // Payroll (schema: payroll)
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollResult> PayrollResults => Set<PayrollResult>();
    public DbSet<TaxTable> TaxTables => Set<TaxTable>();
    public DbSet<SalaryCode> SalaryCodes => Set<SalaryCode>();

    // Scheduling (schema: scheduling)
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<ScheduledShift> ScheduledShifts => Set<ScheduledShift>();
    public DbSet<TimeClockEvent> TimeClockEvents => Set<TimeClockEvent>();
    public DbSet<StaffingTemplate> StaffingTemplates => Set<StaffingTemplate>();
    public DbSet<ShiftSwapRequest> ShiftSwapRequests => Set<ShiftSwapRequest>();
    public DbSet<Timesheet> Timesheets => Set<Timesheet>();

    // Flex (schema: scheduling) — våg 2
    public DbSet<FlexInstallning> FlexInstallningar => Set<FlexInstallning>();
    public DbSet<FlexBalance> FlexBalances => Set<FlexBalance>();

    // Shift Bidding (schema: scheduling)
    public DbSet<OpenShift> OpenShifts => Set<OpenShift>();
    public DbSet<ShiftBid> ShiftBids => Set<ShiftBid>();
    public DbSet<ShiftBidResult> ShiftBidResults => Set<ShiftBidResult>();

    // WFM — Advanced Scheduling (schema: scheduling)
    public DbSet<DemandForecast> DemandForecasts => Set<DemandForecast>();
    public DbSet<DemandPattern> DemandPatterns => Set<DemandPattern>();
    public DbSet<DemandEvent> DemandEvents => Set<DemandEvent>();
    public DbSet<SchedulingConstraint> SchedulingConstraints => Set<SchedulingConstraint>();
    public DbSet<ShiftCoverageRequest> ShiftCoverageRequests => Set<ShiftCoverageRequest>();
    public DbSet<EmployeeAvailability> EmployeeAvailabilities => Set<EmployeeAvailability>();
    public DbSet<FatigueScore> FatigueScores => Set<FatigueScore>();
    public DbSet<SchedulingRun> SchedulingRuns => Set<SchedulingRun>();

    // Case Management (schema: case_mgmt)
    public DbSet<Case> Cases => Set<Case>();

    // LAS (schema: las)
    public DbSet<LASAccumulation> LASAccumulations => Set<LASAccumulation>();

    // HälsoSAM (schema: halsosam)
    public DbSet<RehabCase> RehabCases => Set<RehabCase>();

    // Salary Review (schema: salary_review)
    public DbSet<SalaryReviewRound> SalaryReviewRounds => Set<SalaryReviewRound>();

    // Travel (schema: travel)
    public DbSet<TravelClaim> TravelClaims => Set<TravelClaim>();

    // Recruitment (schema: recruitment)
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();
    public DbSet<CommunicationTemplate> CommunicationTemplates => Set<CommunicationTemplate>();
    public DbSet<OnboardingChecklist> OnboardingChecklists => Set<OnboardingChecklist>();
    public DbSet<RequisitionApproval> RequisitionApprovals => Set<RequisitionApproval>();
    public DbSet<InterviewSchedule> InterviewSchedules => Set<InterviewSchedule>();
    public DbSet<Scorecard> Scorecards => Set<Scorecard>();
    public DbSet<TalentPoolEntry> TalentPoolEntries => Set<TalentPoolEntry>();

    // Integration Hub (schema: integration_hub)
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // Audit (schema: audit)
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    // Notifications (schema: notifications)
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    // Leave (schema: leave)
    public DbSet<VacationBalance> VacationBalances => Set<VacationBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<SickLeaveNotification> SickLeaveNotifications => Set<SickLeaveNotification>();

    // Documents (schema: documents)
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<DocumentSignature> DocumentSignatures => Set<DocumentSignature>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    // Performance (schema: performance)
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<OneOnOneMeeting> OneOnOneMeetings => Set<OneOnOneMeeting>();
    public DbSet<MeetingActionItem> MeetingActionItems => Set<MeetingActionItem>();
    public DbSet<ManagerScorecard> ManagerScorecards => Set<ManagerScorecard>();
    public DbSet<CoachingNudge> CoachingNudges => Set<CoachingNudge>();

    // Reporting (schema: reporting)
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<ReportExecution> ReportExecutions => Set<ReportExecution>();
    public DbSet<ScheduledReport> ScheduledReports => Set<ScheduledReport>();

    // GDPR (schema: gdpr)
    public DbSet<DataSubjectRequest> DataSubjectRequests => Set<DataSubjectRequest>();
    public DbSet<RetentionRecord> RetentionRecords => Set<RetentionRecord>();

    // Competence (schema: competence)
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<MandatoryTraining> MandatoryTrainings => Set<MandatoryTraining>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<EmployeeSkill> EmployeeSkills => Set<EmployeeSkill>();
    public DbSet<PositionSkillRequirement> PositionSkillRequirements => Set<PositionSkillRequirement>();

    // Talent Marketplace (schema: competence)
    public DbSet<SkillCategoryEntity> SkillCategories => Set<SkillCategoryEntity>();
    public DbSet<SkillRelation> SkillRelations => Set<SkillRelation>();
    public DbSet<InferredSkill> InferredSkills => Set<InferredSkill>();
    public DbSet<CareerPath> CareerPaths => Set<CareerPath>();
    public DbSet<CareerPathStep> CareerPathSteps => Set<CareerPathStep>();
    public DbSet<DevelopmentPlan> DevelopmentPlans => Set<DevelopmentPlan>();
    public DbSet<DevelopmentMilestone> DevelopmentMilestones => Set<DevelopmentMilestone>();
    public DbSet<InternalOpportunity> InternalOpportunities => Set<InternalOpportunity>();
    public DbSet<OpportunityApplication> OpportunityApplications => Set<OpportunityApplication>();
    public DbSet<MentorRelation> MentorRelations => Set<MentorRelation>();
    public DbSet<SkillEndorsement> SkillEndorsements => Set<SkillEndorsement>();

    // Benefits (schema: benefits)
    public DbSet<Benefit> Benefits => Set<Benefit>();
    public DbSet<EmployeeBenefit> EmployeeBenefits => Set<EmployeeBenefit>();
    public DbSet<EligibilityRule> EligibilityRules => Set<EligibilityRule>();
    public DbSet<EligibilityCondition> EligibilityConditions => Set<EligibilityCondition>();
    public DbSet<LifeEvent> LifeEvents => Set<LifeEvent>();
    public DbSet<LifeEventOccurrence> LifeEventOccurrences => Set<LifeEventOccurrence>();
    public DbSet<EnrollmentPeriod> EnrollmentPeriods => Set<EnrollmentPeriod>();
    public DbSet<BenefitEnrollment> BenefitEnrollments => Set<BenefitEnrollment>();
    public DbSet<BenefitStatement> BenefitStatements => Set<BenefitStatement>();
    public DbSet<BenefitTransaction> BenefitTransactions => Set<BenefitTransaction>();

    // LMS (schema: lms)
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseEnrollment> CourseEnrollments => Set<CourseEnrollment>();
    public DbSet<LearningPath> LearningPaths => Set<LearningPath>();
    public DbSet<LearningPathStep> LearningPathSteps => Set<LearningPathStep>();

    // Positions (schema: positions)
    public DbSet<Position> Positions_Table => Set<Position>();
    public DbSet<HeadcountPlan> HeadcountPlans => Set<HeadcountPlan>();

    // Pulse (schema: pulse)
    public DbSet<RegionHR.Pulse.Domain.PulseSurvey> PulseSurveys => Set<RegionHR.Pulse.Domain.PulseSurvey>();
    public DbSet<RegionHR.Pulse.Domain.PulseSurveyResponse> PulseSurveyResponses => Set<RegionHR.Pulse.Domain.PulseSurveyResponse>();

    // Communication (schema: communication)
    public DbSet<RegionHR.Communication.Domain.Announcement> Announcements => Set<RegionHR.Communication.Domain.Announcement>();
    public DbSet<RegionHR.Communication.Domain.Recognition> Recognitions => Set<RegionHR.Communication.Domain.Recognition>();

    // ReferenceCheck (schema: recruitment)
    public DbSet<RegionHR.Recruitment.Domain.ReferenceCheck> ReferenceChecks => Set<RegionHR.Recruitment.Domain.ReferenceCheck>();

    // MBL (schema: case_mgmt)
    public DbSet<RegionHR.CaseManagement.Domain.MBLNegotiation> MBLNegotiations => Set<RegionHR.CaseManagement.Domain.MBLNegotiation>();

    // Grievance (schema: case_mgmt)
    public DbSet<RegionHR.CaseManagement.Domain.Grievance> Grievances => Set<RegionHR.CaseManagement.Domain.Grievance>();
    public DbSet<RegionHR.CaseManagement.Domain.GrievanceInvestigation> GrievanceInvestigations => Set<RegionHR.CaseManagement.Domain.GrievanceInvestigation>();
    public DbSet<RegionHR.CaseManagement.Domain.GrievanceHearing> GrievanceHearings => Set<RegionHR.CaseManagement.Domain.GrievanceHearing>();
    public DbSet<RegionHR.CaseManagement.Domain.GrievanceAppeal> GrievanceAppeals => Set<RegionHR.CaseManagement.Domain.GrievanceAppeal>();

    // Feedback (schema: performance)
    public DbSet<RegionHR.Performance.Domain.FeedbackRound> FeedbackRounds => Set<RegionHR.Performance.Domain.FeedbackRound>();
    public DbSet<RegionHR.Performance.Domain.FeedbackResponse> FeedbackResponses => Set<RegionHR.Performance.Domain.FeedbackResponse>();

    // Succession (schema: positions)
    public DbSet<RegionHR.Positions.Domain.SuccessionPlan> SuccessionPlans => Set<RegionHR.Positions.Domain.SuccessionPlan>();

    // Insurance (schema: insurance)
    public DbSet<RegionHR.Insurance.Domain.InsuranceCoverage> InsuranceCoverages => Set<RegionHR.Insurance.Domain.InsuranceCoverage>();

    // Wellness (schema: wellness)
    public DbSet<RegionHR.Wellness.Domain.WellnessClaim> WellnessClaims => Set<RegionHR.Wellness.Domain.WellnessClaim>();

    // PolicyManagement (schema: policy)
    public DbSet<RegionHR.PolicyManagement.Domain.Policy> Policies => Set<RegionHR.PolicyManagement.Domain.Policy>();
    public DbSet<RegionHR.PolicyManagement.Domain.PolicyConfirmation> PolicyConfirmations => Set<RegionHR.PolicyManagement.Domain.PolicyConfirmation>();

    // Offboarding (schema: offboarding)
    public DbSet<OffboardingCase> OffboardingCases => Set<OffboardingCase>();

    // Analytics (schema: analytics)
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<KPIDefinition> KPIDefinitions => Set<KPIDefinition>();
    public DbSet<KPISnapshot> KPISnapshots => Set<KPISnapshot>();
    public DbSet<KPIAlert> KPIAlerts => Set<KPIAlert>();
    public DbSet<PredictionModel> PredictionModels => Set<PredictionModel>();
    public DbSet<PredictionResult> PredictionResults => Set<PredictionResult>();
    public DbSet<PayTransparencyReport> PayTransparencyReports => Set<PayTransparencyReport>();
    public DbSet<PayGapAnalysis> PayGapAnalyses => Set<PayGapAnalysis>();
    public DbSet<PayGapCohort> PayGapCohorts => Set<PayGapCohort>();
    public DbSet<PlanningScenario> PlanningScenarios => Set<PlanningScenario>();
    public DbSet<ScenarioAssumption> ScenarioAssumptions => Set<ScenarioAssumption>();
    public DbSet<ScenarioResult> ScenarioResults => Set<ScenarioResult>();

    // ONA (schema: analytics)
    public DbSet<ONASurvey> ONASurveys => Set<ONASurvey>();
    public DbSet<ONAResponse> ONAResponses => Set<ONAResponse>();
    public DbSet<NetworkNode> NetworkNodes => Set<NetworkNode>();
    public DbSet<NetworkEdge> NetworkEdges => Set<NetworkEdge>();

    // Configuration (schema: configuration)
    public DbSet<TenantConfiguration> TenantConfigurations => Set<TenantConfiguration>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    // Authorization (schema: authorization)
    public DbSet<FieldPermission> FieldPermissions => Set<FieldPermission>();
    public DbSet<DelegatedAccess> DelegatedAccesses => Set<DelegatedAccess>();

    // Arbetsmiljo (schema: arbetsmiljo)
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<SafetyRound> SafetyRounds => Set<SafetyRound>();
    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();

    // Journeys (schema: journeys) — only 2 DbSet, step entities are owned
    public DbSet<JourneyTemplate> JourneyTemplates => Set<JourneyTemplate>();
    public DbSet<JourneyInstance> JourneyInstances => Set<JourneyInstance>();

    // Provisioning (schema: provisioning)
    public DbSet<ProvisioningEvent> ProvisioningEvents => Set<ProvisioningEvent>();
    public DbSet<ProvisioningRule> ProvisioningRules => Set<ProvisioningRule>();

    // Automation (schema: automation)
    public DbSet<RegionHR.Automation.Domain.AutomationRule> AutomationRules => Set<RegionHR.Automation.Domain.AutomationRule>();
    public DbSet<RegionHR.Automation.Domain.AutomationCategory> AutomationCategories => Set<RegionHR.Automation.Domain.AutomationCategory>();
    public DbSet<RegionHR.Automation.Domain.AutomationLevelConfig> AutomationLevelConfigs => Set<RegionHR.Automation.Domain.AutomationLevelConfig>();
    public DbSet<RegionHR.Automation.Domain.AutomationExecution> AutomationExecutions => Set<RegionHR.Automation.Domain.AutomationExecution>();
    public DbSet<RegionHR.Automation.Domain.AutomationSuggestion> AutomationSuggestions => Set<RegionHR.Automation.Domain.AutomationSuggestion>();

    // Migration (schema: migration)
    public DbSet<MigrationJob> MigrationJobs => Set<MigrationJob>();
    public DbSet<MigrationMapping> MigrationMappings => Set<MigrationMapping>();
    public DbSet<MigrationTemplate> MigrationTemplates => Set<MigrationTemplate>();
    public DbSet<MigrationValidationError> MigrationValidationErrors => Set<MigrationValidationError>();
    public DbSet<MigrationLog> MigrationLogs => Set<MigrationLog>();

    // Compensation (schema: compensation)
    public DbSet<CompensationPlan> CompensationPlans => Set<CompensationPlan>();
    public DbSet<CompensationBand> CompensationBands => Set<CompensationBand>();
    public DbSet<CompensationBudget> CompensationBudgets => Set<CompensationBudget>();
    public DbSet<CompensationGuideline> CompensationGuidelines => Set<CompensationGuideline>();
    public DbSet<BonusPlan> BonusPlans => Set<BonusPlan>();
    public DbSet<BonusTarget> BonusTargets => Set<BonusTarget>();
    public DbSet<BonusOutcome> BonusOutcomes => Set<BonusOutcome>();
    public DbSet<VariablePayComponent> VariablePayComponents => Set<VariablePayComponent>();
    public DbSet<TotalRewardsStatement> TotalRewardsStatements => Set<TotalRewardsStatement>();
    public DbSet<CompensationSimulation> CompensationSimulations => Set<CompensationSimulation>();

    // Agreements (schema: agreements)
    public DbSet<CollectiveAgreement> CollectiveAgreements => Set<CollectiveAgreement>();
    public DbSet<AgreementOBRate> AgreementOBRates => Set<AgreementOBRate>();
    public DbSet<AgreementOvertimeRule> AgreementOvertimeRules => Set<AgreementOvertimeRule>();
    public DbSet<AgreementVacationRule> AgreementVacationRules => Set<AgreementVacationRule>();
    public DbSet<AgreementRestRule> AgreementRestRules => Set<AgreementRestRule>();
    public DbSet<AgreementSalaryStructure> AgreementSalaryStructures => Set<AgreementSalaryStructure>();
    public DbSet<AgreementWorkingHours> AgreementWorkingHours => Set<AgreementWorkingHours>();
    public DbSet<AgreementNoticePeriod> AgreementNoticePeriods => Set<AgreementNoticePeriod>();
    public DbSet<AgreementPensionRule> AgreementPensionRules => Set<AgreementPensionRule>();
    public DbSet<AgreementInsurancePackage> AgreementInsurancePackages => Set<AgreementInsurancePackage>();
    public DbSet<PrivateCompensationPlan> PrivateCompensationPlans => Set<PrivateCompensationPlan>();

    // Platform (schema: platform)
    public DbSet<CustomObject> CustomObjects => Set<CustomObject>();
    public DbSet<CustomObjectRecord> CustomObjectRecords => Set<CustomObjectRecord>();
    public DbSet<CustomObjectRelation> CustomObjectRelations => Set<CustomObjectRelation>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowRunInstance> WorkflowRunInstances => Set<WorkflowRunInstance>();

    // VMS (schema: vms)
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<FrameworkAgreement> FrameworkAgreements => Set<FrameworkAgreement>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<StaffingRequest> StaffingRequests => Set<StaffingRequest>();
    public DbSet<ContingentWorker> ContingentWorkers => Set<ContingentWorker>();
    public DbSet<ContingentTimeReport> ContingentTimeReports => Set<ContingentTimeReport>();
    public DbSet<VendorInvoice> VendorInvoices => Set<VendorInvoice>();
    public DbSet<VendorPerformance> VendorPerformances => Set<VendorPerformance>();
    public DbSet<SpendCategory> SpendCategories => Set<SpendCategory>();
    public DbSet<FSkattRegistration> FSkattRegistrations => Set<FSkattRegistration>();
    public DbSet<ContractorClassification> ContractorClassifications => Set<ContractorClassification>();

    // Platform — Event Bus & API Keys (schema: platform)
    public DbSet<DomainEventRecord> DomainEventRecords => Set<DomainEventRecord>();
    public DbSet<EventSubscription> EventSubscriptions => Set<EventSubscription>();
    public DbSet<EventDelivery> EventDeliveries => Set<EventDelivery>();
    public DbSet<RegionHR.Platform.Domain.ApiKey> ApiKeys => Set<RegionHR.Platform.Domain.ApiKey>();

    // Platform — Marketplace (schema: platform)
    public DbSet<RegionHR.Platform.Domain.Extension> Extensions => Set<RegionHR.Platform.Domain.Extension>();
    public DbSet<RegionHR.Platform.Domain.ExtensionInstallation> ExtensionInstallations => Set<RegionHR.Platform.Domain.ExtensionInstallation>();

    // Helpdesk (schema: helpdesk)
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<SLADefinition> SLADefinitions => Set<SLADefinition>();
    public DbSet<SLAMilestone> SLAMilestones => Set<SLAMilestone>();
    public DbSet<HRQueue> HRQueues => Set<HRQueue>();
    public DbSet<CaseTemplate> CaseTemplates_Helpdesk => Set<CaseTemplate>();
    public DbSet<CaseSatisfaction> CaseSatisfactions => Set<CaseSatisfaction>();
    public DbSet<ServiceRequestComment> ServiceRequestComments => Set<ServiceRequestComment>();

    // Knowledge (schema: knowledge)
    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<ConversationSession> ConversationSessions => Set<ConversationSession>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<AssistantAction> AssistantActions => Set<AssistantAction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Register value converters BEFORE model validation — this is the only way
        // to make EF Core accept strongly-typed IDs without [NotMapped]
        // ALL strongly-typed IDs and value objects need converters
        configurationBuilder.Properties<EmployeeId>().HaveConversion<EmployeeIdConverter>();
        configurationBuilder.Properties<OrganizationId>().HaveConversion<OrganizationIdConverter>();
        configurationBuilder.Properties<EmploymentId>().HaveConversion<EmploymentIdConverter>();
        configurationBuilder.Properties<CaseId>().HaveConversion<CaseIdConverter>();
        configurationBuilder.Properties<PayrollRunId>().HaveConversion<PayrollRunIdConverter>();
        configurationBuilder.Properties<ScheduleId>().HaveConversion<ScheduleIdConverter>();
        configurationBuilder.Properties<StaffingTemplateId>().HaveConversion<StaffingTemplateIdConverter>();
        configurationBuilder.Properties<ShiftSwapId>().HaveConversion<ShiftSwapIdConverter>();
        configurationBuilder.Properties<AutomationRuleId>().HaveConversion<AutomationRuleIdConverter>();
        configurationBuilder.Properties<AutomationCategoryId>().HaveConversion<AutomationCategoryIdConverter>();
        configurationBuilder.Properties<CollectiveAgreementId>().HaveConversion<CollectiveAgreementIdConverter>();
        configurationBuilder.Properties<MigrationJobId>().HaveConversion<MigrationJobIdConverter>();
        configurationBuilder.Properties<CompensationPlanId>().HaveConversion<CompensationPlanIdConverter>();
        configurationBuilder.Properties<BonusPlanId>().HaveConversion<BonusPlanIdConverter>();
        configurationBuilder.Properties<VendorId>().HaveConversion<VendorIdConverter>();
        configurationBuilder.Properties<StaffingRequestId>().HaveConversion<StaffingRequestIdConverter>();
        configurationBuilder.Properties<FrameworkAgreementId>().HaveConversion<FrameworkAgreementIdConverter>();
        configurationBuilder.Properties<CareerPathId>().HaveConversion<CareerPathIdConverter>();
        configurationBuilder.Properties<DevelopmentPlanId>().HaveConversion<DevelopmentPlanIdConverter>();
        configurationBuilder.Properties<InternalOpportunityId>().HaveConversion<InternalOpportunityIdConverter>();
        configurationBuilder.Properties<DemandForecastId>().HaveConversion<DemandForecastIdConverter>();
        configurationBuilder.Properties<SchedulingRunId>().HaveConversion<SchedulingRunIdConverter>();
        configurationBuilder.Properties<GrievanceId>().HaveConversion<GrievanceIdConverter>();
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
        configurationBuilder.Properties<Percentage>().HaveConversion<PercentageConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegionHRDbContext).Assembly);
    }
}

// EF Core value converters for strongly-typed IDs
public class EmployeeIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<EmployeeId, Guid>
{
    public EmployeeIdConverter() : base(v => v.Value, v => EmployeeId.From(v)) { }
}

public class OrganizationIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<OrganizationId, Guid>
{
    public OrganizationIdConverter() : base(v => v.Value, v => new OrganizationId(v)) { }
}

public class EmploymentIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<EmploymentId, Guid>
{
    public EmploymentIdConverter() : base(v => v.Value, v => new EmploymentId(v)) { }
}

public class CaseIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<CaseId, Guid>
{
    public CaseIdConverter() : base(v => v.Value, v => new CaseId(v)) { }
}

public class PayrollRunIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<PayrollRunId, Guid>
{
    public PayrollRunIdConverter() : base(v => v.Value, v => new PayrollRunId(v)) { }
}

public class ScheduleIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<ScheduleId, Guid>
{
    public ScheduleIdConverter() : base(v => v.Value, v => new ScheduleId(v)) { }
}

public class StaffingTemplateIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<StaffingTemplateId, Guid>
{
    public StaffingTemplateIdConverter() : base(v => v.Value, v => new StaffingTemplateId(v)) { }
}

public class ShiftSwapIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<ShiftSwapId, Guid>
{
    public ShiftSwapIdConverter() : base(v => v.Value, v => new ShiftSwapId(v)) { }
}

public class MoneyConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Money, decimal>
{
    public MoneyConverter() : base(v => v.Amount, v => new Money(v, "SEK")) { }
}

public class PercentageConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Percentage, decimal>
{
    public PercentageConverter() : base(v => v.Value, v => new Percentage(v)) { }
}

public class AutomationRuleIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<AutomationRuleId, Guid>
{
    public AutomationRuleIdConverter() : base(v => v.Value, v => AutomationRuleId.From(v)) { }
}

public class AutomationCategoryIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<AutomationCategoryId, Guid>
{
    public AutomationCategoryIdConverter() : base(v => v.Value, v => AutomationCategoryId.From(v)) { }
}

public class CollectiveAgreementIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<CollectiveAgreementId, Guid>
{
    public CollectiveAgreementIdConverter() : base(v => v.Value, v => CollectiveAgreementId.From(v)) { }
}

public class MigrationJobIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<MigrationJobId, Guid>
{
    public MigrationJobIdConverter() : base(v => v.Value, v => MigrationJobId.From(v)) { }
}

public class CompensationPlanIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<CompensationPlanId, Guid>
{
    public CompensationPlanIdConverter() : base(v => v.Value, v => CompensationPlanId.From(v)) { }
}

public class BonusPlanIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<BonusPlanId, Guid>
{
    public BonusPlanIdConverter() : base(v => v.Value, v => BonusPlanId.From(v)) { }
}

public class VendorIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<VendorId, Guid>
{
    public VendorIdConverter() : base(v => v.Value, v => VendorId.From(v)) { }
}

public class StaffingRequestIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<StaffingRequestId, Guid>
{
    public StaffingRequestIdConverter() : base(v => v.Value, v => StaffingRequestId.From(v)) { }
}

public class FrameworkAgreementIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<FrameworkAgreementId, Guid>
{
    public FrameworkAgreementIdConverter() : base(v => v.Value, v => FrameworkAgreementId.From(v)) { }
}

public class CareerPathIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<CareerPathId, Guid>
{
    public CareerPathIdConverter() : base(v => v.Value, v => CareerPathId.From(v)) { }
}

public class DevelopmentPlanIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DevelopmentPlanId, Guid>
{
    public DevelopmentPlanIdConverter() : base(v => v.Value, v => DevelopmentPlanId.From(v)) { }
}

public class InternalOpportunityIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<InternalOpportunityId, Guid>
{
    public InternalOpportunityIdConverter() : base(v => v.Value, v => InternalOpportunityId.From(v)) { }
}

public class DemandForecastIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DemandForecastId, Guid>
{
    public DemandForecastIdConverter() : base(v => v.Value, v => DemandForecastId.From(v)) { }
}

public class SchedulingRunIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<SchedulingRunId, Guid>
{
    public SchedulingRunIdConverter() : base(v => v.Value, v => SchedulingRunId.From(v)) { }
}

public class GrievanceIdConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<GrievanceId, Guid>
{
    public GrievanceIdConverter() : base(v => v.Value, v => GrievanceId.From(v)) { }
}
