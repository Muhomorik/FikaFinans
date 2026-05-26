namespace FikaFinans.Application.Storage.Bank;

/// <summary>
/// Lifecycle states of an <see cref="Entities.IsinProgressEntity"/> row.
/// <c>Free</c> is the resting state; <c>Processing</c> is the in-flight
/// lock claimed by a worker (or the local Rx pipeline). The state machine
/// is documented in
/// <see href="../../../../Docs/backend-nav-sync-plan.md">backend-nav-sync-plan.md</see>
/// §"Progress Table".
/// </summary>
public enum IsinProgressState
{
    Free,
    Processing
}
