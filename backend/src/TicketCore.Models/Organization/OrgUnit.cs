using TicketCore.Common;

namespace TicketCore.Models.Organization;

public class OrgUnit
{
    public int OrgUnitId { get; set; }
    public int? ParentId { get; set; }
    public OrgUnitType UnitType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
