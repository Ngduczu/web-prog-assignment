using LMS.Api.domain.Enums.Users;

namespace LMS.Api.domain.Entities.Users;

public class ExternalUser
{
    public Guid  Id { get; private set; }
    
    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public ExternalProvider Provider { get; private set; }
    public string ExternalUserId { get; private set; } = null!;
    
    private ExternalUser(){}

    public ExternalUser(
        Guid userId,
        ExternalProvider provider,
        string externalUserId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Provider = provider;
        ExternalUserId = externalUserId;
    }
}
