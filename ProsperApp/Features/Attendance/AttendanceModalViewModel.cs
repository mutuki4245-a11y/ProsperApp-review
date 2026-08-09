namespace ProsperApp.Features.Attendance;

public sealed record AttendanceModalViewModel(
    AttendancePageState State,
    string CurrentUrl,
    string SaveUrl,
    bool IsClosingContext);
