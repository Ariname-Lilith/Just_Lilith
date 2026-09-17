using System;

namespace Just_Lilith.Core.Contracts;

public sealed record RequestIdentity(Guid RequestId, Guid ConversationId, long ConversationRevision);
