using System;

namespace Plugins.Companion
{
    /// <summary>
    /// Result of a control command. Maps to HTTP status codes in the server.
    /// </summary>
    public class CommandResult
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public string message { get; set; }
        public int httpStatus { get; set; }

        public static CommandResult Ok(string message = null)
        {
            return new CommandResult { ok = true, message = message ?? "", httpStatus = 200 };
        }

        public static CommandResult Fail(string error, string message, int httpStatus)
        {
            return new CommandResult { ok = false, error = error, message = message, httpStatus = httpStatus };
        }

        public static CommandResult BadRequest(string message)
        {
            return Fail("bad_request", message, 400);
        }

        public static CommandResult Unavailable(string message)
        {
            return Fail("unavailable", message, 503);
        }

        public static CommandResult Conflict(string message)
        {
            return Fail("conflict", message, 409);
        }
    }

    /// <summary>
    /// Everything the companion server needs from the host. Two implementations:
    /// PluginMaestroController (real, over Plugininterface.Entry + WorkflowEngine) and
    /// SimulatedMaestroController (self-contained, for local testing without UCCNC).
    /// Implementations must be thread-safe for concurrent HTTP handlers.
    /// </summary>
    public interface IMaestroController
    {
        string MachineId { get; }
        string MachineName { get; }
        string CameraUrl { get; }

        StatusSnapshot GetSnapshot();

        ProjectsDocument GetProjects();
        ToolLibraryDocument GetTools();

        // Jog
        CommandResult Jog(string axis, int dir, string mode, double step, double feed);
        CommandResult JogStop();

        // Manual spindle control (jog screen): on/off + target RPM.
        CommandResult Spindle(bool on, double rpm);

        // Machine
        CommandResult Zero(string axis);
        CommandResult Home(string axis);
        CommandResult GotoZero();
        CommandResult Park(string type);
        CommandResult AutoZero();
        CommandResult FeedHold();
        CommandResult Resume();
        CommandResult Stop();
        CommandResult EStop();

        // Maestro workflow
        CommandResult SelectProject(string projectId);
        CommandResult RunAll(int fromIndex);
        CommandResult RunStep(int index);
        CommandResult ResetProject();
        CommandResult Abort();
        CommandResult ConfirmPrompt();
        CommandResult CancelPrompt();

        /// <summary>Raised when status changes so the server can push an SSE update promptly.</summary>
        event Action SnapshotChanged;
    }
}
