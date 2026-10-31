using TicketCore.Common;
using TicketCore.Core.Organization;

namespace TicketCore.Core.Tests;

public class OrgPlacementTests
{
    [Theory]
    [InlineData(OrgUnitType.Organization, OrgUnitType.BusinessUnit, true)]
    [InlineData(OrgUnitType.Organization, OrgUnitType.Department, true)]
    [InlineData(OrgUnitType.Organization, OrgUnitType.Team, true)]
    [InlineData(OrgUnitType.BusinessUnit, OrgUnitType.Department, true)]
    [InlineData(OrgUnitType.Department, OrgUnitType.Team, true)]
    [InlineData(OrgUnitType.Department, OrgUnitType.Department, false)]
    [InlineData(OrgUnitType.Department, OrgUnitType.BusinessUnit, false)]
    [InlineData(OrgUnitType.Team, OrgUnitType.Team, false)]
    [InlineData(OrgUnitType.BusinessUnit, OrgUnitType.Organization, false)]
    public void Child_must_be_lower_than_parent(OrgUnitType parent, OrgUnitType child, bool allowed)
    {
        Assert.Equal(allowed, OrgUnitService.CheckPlacement(parent, child) is null);
    }
}
