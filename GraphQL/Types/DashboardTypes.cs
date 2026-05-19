namespace AkademVault_API.GraphQL.Types;

// Schedule entry projection for the dashboard widget.
public record DashboardScheduleEntryGql(
    Guid Id,
    string Title,
    string Type,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Location,
    string? Teacher);

// Assignment projection for the dashboard widget.
public record DashboardAssignmentGql(
    Guid Id,
    string Title,
    string Description,
    DateTime DueDate);

// Recent-material projection for the dashboard widget.
public record DashboardMaterialGql(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string UploaderName,
    DateTime UploadedAt);

// Aggregated dashboard payload — replaces 3 separate REST calls with one GraphQL query.
// Includes total counts so the SPA can show "5 у розкладі" without a second round-trip.
public record DashboardGql(
    List<DashboardScheduleEntryGql> TodaySchedule,
    List<DashboardAssignmentGql> UpcomingAssignments,
    List<DashboardMaterialGql> RecentMaterials,
    int ScheduleTotalCount,
    int MaterialTotalCount);
