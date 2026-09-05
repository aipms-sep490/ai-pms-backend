using System;

namespace AIPMS.Application.Features.Tasks.Domain;

public static class TaskStateMachine
{
    public static bool CanTransition(string currentStatus, string newStatus)
    {
        if (currentStatus == newStatus) return true;

        return currentStatus switch
        {
            "TODO" => newStatus is "IN_PROGRESS" or "BLOCKED" or "CANCELLED",
            "IN_PROGRESS" => newStatus is "TODO" or "BLOCKED" or "IN_REVIEW" or "DONE" or "CANCELLED",
            "BLOCKED" => newStatus is "TODO" or "IN_PROGRESS" or "CANCELLED",
            "IN_REVIEW" => newStatus is "IN_PROGRESS" or "DONE" or "CANCELLED",
            "DONE" => newStatus is "IN_PROGRESS" or "CANCELLED",
            "CANCELLED" => newStatus is "TODO",
            _ => false
        };
    }
}
