using Manager.DTO;
using Manager.Models;
using Shared.DTO;

namespace Manager.Services;


/// <summary>
/// Core service for managing password cracking tasks distribution and monitoring.
/// </summary>
/// <remarks>
/// This service orchestrates the entire cracking process:
/// <list type="bullet">
/// <item><description>Splits a cracking task into subtasks for distributed workers</description></item>
/// <item><description>Tracks progress via heartbeat monitoring</description></item>
/// <item><description>Handles failures with retry logic and task recovery</description></item>
/// </list>
/// </remarks>
public interface IManagerService
{
    /// <summary>
    /// Creates a new password cracking task.
    /// </summary>
    /// <param name="request">The crack request containing hash and maximum password length.</param>
    /// <returns>A unique identifier for the created task.</returns>
    /// <remarks>
    /// The task is initially created with PENDING status. The method calculates total
    /// combination space, splits it into subtasks, and dispatches them to workers via RabbitMQ.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when request parameters are invalid.</exception>
    Task<Guid> CreateCrackTask(ManagerCrackRequest request);


    /// <summary>
    /// Retrieves the current status of a cracking task.
    /// </summary>
    /// <param name="requestId">The unique identifier of the task.</param>
    /// <returns>Status response containing progress percentage and found passwords if completed.</returns>
    /// <exception cref="ArgumentException">Thrown when requestId is empty.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no task with the specified ID exists.</exception>
    Task<ManagerStatusResponse> GetStatus(Guid requestId);


    /// <summary>
    /// Processes progress updates from workers for active subtasks.
    /// </summary>
    /// <param name="progress">The progress message containing current index and found words.</param>
    /// <remarks>
    /// Updates the subtask's current position and marks the main task as completed
    /// when all subtasks are finished.
    /// </remarks>
    Task ProcessProgress(RabbitProgressMessage progress);


    /// <summary>
    /// Detects and recovers subtasks that have exceeded their allowed execution time.
    /// </summary>
    /// <param name="timeout">The maximum allowed time without heartbeat before considering a subtask as timed out.</param>
    /// <remarks>
    /// <para>
    /// A <b>timed out subtask</b> is a subtask that:
    /// <list type="bullet">
    /// <item><description>Has status IN_PROGRESS (should be executing)</description></item>
    /// <item><description>Has stopped sending heartbeat signals</description></item>
    /// <item><description>Exceeded the specified timeout threshold</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Possible causes for timeout:
    /// <list type="bullet">
    /// <item><description>Worker process crashed</description></item>
    /// <item><description>Worker deadlocked or hung</description></item>
    /// <item><description>Network failure between Manager and Worker</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Recovery strategy:
    /// <list type="number">
    /// <item><description>If retry count &lt; 3: Resends the subtask to a worker (resumes from last known position)</description></item>
    /// <item><description>If retry count &gt;= 3: Marks entire task as ERROR (max retries exceeded)</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    Task CheckTimedOutSubtasks(TimeSpan timeout);



    /// <summary>
    /// Restores pending tasks after service restart or crash recovery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <b>pending task</b> is a task that:
    /// <list type="bullet">
    /// <item><description>Has been created by the Manager</description></item>
    /// <item><description>Has NOT yet started execution by workers</description></item>
    /// <item><description>Has database status: PENDING</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Recovery logic:
    /// <list type="bullet">
    /// <item><description>Tasks older than 1 hour are marked as ERROR (stale)</description></item>
    /// <item><description>For active pending tasks: resends all non-completed subtasks to workers</description></item>
    /// <item><description>Updates retry count and resets worker ID for resubmitted subtasks</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This method should be called during service startup to recover from previous crashes
    /// or incomplete shutdowns.
    /// </para>
    /// </remarks>
    Task RestorePendingTasks();


    /// <summary>
    /// Retries publishing subtasks that failed to be sent to RabbitMQ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method handles scenarios where RabbitMQ was unavailable during initial task dispatch.
    /// </para>
    /// <para>
    /// <b>When is this needed:</b>
    /// <list type="bullet">
    /// <item><description>RabbitMQ service was down during task creation</description></item>
    /// <item><description>Network issues prevented message publishing</description></item>
    /// <item><description>Manager saved tasks to database but couldn't queue them</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Selection criteria:</b>
    /// <list type="bullet">
    /// <item><description>Only subtasks with status PENDING and no assigned WorkerId</description></item>
    /// <item><description>Excludes subtasks that recently had a heartbeat (prevents duplicate dispatch)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// After RabbitMQ becomes available again, this method processes all pending publishes
    /// and sends the queued subtasks to workers.
    /// </para>
    /// </remarks>
    Task RetryPendingPublishes();


    /// <summary>
    /// Marks a cracking task as failed with an error message.
    /// </summary>
    /// <param name="requestId">The unique identifier of the task to mark as error.</param>
    /// <param name="errorMessage">The error description explaining why the task failed.</param>
    /// <remarks>
    /// This method updates the task status to ERROR and records the provided error message.
    /// Typically called when:
    /// <list type="bullet">
    /// <item><description>A subtask exceeded maximum retry attempts</description></item>
    /// <item><description>A task timed out in PENDING state for too long</description></item>
    /// <item><description>Unrecoverable error occurred during task execution</description></item>
    /// </list>
    /// </remarks>
    Task MarkTaskAsError(Guid requestId, string errorMessage);
}