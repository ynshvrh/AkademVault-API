namespace AkademVault_API.GraphQL.Types;

// Compact group projection used by the GraphQL "groups" browse list.
public record GroupSummaryGql(
    Guid Id,
    string Name,
    string ShortCode,
    string OwnerName,
    int MemberCount,
    DateTime ExpiryDate);

// Member row used inside GroupDetailsGql.
public record GroupMemberGql(Guid Id, string Username, bool IsOwner);

// Full group view returned by the GraphQL "myGroup" query.
public record GroupDetailsGql(
    Guid Id,
    string Name,
    string ShortCode,
    Guid OwnerId,
    string OwnerName,
    DateTime CreatedAt,
    DateTime ExpiryDate,
    List<GroupMemberGql> Members);
