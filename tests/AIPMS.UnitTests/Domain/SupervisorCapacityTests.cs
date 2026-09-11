using AIPMS.Domain.Supervisors;

namespace AIPMS.UnitTests.Domain;

public sealed class SupervisorCapacityTests
{
    [Theory]
    [InlineData(null, 5, 20, 1, 4)]
    [InlineData(3, 5, 2, 1, 1)]
    [InlineData(10, 2, 3, 1, 1)]
    [InlineData(3, 5, 3, 0, 0)]
    [InlineData(10, 2, 3, 2, 0)]
    [InlineData(0, 5, 0, 0, 0)]
    [InlineData(null, 0, 0, 0, 0)]
    [InlineData(2, 3, 5, 4, 0)]
    public void Both_global_and_semester_limits_must_have_room(
        int? profileLimit, int semesterLimit, int active, int semesterActive, int remaining)
    {
        Assert.Equal(remaining,
            new SupervisorCapacity(profileLimit, semesterLimit, active, semesterActive).RemainingSlots);
    }
}
