namespace TicketCore.Common;

/// <summary>Levels of the organization tree, from the top down.</summary>
public enum OrgUnitType : byte
{
    Organization = 1,
    BusinessUnit = 2,
    Department = 3,
    Team = 4
}

public enum UserType : byte
{
    Customer = 1,
    Employee = 2,
    Staff = 3
}

/// <summary>Names of the built-in role templates seeded by the database scripts.</summary>
public static class SystemRoles
{
    public const string SystemAdmin = "System Admin";
}
