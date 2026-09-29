using EduConnect.Web;
using EduConnect.Web.Services;

namespace EduConnect.Tests
{
    public class EventAccessTests
    {
        [Fact] public void Dean_SameCollege_True() => Assert.True(EventAccess.ManagesEventsOf(RoleNames.Dean, 1, null, 1, 5));
        [Fact] public void Dean_OtherCollege_False() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Dean, 1, null, 2, 7));
        [Fact] public void Chair_SameDepartment_True() => Assert.True(EventAccess.ManagesEventsOf(RoleNames.Chairperson, 1, 5, 1, 5));
        [Fact] public void Chair_OtherDepartmentSameCollege_False() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Chairperson, 1, 5, 1, 6));
        [Fact] public void Faculty_Never() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Faculty, 1, 5, 1, 5));
        [Fact] public void UnplacedDean_Never() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Dean, null, null, null, null));
    }
}
