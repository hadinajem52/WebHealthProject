namespace WebHealth.Application.Assignments;

public interface IAssignmentAccessEvaluator
{
    Task<bool> IsAssignedAsync(
        Guid userId,
        Guid ownerSubjectId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}
